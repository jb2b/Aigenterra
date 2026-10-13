using System.Security.Cryptography;
using System.Text.Json;
using Aigenterra.Application.Common;
using Aigenterra.Application.Executions;

namespace Aigenterra.Adapters.Codex;

/// <summary>Accepts a bounded projection of a runtime command item, never an agent-authored claim.</summary>
public sealed class CodexCommandResultIngestor(ResolveExecutionRuntimeReference resolve,
    GetTaskExecutionTimeline timeline, RecordObservation record)
{
    public async ValueTask<UseCaseResult<CodexHookIngestReceipt>> IngestAsync(ReadOnlyMemory<byte> payload,
        DateTimeOffset observedAt, CancellationToken cancellationToken = default)
    {
        string session, turn;
        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            session = CodexCommandEvidenceDecoder.Text(root, "session_id") ?? throw new JsonException();
            turn = CodexCommandEvidenceDecoder.Text(root, "turn_id") ?? throw new JsonException();
            var item = root.GetProperty("item");
            if (CodexCommandEvidenceDecoder.Text(root, "source") != "codex-app-server-command/v1"
                || CodexCommandEvidenceDecoder.Text(root, "runtime_version") is null
                || CodexCommandEvidenceDecoder.Text(item, "type") != "commandExecution"
                || CodexCommandEvidenceDecoder.Text(item, "id") is null
                || CodexCommandEvidenceDecoder.Text(item, "status") is not ("completed" or "failed" or "declined"))
                throw new JsonException();
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            return UseCaseResult<CodexHookIngestReceipt>.Failure(ApplicationErrorType.Validation,
                "invalid_codex_command_result", "Expected a versioned terminal Codex command item with session and turn IDs.");
        }
        var task = await resolve.ExecuteAsync(new(ExecutionResourceType.TaskExecution, new("codex", "turn", turn)), cancellationToken);
        var conversation = await resolve.ExecuteAsync(new(ExecutionResourceType.Conversation, new("codex", "session", session)), cancellationToken);
        if (!task.IsSuccess || !conversation.IsSuccess) return Missing();
        var known = await timeline.ExecuteAsync(new(task.Value!.ExecutionId), cancellationToken);
        if (!known.IsSuccess || known.Value!.ConversationId != conversation.Value!.ExecutionId) return Missing();
        // Identical replay is a no-op; conflicting runtime records are retained so evaluation can refuse certainty.
        var reference = new RuntimeReferenceInput("codex", "command-result", Convert.ToHexString(SHA256.HashData(payload.Span)));
        var existing = await resolve.ExecuteAsync(new(ExecutionResourceType.Observation, reference), cancellationToken);
        if (existing.IsSuccess) return Receipt(existing.Value!.ExecutionId, false);
        var appended = await record.ExecuteAsync(new(task.Value.ExecutionId, null, observedAt, "toolresult",
            "Codex terminal command metadata captured from app-server.", reference, new("application/json", payload)), cancellationToken);
        if (appended.IsSuccess) return Receipt(appended.Value!.ObservationId, true);
        if (appended.Error!.Code == "duplicate_runtime_reference")
        {
            existing = await resolve.ExecuteAsync(new(ExecutionResourceType.Observation, reference), cancellationToken);
            if (existing.IsSuccess) return Receipt(existing.Value!.ExecutionId, false);
        }
        return UseCaseResult<CodexHookIngestReceipt>.Failure(appended.Error.Type, appended.Error.Code, appended.Error.Message);

        UseCaseResult<CodexHookIngestReceipt> Receipt(Guid observationId, bool created) =>
            UseCaseResult<CodexHookIngestReceipt>.Success(new(created ? CodexIngestDisposition.Recorded : CodexIngestDisposition.Duplicate,
                conversation.Value!.ExecutionId, task.Value!.ExecutionId, null, observationId));
    }
    private static UseCaseResult<CodexHookIngestReceipt> Missing() => UseCaseResult<CodexHookIngestReceipt>.Failure(
        ApplicationErrorType.Conflict, "codex_execution_context_not_found", "Record the matching session and turn hooks before capturing command results.");
}
