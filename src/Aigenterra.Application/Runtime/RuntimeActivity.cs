using Aigenterra.Application.Recording;

namespace Aigenterra.Application.Runtime;

public sealed record RuntimeEvidenceReference(Guid CaptureId, string NormalizerVersion, int Ordinal);
public sealed record RuntimeEvidenceField(string Field, Guid CaptureId, string Pointer, string Derivation);
public sealed record RuntimeEvidenceFact(Guid CaptureId, int Ordinal, Guid RuntimeNamespaceId,
    string Kind, string ThreadId, string? TurnId, string? ExternalId, string Status, string Derivation,
    string Authority, IReadOnlyDictionary<string, string?> Values, IReadOnlyList<RuntimeEvidenceField> Fields);
public sealed record RuntimeEvidenceBatch(Guid CaptureId, string NormalizerVersion, string Outcome, string? Reason,
    IReadOnlyList<RuntimeEvidenceFact> Facts);
public interface IRuntimeEvidenceNormalizer
{
    string Version { get; }
    RuntimeEvidenceBatch Normalize(RuntimeCaptureEnvelope capture, VerifiedRuntimeRequest? request);
}
public interface IRuntimeEvidenceStore
{
    Task AppendAsync(RuntimeEvidenceBatch batch, CancellationToken token = default);
    Task<IReadOnlyList<RuntimeEvidenceBatch>> ReadAsync(Guid runtimeNamespaceId, string normalizerVersion, CancellationToken token = default);
}
public sealed record RuntimeActivity(string Id, Guid RuntimeNamespaceId, string ThreadId, string? TurnId,
    string? ExternalId, string Kind, string? Server, string? Tool, string Status, string Identity,
    string Coverage, bool Conflict, IReadOnlyList<string> Derivations, IReadOnlyList<RuntimeEvidenceReference> Evidence);

public static class RuntimeActivityProjector
{
    public static IReadOnlyList<RuntimeActivity> Project(IEnumerable<RuntimeEvidenceBatch> batches)
    {
        var facts = batches.SelectMany(b => b.Facts.Where(f => f.Kind == "activity").Select(f => (Fact: f, Version: b.NormalizerVersion)));
        return facts.GroupBy(x => Key(x.Fact)).Select(group =>
        {
            var all = group.ToArray();
            var first = all[0].Fact;
            var terminal = all.Where(x => x.Fact.Status is "Completed" or "Failed" or "Interrupted" or "TerminalUnknown").ToArray();
            var candidates = terminal.Length > 0 ? terminal : all;
            if (candidates.Any(x => x.Fact.Authority == "Runtime")) candidates = candidates.Where(x => x.Fact.Authority == "Runtime").ToArray();
            var statuses = candidates.Select(x => x.Fact.Status).Distinct(StringComparer.Ordinal).ToArray();
            var kinds = Values("activityKind");
            var servers = Values("server");
            var tools = Values("tool");
            var conflict = (terminal.Length > 0 && statuses.Length > 1) || kinds.Length > 1 || servers.Length > 1 || tools.Length > 1;
            var status = statuses.Length == 1 ? statuses[0] : terminal.Length > 0 ? "TerminalUnknown"
                : statuses.Contains("Running", StringComparer.Ordinal) ? "Running"
                : statuses.Contains("ResultObserved", StringComparer.Ordinal) ? "ResultObserved" : "Requested";
            return new RuntimeActivity(group.Key, first.RuntimeNamespaceId, first.ThreadId, first.TurnId, first.ExternalId,
                kinds.Length == 1 ? kinds[0] : "Unknown", servers.Length == 1 ? servers[0] : null,
                tools.Length == 1 ? tools[0] : null, status,
                first.ExternalId is not null && first.TurnId is not null ? "Explicit" : "Unresolved", "Partial", conflict,
                all.Select(x => x.Fact.Derivation).Distinct().Order(StringComparer.Ordinal).ToArray(),
                all.Select(x => new RuntimeEvidenceReference(x.Fact.CaptureId, x.Version, x.Fact.Ordinal)).Distinct()
                    .OrderBy(x => x.CaptureId).ThenBy(x => x.NormalizerVersion, StringComparer.Ordinal).ThenBy(x => x.Ordinal).ToArray());
            string[] Values(string key) => all.Select(x => x.Fact.Values.GetValueOrDefault(key)).OfType<string>()
                .Where(x => key != "activityKind" || x != "Unknown").Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        }).OrderBy(x => x.Id, StringComparer.Ordinal).ToArray();
    }

    private static string Key(RuntimeEvidenceFact fact) => fact.ExternalId is not null && fact.TurnId is not null
        ? RuntimeIdentity.Key(fact.RuntimeNamespaceId.ToString(), fact.ThreadId, fact.TurnId, fact.ExternalId)
        : RuntimeIdentity.Key(fact.RuntimeNamespaceId.ToString(), fact.CaptureId.ToString(), fact.Ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture));
}
