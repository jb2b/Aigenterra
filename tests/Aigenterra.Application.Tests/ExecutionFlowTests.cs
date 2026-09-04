using System.Text;
using Aigenterra.Application.Common;
using Aigenterra.Application.Executions;

namespace Aigenterra.Application.Tests;

/// <summary>Verifies execution use cases across complete and exceptional application flows.</summary>
public sealed class ExecutionFlowTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Complete_execution_flow_produces_an_ordered_timeline_without_an_experiment()
    {
        var store = new TestExecutionStore();
        var clock = new TestTimeProvider(Now);
        var conversation = await new CreateConversation(store, clock).ExecuteAsync(
            new CreateConversationCommand(null, Runtime("thread", "conversation-1")));
        var task = await new StartTaskExecution(store, clock).ExecuteAsync(
            new StartTaskExecutionCommand(
                conversation.Value!.ConversationId,
                "Investigate the failing build",
                Now.AddMinutes(1),
                Runtime("turn", "task-1")));
        var rootAgent = await new StartAgentExecution(store, clock).ExecuteAsync(
            new StartAgentExecutionCommand(
                task.Value!.TaskExecutionId,
                null,
                Now.AddMinutes(1),
                Runtime("agent", "root")));
        var childAgent = await new StartAgentExecution(store, clock).ExecuteAsync(
            new StartAgentExecutionCommand(
                task.Value.TaskExecutionId,
                rootAgent.Value!.AgentExecutionId,
                Now.AddMinutes(2),
                Runtime("agent", "explorer")));

        var laterObserved = await Record(
            store,
            clock,
            task.Value.TaskExecutionId,
            childAgent.Value!.AgentExecutionId,
            Now.AddMinutes(5),
            "toolResult",
            "Build completed",
            true);
        var earlierObserved = await Record(
            store,
            clock,
            task.Value.TaskExecutionId,
            rootAgent.Value.AgentExecutionId,
            Now.AddMinutes(3),
            "toolInvocation",
            "Started the build",
            false);

        await new FinishAgentExecution(store, clock).ExecuteAsync(
            new FinishAgentExecutionCommand(
                task.Value.TaskExecutionId,
                childAgent.Value.AgentExecutionId,
                "completed",
                Now.AddMinutes(6)));
        await new FinishTaskExecution(store, clock).ExecuteAsync(
            new FinishTaskExecutionCommand(
                task.Value.TaskExecutionId,
                "completed",
                Now.AddMinutes(7)));

        var lateObservation = await Record(
            store,
            clock,
            task.Value.TaskExecutionId,
            rootAgent.Value.AgentExecutionId,
            Now.AddMinutes(4),
            "message",
            "Delayed runtime message",
            false);

        var result = await new GetTaskExecutionTimeline(store).ExecuteAsync(
            new GetTaskExecutionTimelineQuery(task.Value.TaskExecutionId));

        Assert.True(result.IsSuccess);
        Assert.Equal("completed", result.Value!.Status);
        Assert.Equal(2, result.Value.Agents.Count);
        Assert.Equal(rootAgent.Value.AgentExecutionId, result.Value.Agents[1].ParentAgentExecutionId);
        Assert.Equal(
            [earlierObserved.Value!.ObservationId, lateObservation.Value!.ObservationId, laterObserved.Value!.ObservationId],
            result.Value.Observations.Select(item => item.ObservationId));
        Assert.True(result.Value.Observations.Single(item =>
            item.ObservationId == laterObserved.Value.ObservationId).HasRawEvidence);
        Assert.Equal([2L, 3L, 1L], result.Value.Observations.Select(item => item.Sequence));
    }

    [Fact]
    public async Task Task_requires_an_existing_conversation()
    {
        var store = new TestExecutionStore();
        var result = await new StartTaskExecution(store, new TestTimeProvider(Now)).ExecuteAsync(
            new StartTaskExecutionCommand(Guid.NewGuid(), "Do work", null, null));

        Assert.Equal(ApplicationErrorType.NotFound, result.Error!.Type);
        Assert.Equal("conversation_not_found", result.Error.Code);
    }

    [Fact]
    public async Task Parent_agent_must_belong_to_the_same_task()
    {
        var context = await CreateTwoTasks();
        var parent = await new StartAgentExecution(context.Store, context.Clock).ExecuteAsync(
            new StartAgentExecutionCommand(context.FirstTaskId, null, null, null));

        var result = await new StartAgentExecution(context.Store, context.Clock).ExecuteAsync(
            new StartAgentExecutionCommand(
                context.SecondTaskId,
                parent.Value!.AgentExecutionId,
                null,
                null));

        Assert.Equal(ApplicationErrorType.Conflict, result.Error!.Type);
        Assert.Equal("parent_agent_task_mismatch", result.Error.Code);
    }

    [Fact]
    public async Task Observation_agent_must_belong_to_the_same_task()
    {
        var context = await CreateTwoTasks();
        var agent = await new StartAgentExecution(context.Store, context.Clock).ExecuteAsync(
            new StartAgentExecutionCommand(context.FirstTaskId, null, null, null));

        var result = await Record(
            context.Store,
            context.Clock,
            context.SecondTaskId,
            agent.Value!.AgentExecutionId,
            Now,
            "message",
            "Wrong task",
            false);

        Assert.Equal(ApplicationErrorType.Conflict, result.Error!.Type);
        Assert.Equal("agent_task_mismatch", result.Error.Code);
    }

    [Fact]
    public async Task Missing_timeline_is_reported_as_not_found()
    {
        var result = await new GetTaskExecutionTimeline(new TestExecutionStore()).ExecuteAsync(
            new GetTaskExecutionTimelineQuery(Guid.NewGuid()));

        Assert.Equal(ApplicationErrorType.NotFound, result.Error!.Type);
    }

    private static async Task<UseCaseResult<ObservationRecorded>> Record(
        TestExecutionStore store,
        TimeProvider clock,
        Guid taskId,
        Guid? agentId,
        DateTimeOffset observedAt,
        string kind,
        string summary,
        bool withEvidence) =>
        await new RecordObservation(store, clock).ExecuteAsync(new RecordObservationCommand(
            taskId,
            agentId,
            observedAt,
            kind,
            summary,
            null,
            withEvidence
                ? new SourceEvidence("application/json", Encoding.UTF8.GetBytes("{\"ok\":true}"))
                : null));

    private static RuntimeReferenceInput Runtime(string kind, string id) =>
        new("test-runtime", kind, id);

    private static async Task<TwoTaskContext> CreateTwoTasks()
    {
        var store = new TestExecutionStore();
        var clock = new TestTimeProvider(Now);
        var conversation = await new CreateConversation(store, clock).ExecuteAsync(
            new CreateConversationCommand(null, null));
        var startTask = new StartTaskExecution(store, clock);
        var first = await startTask.ExecuteAsync(new StartTaskExecutionCommand(
            conversation.Value!.ConversationId,
            "First task",
            null,
            null));
        var second = await startTask.ExecuteAsync(new StartTaskExecutionCommand(
            conversation.Value.ConversationId,
            "Second task",
            null,
            null));

        return new TwoTaskContext(
            store,
            clock,
            first.Value!.TaskExecutionId,
            second.Value!.TaskExecutionId);
    }

    /// <summary>Provides two related task executions for cross-aggregate validation tests.</summary>
    private sealed record TwoTaskContext(
        TestExecutionStore Store,
        TestTimeProvider Clock,
        Guid FirstTaskId,
        Guid SecondTaskId);
}
