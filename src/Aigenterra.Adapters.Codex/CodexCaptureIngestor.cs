using Aigenterra.Application.Common;
using Aigenterra.Application.Recording;

namespace Aigenterra.Adapters.Codex;

public sealed class CodexCaptureIngestor(IRawCaptureStore archive, ICodexHookIngestor hooks)
{
    public async Task<UseCaseResult<CaptureOutcome>> IngestAsync(CaptureEnvelope capture, CancellationToken cancellationToken = default)
    {
        if (capture.Version != 1 || capture.CaptureId == Guid.Empty || capture.Payload is null
            || capture.CapturedAt.Offset != TimeSpan.Zero || capture.ContextPhase is not (null or "start" or "end"))
            return UseCaseResult<CaptureOutcome>.Failure(ApplicationErrorType.Validation, "invalid_capture", "Invalid capture envelope.");
        // A separate durable insert comes before all provider parsing and use-case projection.
        if (await archive.ArchiveAsync(capture, cancellationToken) == CaptureArchiveResult.Conflict)
            return UseCaseResult<CaptureOutcome>.Failure(ApplicationErrorType.Conflict, "capture_id_conflict", "Capture ID already has different content.");
        if (await archive.FindOutcomeAsync(capture.CaptureId, cancellationToken) is { } existing)
            return UseCaseResult<CaptureOutcome>.Success(existing);

        var result = await hooks.IngestAsync(capture.Payload, capture.CapturedAt, cancellationToken);
        var outcome = result.IsSuccess
            ? new CaptureOutcome(capture.CaptureId,
                result.Value!.Disposition == CodexIngestDisposition.Ignored ? "archived_only" : "projected",
                result.Value.Disposition == CodexIngestDisposition.Ignored ? "unsupported_hook" : null,
                result.Value.ConversationId, result.Value.TaskExecutionId, result.Value.AgentExecutionId, result.Value.ObservationId)
            : new CaptureOutcome(capture.CaptureId, "archived_only", result.Error!.Code);
        await archive.CompleteAsync(outcome, cancellationToken);
        return UseCaseResult<CaptureOutcome>.Success((await archive.FindOutcomeAsync(capture.CaptureId, cancellationToken))!);
    }
}
