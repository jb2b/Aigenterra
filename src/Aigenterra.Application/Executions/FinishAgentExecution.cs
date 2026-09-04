using Aigenterra.Application.Common;
using Aigenterra.Application.Executions.Ports;
using Aigenterra.Domain.Executions;

namespace Aigenterra.Application.Executions;

public sealed class FinishAgentExecution(IExecutionWriteStore store, TimeProvider timeProvider)
{
    public async ValueTask<UseCaseResult<ExecutionFinished>> ExecuteAsync(
        FinishAgentExecutionCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command.TaskExecutionId == Guid.Empty || command.AgentExecutionId == Guid.Empty)
        {
            return Failure(
                ApplicationErrorType.Validation,
                "invalid_execution_id",
                "TaskExecutionId and AgentExecutionId cannot be empty.");
        }

        if (!ExecutionInput.TryParseTerminalStatus(command.Outcome, out var status, out var error))
        {
            return Failure(error!.Type, error.Code, error.Message);
        }

        var taskId = new TaskExecutionId(command.TaskExecutionId);
        if (await store.FindTaskExecutionAsync(taskId, cancellationToken) is null)
        {
            return Failure(
                ApplicationErrorType.NotFound,
                "task_execution_not_found",
                $"Task execution {taskId} was not found.");
        }

        var agentId = new AgentExecutionId(command.AgentExecutionId);
        var execution = await store.FindAgentExecutionAsync(agentId, cancellationToken);
        if (execution is null)
        {
            return Failure(
                ApplicationErrorType.NotFound,
                "agent_execution_not_found",
                $"Agent execution {agentId} was not found.");
        }

        if (execution.TaskExecutionId != taskId)
        {
            return Failure(
                ApplicationErrorType.Conflict,
                "agent_task_mismatch",
                "The agent execution belongs to another task execution.");
        }

        var finishResult = execution.Finish(
            status,
            command.EndedAt ?? timeProvider.GetUtcNow());
        var failure = MapFinishFailure(finishResult);
        if (failure is not null)
        {
            return failure;
        }

        if (finishResult == ExecutionFinishResult.Applied)
        {
            await store.SaveAgentExecutionAsync(execution, cancellationToken);
        }

        return UseCaseResult<ExecutionFinished>.Success(new ExecutionFinished(
            execution.Id.Value,
            ExecutionInput.ToContractValue(execution.Status),
            execution.EndedAt!.Value));
    }

    private static UseCaseResult<ExecutionFinished>? MapFinishFailure(ExecutionFinishResult result) =>
        result switch
        {
            ExecutionFinishResult.InvalidStatus => Failure(
                ApplicationErrorType.Validation,
                "invalid_execution_outcome",
                "An execution can only finish as completed, failed, or interrupted."),
            ExecutionFinishResult.EndedBeforeStarted => Failure(
                ApplicationErrorType.Validation,
                "end_before_start",
                "EndedAt cannot be before StartedAt."),
            ExecutionFinishResult.ConflictingTerminalStatus => Failure(
                ApplicationErrorType.Conflict,
                "conflicting_terminal_status",
                "The agent execution already has another terminal status."),
            _ => null
        };

    private static UseCaseResult<ExecutionFinished> Failure(
        ApplicationErrorType type,
        string code,
        string message) =>
        UseCaseResult<ExecutionFinished>.Failure(type, code, message);
}
