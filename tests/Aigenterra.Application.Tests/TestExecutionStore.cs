using Aigenterra.Application.Executions;
using Aigenterra.Application.Executions.Ports;
using Aigenterra.Domain.Executions;

namespace Aigenterra.Application.Tests;

internal sealed class TestExecutionStore : IExecutionWriteStore, ITaskExecutionTimelineReader
{
    private readonly Dictionary<ConversationId, Conversation> conversations = [];
    private readonly Dictionary<TaskExecutionId, TaskExecution> tasks = [];
    private readonly Dictionary<AgentExecutionId, AgentExecution> agents = [];
    private readonly Dictionary<TaskExecutionId, List<StoredObservation>> observations = [];
    private readonly HashSet<(string Type, RuntimeReference Reference)> runtimeReferences = [];

    public ValueTask<StoreAddResult> AddConversationAsync(
        Conversation conversation,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(Add(
            "conversation",
            conversation.Id,
            conversation.RuntimeReference,
            conversations,
            conversation));

    public ValueTask<Conversation?> FindConversationAsync(
        ConversationId conversationId,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(conversations.GetValueOrDefault(conversationId));

    public ValueTask<StoreAddResult> AddTaskExecutionAsync(
        TaskExecution taskExecution,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(Add(
            "task",
            taskExecution.Id,
            taskExecution.RuntimeReference,
            tasks,
            taskExecution));

    public ValueTask<TaskExecution?> FindTaskExecutionAsync(
        TaskExecutionId taskExecutionId,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(tasks.GetValueOrDefault(taskExecutionId));

    public ValueTask SaveTaskExecutionAsync(
        TaskExecution taskExecution,
        CancellationToken cancellationToken = default)
    {
        tasks[taskExecution.Id] = taskExecution;
        return ValueTask.CompletedTask;
    }

    public ValueTask<StoreAddResult> AddAgentExecutionAsync(
        AgentExecution agentExecution,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(Add(
            "agent",
            agentExecution.Id,
            agentExecution.RuntimeReference,
            agents,
            agentExecution));

    public ValueTask<AgentExecution?> FindAgentExecutionAsync(
        AgentExecutionId agentExecutionId,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(agents.GetValueOrDefault(agentExecutionId));

    public ValueTask SaveAgentExecutionAsync(
        AgentExecution agentExecution,
        CancellationToken cancellationToken = default)
    {
        agents[agentExecution.Id] = agentExecution;
        return ValueTask.CompletedTask;
    }

    public ValueTask<AppendObservationStoreResult> AppendObservationAsync(
        Observation observation,
        SourceEvidence? sourceEvidence,
        DateTimeOffset recordedAt,
        CancellationToken cancellationToken = default)
    {
        var list = observations.GetValueOrDefault(observation.TaskExecutionId);
        if (list?.Any(item => item.Observation.Id == observation.Id) == true)
        {
            return ValueTask.FromResult(new AppendObservationStoreResult(
                StoreAddResult.DuplicateId,
                null));
        }

        if (!TryAddRuntimeReference("observation", observation.RuntimeReference))
        {
            return ValueTask.FromResult(new AppendObservationStoreResult(
                StoreAddResult.DuplicateRuntimeReference,
                null));
        }

        list ??= [];
        observations[observation.TaskExecutionId] = list;
        var sequence = list.Count == 0 ? 1 : list.Max(item => item.Sequence) + 1;
        list.Add(new StoredObservation(
            observation,
            sequence,
            recordedAt.ToUniversalTime(),
            sourceEvidence is not null));

        return ValueTask.FromResult(new AppendObservationStoreResult(
            StoreAddResult.Added,
            new AppendObservationReceipt(observation.Id, sequence, recordedAt.ToUniversalTime())));
    }

    public ValueTask<TaskExecutionTimeline?> GetTimelineAsync(
        TaskExecutionId taskExecutionId,
        CancellationToken cancellationToken = default)
    {
        if (!tasks.TryGetValue(taskExecutionId, out var task))
        {
            return ValueTask.FromResult<TaskExecutionTimeline?>(null);
        }

        var agentItems = agents.Values
            .Where(agent => agent.TaskExecutionId == taskExecutionId)
            .OrderBy(agent => agent.StartedAt)
            .Select(agent => new AgentExecutionTimelineItem(
                agent.Id.Value,
                agent.ParentAgentExecutionId?.Value,
                ToContract(agent.Status),
                agent.StartedAt,
                agent.EndedAt,
                ToModel(agent.RuntimeReference)))
            .ToArray();

        var observationItems = observations.GetValueOrDefault(taskExecutionId, [])
            .OrderBy(item => item.Observation.ObservedAt)
            .ThenBy(item => item.Sequence)
            .Select(item => new ObservationTimelineItem(
                item.Observation.Id.Value,
                item.Observation.AgentExecutionId?.Value,
                item.Observation.ObservedAt,
                item.RecordedAt,
                item.Sequence,
                ToContract(item.Observation.Kind),
                item.Observation.Summary,
                ToModel(item.Observation.RuntimeReference),
                item.HasRawEvidence))
            .ToArray();

        return ValueTask.FromResult<TaskExecutionTimeline?>(new TaskExecutionTimeline(
            task.Id.Value,
            task.ConversationId.Value,
            task.Objective,
            ToContract(task.Status),
            task.StartedAt,
            task.EndedAt,
            ToModel(task.RuntimeReference),
            agentItems,
            observationItems));
    }

    private StoreAddResult Add<TKey, TValue>(
        string type,
        TKey key,
        RuntimeReference? runtimeReference,
        Dictionary<TKey, TValue> target,
        TValue value)
        where TKey : notnull
    {
        if (target.ContainsKey(key))
        {
            return StoreAddResult.DuplicateId;
        }

        if (!TryAddRuntimeReference(type, runtimeReference))
        {
            return StoreAddResult.DuplicateRuntimeReference;
        }

        target.Add(key, value);
        return StoreAddResult.Added;
    }

    private bool TryAddRuntimeReference(string type, RuntimeReference? reference) =>
        reference is null || runtimeReferences.Add((type, reference));

    private static string ToContract<TEnum>(TEnum value)
        where TEnum : struct, Enum =>
        value.ToString().ToLowerInvariant();

    private static RuntimeReferenceModel? ToModel(RuntimeReference? reference) =>
        reference is null
            ? null
            : new RuntimeReferenceModel(
                reference.Runtime,
                reference.ResourceKind,
                reference.ExternalId);

    private sealed record StoredObservation(
        Observation Observation,
        long Sequence,
        DateTimeOffset RecordedAt,
        bool HasRawEvidence);
}

internal sealed class TestTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => utcNow;
}
