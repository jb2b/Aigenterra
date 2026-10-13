using System.Security.Cryptography;
using Aigenterra.Application.Common;

namespace Aigenterra.Application.Recording;

/// <summary>Source identity is explicit enrollment, never inferred from paths, PIDs or payload text.</summary>
public sealed record RuntimeCaptureSource(
    Guid RuntimeNamespaceId,
    Guid SourceId,
    Guid ConnectionEpochId,
    string Provider,
    string SourceKind,
    string EvidenceStage,
    string Provenance,
    Guid? RuntimeInstanceId = null,
    string? RuntimeVersion = null,
    long? SourceSequence = null);

/// <summary>Separate contract: adding v2 must not change serialization or fingerprints of v1.</summary>
public sealed record RuntimeCaptureEnvelope(
    int Version,
    Guid CaptureId,
    DateTimeOffset CapturedAt,
    DateTimeOffset ReceivedAt,
    RuntimeCaptureSource Source,
    string MediaType,
    long PayloadLength,
    string PayloadSha256,
    byte[] Payload,
    string? ContextPhase = null,
    RepositoryContext? RepositoryContext = null);

public sealed record ArchivedRuntimeCapture(RuntimeCaptureEnvelope Envelope, long IngestPosition, DateTimeOffset RecordedAt);
public sealed record RuntimeCaptureReceipt(Guid CaptureId, bool Archived, bool Duplicate, string Normalization);

public sealed class ArchiveRuntimeCapture(IRawCaptureStore store)
{
    public async Task<UseCaseResult<RuntimeCaptureReceipt>> ExecuteAsync(RuntimeCaptureEnvelope capture,
        CancellationToken cancellationToken = default)
    {
        var source = capture.Source;
        if (capture.Version != 2 || capture.CaptureId == Guid.Empty || capture.Payload is null
            || source is null || source.RuntimeNamespaceId == Guid.Empty || source.SourceId == Guid.Empty
            || source.ConnectionEpochId == Guid.Empty || source.RuntimeInstanceId == Guid.Empty || source.SourceSequence < 0
            || !Text(source.Provider) || !Text(source.SourceKind) || !Text(source.EvidenceStage) || !Text(source.Provenance)
            || source.RuntimeVersion is { Length: > 128 } || !Text(capture.MediaType)
            || capture.CapturedAt.Offset != TimeSpan.Zero || capture.ReceivedAt.Offset != TimeSpan.Zero
            || capture.ContextPhase is not (null or "start" or "end") || capture.PayloadLength != capture.Payload.LongLength
            || !string.Equals(capture.PayloadSha256, Convert.ToHexString(SHA256.HashData(capture.Payload)), StringComparison.Ordinal))
            return UseCaseResult<RuntimeCaptureReceipt>.Failure(ApplicationErrorType.Validation, "invalid_runtime_capture", "Invalid v2 metadata, length or SHA-256 (uppercase hex required).");

        // Opaque bytes are durable before any provider interpretation. Oversize enforcement belongs to ingress/spool.
        var result = await store.ArchiveAsync(capture, cancellationToken);
        return result == CaptureArchiveResult.Conflict
            ? UseCaseResult<RuntimeCaptureReceipt>.Failure(ApplicationErrorType.Conflict, "capture_id_conflict", "Capture ID already has different content.")
            : UseCaseResult<RuntimeCaptureReceipt>.Success(new(capture.CaptureId, true, result == CaptureArchiveResult.Duplicate, "pending"));
    }

    private static bool Text(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 128;
}
