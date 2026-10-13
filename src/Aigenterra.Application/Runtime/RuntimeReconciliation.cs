using Aigenterra.Application.Recording;

namespace Aigenterra.Application.Runtime;

public sealed record RuntimeEnrollment(string Fingerprint, IReadOnlyList<Guid> HookSourceIds, IReadOnlyList<string> ThreadIds);
public sealed record RuntimeReconciliationInput(int Version, string Kind, DateTimeOffset ObservedAt, string Outcome,
    string? ThreadId = null, Guid? CycleId = null, string? NextCursor = null, RuntimeEnrollment? Enrollment = null);
public sealed record RuntimeReconciliationRecord(Guid CaptureId, Guid RuntimeNamespaceId, Guid SourceId, long IngestPosition, RuntimeReconciliationInput Input);
public sealed record ScopedRuntimeEvidence(Guid SourceId, string SourceKind, RuntimeEvidenceBatch Batch);
public sealed record RuntimeReconciliationHealth(string ReaderState, string OwnerState, string Coverage, string Gate,
    RuntimeReconciliationRecord? Latest, long GapCount, IReadOnlyList<RuntimeReconciliationRecord> RecentGaps);
public sealed record RuntimeReconciliationTarget(string ThreadId, bool ActiveOrUnresolved, string EvidenceKey);
public sealed record RuntimeReconciliationTargets(string EnrollmentFingerprint, IReadOnlyList<RuntimeReconciliationTarget> Targets, string? Next, string Coverage);
public interface IRuntimeReconciliationDecoder
{
    RuntimeReconciliationInput? Decode(RuntimeCaptureEnvelope capture);
}
public interface IRuntimeReconciliationStore
{
    Task AppendAsync(RuntimeReconciliationRecord record, CancellationToken token = default);
    Task<RuntimeEnrollment?> ReadEnrollmentAsync(Guid runtimeNamespaceId, Guid sourceId, CancellationToken token = default);
    Task<IReadOnlyList<ScopedRuntimeEvidence>> ReadEvidenceAsync(Guid runtimeNamespaceId, IReadOnlyList<Guid> sources, CancellationToken token = default);
    Task<RuntimeReconciliationHealth?> ReadHealthAsync(Guid runtimeNamespaceId, Guid sourceId, CancellationToken token = default);
}
public sealed class GetRuntimeReconciliationTargets(IRuntimeReconciliationStore store)
{
    public async Task<RuntimeReconciliationTargets?> ExecuteAsync(Guid ns, Guid source, string? after, int limit, CancellationToken token = default)
    {
        if (ns == Guid.Empty || source == Guid.Empty || limit is < 1 or > 500 || after?.Length > 512) throw new ArgumentException("Invalid target page.");
        var enrollment = await store.ReadEnrollmentAsync(ns, source, token);
        if (enrollment is null) return null;
        var evidence = await store.ReadEvidenceAsync(ns, enrollment.HookSourceIds.Append(source).Distinct().ToArray(), token);
        var targets = RuntimeReconciliationPolicy.Targets(enrollment, source, evidence);
        var page = targets.Where(t => after is null || string.CompareOrdinal(t.ThreadId, after) > 0).Take(limit + 1).ToArray();
        return new(enrollment.Fingerprint, page.Take(limit).ToArray(), page.Length > limit ? page[limit - 1].ThreadId : null, "Partial");
    }
}

public static class RuntimeReconciliationPolicy
{
    public const string Version = "runtime.reconciliation/1";
    public static IReadOnlyList<RuntimeReconciliationTarget> Targets(RuntimeEnrollment enrollment, Guid readerSource, IReadOnlyList<ScopedRuntimeEvidence> inputs)
    {
        var selected = new HashSet<string>(enrollment.ThreadIds, StringComparer.Ordinal);
        var allowed = inputs.Where(x => x.SourceId == readerSource || enrollment.HookSourceIds.Contains(x.SourceId)).ToArray();
        foreach (var input in allowed.Where(x => x.SourceKind == "hook" && enrollment.HookSourceIds.Contains(x.SourceId)))
            foreach (var fact in input.Batch.Facts.Where(f => f.Authority == "Hook")) selected.Add(fact.ThreadId);
        var batches = allowed.Select(x => x.Batch).ToArray();
        var topology = RuntimeAgentProjector.Project(batches);
        bool changed;
        do
        {
            changed = false;
            foreach (var relation in topology.Relations)
                if (selected.Contains(relation.ParentThreadId)) changed |= selected.Add(relation.ChildThreadId);
        } while (changed);
        var activities = RuntimeRecoveryProjector.Apply(RuntimeActivityProjector.Project(batches), topology);
        return selected.Order(StringComparer.Ordinal).Select(thread =>
        {
            var segments = topology.Segments.Where(s => s.ThreadId == thread).ToArray();
            var work = activities.Where(a => a.ThreadId == thread).ToArray();
            var active = segments.Length == 0 || segments.Any(s => s.Status is "Running" or "TerminalUnknown")
                || work.Any(a => a.Status is "Requested" or "Running" or "ResultObserved" or "TerminalUnknown");
            // Only changed semantic evidence wakes an idle target. Duplicate deliveries do not alter this hash.
            var key = RuntimeIdentity.Key(segments.OrderBy(s => s.Id, StringComparer.Ordinal).Select(s => s.Id + ":" + s.Status)
                .Concat(work.OrderBy(a => a.Id, StringComparer.Ordinal).Select(a => a.Id + ":" + a.Status)).ToArray());
            return new RuntimeReconciliationTarget(thread, active, key);
        }).ToArray();
    }
    public static TimeSpan Interval(bool activeOrUnresolved) => TimeSpan.FromSeconds(activeOrUnresolved ? 5 : 60);
}

public static class RuntimeRecoveryProjector
{
    public static IReadOnlyList<RuntimeActivity> Apply(IReadOnlyList<RuntimeActivity> activities, RuntimeAgentTopology topology) => activities.Select(activity =>
    {
        var interrupted = topology.Segments.Any(segment => segment.RuntimeNamespaceId == activity.RuntimeNamespaceId
            && segment.ThreadId == activity.ThreadId && segment.TurnId == activity.TurnId && segment.Status == "Interrupted");
        return interrupted && activity.Status is "Requested" or "Running" or "ResultObserved"
            ? activity with { Status = "TerminalUnknown" } : activity;
    }).ToArray();
}
