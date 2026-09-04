using Aigenterra.Application.Common;
using Aigenterra.Application.Executions.Ports;
using Aigenterra.Domain.Executions;

namespace Aigenterra.Application.Executions;

public sealed class CreateConversation(IExecutionWriteStore store, TimeProvider timeProvider)
{
    public async ValueTask<UseCaseResult<ConversationCreated>> ExecuteAsync(
        CreateConversationCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!ExecutionInput.TryCreateRuntimeReference(
                command.RuntimeReference,
                out var runtimeReference,
                out var error))
        {
            return UseCaseResult<ConversationCreated>.Failure(
                error!.Type,
                error.Code,
                error.Message);
        }

        var conversation = new Conversation(
            ConversationId.New(),
            command.StartedAt ?? timeProvider.GetUtcNow(),
            runtimeReference);

        var addResult = await store.AddConversationAsync(conversation, cancellationToken);
        if (addResult != StoreAddResult.Added)
        {
            return Duplicate<ConversationCreated>(addResult, "conversation");
        }

        return UseCaseResult<ConversationCreated>.Success(new ConversationCreated(
            conversation.Id.Value,
            conversation.StartedAt,
            ExecutionInput.ToModel(conversation.RuntimeReference)));
    }

    private static UseCaseResult<T> Duplicate<T>(StoreAddResult result, string resource) =>
        UseCaseResult<T>.Failure(
            ApplicationErrorType.Conflict,
            result == StoreAddResult.DuplicateRuntimeReference
                ? "duplicate_runtime_reference"
                : "duplicate_id",
            $"A {resource} with the same identity already exists.");
}
