using Aigenterra.Application.Common;
using Aigenterra.Application.Executions.Ports;
using Aigenterra.Domain.Executions;

namespace Aigenterra.Application.Executions;

/// <summary>Starts a task execution for an existing conversation.</summary>
public sealed class StartTaskExecution(IExecutionWriteStore store, TimeProvider timeProvider)
{
    public async ValueTask<UseCaseResult<TaskExecutionStarted>> ExecuteAsync(
        StartTaskExecutionCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command.ConversationId == Guid.Empty)
        {
            return Failure(
                ApplicationErrorType.Validation,
                "invalid_conversation_id",
                "ConversationId cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(command.Objective))
        {
            return Failure(
                ApplicationErrorType.Validation,
                "invalid_objective",
                "Objective is required.");
        }

        if (!ExecutionInput.TryCreateRuntimeReference(
                command.RuntimeReference,
                out var runtimeReference,
                out var error))
        {
            return Failure(error!.Type, error.Code, error.Message);
        }

        var conversationId = new ConversationId(command.ConversationId);
        if (await store.FindConversationAsync(conversationId, cancellationToken) is null)
        {
            return Failure(
                ApplicationErrorType.NotFound,
                "conversation_not_found",
                $"Conversation {conversationId} was not found.");
        }

        var taskExecution = new TaskExecution(
            TaskExecutionId.New(),
            conversationId,
            command.Objective,
            command.StartedAt ?? timeProvider.GetUtcNow(),
            runtimeReference);

        var addResult = await store.AddTaskExecutionAsync(taskExecution, cancellationToken);
        if (addResult != StoreAddResult.Added)
        {
            return Failure(
                ApplicationErrorType.Conflict,
                addResult == StoreAddResult.DuplicateRuntimeReference
                    ? "duplicate_runtime_reference"
                    : "duplicate_id",
                "A task execution with the same identity already exists.");
        }

        return UseCaseResult<TaskExecutionStarted>.Success(new TaskExecutionStarted(
            taskExecution.Id.Value,
            taskExecution.ConversationId.Value,
            ExecutionInput.ToContractValue(taskExecution.Status),
            taskExecution.StartedAt));
    }

    private static UseCaseResult<TaskExecutionStarted> Failure(
        ApplicationErrorType type,
        string code,
        string message) =>
        UseCaseResult<TaskExecutionStarted>.Failure(type, code, message);
}
