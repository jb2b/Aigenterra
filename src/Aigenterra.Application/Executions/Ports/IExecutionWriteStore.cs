using Aigenterra.Domain.Executions;

namespace Aigenterra.Application.Executions.Ports;

public enum StoreAddResult
{
    Added,
    DuplicateId,
    DuplicateRuntimeReference
}

public sealed record AppendObservationReceipt(
    ObservationId ObservationId,
    long Sequence,
    DateTimeOffset RecordedAt);

public sealed record AppendObservationStoreResult(
    StoreAddResult Result,
    AppendObservationReceipt? Receipt);

public interface IExecutionWriteStore
{
    ValueTask<StoreAddResult> AddConversationAsync(
        Conversation conversation,
        CancellationToken cancellationToken = default);

    ValueTask<Conversation?> FindConversationAsync(
        ConversationId conversationId,
        CancellationToken cancellationToken = default);

    ValueTask<StoreAddResult> AddTaskExecutionAsync(
        TaskExecution taskExecution,
        CancellationToken cancellationToken = default);

    ValueTask<TaskExecution?> FindTaskExecutionAsync(
        TaskExecutionId taskExecutionId,
        CancellationToken cancellationToken = default);

    ValueTask SaveTaskExecutionAsync(
        TaskExecution taskExecution,
        CancellationToken cancellationToken = default);

    ValueTask<StoreAddResult> AddAgentExecutionAsync(
        AgentExecution agentExecution,
        CancellationToken cancellationToken = default);

    ValueTask<AgentExecution?> FindAgentExecutionAsync(
        AgentExecutionId agentExecutionId,
        CancellationToken cancellationToken = default);

    ValueTask SaveAgentExecutionAsync(
        AgentExecution agentExecution,
        CancellationToken cancellationToken = default);

    ValueTask<AppendObservationStoreResult> AppendObservationAsync(
        Observation observation,
        SourceEvidence? sourceEvidence,
        DateTimeOffset recordedAt,
        CancellationToken cancellationToken = default);
}
