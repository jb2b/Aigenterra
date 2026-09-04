using Aigenterra.Application.Common;
using Aigenterra.Application.Executions.Ports;

namespace Aigenterra.Application.Executions;

/// <summary>
/// Identifies the execution concept addressed by a runtime reference.
/// </summary>
public enum ExecutionResourceType
{
    Conversation,
    TaskExecution,
    AgentExecution,
    Observation
}

/// <summary>
/// Requests resolution of an execution identity from a runtime reference.
/// </summary>
public sealed record ResolveExecutionRuntimeReferenceQuery(
    ExecutionResourceType ResourceType,
    RuntimeReferenceInput RuntimeReference);

/// <summary>
/// Reports the Aigenterra identity resolved from a runtime reference.
/// </summary>
public sealed record ExecutionRuntimeReferenceResolved(Guid ExecutionId);

/// <summary>
/// Resolves provider-neutral runtime references to Aigenterra identities.
/// </summary>
public sealed class ResolveExecutionRuntimeReference(IExecutionRuntimeReferenceReader reader)
{
    public async ValueTask<UseCaseResult<ExecutionRuntimeReferenceResolved>> ExecuteAsync(
        ResolveExecutionRuntimeReferenceQuery query,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(query.ResourceType))
        {
            return Failure("invalid_execution_resource_type", "Execution resource type is invalid.");
        }

        if (!ExecutionInput.TryCreateRuntimeReference(
                query.RuntimeReference,
                out var runtimeReference,
                out var error))
        {
            return UseCaseResult<ExecutionRuntimeReferenceResolved>.Failure(
                error!.Type,
                error.Code,
                error.Message);
        }

        var executionId = query.ResourceType switch
        {
            ExecutionResourceType.Conversation =>
                (await reader.FindConversationIdAsync(runtimeReference!, cancellationToken))?.Value,
            ExecutionResourceType.TaskExecution =>
                (await reader.FindTaskExecutionIdAsync(runtimeReference!, cancellationToken))?.Value,
            ExecutionResourceType.AgentExecution =>
                (await reader.FindAgentExecutionIdAsync(runtimeReference!, cancellationToken))?.Value,
            ExecutionResourceType.Observation =>
                (await reader.FindObservationIdAsync(runtimeReference!, cancellationToken))?.Value,
            _ => null
        };

        return executionId is { } id
            ? UseCaseResult<ExecutionRuntimeReferenceResolved>.Success(
                new ExecutionRuntimeReferenceResolved(id))
            : UseCaseResult<ExecutionRuntimeReferenceResolved>.Failure(
                ApplicationErrorType.NotFound,
                "runtime_reference_not_found",
                "No execution resource was found for the supplied runtime reference.");
    }

    private static UseCaseResult<ExecutionRuntimeReferenceResolved> Failure(
        string code,
        string message) =>
        UseCaseResult<ExecutionRuntimeReferenceResolved>.Failure(
            ApplicationErrorType.Validation,
            code,
            message);
}
