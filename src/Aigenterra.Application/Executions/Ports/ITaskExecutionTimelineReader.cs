using Aigenterra.Domain.Executions;

namespace Aigenterra.Application.Executions.Ports;

/// <summary>Reads the composed inspection view for a task execution.</summary>
public interface ITaskExecutionTimelineReader
{
    ValueTask<TaskExecutionTimeline?> GetTimelineAsync(
        TaskExecutionId taskExecutionId,
        CancellationToken cancellationToken = default);
}
