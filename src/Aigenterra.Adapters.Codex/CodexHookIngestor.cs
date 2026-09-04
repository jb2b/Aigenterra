using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Aigenterra.Application.Common;
using Aigenterra.Application.Executions;

namespace Aigenterra.Adapters.Codex;

public sealed class CodexHookIngestor(
    CreateConversation createConversation,
    StartTaskExecution startTaskExecution,
    StartAgentExecution startAgentExecution,
    RecordObservation recordObservation,
    FinishAgentExecution finishAgentExecution,
    FinishTaskExecution finishTaskExecution,
    ResolveExecutionRuntimeReference resolveRuntimeReference) : ICodexHookIngestor
{
    private const string RuntimeName = "codex";

    public async ValueTask<UseCaseResult<CodexHookIngestReceipt>> IngestAsync(
        ReadOnlyMemory<byte> payload,
        DateTimeOffset observedAt,
        CancellationToken cancellationToken = default)
    {
        CodexHookEvent hookEvent;
        try
        {
            hookEvent = CodexHookEvent.Parse(payload);
        }
        catch (JsonException exception)
        {
            return Failure(
                ApplicationErrorType.Validation,
                "invalid_codex_hook_payload",
                exception.Message);
        }

        if (!CodexHookEvent.IsSupported(hookEvent.HookEventName))
        {
            return UseCaseResult<CodexHookIngestReceipt>.Success(new CodexHookIngestReceipt(
                CodexIngestDisposition.Ignored,
                null,
                null,
                null,
                null));
        }

        var normalizedObservedAt = observedAt.ToUniversalTime();
        return hookEvent.HookEventName switch
        {
            "SessionStart" => await IngestSessionStartAsync(
                hookEvent,
                normalizedObservedAt,
                cancellationToken),
            "UserPromptSubmit" => await IngestUserPromptAsync(
                hookEvent,
                payload,
                normalizedObservedAt,
                cancellationToken),
            "PreToolUse" => await IngestTaskObservationAsync(
                hookEvent,
                payload,
                normalizedObservedAt,
                "tool-invocation",
                hookEvent.ToolUseId!,
                "toolinvocation",
                $"Codex tool invoked: {hookEvent.ToolName}.",
                cancellationToken),
            "PostToolUse" => await IngestTaskObservationAsync(
                hookEvent,
                payload,
                normalizedObservedAt,
                "tool-result",
                hookEvent.ToolUseId!,
                "toolresult",
                $"Codex tool returned: {hookEvent.ToolName}.",
                cancellationToken),
            "SubagentStart" => await IngestSubagentSignalAsync(
                hookEvent,
                payload,
                normalizedObservedAt,
                "subagent-start",
                hookEvent.AgentId!,
                $"Codex subagent started: {hookEvent.AgentType}.",
                cancellationToken),
            "SubagentStop" => await IngestSubagentSignalAsync(
                hookEvent,
                payload,
                normalizedObservedAt,
                "subagent-stop",
                CreateStopIdentity(hookEvent),
                $"Codex subagent emitted a stop signal: {hookEvent.AgentType}.",
                cancellationToken),
            "Stop" => await IngestRootSignalAsync(
                hookEvent,
                payload,
                normalizedObservedAt,
                "turn-stop",
                CreateStopIdentity(hookEvent),
                "Codex turn emitted a stop signal.",
                false,
                cancellationToken),
            "Interrupt" => await IngestRootSignalAsync(
                hookEvent,
                payload,
                normalizedObservedAt,
                "turn-interrupt",
                hookEvent.TurnId!,
                "Codex turn was interrupted.",
                true,
                cancellationToken),
            _ => throw new InvalidOperationException("Unsupported Codex hook event reached ingestion.")
        };
    }

    private async ValueTask<UseCaseResult<CodexHookIngestReceipt>> IngestSessionStartAsync(
        CodexHookEvent hookEvent,
        DateTimeOffset observedAt,
        CancellationToken cancellationToken)
    {
        var conversation = await EnsureConversationAsync(
            hookEvent.SessionId!,
            observedAt,
            cancellationToken);
        return conversation.IsSuccess
            ? Success(
                conversation.Value!.Created
                    ? CodexIngestDisposition.Recorded
                    : CodexIngestDisposition.Duplicate,
                conversationId: conversation.Value.Id)
            : CopyFailure(conversation);
    }

    private async ValueTask<UseCaseResult<CodexHookIngestReceipt>> IngestUserPromptAsync(
        CodexHookEvent hookEvent,
        ReadOnlyMemory<byte> payload,
        DateTimeOffset observedAt,
        CancellationToken cancellationToken)
    {
        var conversation = await EnsureConversationAsync(
            hookEvent.SessionId!,
            observedAt,
            cancellationToken);
        if (!conversation.IsSuccess)
        {
            return CopyFailure(conversation);
        }

        var task = await EnsureTaskExecutionAsync(
            conversation.Value!.Id,
            hookEvent.TurnId!,
            hookEvent.Prompt!,
            observedAt,
            cancellationToken);
        if (!task.IsSuccess)
        {
            return CopyFailure(task);
        }

        var rootAgent = await EnsureAgentExecutionAsync(
            task.Value!.Id,
            "turn-root-agent",
            hookEvent.TurnId!,
            observedAt,
            cancellationToken);
        if (!rootAgent.IsSuccess)
        {
            return CopyFailure(rootAgent);
        }

        var observation = await EnsureObservationAsync(
            task.Value.Id,
            rootAgent.Value!.Id,
            observedAt,
            "message",
            "Codex user prompt submitted.",
            "user-prompt",
            hookEvent.TurnId!,
            payload,
            cancellationToken);
        return observation.IsSuccess
            ? Success(
                observation.Value!.Created
                    ? CodexIngestDisposition.Recorded
                    : CodexIngestDisposition.Duplicate,
                conversation.Value.Id,
                task.Value.Id,
                rootAgent.Value.Id,
                observation.Value.Id)
            : CopyFailure(observation);
    }

    private async ValueTask<UseCaseResult<CodexHookIngestReceipt>> IngestTaskObservationAsync(
        CodexHookEvent hookEvent,
        ReadOnlyMemory<byte> payload,
        DateTimeOffset observedAt,
        string observationResourceKind,
        string externalId,
        string observationKind,
        string summary,
        CancellationToken cancellationToken)
    {
        var task = await ResolveAsync(
            ExecutionResourceType.TaskExecution,
            Reference("turn", hookEvent.TurnId!),
            cancellationToken);
        if (!task.IsSuccess)
        {
            return CorrelationFailure(hookEvent.TurnId!);
        }

        var observation = await EnsureObservationAsync(
            task.Value!.ExecutionId,
            null,
            observedAt,
            observationKind,
            summary,
            observationResourceKind,
            externalId,
            payload,
            cancellationToken);
        return observation.IsSuccess
            ? Success(
                observation.Value!.Created
                    ? CodexIngestDisposition.Recorded
                    : CodexIngestDisposition.Duplicate,
                taskExecutionId: task.Value.ExecutionId,
                observationId: observation.Value.Id)
            : CopyFailure(observation);
    }

    private async ValueTask<UseCaseResult<CodexHookIngestReceipt>> IngestSubagentSignalAsync(
        CodexHookEvent hookEvent,
        ReadOnlyMemory<byte> payload,
        DateTimeOffset observedAt,
        string observationResourceKind,
        string observationExternalId,
        string summary,
        CancellationToken cancellationToken)
    {
        var task = await ResolveAsync(
            ExecutionResourceType.TaskExecution,
            Reference("turn", hookEvent.TurnId!),
            cancellationToken);
        if (!task.IsSuccess)
        {
            return CorrelationFailure(hookEvent.TurnId!);
        }

        var agent = await EnsureAgentExecutionAsync(
            task.Value!.ExecutionId,
            "subagent",
            hookEvent.AgentId!,
            observedAt,
            cancellationToken);
        if (!agent.IsSuccess)
        {
            return CopyFailure(agent);
        }

        var observation = await EnsureObservationAsync(
            task.Value.ExecutionId,
            agent.Value!.Id,
            observedAt,
            "statustransition",
            summary,
            observationResourceKind,
            observationExternalId,
            payload,
            cancellationToken);
        return observation.IsSuccess
            ? Success(
                observation.Value!.Created
                    ? CodexIngestDisposition.Recorded
                    : CodexIngestDisposition.Duplicate,
                taskExecutionId: task.Value.ExecutionId,
                agentExecutionId: agent.Value.Id,
                observationId: observation.Value.Id)
            : CopyFailure(observation);
    }

    private async ValueTask<UseCaseResult<CodexHookIngestReceipt>> IngestRootSignalAsync(
        CodexHookEvent hookEvent,
        ReadOnlyMemory<byte> payload,
        DateTimeOffset observedAt,
        string observationResourceKind,
        string observationExternalId,
        string summary,
        bool interrupt,
        CancellationToken cancellationToken)
    {
        var task = await ResolveAsync(
            ExecutionResourceType.TaskExecution,
            Reference("turn", hookEvent.TurnId!),
            cancellationToken);
        if (!task.IsSuccess)
        {
            return CorrelationFailure(hookEvent.TurnId!);
        }

        var rootAgent = await EnsureAgentExecutionAsync(
            task.Value!.ExecutionId,
            "turn-root-agent",
            hookEvent.TurnId!,
            observedAt,
            cancellationToken);
        if (!rootAgent.IsSuccess)
        {
            return CopyFailure(rootAgent);
        }

        var observation = await EnsureObservationAsync(
            task.Value.ExecutionId,
            rootAgent.Value!.Id,
            observedAt,
            "statustransition",
            summary,
            observationResourceKind,
            observationExternalId,
            payload,
            cancellationToken);
        if (!observation.IsSuccess)
        {
            return CopyFailure(observation);
        }

        if (interrupt)
        {
            var finishAgent = await finishAgentExecution.ExecuteAsync(
                new FinishAgentExecutionCommand(
                    task.Value.ExecutionId,
                    rootAgent.Value.Id,
                    "interrupted",
                    observedAt),
                cancellationToken);
            if (!finishAgent.IsSuccess)
            {
                return CopyFailure(finishAgent);
            }

            var finishTask = await finishTaskExecution.ExecuteAsync(
                new FinishTaskExecutionCommand(
                    task.Value.ExecutionId,
                    "interrupted",
                    observedAt),
                cancellationToken);
            if (!finishTask.IsSuccess)
            {
                return CopyFailure(finishTask);
            }
        }

        return Success(
            observation.Value!.Created
                ? CodexIngestDisposition.Recorded
                : CodexIngestDisposition.Duplicate,
            taskExecutionId: task.Value.ExecutionId,
            agentExecutionId: rootAgent.Value.Id,
            observationId: observation.Value.Id);
    }

    private async ValueTask<UseCaseResult<EnsuredExecution>> EnsureConversationAsync(
        string sessionId,
        DateTimeOffset observedAt,
        CancellationToken cancellationToken)
    {
        var reference = Reference("session", sessionId);
        var existing = await ResolveAsync(
            ExecutionResourceType.Conversation,
            reference,
            cancellationToken);
        if (existing.IsSuccess)
        {
            return Ensured(existing.Value!.ExecutionId, false);
        }

        var created = await createConversation.ExecuteAsync(
            new CreateConversationCommand(observedAt, reference),
            cancellationToken);
        if (created.IsSuccess)
        {
            return Ensured(created.Value!.ConversationId, true);
        }

        return await ResolveAfterDuplicateAsync(
            ExecutionResourceType.Conversation,
            reference,
            created.Error!,
            cancellationToken);
    }

    private async ValueTask<UseCaseResult<EnsuredExecution>> EnsureTaskExecutionAsync(
        Guid conversationId,
        string turnId,
        string objective,
        DateTimeOffset observedAt,
        CancellationToken cancellationToken)
    {
        var reference = Reference("turn", turnId);
        var existing = await ResolveAsync(
            ExecutionResourceType.TaskExecution,
            reference,
            cancellationToken);
        if (existing.IsSuccess)
        {
            return Ensured(existing.Value!.ExecutionId, false);
        }

        var created = await startTaskExecution.ExecuteAsync(
            new StartTaskExecutionCommand(
                conversationId,
                objective,
                observedAt,
                reference),
            cancellationToken);
        if (created.IsSuccess)
        {
            return Ensured(created.Value!.TaskExecutionId, true);
        }

        return await ResolveAfterDuplicateAsync(
            ExecutionResourceType.TaskExecution,
            reference,
            created.Error!,
            cancellationToken);
    }

    private async ValueTask<UseCaseResult<EnsuredExecution>> EnsureAgentExecutionAsync(
        Guid taskExecutionId,
        string resourceKind,
        string externalId,
        DateTimeOffset observedAt,
        CancellationToken cancellationToken)
    {
        var reference = Reference(resourceKind, externalId);
        var existing = await ResolveAsync(
            ExecutionResourceType.AgentExecution,
            reference,
            cancellationToken);
        if (existing.IsSuccess)
        {
            return Ensured(existing.Value!.ExecutionId, false);
        }

        var created = await startAgentExecution.ExecuteAsync(
            new StartAgentExecutionCommand(
                taskExecutionId,
                null,
                observedAt,
                reference),
            cancellationToken);
        if (created.IsSuccess)
        {
            return Ensured(created.Value!.AgentExecutionId, true);
        }

        return await ResolveAfterDuplicateAsync(
            ExecutionResourceType.AgentExecution,
            reference,
            created.Error!,
            cancellationToken);
    }

    private async ValueTask<UseCaseResult<EnsuredExecution>> EnsureObservationAsync(
        Guid taskExecutionId,
        Guid? agentExecutionId,
        DateTimeOffset observedAt,
        string kind,
        string summary,
        string resourceKind,
        string externalId,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        var reference = Reference(resourceKind, externalId);
        var existing = await ResolveAsync(
            ExecutionResourceType.Observation,
            reference,
            cancellationToken);
        if (existing.IsSuccess)
        {
            return Ensured(existing.Value!.ExecutionId, false);
        }

        var recorded = await recordObservation.ExecuteAsync(
            new RecordObservationCommand(
                taskExecutionId,
                agentExecutionId,
                observedAt,
                kind,
                summary,
                reference,
                new SourceEvidence("application/json", payload)),
            cancellationToken);
        if (recorded.IsSuccess)
        {
            return Ensured(recorded.Value!.ObservationId, true);
        }

        return await ResolveAfterDuplicateAsync(
            ExecutionResourceType.Observation,
            reference,
            recorded.Error!,
            cancellationToken);
    }

    private async ValueTask<UseCaseResult<EnsuredExecution>> ResolveAfterDuplicateAsync(
        ExecutionResourceType resourceType,
        RuntimeReferenceInput reference,
        ApplicationError error,
        CancellationToken cancellationToken)
    {
        if (error.Type != ApplicationErrorType.Conflict
            || error.Code != "duplicate_runtime_reference")
        {
            return UseCaseResult<EnsuredExecution>.Failure(
                error.Type,
                error.Code,
                error.Message);
        }

        var resolved = await ResolveAsync(resourceType, reference, cancellationToken);
        return resolved.IsSuccess
            ? Ensured(resolved.Value!.ExecutionId, false)
            : UseCaseResult<EnsuredExecution>.Failure(
                error.Type,
                error.Code,
                error.Message);
    }

    private ValueTask<UseCaseResult<ExecutionRuntimeReferenceResolved>> ResolveAsync(
        ExecutionResourceType resourceType,
        RuntimeReferenceInput reference,
        CancellationToken cancellationToken) =>
        resolveRuntimeReference.ExecuteAsync(
            new ResolveExecutionRuntimeReferenceQuery(resourceType, reference),
            cancellationToken);

    private static RuntimeReferenceInput Reference(string resourceKind, string externalId) =>
        new(RuntimeName, resourceKind, externalId);

    private static string CreateStopIdentity(CodexHookEvent hookEvent)
    {
        var contextId = hookEvent.AgentId ?? hookEvent.TurnId!;
        var identityText = string.Join(
            '\u001f',
            hookEvent.HookEventName,
            hookEvent.SessionId,
            hookEvent.TurnId,
            hookEvent.AgentId,
            hookEvent.StopHookActive,
            hookEvent.LastAssistantMessage);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identityText)))
            .ToLowerInvariant();
        return $"{contextId}:{hash}";
    }

    private static UseCaseResult<EnsuredExecution> Ensured(Guid id, bool created) =>
        UseCaseResult<EnsuredExecution>.Success(new EnsuredExecution(id, created));

    private static UseCaseResult<CodexHookIngestReceipt> Success(
        CodexIngestDisposition disposition,
        Guid? conversationId = null,
        Guid? taskExecutionId = null,
        Guid? agentExecutionId = null,
        Guid? observationId = null) =>
        UseCaseResult<CodexHookIngestReceipt>.Success(new CodexHookIngestReceipt(
            disposition,
            conversationId,
            taskExecutionId,
            agentExecutionId,
            observationId));

    private static UseCaseResult<CodexHookIngestReceipt> CorrelationFailure(string turnId) =>
        Failure(
            ApplicationErrorType.Conflict,
            "codex_execution_context_not_found",
            $"No task execution exists for Codex turn '{turnId}'.");

    private static UseCaseResult<CodexHookIngestReceipt> Failure(
        ApplicationErrorType type,
        string code,
        string message) =>
        UseCaseResult<CodexHookIngestReceipt>.Failure(type, code, message);

    private static UseCaseResult<CodexHookIngestReceipt> CopyFailure<T>(UseCaseResult<T> result) =>
        Failure(result.Error!.Type, result.Error.Code, result.Error.Message);

    private sealed record EnsuredExecution(Guid Id, bool Created);
}
