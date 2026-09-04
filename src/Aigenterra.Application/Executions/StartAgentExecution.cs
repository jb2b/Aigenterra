using Aigenterra.Application.Common;
using Aigenterra.Application.Executions.Ports;
using Aigenterra.Domain.Executions;

namespace Aigenterra.Application.Executions;

public sealed class StartAgentExecution(IExecutionWriteStore store, TimeProvider timeProvider)
{
    public async ValueTask<UseCaseResult<AgentExecutionStarted>> ExecuteAsync(
        StartAgentExecutionCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!TryCreateIds(command, out var taskId, out var parentId, out var idError))
        {
            return Failure(idError!.Type, idError.Code, idError.Message);
        }

        if (!ExecutionInput.TryCreateRuntimeReference(
                command.RuntimeReference,
                out var runtimeReference,
                out var runtimeError))
        {
            return Failure(runtimeError!.Type, runtimeError.Code, runtimeError.Message);
        }

        if (await store.FindTaskExecutionAsync(taskId, cancellationToken) is null)
        {
            return Failure(
                ApplicationErrorType.NotFound,
                "task_execution_not_found",
                $"Task execution {taskId} was not found.");
        }

        if (parentId is { } actualParentId)
        {
            var parent = await store.FindAgentExecutionAsync(actualParentId, cancellationToken);
            if (parent is null)
            {
                return Failure(
                    ApplicationErrorType.NotFound,
                    "parent_agent_execution_not_found",
                    $"Parent agent execution {actualParentId} was not found.");
            }

            if (parent.TaskExecutionId != taskId)
            {
                return Failure(
                    ApplicationErrorType.Conflict,
                    "parent_agent_task_mismatch",
                    "The parent agent execution belongs to another task execution.");
            }
        }

        var agentExecution = new AgentExecution(
            AgentExecutionId.New(),
            taskId,
            parentId,
            command.StartedAt ?? timeProvider.GetUtcNow(),
            runtimeReference);

        var addResult = await store.AddAgentExecutionAsync(agentExecution, cancellationToken);
        if (addResult != StoreAddResult.Added)
        {
            return Failure(
                ApplicationErrorType.Conflict,
                addResult == StoreAddResult.DuplicateRuntimeReference
                    ? "duplicate_runtime_reference"
                    : "duplicate_id",
                "An agent execution with the same identity already exists.");
        }

        return UseCaseResult<AgentExecutionStarted>.Success(new AgentExecutionStarted(
            agentExecution.Id.Value,
            agentExecution.TaskExecutionId.Value,
            agentExecution.ParentAgentExecutionId?.Value,
            ExecutionInput.ToContractValue(agentExecution.Status),
            agentExecution.StartedAt));
    }

    private static bool TryCreateIds(
        StartAgentExecutionCommand command,
        out TaskExecutionId taskId,
        out AgentExecutionId? parentId,
        out ApplicationError? error)
    {
        taskId = default;
        parentId = null;
        error = null;

        if (command.TaskExecutionId == Guid.Empty)
        {
            error = new ApplicationError(
                ApplicationErrorType.Validation,
                "invalid_task_execution_id",
                "TaskExecutionId cannot be empty.");
            return false;
        }

        if (command.ParentAgentExecutionId == Guid.Empty)
        {
            error = new ApplicationError(
                ApplicationErrorType.Validation,
                "invalid_parent_agent_execution_id",
                "ParentAgentExecutionId cannot be empty.");
            return false;
        }

        taskId = new TaskExecutionId(command.TaskExecutionId);
        parentId = command.ParentAgentExecutionId is { } value
            ? new AgentExecutionId(value)
            : null;
        return true;
    }

    private static UseCaseResult<AgentExecutionStarted> Failure(
        ApplicationErrorType type,
        string code,
        string message) =>
        UseCaseResult<AgentExecutionStarted>.Failure(type, code, message);
}
