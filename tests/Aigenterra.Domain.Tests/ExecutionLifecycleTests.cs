using Aigenterra.Domain.Executions;

namespace Aigenterra.Domain.Tests;

public sealed class ExecutionLifecycleTests
{
    private static readonly DateTimeOffset StartedAt = new(2026, 9, 4, 10, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(ExecutionStatus.Completed)]
    [InlineData(ExecutionStatus.Failed)]
    [InlineData(ExecutionStatus.Interrupted)]
    public void Task_execution_can_finish_with_a_terminal_status(ExecutionStatus finalStatus)
    {
        var execution = CreateTaskExecution();

        var result = execution.Finish(finalStatus, StartedAt.AddMinutes(1));

        Assert.Equal(ExecutionFinishResult.Applied, result);
        Assert.Equal(finalStatus, execution.Status);
        Assert.Equal(StartedAt.AddMinutes(1), execution.EndedAt);
    }

    [Fact]
    public void Repeating_the_same_terminal_status_is_idempotent_and_keeps_the_first_end_time()
    {
        var execution = CreateTaskExecution();
        var firstEnd = StartedAt.AddMinutes(1);
        execution.Finish(ExecutionStatus.Completed, firstEnd);

        var result = execution.Finish(ExecutionStatus.Completed, StartedAt.AddMinutes(2));

        Assert.Equal(ExecutionFinishResult.AlreadyApplied, result);
        Assert.Equal(firstEnd, execution.EndedAt);
    }

    [Fact]
    public void A_terminal_status_cannot_be_replaced_by_another_terminal_status()
    {
        var execution = CreateTaskExecution();
        execution.Finish(ExecutionStatus.Completed, StartedAt.AddMinutes(1));

        var result = execution.Finish(ExecutionStatus.Failed, StartedAt.AddMinutes(2));

        Assert.Equal(ExecutionFinishResult.ConflictingTerminalStatus, result);
        Assert.Equal(ExecutionStatus.Completed, execution.Status);
    }

    [Fact]
    public void Finish_rejects_running_and_an_end_before_start()
    {
        var execution = CreateTaskExecution();

        Assert.Equal(
            ExecutionFinishResult.InvalidStatus,
            execution.Finish(ExecutionStatus.Running, StartedAt.AddMinutes(1)));
        Assert.Equal(
            ExecutionFinishResult.EndedBeforeStarted,
            execution.Finish(ExecutionStatus.Completed, StartedAt.AddTicks(-1)));
        Assert.Equal(ExecutionStatus.Running, execution.Status);
    }

    [Fact]
    public void Agent_execution_uses_the_same_lifecycle_rules()
    {
        var execution = new AgentExecution(
            AgentExecutionId.New(),
            TaskExecutionId.New(),
            null,
            StartedAt);

        var result = execution.Finish(ExecutionStatus.Interrupted, StartedAt.AddSeconds(5));

        Assert.Equal(ExecutionFinishResult.Applied, result);
        Assert.Equal(ExecutionStatus.Interrupted, execution.Status);
    }

    private static TaskExecution CreateTaskExecution() =>
        new(
            TaskExecutionId.New(),
            ConversationId.New(),
            "Inspect the execution",
            StartedAt);
}
