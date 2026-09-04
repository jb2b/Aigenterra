using Aigenterra.Application.Common;
using Aigenterra.Application.Executions.Ports;
using Aigenterra.Domain.Executions;

namespace Aigenterra.Application.Executions;

/// <summary>
/// Requests a task timeline through an external runtime identity.
/// </summary>
public sealed record GetTaskExecutionTimelineByRuntimeReferenceQuery(
    RuntimeReferenceInput RuntimeReference);

/// <summary>
/// Resolves a runtime identity and retrieves the corresponding task timeline.
/// </summary>
public sealed class GetTaskExecutionTimelineByRuntimeReference(
    IExecutionRuntimeReferenceReader runtimeReferenceReader,
    ITaskExecutionTimelineReader timelineReader)
{
    public async ValueTask<UseCaseResult<TaskExecutionTimeline>> ExecuteAsync(
        GetTaskExecutionTimelineByRuntimeReferenceQuery query,
        CancellationToken cancellationToken = default)
    {
        if (!ExecutionInput.TryCreateRuntimeReference(
                query.RuntimeReference,
                out var runtimeReference,
                out var error))
        {
            return UseCaseResult<TaskExecutionTimeline>.Failure(
                error!.Type,
                error.Code,
                error.Message);
        }

        var taskExecutionId = await runtimeReferenceReader.FindTaskExecutionIdAsync(
            runtimeReference!,
            cancellationToken);
        if (taskExecutionId is null)
        {
            return UseCaseResult<TaskExecutionTimeline>.Failure(
                ApplicationErrorType.NotFound,
                "runtime_reference_not_found",
                "No task execution was found for the supplied runtime reference.");
        }

        var timeline = await timelineReader.GetTimelineAsync(
            taskExecutionId.Value,
            cancellationToken);
        return timeline is null
            ? UseCaseResult<TaskExecutionTimeline>.Failure(
                ApplicationErrorType.NotFound,
                "task_execution_not_found",
                $"Task execution {taskExecutionId.Value} was not found.")
            : UseCaseResult<TaskExecutionTimeline>.Success(timeline);
    }
}
