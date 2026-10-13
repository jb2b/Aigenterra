using Aigenterra.Application.Runtime;

namespace Aigenterra.Application.Tests;

public sealed class RuntimeTurnProjectionTests
{
    [Fact]
    public void Explicit_interruption_overrides_hook_stop_without_losing_provenance_or_crossing_namespaces()
    {
        var scope = Guid.NewGuid();
        var stop = Fact(scope, "Completed", "Hook", "Inferred");
        var interrupted = Fact(scope, "Interrupted", "Runtime", "Observed");
        var unrelated = Fact(Guid.NewGuid(), "Failed", "Runtime", "Observed");
        var result = RuntimeTurnProjector.Project(scope, "child", "turn", "v1", [stop, interrupted, unrelated, interrupted])!;
        Assert.Equal("Interrupted", result.Status);
        Assert.False(result.Conflict);
        Assert.Equal("Child", result.ThreadScope);
        Assert.Equal("root", result.ConversationThreadId);
        Assert.Equal("Partial", result.Coverage);
        Assert.Equal(2, result.Evidence.Count);
        var shuffled = RuntimeTurnProjector.Project(scope, "child", "turn", "v1", [unrelated, interrupted, stop])!;
        Assert.Equal(result.Status, shuffled.Status);
        Assert.Equal(result.Evidence, shuffled.Evidence);
    }

    [Fact]
    public void Equal_authority_terminal_conflicts_remain_unknown_and_history_is_not_live()
    {
        var scope = Guid.NewGuid();
        var completed = Fact(scope, "Completed", "Runtime", "Reconciled");
        var history = RuntimeTurnProjector.Project(scope, "child", "turn", "v1", [completed])!;
        Assert.Equal("Reconciled", history.Derivation);
        Assert.Equal("Partial", history.Coverage);
        var conflict = RuntimeTurnProjector.Project(scope, "child", "turn", "v1", [completed, Fact(scope, "Failed", "Runtime", "Observed")])!;
        Assert.Equal("TerminalUnknown", conflict.Status);
        Assert.True(conflict.Conflict);
        Assert.Equal(2, conflict.Evidence.Count);
        Assert.Null(RuntimeTurnProjector.Project(scope, "child", "different-turn", "v1", [completed]));
    }

    private static RuntimeFact Fact(Guid scope, string status, string authority, string derivation) =>
        new(Guid.NewGuid(), 0, scope, "turn", "child", "turn", status, "Child", "root", authority, derivation, null, null, []);
}
