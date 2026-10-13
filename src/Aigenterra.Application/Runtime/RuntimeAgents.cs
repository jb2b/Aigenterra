namespace Aigenterra.Application.Runtime;

public sealed record RuntimeAgent(string Id, Guid RuntimeNamespaceId, string ThreadId, string LifetimeState,
    string? Nickname, string? Role, string? Model, bool MetadataConflict, IReadOnlyList<RuntimeEvidenceReference> Evidence);
public sealed record RuntimeExecutionSegment(string Id, Guid RuntimeNamespaceId, string ThreadId, string TurnId,
    string Status, string? ParentTaskTurnId, string Assignment, string Coverage, bool Conflict, IReadOnlyList<RuntimeEvidenceReference> Evidence);
public sealed record RuntimeAgentRelation(string Id, Guid RuntimeNamespaceId, string ParentThreadId, string ChildThreadId,
    string? ParentTurnId, string? ChildTurnId, string Kind, string? CallId, IReadOnlyList<RuntimeEvidenceReference> Evidence);
public sealed record RuntimeAgentTopology(IReadOnlyList<RuntimeAgent> Agents, IReadOnlyList<RuntimeExecutionSegment> Segments,
    IReadOnlyList<RuntimeAgentRelation> Relations);

public static class RuntimeAgentProjector
{
    public static RuntimeAgentTopology Project(IEnumerable<RuntimeEvidenceBatch> batches)
    {
        var facts = batches.SelectMany(b => b.Facts.Select(f => (Fact: f, Reference: new RuntimeEvidenceReference(f.CaptureId, b.NormalizerVersion, f.Ordinal)))).ToArray();
        var agents = facts.Where(x => x.Fact.Kind == "agent").GroupBy(x => (x.Fact.RuntimeNamespaceId, x.Fact.ThreadId)).Select(group =>
        {
            var names = Values("nickname"); var roles = Values("role"); var models = Values("model");
            return new RuntimeAgent(RuntimeIdentity.Key(group.Key.RuntimeNamespaceId.ToString(), group.Key.ThreadId), group.Key.RuntimeNamespaceId,
                group.Key.ThreadId, "Unknown", names.Length == 1 ? names[0] : null, roles.Length == 1 ? roles[0] : null, models.Length == 1 ? models[0] : null,
                names.Length > 1 || roles.Length > 1 || models.Length > 1, References(group.Select(x => x.Reference)));
            string[] Values(string key) => group.Select(x => x.Fact.Values.GetValueOrDefault(key)).OfType<string>().Distinct(StringComparer.Ordinal).ToArray();
        }).OrderBy(x => x.Id, StringComparer.Ordinal).ToArray();
        var segments = facts.Where(x => x.Fact.Kind == "segment" && x.Fact.TurnId is not null)
            .GroupBy(x => (x.Fact.RuntimeNamespaceId, x.Fact.ThreadId, TurnId: x.Fact.TurnId!)).Select(group =>
            {
                var terminal = group.Where(x => x.Fact.Status != "Running").ToArray();
                var candidates = terminal.Length > 0 ? terminal : group.ToArray();
                if (candidates.Any(x => x.Fact.Authority == "Runtime")) candidates = candidates.Where(x => x.Fact.Authority == "Runtime").ToArray();
                var states = candidates.Select(x => x.Fact.Status).Distinct(StringComparer.Ordinal).ToArray();
                return new RuntimeExecutionSegment(RuntimeIdentity.Key(group.Key.RuntimeNamespaceId.ToString(), group.Key.ThreadId, group.Key.TurnId),
                    group.Key.RuntimeNamespaceId, group.Key.ThreadId, group.Key.TurnId, states.Length == 1 ? states[0] : "TerminalUnknown",
                    null, "Unknown", "Partial", states.Length > 1, References(group.Select(x => x.Reference)));
            }).OrderBy(x => x.Id, StringComparer.Ordinal).ToArray();
        var relations = facts.Where(x => x.Fact.Kind == "relation" && x.Fact.Values.GetValueOrDefault("childThreadId") is not null)
            .GroupBy(x => RuntimeIdentity.Key(x.Fact.RuntimeNamespaceId.ToString(), x.Fact.ThreadId, x.Fact.TurnId ?? "",
                x.Fact.Values["childThreadId"]!, x.Fact.Values.GetValueOrDefault("relationKind") ?? "Unknown", x.Fact.ExternalId ?? ""))
            .Select(group =>
            {
                var first = group.First().Fact;
                return new RuntimeAgentRelation(group.Key, first.RuntimeNamespaceId, first.ThreadId, first.Values["childThreadId"]!, first.TurnId, null,
                    first.Values.GetValueOrDefault("relationKind") ?? "Unknown", first.ExternalId, References(group.Select(x => x.Reference)));
            }).OrderBy(x => x.Id, StringComparer.Ordinal).ToArray();
        return new(agents, segments, relations);
    }

    private static RuntimeEvidenceReference[] References(IEnumerable<RuntimeEvidenceReference> references) => references.Distinct()
        .OrderBy(x => x.CaptureId).ThenBy(x => x.NormalizerVersion, StringComparer.Ordinal).ThenBy(x => x.Ordinal).ToArray();
}
