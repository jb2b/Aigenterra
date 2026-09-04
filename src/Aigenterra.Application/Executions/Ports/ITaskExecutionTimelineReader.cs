using Aigenterra.Domain.Executions;

namespace Aigenterra.Application.Executions.Ports;

public interface ITaskExecutionTimelineReader
{
    ValueTask<TaskExecutionTimeline?> GetTimelineAsync(
        TaskExecutionId taskExecutionId,
        CancellationToken cancellationToken = default);
}
