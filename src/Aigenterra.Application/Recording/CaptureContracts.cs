namespace Aigenterra.Application.Recording;

/// <summary>A point-in-time local Git observation, never inferred from later repository state.</summary>
public sealed record RepositoryContext(
    DateTimeOffset CapturedAt,
    string Status,
    string? RepositoryRoot = null,
    string? CommonGitDirectory = null,
    string? WorktreePath = null,
    string? Branch = null,
    string? HeadSha = null,
    bool? IsDirty = null,
    IReadOnlyList<string>? ChangedFiles = null,
    IReadOnlyList<string>? UntrackedFiles = null);

public interface IRepositoryContextReader
{
    Task<RepositoryContext> ReadAsync(string workingDirectory, CancellationToken cancellationToken = default);
}

/// <summary>The envelope is versioned independently of the opaque, unmodified source bytes.</summary>
public sealed record CaptureEnvelope(
    int Version,
    Guid CaptureId,
    DateTimeOffset CapturedAt,
    byte[] Payload,
    string? ContextPhase = null,
    RepositoryContext? RepositoryContext = null);

public enum CaptureArchiveResult { Added, Duplicate, Conflict }

public sealed record CaptureOutcome(
    Guid CaptureId,
    string Disposition,
    string? Reason,
    Guid? ConversationId = null,
    Guid? TaskExecutionId = null,
    Guid? AgentExecutionId = null,
    Guid? ObservationId = null);

public interface IRawCaptureStore
{
    Task<CaptureArchiveResult> ArchiveAsync(CaptureEnvelope capture, CancellationToken cancellationToken = default);
    Task<CaptureArchiveResult> ArchiveAsync(RuntimeCaptureEnvelope capture, CancellationToken cancellationToken = default);
    Task<ArchivedRuntimeCapture?> ReadRuntimeAsync(Guid captureId, CancellationToken cancellationToken = default);
    Task<CaptureOutcome?> FindOutcomeAsync(Guid captureId, CancellationToken cancellationToken = default);
    Task CompleteAsync(CaptureOutcome outcome, CancellationToken cancellationToken = default);
}
