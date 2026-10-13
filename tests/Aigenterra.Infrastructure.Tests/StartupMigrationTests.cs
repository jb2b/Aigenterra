using System.Reflection;
using Aigenterra.Infrastructure.Executions.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace Aigenterra.Infrastructure.Tests;

public sealed class StartupMigrationTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Startup_applies_reviewed_migrations_to_empty_database_and_restart_is_idempotent()
    {
        var connection = await fixture.CreateDatabaseAsync("0");
        await using var services = new ServiceCollection().AddPostgresExecutions(connection).BuildServiceProvider();

        await services.MigrateExecutionDatabaseAsync();
        await using var db = PostgresFixture.Context(connection);
        var applied = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
        Assert.Equal(13, applied.Length);
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.False(db.Database.HasPendingModelChanges());

        await services.MigrateExecutionDatabaseAsync();
        Assert.Equal(applied, await db.Database.GetAppliedMigrationsAsync());
    }

    [Fact]
    public async Task Unapproved_pending_migration_prevents_even_earlier_approved_migrations()
    {
        var connection = await fixture.CreateDatabaseAsync("0");
        var registrations = new ServiceCollection();
        registrations.AddDbContext<ExecutionDbContext>(options => options.UseNpgsql(connection)
            .ReplaceService<IMigrationsAssembly, UnapprovedMigrationsAssembly>());
        await using var services = registrations.BuildServiceProvider();

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => services.MigrateExecutionDatabaseAsync());
        Assert.Contains("Unapproved execution migrations", failure.Message, StringComparison.Ordinal);
        await using var db = PostgresFixture.Context(connection);
        Assert.Empty(await db.Database.GetAppliedMigrationsAsync());
        Assert.Equal(13, (await db.Database.GetPendingMigrationsAsync()).Count());
    }

    // Present a future migration alongside the real migrations. Execution must be rejected
    // before EF asks to instantiate any migration; this adds no migration to production.
    private sealed class UnapprovedMigrationsAssembly : IMigrationsAssembly
    {
        public Assembly Assembly => typeof(ExecutionDbContext).Assembly;
        public ModelSnapshot? ModelSnapshot => null;
        public IReadOnlyDictionary<string, TypeInfo> Migrations { get; } =
            typeof(ExecutionDbContext).Assembly.DefinedTypes
                .Where(type => type.GetCustomAttribute<MigrationAttribute>() is not null)
                .ToDictionary(type => type.GetCustomAttribute<MigrationAttribute>()!.Id, type => type)
                .Append(new KeyValuePair<string, TypeInfo>(
                    "20990101000000_UnreviewedChange", typeof(UnreviewedMigration).GetTypeInfo()))
                .ToDictionary(entry => entry.Key, entry => entry.Value);

        public string? FindMigrationId(string nameOrId) => Migrations.Keys.FirstOrDefault(id => id == nameOrId);
        public Migration CreateMigration(TypeInfo migrationClass, string activeProvider) =>
            throw new InvalidOperationException("No migration should execute before approval.");
    }

    private sealed class UnreviewedMigration : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder) => throw new NotSupportedException();
    }
}
