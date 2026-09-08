using Aigenterra.Infrastructure.Executions.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Aigenterra.Api.Health;

/// <summary>Checks database connectivity using the same configuration as execution requests.</summary>
internal sealed class ExecutionDatabaseHealthCheck(ExecutionDbContext database) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default) =>
        await database.Database.CanConnectAsync(cancellationToken)
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("Execution database is unavailable.");
}
