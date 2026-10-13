using Aigenterra.Application.Recording;

namespace Aigenterra.Application.Runtime;

public sealed record RuntimeProjectionVersions(string Lifecycle, string Evidence, string Health, string Reconciliation, string Projector);
public sealed record RuntimeProjectionGeneration(Guid Id, Guid RuntimeNamespaceId, Guid? BaseGenerationId,
    RuntimeProjectionVersions Versions, string State, int InputCount, string ManifestSha256, string? ProjectionSha256);
public sealed record RuntimeProjectionInput(Guid CaptureId, string EnvelopeFingerprint, RuntimeCaptureSource Source, bool Complete);
public sealed record RuntimeProjectionInputResult(Guid CaptureId, RuntimeNormalization Lifecycle, RuntimeEvidenceBatch Evidence,
    RuntimeSourceHealth? Health, RuntimeReconciliationInput? Reconciliation, string? Limitation);
public sealed record RuntimeProjectionSourceInput(Guid CaptureId, Guid SourceId, RuntimeReconciliationInput Input);
public sealed record RuntimeProjectionInputOutcome(Guid CaptureId, string Lifecycle, string Evidence, string? Limitation);
public sealed record RuntimeProjectionDocument(Guid RuntimeNamespaceId, string Gate, string Coverage,
    IReadOnlyList<RuntimeTurnProjection> Turns, IReadOnlyList<RuntimeActivity> Activities, RuntimeAgentTopology Topology,
    IReadOnlyList<RuntimeSourceHealth> SourceHealth, IReadOnlyList<RuntimeProjectionSourceInput> Reconciliation,
    IReadOnlyList<RuntimeProjectionInputOutcome> InputOutcomes);
public sealed record RuntimeProjectionActivation(Guid RuntimeNamespaceId, long Revision, Guid GenerationId, Guid? PreviousGenerationId, string ProjectionSha256);
public interface IRuntimeProjectionStore
{
    Task<RuntimeProjectionGeneration> FreezeAsync(Guid ns, RuntimeProjectionVersions versions, CancellationToken token = default);
    Task<RuntimeProjectionGeneration?> ReadGenerationAsync(Guid id, CancellationToken token = default);
    Task<IReadOnlyList<RuntimeProjectionInput>> ReadInputsAsync(Guid id, CancellationToken token = default);
    Task SaveResultAsync(Guid id, RuntimeProjectionInputResult result, CancellationToken token = default);
    Task<IReadOnlyList<RuntimeProjectionInputResult>> ReadResultsAsync(Guid id, CancellationToken token = default);
    Task<RuntimeProjectionGeneration> CompleteAsync(Guid id, RuntimeProjectionDocument document, CancellationToken token = default);
    Task<RuntimeProjectionDocument?> ReadDocumentAsync(Guid id, CancellationToken token = default);
    Task<RuntimeProjectionActivation> ActivateAsync(Guid id, Guid? expectedActiveId, CancellationToken token = default);
    Task<RuntimeProjectionActivation?> ReadActiveAsync(Guid ns, CancellationToken token = default);
    Task<bool> HasNewInputsAsync(Guid ns, Guid generationId, CancellationToken token = default);
    Task<Guid?> FindBuildingAsync(Guid ns, RuntimeProjectionVersions versions, CancellationToken token = default);
}

