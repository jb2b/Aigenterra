namespace Aigenterra.Application.Executions.Ports;

/// <summary>Returns executions by descending start time, with a stable identity tie-breaker.</summary>
public interface IRecentTaskExecutionReader
{
    ValueTask<IReadOnlyList<RecentTaskExecution>> GetRecentAsync(
        int limit, CancellationToken cancellationToken = default);
}
