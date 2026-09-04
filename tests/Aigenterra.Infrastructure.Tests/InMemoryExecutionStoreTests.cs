using System.Text;
using Aigenterra.Application.Executions;
using Aigenterra.Application.Executions.Ports;
using Aigenterra.Domain.Executions;
using Aigenterra.Infrastructure.Executions;

namespace Aigenterra.Infrastructure.Tests;

/// <summary>
/// Verifies concurrency, evidence ownership and read behavior of the in-memory adapter.
/// </summary>
public sealed class InMemoryExecutionStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Parallel_appends_receive_unique_monotone_sequences()
    {
        var (store, task) = await CreateTask();
        const int observationCount = 100;

        var appends = Enumerable.Range(0, observationCount)
            .Select(index => store.AppendObservationAsync(
                new Observation(
                    ObservationId.New(),
                    task.Id,
                    null,
                    Now,
                    ObservationKind.Measurement,
                    $"Measurement {index}"),
                null,
                Now.AddSeconds(index)).AsTask())
            .ToArray();

        var results = await Task.WhenAll(appends);

        Assert.All(results, result => Assert.Equal(StoreAddResult.Added, result.Result));
        Assert.Equal(
            Enumerable.Range(1, observationCount).Select(value => (long)value),
            results.Select(result => result.Receipt!.Sequence).Order());
    }

    [Fact]
    public async Task Runtime_reference_is_unique_per_resource_type()
    {
        var store = new InMemoryExecutionStore();
        var reference = new RuntimeReference("codex", "thread", "external-1");

        var first = await store.AddConversationAsync(
            new Conversation(ConversationId.New(), Now, reference));
        var duplicate = await store.AddConversationAsync(
            new Conversation(ConversationId.New(), Now, reference));

        Assert.Equal(StoreAddResult.Added, first);
        Assert.Equal(StoreAddResult.DuplicateRuntimeReference, duplicate);
    }

    [Fact]
    public async Task Runtime_references_resolve_to_the_matching_execution_identity()
    {
        var store = new InMemoryExecutionStore();
        var conversationReference = new RuntimeReference("codex", "session", "session-1");
        var taskReference = new RuntimeReference("codex", "turn", "turn-1");
        var agentReference = new RuntimeReference("codex", "subagent", "agent-1");
        var observationReference = new RuntimeReference("codex", "tool-result", "tool-1");
        var conversation = new Conversation(ConversationId.New(), Now, conversationReference);
        var task = new TaskExecution(
            TaskExecutionId.New(),
            conversation.Id,
            "Resolve runtime identities",
            Now,
            taskReference);
        var agent = new AgentExecution(
            AgentExecutionId.New(),
            task.Id,
            null,
            Now,
            agentReference);
        var observation = new Observation(
            ObservationId.New(),
            task.Id,
            agent.Id,
            Now,
            ObservationKind.ToolResult,
            "Tool result",
            observationReference);

        await store.AddConversationAsync(conversation);
        await store.AddTaskExecutionAsync(task);
        await store.AddAgentExecutionAsync(agent);
        await store.AppendObservationAsync(observation, null, Now);

        Assert.Equal(conversation.Id, await store.FindConversationIdAsync(conversationReference));
        Assert.Equal(task.Id, await store.FindTaskExecutionIdAsync(taskReference));
        Assert.Equal(agent.Id, await store.FindAgentExecutionIdAsync(agentReference));
        Assert.Equal(observation.Id, await store.FindObservationIdAsync(observationReference));
    }

    [Fact]
    public async Task Raw_evidence_is_stored_as_a_defensive_copy_under_the_observation_id()
    {
        var (store, task) = await CreateTask();
        var observation = new Observation(
            ObservationId.New(),
            task.Id,
            null,
            Now,
            ObservationKind.ToolResult,
            "Tool returned evidence");
        var sourceBytes = Encoding.UTF8.GetBytes("{\"result\":\"ok\"}");

        await store.AppendObservationAsync(
            observation,
            new SourceEvidence("application/json", sourceBytes),
            Now);
        sourceBytes[0] = (byte)'X';
        var firstSnapshot = store.GetRawEvidenceSnapshot(observation.Id)!;
        firstSnapshot[1] = (byte)'X';
        var secondSnapshot = store.GetRawEvidenceSnapshot(observation.Id)!;

        Assert.Equal("{\"result\":\"ok\"}", Encoding.UTF8.GetString(secondSnapshot));
    }

    [Fact]
    public async Task Timeline_uses_observed_time_then_sequence_and_only_exposes_evidence_presence()
    {
        var (store, task) = await CreateTask();
        var later = CreateObservation(task.Id, Now.AddMinutes(2), "Later");
        var earlier = CreateObservation(task.Id, Now.AddMinutes(1), "Earlier");

        await store.AppendObservationAsync(later, null, Now);
        await store.AppendObservationAsync(
            earlier,
            new SourceEvidence("application/json", "{}"u8.ToArray()),
            Now.AddSeconds(1));

        var timeline = await store.GetTimelineAsync(task.Id);

        Assert.Equal([earlier.Id.Value, later.Id.Value], timeline!.Observations.Select(item => item.ObservationId));
        Assert.Equal([2L, 1L], timeline.Observations.Select(item => item.Sequence));
        Assert.True(timeline.Observations[0].HasRawEvidence);
        Assert.False(timeline.Observations[1].HasRawEvidence);
    }

    private static async Task<(InMemoryExecutionStore Store, TaskExecution Task)> CreateTask()
    {
        var store = new InMemoryExecutionStore();
        var conversation = new Conversation(ConversationId.New(), Now);
        var task = new TaskExecution(
            TaskExecutionId.New(),
            conversation.Id,
            "Exercise the store",
            Now);
        await store.AddConversationAsync(conversation);
        await store.AddTaskExecutionAsync(task);
        return (store, task);
    }

    private static Observation CreateObservation(
        TaskExecutionId taskId,
        DateTimeOffset observedAt,
        string summary) =>
        new(
            ObservationId.New(),
            taskId,
            null,
            observedAt,
            ObservationKind.Message,
            summary);
}
