using Aigenterra.Application.Common;

namespace Aigenterra.Adapters.Codex;

public enum CodexIngestDisposition
{
    Recorded,
    Duplicate,
    Ignored
}

public sealed record CodexHookIngestReceipt(
    CodexIngestDisposition Disposition,
    Guid? ConversationId,
    Guid? TaskExecutionId,
    Guid? AgentExecutionId,
    Guid? ObservationId);

public interface ICodexHookIngestor
{
    ValueTask<UseCaseResult<CodexHookIngestReceipt>> IngestAsync(
        ReadOnlyMemory<byte> payload,
        DateTimeOffset observedAt,
        CancellationToken cancellationToken = default);
}
