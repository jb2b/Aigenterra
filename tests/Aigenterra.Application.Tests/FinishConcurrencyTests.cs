using Aigenterra.Application.Executions;
using Aigenterra.Domain.Executions;

namespace Aigenterra.Application.Tests;

public sealed class FinishConcurrencyTests
{
    [Theory]
    [InlineData(false, "completed")]
    [InlineData(false, "failed")]
    [InlineData(true, "completed")]
    [InlineData(true, "failed")]
    public async Task A_finish_that_loses_during_persistence_reloads_the_winning_state(bool agent, string requestedOutcome)
    {
        var now = new DateTimeOffset(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);
        var store = new TestExecutionStore();
        var conversation = new Conversation(ConversationId.New(), now);
        var task = new TaskExecution(TaskExecutionId.New(), conversation.Id, "Race", now);
        var participation = new AgentExecution(AgentExecutionId.New(), task.Id, null, now);
        await store.AddConversationAsync(conversation);
        await store.AddTaskExecutionAsync(task);
        await store.AddAgentExecutionAsync(participation);
        var winnerEnd = now.AddTicks(1);
        var requestedEnd = now.AddTicks(2);

        if (agent)
        {
            store.BeforeAgentFinish = () =>
            {
                participation.Finish(ExecutionStatus.Completed, winnerEnd);
                Assert.True(store.TryFinishAgentExecutionAsync(participation).Result);
            };
            var result = await new FinishAgentExecution(store, new TestTimeProvider(now)).ExecuteAsync(
                new(task.Id.Value, participation.Id.Value, requestedOutcome, requestedEnd));
            Assert.Equal(requestedOutcome == "completed", result.IsSuccess);
            if (result.IsSuccess) { Assert.Equal(winnerEnd, result.Value!.EndedAt); }
            else { Assert.Equal("conflicting_terminal_status", result.Error!.Code); }
            Assert.Equal(winnerEnd, (await store.FindAgentExecutionAsync(participation.Id))!.EndedAt);
        }
        else
        {
            store.BeforeTaskFinish = () =>
            {
                task.Finish(ExecutionStatus.Completed, winnerEnd);
                Assert.True(store.TryFinishTaskExecutionAsync(task).Result);
            };
            var result = await new FinishTaskExecution(store, new TestTimeProvider(now)).ExecuteAsync(
                new(task.Id.Value, requestedOutcome, requestedEnd));
            Assert.Equal(requestedOutcome == "completed", result.IsSuccess);
            if (result.IsSuccess) { Assert.Equal(winnerEnd, result.Value!.EndedAt); }
            else { Assert.Equal("conflicting_terminal_status", result.Error!.Code); }
            Assert.Equal(winnerEnd, (await store.FindTaskExecutionAsync(task.Id))!.EndedAt);
        }
    }
}
