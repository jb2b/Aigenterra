using Aigenterra.Domain.Executions;

namespace Aigenterra.Application.Executions.Ports;

public interface IExecutionRuntimeReferenceReader
{
    ValueTask<ConversationId?> FindConversationIdAsync(
        RuntimeReference runtimeReference,
        CancellationToken cancellationToken = default);

    ValueTask<TaskExecutionId?> FindTaskExecutionIdAsync(
        RuntimeReference runtimeReference,
        CancellationToken cancellationToken = default);

    ValueTask<AgentExecutionId?> FindAgentExecutionIdAsync(
        RuntimeReference runtimeReference,
        CancellationToken cancellationToken = default);

    ValueTask<ObservationId?> FindObservationIdAsync(
        RuntimeReference runtimeReference,
        CancellationToken cancellationToken = default);
}
