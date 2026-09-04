using Aigenterra.Application.Common;
using Aigenterra.Application.Executions.Ports;
using Aigenterra.Domain.Executions;

namespace Aigenterra.Application.Executions;

/// <summary>Retrieves an inspection timeline by Aigenterra task identity.</summary>
public sealed class GetTaskExecutionTimeline(ITaskExecutionTimelineReader reader)
{
    public async ValueTask<UseCaseResult<TaskExecutionTimeline>> ExecuteAsync(
        GetTaskExecutionTimelineQuery query,
        CancellationToken cancellationToken = default)
    {
        if (query.TaskExecutionId == Guid.Empty)
        {
            return UseCaseResult<TaskExecutionTimeline>.Failure(
                ApplicationErrorType.Validation,
                "invalid_task_execution_id",
                "TaskExecutionId cannot be empty.");
        }

        var taskId = new TaskExecutionId(query.TaskExecutionId);
        var timeline = await reader.GetTimelineAsync(taskId, cancellationToken);
        return timeline is null
            ? UseCaseResult<TaskExecutionTimeline>.Failure(
                ApplicationErrorType.NotFound,
                "task_execution_not_found",
                $"Task execution {taskId} was not found.")
            : UseCaseResult<TaskExecutionTimeline>.Success(timeline);
    }
}
