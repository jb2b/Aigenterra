using Aigenterra.Application.Common;
using Aigenterra.Application.Executions.Ports;
using Aigenterra.Domain.Executions;

namespace Aigenterra.Application.Executions;

public sealed class FinishTaskExecution(IExecutionWriteStore store, TimeProvider timeProvider)
{
    public async ValueTask<UseCaseResult<ExecutionFinished>> ExecuteAsync(
        FinishTaskExecutionCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command.TaskExecutionId == Guid.Empty)
        {
            return Failure(
                ApplicationErrorType.Validation,
                "invalid_task_execution_id",
                "TaskExecutionId cannot be empty.");
        }

        if (!ExecutionInput.TryParseTerminalStatus(command.Outcome, out var status, out var error))
        {
            return Failure(error!.Type, error.Code, error.Message);
        }

        var taskId = new TaskExecutionId(command.TaskExecutionId);
        var execution = await store.FindTaskExecutionAsync(taskId, cancellationToken);
        if (execution is null)
        {
            return Failure(
                ApplicationErrorType.NotFound,
                "task_execution_not_found",
                $"Task execution {taskId} was not found.");
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
            await store.SaveTaskExecutionAsync(execution, cancellationToken);
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
                "The task execution already has another terminal status."),
            _ => null
        };

    private static UseCaseResult<ExecutionFinished> Failure(
        ApplicationErrorType type,
        string code,
        string message) =>
        UseCaseResult<ExecutionFinished>.Failure(type, code, message);
}
