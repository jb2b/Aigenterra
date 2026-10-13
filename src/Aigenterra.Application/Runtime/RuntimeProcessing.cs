using System.Security.Cryptography;
using System.Text;
using Aigenterra.Application.Recording;

namespace Aigenterra.Application.Runtime;

public sealed record RuntimeWork(Guid CaptureId, Guid LeaseId, int Attempts);
public interface IRuntimeWorkStore
{
    Task<RuntimeWork?> ClaimAsync(bool normalize, bool health, CancellationToken cancellationToken = default);
    Task FinishAsync(RuntimeWork work, string outcome, bool retry, CancellationToken cancellationToken = default);
}

/// <summary>Recorder sidecar; original protocol messages are separate, immutable captures.</summary>
public sealed record RuntimeRequestContext(int Version, Guid RequestCaptureId, Guid ResponseCaptureId, Guid CycleId);
public sealed record VerifiedRuntimeRequest(Guid ContextCaptureId, Guid RequestCaptureId, Guid ResponseCaptureId,
    Guid RuntimeNamespaceId, Guid SourceId, Guid ConnectionEpochId, Guid CycleId,
    string Method, string ThreadId, string? TurnId, string? Cursor);
public interface IRuntimeRequestContextDecoder
{
    RuntimeRequestContext? Read(RuntimeCaptureEnvelope context);
    VerifiedRuntimeRequest? Decode(RuntimeCaptureEnvelope context, RuntimeCaptureEnvelope request, RuntimeCaptureEnvelope response);
}
public interface IRuntimeRequestStore
{
    Task AppendAsync(VerifiedRuntimeRequest request, CancellationToken cancellationToken = default);
    Task<VerifiedRuntimeRequest?> ReadAsync(Guid responseCaptureId, CancellationToken cancellationToken = default);
}
public interface IContextualRuntimeNormalizer : IRuntimeCaptureNormalizer
{
    bool RequiresRequestContext(RuntimeCaptureEnvelope capture);
    RuntimeNormalization Normalize(RuntimeCaptureEnvelope capture, VerifiedRuntimeRequest? request);
}

public static class RuntimeIdentity
{
    // Length-delimited parts avoid collisions between arbitrary provider IDs containing separators.
    public static string Key(params string[] parts) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(string.Concat(parts.Select(p => p.Length + ":" + p)))));
    public static Guid Id(params string[] parts) => new(Convert.FromHexString(Key(parts)).AsSpan(0, 16));
}

public sealed class ProcessRuntimeCapture(IRawCaptureStore archive, IRuntimeFactStore facts,
    IRuntimeCaptureNormalizer normalizer, IRuntimeRequestContextDecoder decoder,
    IRuntimeRequestStore requests, RecordRuntimeSourceHealth health,
    IRuntimeEvidenceNormalizer evidenceNormalizer, IRuntimeEvidenceStore evidenceStore)
{
    public async Task<string> ExecuteAsync(Guid captureId, CancellationToken token = default)
    {
        var saved = await archive.ReadRuntimeAsync(captureId, token) ?? throw new InvalidOperationException("Missing archived work input.");
        var capture = saved.Envelope;
        if (capture.Source is { SourceKind: "diagnostic", Provenance: "inventory" })
            return (await health.ExecuteAsync(captureId, token)).IsSuccess ? "health_recorded" : "health_rejected";
        if (capture.Source is { SourceKind: "diagnostic", EvidenceStage: "RecorderDiagnostic", Provenance: "request_context" })
        {
            var context = decoder.Read(capture);
            if (context is not { Version: 1 } || context.RequestCaptureId == Guid.Empty || context.ResponseCaptureId == Guid.Empty || context.CycleId == Guid.Empty)
                return "context_rejected";
            var request = await archive.ReadRuntimeAsync(context.RequestCaptureId, token);
            var response = await archive.ReadRuntimeAsync(context.ResponseCaptureId, token);
            if (request is null || response is null) return "pending_context";
            var verified = decoder.Decode(capture, request.Envelope, response.Envelope);
            if (verified is null) return "context_rejected";
            await requests.AppendAsync(verified, token);
            return "context_recorded";
        }
        var linked = await requests.ReadAsync(captureId, token);
        if (linked is null && (capture.Source.Provenance == "history_response"
            || normalizer is IContextualRuntimeNormalizer contextRequired && contextRequired.RequiresRequestContext(capture))) return "pending_context";
        var result = normalizer is IContextualRuntimeNormalizer contextual ? contextual.Normalize(capture, linked) : normalizer.Normalize(capture);
        var attempt = new NormalizationAttempt(RuntimeIdentity.Id(captureId.ToString(), normalizer.Version), captureId,
            normalizer.Version, saved.RecordedAt, result.Status, result.Reason, result.Facts);
        await facts.AppendAsync(attempt, token);
        var evidence = evidenceNormalizer.Normalize(capture, linked);
        await evidenceStore.AppendAsync(evidence, token);
        return result.Status == "normalized" || evidence.Outcome == "normalized" ? "normalized" : result.Status;
    }
}
