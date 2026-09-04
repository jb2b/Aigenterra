using Aigenterra.Domain.Executions;

namespace Aigenterra.Domain.Tests;

/// <summary>
/// Verifies typed execution identities and runtime-reference invariants.
/// </summary>
public sealed class ExecutionIdentityTests
{
    [Fact]
    public void Typed_ids_reject_empty_guids()
    {
        Assert.Throws<ArgumentException>(() => new ConversationId(Guid.Empty));
        Assert.Throws<ArgumentException>(() => new TaskExecutionId(Guid.Empty));
        Assert.Throws<ArgumentException>(() => new AgentExecutionId(Guid.Empty));
        Assert.Throws<ArgumentException>(() => new ObservationId(Guid.Empty));
    }

    [Fact]
    public void New_typed_ids_are_non_empty_and_unique()
    {
        var first = TaskExecutionId.New();
        var second = TaskExecutionId.New();

        Assert.NotEqual(Guid.Empty, first.Value);
        Assert.NotEqual(first, second);
    }

    [Theory]
    [InlineData("", "turn", "42")]
    [InlineData("codex", " ", "42")]
    [InlineData("codex", "turn", "")]
    public void Runtime_reference_requires_all_parts(string runtime, string resourceKind, string externalId)
    {
        Assert.Throws<ArgumentException>(() => new RuntimeReference(runtime, resourceKind, externalId));
    }
}
