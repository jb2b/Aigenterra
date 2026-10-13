using Aigenterra.Application.Runtime;

namespace Aigenterra.Application.Tests;

public sealed class RuntimeReconciliationTests
{
    [Fact]
    public void Targets_include_only_selected_hook_roots_and_their_explicit_descendants()
    {
        var ns = Guid.NewGuid(); var hook = Guid.NewGuid(); var reader = Guid.NewGuid(); var unrelated = Guid.NewGuid();
        var enrollment = new RuntimeEnrollment(new string('A', 64), [hook], ["selected"]);
        ScopedRuntimeEvidence[] inputs =
        [
            new(hook, "hook", Batch(Fact("agent", "hookRoot", null, "Unknown", "Hook"))),
            new(unrelated, "hook", Batch(Fact("agent", "unrelatedHook", null, "Unknown", "Hook"))),
            new(reader, "app_server", Batch(Fact("relation", "hookRoot", null, "Unknown", "Runtime", "child"),
                Fact("relation", "child", null, "Unknown", "Runtime", "grandchild"),
                Fact("relation", "unselectedHistory", null, "Unknown", "Runtime", "unrelatedChild"),
                Fact("segment", "selected", "done", "Completed", "Runtime")))
        ];
        var targets = RuntimeReconciliationPolicy.Targets(enrollment, reader, inputs);
        Assert.Equal(new[] { "child", "grandchild", "hookRoot", "selected" }, targets.Select(t => t.ThreadId));
        Assert.False(Assert.Single(targets, t => t.ThreadId == "selected").ActiveOrUnresolved);
        Assert.Equal(TimeSpan.FromSeconds(5), RuntimeReconciliationPolicy.Interval(true));
        Assert.Equal(TimeSpan.FromSeconds(60), RuntimeReconciliationPolicy.Interval(false));
        RuntimeEvidenceFact Fact(string kind, string thread, string? turn, string status, string authority, string? child = null) =>
            new(Guid.NewGuid(), 0, ns, kind, thread, turn, null, status, "Reconciled", authority,
                new Dictionary<string, string?> { ["childThreadId"] = child, ["relationKind"] = "parent_child" }, []);
    }

    [Fact]
    public void Interrupted_turn_does_not_invent_tool_success_or_override_late_completed_tool()
    {
        var ns = Guid.NewGuid();
        var topology = new RuntimeAgentTopology([], [new("segment", ns, "child", "turn", "Interrupted", null, "Unknown", "Partial", false, [])], []);
        RuntimeActivity Activity(string status) => new(status, ns, "child", "turn", "call", "mcpToolCall", "server", "tool", status, "Explicit", "Partial", false, ["Reconciled"], []);
        var results = RuntimeRecoveryProjector.Apply([Activity("Requested"), Activity("Completed"), Activity("Failed")], topology);
        Assert.Equal(new[] { "TerminalUnknown", "Completed", "Failed" }, results.Select(x => x.Status));
        Assert.Equal("Interrupted", topology.Segments[0].Status);
    }
    private static RuntimeEvidenceBatch Batch(params RuntimeEvidenceFact[] facts) => new(Guid.NewGuid(), "codex.evidence/2", "normalized", null, facts);
}
