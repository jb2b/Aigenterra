using Aigenterra.Application.Common;
using Aigenterra.Application.Recording;

namespace Aigenterra.Application.Runtime;

public sealed record RuntimeHealthCheck(string Capability, string Status, string Detail);
public sealed record RuntimeHookHealth(string Name, bool? Configured, bool? Enabled, string Trust,
    string? CurrentHash, string? ApprovedHash);
public sealed record RuntimeBinaryInventory(string? ExecutablePath, string? ExecutableVersion, string? ReportedRuntimeVersion,
    string? ExecutableSha256, string? DesktopApplicationVersion, string Surface, string SurfaceEvidence, string? Model);
public sealed record RuntimeCapabilityProfile(string Id, string ProfileSha256, string? SchemaSha256,
    string Compatibility, string PassiveLive, string PassiveHistory, string Coverage, IReadOnlyDictionary<string, bool>? Features);
public sealed record RuntimeCaptureHealth(int? Pending, int? Retained, int? Incomplete, bool? KnownCaptureFailure);
public sealed record RuntimeSourceHealth(Guid CaptureId, Guid RuntimeNamespaceId, Guid SourceId, Guid? RuntimeInstanceId,
    DateTimeOffset ObservedAt, string DetectorVersion, RuntimeBinaryInventory Binary, RuntimeCapabilityProfile Profile,
    string ConnectionState, DateTimeOffset? LastFrameAt, DateTimeOffset? LastSuccessfulRequestAt, int? ReconnectCount,
    RuntimeCaptureHealth Capture, string ProjectionState, IReadOnlyList<RuntimeHookHealth> Hooks, IReadOnlyList<RuntimeHealthCheck> Checks);
public sealed record RuntimeSourceHealthView(RuntimeSourceHealth Snapshot, double InventoryAgeSeconds, double? LastFrameAgeSeconds);
public sealed record RuntimeInstanceInventory(long Position, RuntimeSourceHealth LatestDiagnostic);

public interface IRuntimeHealthDecoder
{
    UseCaseResult<RuntimeSourceHealth> Decode(RuntimeCaptureEnvelope capture);
}

public interface IRuntimeSourceHealthStore
{
    Task AppendAsync(RuntimeSourceHealth health, long ingestPosition, CancellationToken cancellationToken = default);
    Task<RuntimeSourceHealth?> ReadAsync(Guid runtimeNamespaceId, Guid sourceId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RuntimeInstanceInventory>> ReadInstancesAsync(Guid runtimeNamespaceId, long after, int limit, CancellationToken cancellationToken = default);
}

public sealed class RecordRuntimeSourceHealth(IRawCaptureStore archive, IRuntimeHealthDecoder decoder, IRuntimeSourceHealthStore store)
{
    public async Task<UseCaseResult<RuntimeSourceHealth>> ExecuteAsync(Guid captureId, CancellationToken cancellationToken = default)
    {
        var archived = await archive.ReadRuntimeAsync(captureId, cancellationToken);
        if (archived is null)
            return UseCaseResult<RuntimeSourceHealth>.Failure(ApplicationErrorType.NotFound, "runtime_capture_not_found", "No archived v2 capture with this ID.");
        var result = decoder.Decode(archived.Envelope);
        if (result.IsSuccess) await store.AppendAsync(result.Value!, archived.IngestPosition, cancellationToken);
        return result;
    }
}

public sealed class GetRuntimeSourceHealth(IRuntimeSourceHealthStore store, TimeProvider clock)
{
    public async Task<RuntimeSourceHealthView?> ExecuteAsync(Guid runtimeNamespaceId, Guid sourceId, CancellationToken cancellationToken = default)
    {
        var health = await store.ReadAsync(runtimeNamespaceId, sourceId, cancellationToken);
        if (health is null) return null;
        var now = clock.GetUtcNow();
        // Age is a displayed measurement. No event traffic on an idle source is not a failure diagnosis.
        return new(health, (now - health.ObservedAt).TotalSeconds,
            health.LastFrameAt is { } frame ? (now - frame).TotalSeconds : null);
    }
}

public sealed class GetRuntimeInstances(IRuntimeSourceHealthStore store)
{
    public Task<IReadOnlyList<RuntimeInstanceInventory>> ExecuteAsync(Guid runtimeNamespaceId, long after = 0, int limit = 100,
        CancellationToken cancellationToken = default)
    {
        if (runtimeNamespaceId == Guid.Empty || after < 0 || limit is < 1 or > 500) throw new ArgumentOutOfRangeException(nameof(limit));
        return store.ReadInstancesAsync(runtimeNamespaceId, after, limit, cancellationToken);
    }
}
