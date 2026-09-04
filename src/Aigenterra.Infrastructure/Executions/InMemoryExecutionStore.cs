using Aigenterra.Application.Executions;
using Aigenterra.Application.Executions.Ports;
using Aigenterra.Domain.Executions;

namespace Aigenterra.Infrastructure.Executions;

public sealed class InMemoryExecutionStore :
    IExecutionWriteStore,
    ITaskExecutionTimelineReader,
    IExecutionRuntimeReferenceReader
{
    private readonly object gate = new();
    private readonly Dictionary<ConversationId, Conversation> conversations = [];
    private readonly Dictionary<TaskExecutionId, TaskExecution> taskExecutions = [];
    private readonly Dictionary<AgentExecutionId, AgentExecution> agentExecutions = [];
    private readonly Dictionary<ObservationId, StoredObservation> observations = [];
    private readonly Dictionary<TaskExecutionId, List<ObservationId>> observationsByTask = [];
    private readonly Dictionary<TaskExecutionId, long> lastSequenceByTask = [];
    private readonly Dictionary<RuntimeIdentity, object> runtimeIdentities = [];

    public ValueTask<StoreAddResult> AddConversationAsync(
        Conversation conversation,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            return ValueTask.FromResult(Add(
                "conversation",
                conversation.Id,
                conversation.RuntimeReference,
                conversations,
                conversation));
        }
    }

    public ValueTask<Conversation?> FindConversationAsync(
        ConversationId conversationId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            return ValueTask.FromResult(conversations.GetValueOrDefault(conversationId));
        }
    }

    public ValueTask<StoreAddResult> AddTaskExecutionAsync(
        TaskExecution taskExecution,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            return ValueTask.FromResult(Add(
                "task-execution",
                taskExecution.Id,
                taskExecution.RuntimeReference,
                taskExecutions,
                taskExecution));
        }
    }

    public ValueTask<TaskExecution?> FindTaskExecutionAsync(
        TaskExecutionId taskExecutionId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            return ValueTask.FromResult(taskExecutions.GetValueOrDefault(taskExecutionId));
        }
    }

    public ValueTask SaveTaskExecutionAsync(
        TaskExecution taskExecution,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            EnsureKnown(taskExecutions, taskExecution.Id, "task execution");
            taskExecutions[taskExecution.Id] = taskExecution;
            return ValueTask.CompletedTask;
        }
    }

    public ValueTask<StoreAddResult> AddAgentExecutionAsync(
        AgentExecution agentExecution,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            return ValueTask.FromResult(Add(
                "agent-execution",
                agentExecution.Id,
                agentExecution.RuntimeReference,
                agentExecutions,
                agentExecution));
        }
    }

    public ValueTask<AgentExecution?> FindAgentExecutionAsync(
        AgentExecutionId agentExecutionId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            return ValueTask.FromResult(agentExecutions.GetValueOrDefault(agentExecutionId));
        }
    }

    public ValueTask SaveAgentExecutionAsync(
        AgentExecution agentExecution,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            EnsureKnown(agentExecutions, agentExecution.Id, "agent execution");
            agentExecutions[agentExecution.Id] = agentExecution;
            return ValueTask.CompletedTask;
        }
    }

    public ValueTask<AppendObservationStoreResult> AppendObservationAsync(
        Observation observation,
        SourceEvidence? sourceEvidence,
        DateTimeOffset recordedAt,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            if (observations.ContainsKey(observation.Id))
            {
                return ValueTask.FromResult(new AppendObservationStoreResult(
                    StoreAddResult.DuplicateId,
                    null));
            }

            if (!TryReserveRuntimeIdentity(
                    "observation",
                    observation.RuntimeReference,
                    observation.Id))
            {
                return ValueTask.FromResult(new AppendObservationStoreResult(
                    StoreAddResult.DuplicateRuntimeReference,
                    null));
            }

            var sequence = lastSequenceByTask.GetValueOrDefault(observation.TaskExecutionId) + 1;
            lastSequenceByTask[observation.TaskExecutionId] = sequence;

            var normalizedRecordedAt = recordedAt.ToUniversalTime();
            var evidence = sourceEvidence is null
                ? null
                : new StoredEvidence(
                    sourceEvidence.MediaType,
                    sourceEvidence.Content.ToArray());
            observations.Add(
                observation.Id,
                new StoredObservation(observation, sequence, normalizedRecordedAt, evidence));

            if (!observationsByTask.TryGetValue(observation.TaskExecutionId, out var taskObservations))
            {
                taskObservations = [];
                observationsByTask.Add(observation.TaskExecutionId, taskObservations);
            }

            taskObservations.Add(observation.Id);

            return ValueTask.FromResult(new AppendObservationStoreResult(
                StoreAddResult.Added,
                new AppendObservationReceipt(
                    observation.Id,
                    sequence,
                    normalizedRecordedAt)));
        }
    }

    public ValueTask<TaskExecutionTimeline?> GetTimelineAsync(
        TaskExecutionId taskExecutionId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            if (!taskExecutions.TryGetValue(taskExecutionId, out var taskExecution))
            {
                return ValueTask.FromResult<TaskExecutionTimeline?>(null);
            }

            var agents = agentExecutions.Values
                .Where(agent => agent.TaskExecutionId == taskExecutionId)
                .OrderBy(agent => agent.StartedAt)
                .ThenBy(agent => agent.Id.Value)
                .Select(ToTimelineItem)
                .ToArray();

            var timelineObservations = observationsByTask
                .GetValueOrDefault(taskExecutionId, [])
                .Select(id => observations[id])
                .OrderBy(item => item.Observation.ObservedAt)
                .ThenBy(item => item.Sequence)
                .Select(ToTimelineItem)
                .ToArray();

            return ValueTask.FromResult<TaskExecutionTimeline?>(new TaskExecutionTimeline(
                taskExecution.Id.Value,
                taskExecution.ConversationId.Value,
                taskExecution.Objective,
                ToContractValue(taskExecution.Status),
                taskExecution.StartedAt,
                taskExecution.EndedAt,
                ToModel(taskExecution.RuntimeReference),
                agents,
                timelineObservations));
        }
    }

    public ValueTask<ConversationId?> FindConversationIdAsync(
        RuntimeReference runtimeReference,
        CancellationToken cancellationToken = default) =>
        FindRuntimeIdentityAsync<ConversationId>(
            "conversation",
            runtimeReference,
            cancellationToken);

    public ValueTask<TaskExecutionId?> FindTaskExecutionIdAsync(
        RuntimeReference runtimeReference,
        CancellationToken cancellationToken = default) =>
        FindRuntimeIdentityAsync<TaskExecutionId>(
            "task-execution",
            runtimeReference,
            cancellationToken);

    public ValueTask<AgentExecutionId?> FindAgentExecutionIdAsync(
        RuntimeReference runtimeReference,
        CancellationToken cancellationToken = default) =>
        FindRuntimeIdentityAsync<AgentExecutionId>(
            "agent-execution",
            runtimeReference,
            cancellationToken);

    public ValueTask<ObservationId?> FindObservationIdAsync(
        RuntimeReference runtimeReference,
        CancellationToken cancellationToken = default) =>
        FindRuntimeIdentityAsync<ObservationId>(
            "observation",
            runtimeReference,
            cancellationToken);

    internal byte[]? GetRawEvidenceSnapshot(ObservationId observationId)
    {
        lock (gate)
        {
            return observations.GetValueOrDefault(observationId)?.Evidence?.Content.ToArray();
        }
    }

    private StoreAddResult Add<TKey, TValue>(
        string resourceType,
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

        if (!TryReserveRuntimeIdentity(resourceType, runtimeReference, key))
        {
            return StoreAddResult.DuplicateRuntimeReference;
        }

        target.Add(key, value);
        return StoreAddResult.Added;
    }

    private bool TryReserveRuntimeIdentity(
        string resourceType,
        RuntimeReference? reference,
        object executionId) =>
        reference is null || runtimeIdentities.TryAdd(
            new RuntimeIdentity(resourceType, reference),
            executionId);

    private ValueTask<TId?> FindRuntimeIdentityAsync<TId>(
        string resourceType,
        RuntimeReference runtimeReference,
        CancellationToken cancellationToken)
        where TId : struct
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            var found = runtimeIdentities.TryGetValue(
                new RuntimeIdentity(resourceType, runtimeReference),
                out var executionId);
            return ValueTask.FromResult(
                found && executionId is TId typedExecutionId
                    ? (TId?)typedExecutionId
                    : null);
        }
    }

    private static void EnsureKnown<TKey, TValue>(
        Dictionary<TKey, TValue> target,
        TKey key,
        string resourceName)
        where TKey : notnull
    {
        if (!target.ContainsKey(key))
        {
            throw new InvalidOperationException($"Cannot save an unknown {resourceName}.");
        }
    }

    private static AgentExecutionTimelineItem ToTimelineItem(AgentExecution agent) =>
        new(
            agent.Id.Value,
            agent.ParentAgentExecutionId?.Value,
            ToContractValue(agent.Status),
            agent.StartedAt,
            agent.EndedAt,
            ToModel(agent.RuntimeReference));

    private static ObservationTimelineItem ToTimelineItem(StoredObservation stored) =>
        new(
            stored.Observation.Id.Value,
            stored.Observation.AgentExecutionId?.Value,
            stored.Observation.ObservedAt,
            stored.RecordedAt,
            stored.Sequence,
            ToContractValue(stored.Observation.Kind),
            stored.Observation.Summary,
            ToModel(stored.Observation.RuntimeReference),
            stored.Evidence is not null);

    private static RuntimeReferenceModel? ToModel(RuntimeReference? reference) =>
        reference is null
            ? null
            : new RuntimeReferenceModel(
                reference.Runtime,
                reference.ResourceKind,
                reference.ExternalId);

    private static string ToContractValue<TEnum>(TEnum value)
        where TEnum : struct, Enum =>
        value.ToString().ToLowerInvariant();

    private sealed record RuntimeIdentity(string ResourceType, RuntimeReference Reference);

    private sealed record StoredObservation(
        Observation Observation,
        long Sequence,
        DateTimeOffset RecordedAt,
        StoredEvidence? Evidence);

    private sealed record StoredEvidence(string MediaType, byte[] Content);
}
