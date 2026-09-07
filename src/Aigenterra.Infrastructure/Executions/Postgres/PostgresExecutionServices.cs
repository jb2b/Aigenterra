using Aigenterra.Application.Executions.Ports;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;

namespace Aigenterra.Infrastructure.Executions.Postgres;

public static class PostgresExecutionServices
{
    public static IServiceCollection AddPostgresExecutions(this IServiceCollection services, string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        services.AddDbContext<ExecutionDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<PostgresExecutionStore>();
        services.AddScoped<IExecutionWriteStore>(provider => provider.GetRequiredService<PostgresExecutionStore>());
        services.AddScoped<IExecutionRuntimeReferenceReader>(provider => provider.GetRequiredService<PostgresExecutionStore>());
        services.AddScoped<ITaskExecutionTimelineReader>(provider => provider.GetRequiredService<PostgresExecutionStore>());
        services.AddScoped<IRecentTaskExecutionReader, PostgresRecentTaskExecutionReader>();
        return services;
    }

    public static async Task MigrateExecutionDevelopmentDatabaseAsync(this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ExecutionDbContext>();
        var pending = await db.Database.GetPendingMigrationsAsync(cancellationToken);
        // Explicit allowlist: future migrations require review before automatic startup execution.
        if (pending.Any(id => !id.EndsWith("_InitialExecutionPersistence", StringComparison.Ordinal)
            && !id.EndsWith("_RecentTaskExecutionsIndex", StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("Unapproved execution migrations are pending. Apply a reviewed migration script explicitly.");
        }

        await db.Database.MigrateAsync(cancellationToken);
    }
}

/// <summary>
/// Allows migration generation without starting either host. Database operations require the environment connection.
/// </summary>
public sealed class ExecutionDbContextFactory : IDesignTimeDbContextFactory<ExecutionDbContext>
{
    public ExecutionDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__aigenterra")
            ?? "Host=localhost;Database=aigenterra;Username=postgres";
        return new(new DbContextOptionsBuilder<ExecutionDbContext>().UseNpgsql(connection).Options);
    }
}
