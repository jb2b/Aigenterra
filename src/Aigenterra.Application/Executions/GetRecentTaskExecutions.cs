using Aigenterra.Application.Common;
using Aigenterra.Application.Executions.Ports;

namespace Aigenterra.Application.Executions;

public sealed record GetRecentTaskExecutionsQuery(int Limit = 50);

public sealed record RecentTaskExecution(
    Guid TaskExecutionId,
    Guid ConversationId,
    string Objective,
    string Status,
    DateTimeOffset StartedAt,
    RuntimeReferenceModel? RuntimeReference,
    int AgentCount,
    int ObservationCount);

/// <summary>Reads a bounded overview without loading execution aggregates or evidence.</summary>
public sealed class GetRecentTaskExecutions(IRecentTaskExecutionReader reader)
{
    public async ValueTask<UseCaseResult<IReadOnlyList<RecentTaskExecution>>> ExecuteAsync(
        GetRecentTaskExecutionsQuery query,
        CancellationToken cancellationToken = default)
    {
        if (query.Limit is < 1 or > 100)
        {
            return UseCaseResult<IReadOnlyList<RecentTaskExecution>>.Failure(
                ApplicationErrorType.Validation, "invalid_execution_limit", "Limit must be between 1 and 100.");
        }

        return UseCaseResult<IReadOnlyList<RecentTaskExecution>>.Success(
            await reader.GetRecentAsync(query.Limit, cancellationToken));
    }
}
