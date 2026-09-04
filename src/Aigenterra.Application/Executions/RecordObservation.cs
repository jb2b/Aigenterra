using Aigenterra.Application.Common;
using Aigenterra.Application.Executions.Ports;
using Aigenterra.Domain.Executions;

namespace Aigenterra.Application.Executions;

public sealed class RecordObservation(IExecutionWriteStore store, TimeProvider timeProvider)
{
    public async ValueTask<UseCaseResult<ObservationRecorded>> ExecuteAsync(
        RecordObservationCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!TryCreateValues(
                command,
                out var taskId,
                out var agentId,
                out var kind,
                out var runtimeReference,
                out var error))
        {
            return Failure(error!.Type, error.Code, error.Message);
        }

        if (await store.FindTaskExecutionAsync(taskId, cancellationToken) is null)
        {
            return Failure(
                ApplicationErrorType.NotFound,
                "task_execution_not_found",
                $"Task execution {taskId} was not found.");
        }

        if (agentId is { } actualAgentId)
        {
            var agent = await store.FindAgentExecutionAsync(actualAgentId, cancellationToken);
            if (agent is null)
            {
                return Failure(
                    ApplicationErrorType.NotFound,
                    "agent_execution_not_found",
                    $"Agent execution {actualAgentId} was not found.");
            }

            if (agent.TaskExecutionId != taskId)
            {
                return Failure(
                    ApplicationErrorType.Conflict,
                    "agent_task_mismatch",
                    "The agent execution belongs to another task execution.");
            }
        }

        var observation = new Observation(
            ObservationId.New(),
            taskId,
            agentId,
            command.ObservedAt,
            kind,
            command.Summary!,
            runtimeReference);

        var appendResult = await store.AppendObservationAsync(
            observation,
            command.SourceEvidence,
            timeProvider.GetUtcNow(),
            cancellationToken);

        if (appendResult.Result != StoreAddResult.Added)
        {
            return Failure(
                ApplicationErrorType.Conflict,
                appendResult.Result == StoreAddResult.DuplicateRuntimeReference
                    ? "duplicate_runtime_reference"
                    : "duplicate_id",
                "An observation with the same identity already exists.");
        }

        var receipt = appendResult.Receipt!;
        return UseCaseResult<ObservationRecorded>.Success(new ObservationRecorded(
            receipt.ObservationId.Value,
            receipt.Sequence,
            receipt.RecordedAt));
    }

    private static bool TryCreateValues(
        RecordObservationCommand command,
        out TaskExecutionId taskId,
        out AgentExecutionId? agentId,
        out ObservationKind kind,
        out RuntimeReference? runtimeReference,
        out ApplicationError? error)
    {
        taskId = default;
        agentId = null;
        kind = default;
        runtimeReference = null;
        error = null;

        if (command.TaskExecutionId == Guid.Empty)
        {
            error = new ApplicationError(
                ApplicationErrorType.Validation,
                "invalid_task_execution_id",
                "TaskExecutionId cannot be empty.");
            return false;
        }

        if (command.AgentExecutionId == Guid.Empty)
        {
            error = new ApplicationError(
                ApplicationErrorType.Validation,
                "invalid_agent_execution_id",
                "AgentExecutionId cannot be empty.");
            return false;
        }

        if (!Enum.TryParse(command.Kind, true, out kind) || !Enum.IsDefined(kind))
        {
            error = new ApplicationError(
                ApplicationErrorType.Validation,
                "invalid_observation_kind",
                "Observation kind is not part of the normalized taxonomy.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(command.Summary))
        {
            error = new ApplicationError(
                ApplicationErrorType.Validation,
                "invalid_observation_summary",
                "Observation summary is required.");
            return false;
        }

        if (!ExecutionInput.TryCreateRuntimeReference(
                command.RuntimeReference,
                out runtimeReference,
                out error))
        {
            return false;
        }

        taskId = new TaskExecutionId(command.TaskExecutionId);
        agentId = command.AgentExecutionId is { } value
            ? new AgentExecutionId(value)
            : null;
        return true;
    }

    private static UseCaseResult<ObservationRecorded> Failure(
        ApplicationErrorType type,
        string code,
        string message) =>
        UseCaseResult<ObservationRecorded>.Failure(type, code, message);
}
