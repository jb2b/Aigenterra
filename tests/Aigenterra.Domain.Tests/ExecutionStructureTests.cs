using Aigenterra.Domain.Executions;

namespace Aigenterra.Domain.Tests;

/// <summary>Verifies aggregate relationships and observation invariants.</summary>
public sealed class ExecutionStructureTests
{
    [Fact]
    public void Agent_execution_cannot_be_its_own_parent()
    {
        var agentId = AgentExecutionId.New();

        Assert.Throws<ArgumentException>(() => new AgentExecution(
            agentId,
            TaskExecutionId.New(),
            agentId,
            DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Task_execution_requires_an_objective()
    {
        Assert.Throws<ArgumentException>(() => new TaskExecution(
            TaskExecutionId.New(),
            ConversationId.New(),
            " ",
            DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Observation_may_be_task_wide_or_agent_specific()
    {
        var taskId = TaskExecutionId.New();
        var taskObservation = CreateObservation(taskId, null, ObservationKind.Message);
        var agentId = AgentExecutionId.New();
        var agentObservation = CreateObservation(taskId, agentId, ObservationKind.ToolInvocation);

        Assert.Null(taskObservation.AgentExecutionId);
        Assert.Equal(agentId, agentObservation.AgentExecutionId);
    }

    [Theory]
    [InlineData(ObservationKind.Message)]
    [InlineData(ObservationKind.ToolInvocation)]
    [InlineData(ObservationKind.ToolResult)]
    [InlineData(ObservationKind.StatusTransition)]
    [InlineData(ObservationKind.Error)]
    [InlineData(ObservationKind.Approval)]
    [InlineData(ObservationKind.TestExecution)]
    [InlineData(ObservationKind.Measurement)]
    public void Observation_accepts_each_normalized_kind(ObservationKind kind)
    {
        var observation = CreateObservation(TaskExecutionId.New(), null, kind);

        Assert.Equal(kind, observation.Kind);
    }

    [Fact]
    public void Observation_requires_a_summary_and_a_defined_kind()
    {
        Assert.Throws<ArgumentException>(() => new Observation(
            ObservationId.New(),
            TaskExecutionId.New(),
            null,
            DateTimeOffset.UtcNow,
            ObservationKind.Message,
            " "));

        Assert.Throws<ArgumentOutOfRangeException>(() => CreateObservation(
            TaskExecutionId.New(),
            null,
            (ObservationKind)999));
    }

    private static Observation CreateObservation(
        TaskExecutionId taskId,
        AgentExecutionId? agentId,
        ObservationKind kind) =>
        new(
            ObservationId.New(),
            taskId,
            agentId,
            DateTimeOffset.UtcNow,
            kind,
            "Observed activity");
}