public sealed class RebuildRuntimeProjection(IRuntimeProjectionStore store, IRawCaptureStore archive,
    IContextualRuntimeNormalizer lifecycle, IRuntimeEvidenceNormalizer evidence, IRuntimeRequestContextDecoder requests,
    IRuntimeHealthDecoder health, IRuntimeReconciliationDecoder reconciliation)
{
    public RuntimeProjectionVersions Versions => new(lifecycle.Version, evidence.Version, "codex.health/1", RuntimeReconciliationPolicy.Version, "runtime.projection/1");

    public async Task<RuntimeProjectionGeneration> ExecuteAsync(Guid id, CancellationToken token = default)
    {
        var generation = await store.ReadGenerationAsync(id, token) ?? throw new ArgumentException("Unknown generation.");
        if (generation.Versions != Versions) throw new InvalidOperationException("Pinned generation versions do not match this binary; use its matching binary or create a new generation.");
        if (generation.State == "Ready") return generation;
        var inputs = await store.ReadInputsAsync(id, token);
        var included = inputs.Select(x => x.CaptureId).ToHashSet();
        var linked = new Dictionary<Guid, VerifiedRuntimeRequest?>();
        // Resolve only within the frozen manifest. A context arriving tomorrow cannot change this generation.
        foreach (var input in inputs.Where(x => x.Source.Provenance == "request_context").OrderBy(x => x.CaptureId))
        {
            var capture = (await archive.ReadRuntimeAsync(input.CaptureId, token))!.Envelope;
            var context = requests.Read(capture);
            if (context is null || !included.Contains(context.RequestCaptureId) || !included.Contains(context.ResponseCaptureId)) continue;
            var sent = (await archive.ReadRuntimeAsync(context.RequestCaptureId, token))!.Envelope;
            var received = (await archive.ReadRuntimeAsync(context.ResponseCaptureId, token))!.Envelope;
            var verified = requests.Decode(capture, sent, received);
            if (verified is null) continue;
            if (linked.TryGetValue(verified.ResponseCaptureId, out var previous))
            {
                // Multiple contradictory context records never choose an arbitrary owner.
                if (previous is null || previous with { ContextCaptureId = verified.ContextCaptureId } != verified) linked[verified.ResponseCaptureId] = null;
            }
            else linked.Add(verified.ResponseCaptureId, verified);
        }
        foreach (var input in inputs.Where(x => !x.Complete).OrderBy(x => x.CaptureId))
        {
            token.ThrowIfCancellationRequested();
            var capture = (await archive.ReadRuntimeAsync(input.CaptureId, token))!.Envelope;
            var context = linked.GetValueOrDefault(input.CaptureId);
            var needsContext = context is null && (capture.Source.Provenance == "history_response" || lifecycle.RequiresRequestContext(capture));
            var life = needsContext ? new RuntimeNormalization("pending", "missing_or_conflicting_frozen_context", []) : lifecycle.Normalize(capture, context);
            var batch = needsContext ? new RuntimeEvidenceBatch(capture.CaptureId, evidence.Version, "pending", "missing_or_conflicting_frozen_context", []) : evidence.Normalize(capture, context);
            var diagnostic = capture.Source.Provenance == "inventory" ? health.Decode(capture) : null;
            if (diagnostic?.IsSuccess == true && diagnostic.Value!.DetectorVersion != generation.Versions.Health) throw new InvalidOperationException("Health decoder version mismatch.");
            await store.SaveResultAsync(id, new(capture.CaptureId, life, batch, diagnostic?.IsSuccess == true ? diagnostic.Value : null,
                reconciliation.Decode(capture), needsContext ? "pending_context" : null), token);
        }
        return await store.CompleteAsync(id, RuntimeProjectionProjector.Project(generation, inputs, await store.ReadResultsAsync(id, token)), token);
    }
}

public static class RuntimeProjectionProjector
{
    public static RuntimeProjectionDocument Project(RuntimeProjectionGeneration generation, IReadOnlyList<RuntimeProjectionInput> inputs,
        IReadOnlyList<RuntimeProjectionInputResult> results)
    {
        var batches = results.Select(x => x.Evidence).ToArray();
        var topology = RuntimeAgentProjector.Project(batches);
        var lifecycle = results.SelectMany(x => x.Lifecycle.Facts).ToArray();
        var turns = lifecycle.Where(x => x.Kind == "turn" && x.TurnId is not null).Select(x => (x.ThreadId, TurnId: x.TurnId!)).Distinct()
            .OrderBy(x => x.ThreadId, StringComparer.Ordinal).ThenBy(x => x.TurnId, StringComparer.Ordinal)
            .Select(x => RuntimeTurnProjector.Project(generation.RuntimeNamespaceId, x.ThreadId, x.TurnId, generation.Versions.Lifecycle, lifecycle)!).ToArray();
        var source = inputs.ToDictionary(x => x.CaptureId, x => x.Source.SourceId);
        return new(generation.RuntimeNamespaceId, "KEEP_GATE_BLOCKED", "Partial", turns,
            RuntimeRecoveryProjector.Apply(RuntimeActivityProjector.Project(batches), topology), topology,
            // Include every diagnostic, ordered by capture identity. Freshness is a read-time concern, not replay state.
            results.Select(x => x.Health).OfType<RuntimeSourceHealth>().OrderBy(x => x.CaptureId).ToArray(),
            results.Where(x => x.Reconciliation is not null).OrderBy(x => x.CaptureId)
                .Select(x => new RuntimeProjectionSourceInput(x.CaptureId, source[x.CaptureId], x.Reconciliation!)).ToArray(),
            results.OrderBy(x => x.CaptureId).Select(x => new RuntimeProjectionInputOutcome(x.CaptureId, x.Lifecycle.Status, x.Evidence.Outcome, x.Limitation)).ToArray());
    }
}
