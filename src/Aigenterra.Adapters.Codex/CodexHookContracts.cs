using Aigenterra.Application.Common;

namespace Aigenterra.Adapters.Codex;

/// <summary>Describes how the adapter handled a Codex hook delivery.</summary>
public enum CodexIngestDisposition
{
    Recorded,
    Duplicate,
    Ignored
}

/// <summary>Reports the Aigenterra identities affected by a Codex hook delivery.</summary>
public sealed record CodexHookIngestReceipt(
    CodexIngestDisposition Disposition,
    Guid? ConversationId,
    Guid? TaskExecutionId,
    Guid? AgentExecutionId,
    Guid? ObservationId);

/// <summary>Translates opaque Codex hook payloads into execution use cases.</summary>
public interface ICodexHookIngestor
{
    ValueTask<UseCaseResult<CodexHookIngestReceipt>> IngestAsync(
        ReadOnlyMemory<byte> payload,
        DateTimeOffset observedAt,
        CancellationToken cancellationToken = default);
}
