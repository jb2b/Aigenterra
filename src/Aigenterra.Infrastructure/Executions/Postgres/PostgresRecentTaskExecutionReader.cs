using Aigenterra.Application.Executions;
using Aigenterra.Application.Executions.Ports;
using Microsoft.EntityFrameworkCore;

namespace Aigenterra.Infrastructure.Executions.Postgres;

public sealed class PostgresRecentTaskExecutionReader(ExecutionDbContext db) : IRecentTaskExecutionReader
{
    public async ValueTask<IReadOnlyList<RecentTaskExecution>> GetRecentAsync(
        int limit, CancellationToken cancellationToken = default) =>
        await db.Tasks.AsNoTracking()
            .OrderByDescending(row => row.StartedAtTicks).ThenByDescending(row => row.Id)
            .Take(limit)
            .Select(row => new RecentTaskExecution(
                row.Id, row.ConversationId, row.Objective, row.Status,
                ExecutionMapping.Time(row.StartedAtTicks),
                row.Runtime == null ? null : new RuntimeReferenceModel(row.Runtime, row.ResourceKind!, row.ExternalId!),
                db.Agents.Count(agent => agent.TaskExecutionId == row.Id),
                db.Observations.Count(observation => observation.TaskExecutionId == row.Id)))
            .ToArrayAsync(cancellationToken);
}
