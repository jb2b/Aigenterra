using Aigenterra.Application.Executions.Ports;
using Aigenterra.Application.Evaluations;
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
        services.AddScoped<IObservationEvidenceReader>(provider => provider.GetRequiredService<PostgresExecutionStore>());
        services.AddScoped<Application.Recording.IRawCaptureStore, PostgresRawCaptureStore>();
        services.AddScoped<Application.Runtime.IRuntimeFactStore, PostgresRuntimeFactStore>();
        services.AddScoped<Application.Runtime.IRuntimeSourceHealthStore, PostgresRuntimeSourceHealthStore>();
        services.AddScoped<IExecutionWriteStore>(provider => provider.GetRequiredService<PostgresExecutionStore>());
        services.AddScoped<IExecutionRuntimeReferenceReader>(provider => provider.GetRequiredService<PostgresExecutionStore>());
        services.AddScoped<ITaskExecutionTimelineReader>(provider => provider.GetRequiredService<PostgresExecutionStore>());
        services.AddScoped<IRecentTaskExecutionReader, PostgresRecentTaskExecutionReader>();
        services.AddScoped<PostgresEvaluationStore>();
        services.AddScoped<IExecutionEvaluationInputReader>(p => p.GetRequiredService<PostgresEvaluationStore>());
        services.AddScoped<IEvaluationStore>(p => p.GetRequiredService<PostgresEvaluationStore>());
        return services;
    }

    public static async Task MigrateExecutionDatabaseAsync(this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ExecutionDbContext>();
        var pending = await db.Database.GetPendingMigrationsAsync(cancellationToken);
        // Explicit allowlist: future migrations require review before automatic startup execution.
        string[] approved =
        [
            "20260907142101_InitialExecutionPersistence",
            "20260907145324_RecentTaskExecutionsIndex",
            "20260908000000_CompleteStoppedCodexTurns",
            "20260909161028_EvidenceBackedEvaluations",
            "20260916150255_RecordingHardening",
            "20261013185755_VersionedRuntimeEvidence",
            "20261013190506_RuntimeLifecycleFacts",
            "20261013191550_RuntimeSourceHealth"
        ];
        if (pending.Any(id => !approved.Contains(id, StringComparer.Ordinal)))
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
