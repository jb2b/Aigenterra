using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aigenterra.Infrastructure.Executions.Postgres;

public sealed class ExecutionDbContext(DbContextOptions<ExecutionDbContext> options) : DbContext(options)
{
    internal DbSet<ConversationRow> Conversations => Set<ConversationRow>();
    internal DbSet<TaskExecutionRow> Tasks => Set<TaskExecutionRow>();
    internal DbSet<AgentExecutionRow> Agents => Set<AgentExecutionRow>();
    internal DbSet<ObservationRow> Observations => Set<ObservationRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var conversations = modelBuilder.Entity<ConversationRow>();
        ConfigureIdentity(conversations, "conversations");
        ConfigureTime(conversations, "StartedAtTicks");

        var tasks = modelBuilder.Entity<TaskExecutionRow>();
        ConfigureIdentity(tasks, "task_executions");
        ConfigureLifecycle(tasks, "task_executions");
        tasks.HasIndex(row => new { row.StartedAtTicks, row.Id })
            .IsDescending().HasDatabaseName("ix_tasks_recent");
        tasks.ToTable(table =>
        {
            table.HasCheckConstraint("ck_task_sequence", "last_sequence >= 0");
            table.HasCheckConstraint("ck_task_objective", "length(btrim(objective)) > 0");
        });
        tasks.HasOne<ConversationRow>().WithMany().HasForeignKey(row => row.ConversationId)
            .OnDelete(DeleteBehavior.Restrict);

        var agents = modelBuilder.Entity<AgentExecutionRow>();
        ConfigureIdentity(agents, "agent_executions");
        ConfigureLifecycle(agents, "agent_executions");
        agents.HasAlternateKey(row => new { row.TaskExecutionId, row.Id });
        agents.HasOne<TaskExecutionRow>().WithMany().HasForeignKey(row => row.TaskExecutionId)
            .OnDelete(DeleteBehavior.Restrict);
        agents.HasOne<AgentExecutionRow>().WithMany()
            .HasForeignKey(row => new { row.TaskExecutionId, row.ParentAgentExecutionId })
            .HasPrincipalKey(row => new { row.TaskExecutionId, row.Id }).OnDelete(DeleteBehavior.Restrict);
        agents.ToTable(table => table.HasCheckConstraint("ck_agent_parent", "parent_agent_execution_id <> id"));
        agents.HasIndex(row => new { row.TaskExecutionId, row.StartedAtTicks, row.Id })
            .HasDatabaseName("ix_agents_timeline");

        var observations = modelBuilder.Entity<ObservationRow>();
        ConfigureIdentity(observations, "observations");
        ConfigureTime(observations, "ObservedAtTicks");
        ConfigureTime(observations, "RecordedAtTicks");
        observations.HasOne<TaskExecutionRow>().WithMany().HasForeignKey(row => row.TaskExecutionId)
            .OnDelete(DeleteBehavior.Restrict);
        observations.HasOne<AgentExecutionRow>().WithMany()
            .HasForeignKey(row => new { row.TaskExecutionId, row.AgentExecutionId })
            .HasPrincipalKey(row => new { row.TaskExecutionId, row.Id }).OnDelete(DeleteBehavior.Restrict);
        observations.HasIndex(row => new { row.TaskExecutionId, row.Sequence }).IsUnique()
            .HasDatabaseName("ux_observations_sequence");
        observations.HasIndex(row => new { row.TaskExecutionId, row.ObservedAtTicks, row.Sequence })
            .HasDatabaseName("ix_observations_timeline");
        observations.Property(row => row.Evidence).HasColumnType("bytea");
        observations.ToTable(table =>
        {
            table.HasCheckConstraint("ck_observation_sequence", "sequence > 0");
            table.HasCheckConstraint("ck_observation_summary", "length(btrim(summary)) > 0");
            table.HasCheckConstraint("ck_observation_kind",
                "kind IN ('message','toolinvocation','toolresult','statustransition','error','approval','testexecution','measurement')");
            table.HasCheckConstraint("ck_observation_evidence",
                "(evidence IS NULL AND media_type IS NULL) OR " +
                "(evidence IS NOT NULL AND octet_length(evidence) > 0 AND media_type IS NOT NULL AND length(btrim(media_type)) > 0)");
        });

        EvaluationRows.Configure(modelBuilder);
        CaptureRows.Configure(modelBuilder);

        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entity.GetProperties())
            {
                property.SetColumnName(Regex.Replace(property.Name, "([a-z0-9])([A-Z])", "$1_$2").ToLowerInvariant());
            }
        }
    }

    private static void ConfigureIdentity<T>(EntityTypeBuilder<T> entity, string table) where T : class
    {
        entity.ToTable(table, constraints =>
        {
            constraints.HasCheckConstraint($"ck_{table}_id", "id <> '00000000-0000-0000-0000-000000000000'::uuid");
            constraints.HasCheckConstraint($"ck_{table}_reference",
                "(runtime IS NULL AND resource_kind IS NULL AND external_id IS NULL) OR " +
                "(runtime IS NOT NULL AND resource_kind IS NOT NULL AND external_id IS NOT NULL " +
                "AND length(btrim(runtime)) > 0 AND length(btrim(resource_kind)) > 0 AND length(btrim(external_id)) > 0)");
        });
        entity.HasKey("Id").HasName($"pk_{table}");
        entity.Property<Guid>("Id").ValueGeneratedNever();
        foreach (var name in new[] { "Runtime", "ResourceKind", "ExternalId" })
        {
            entity.Property<string>(name).UseCollation("C");
        }

        entity.HasIndex("Runtime", "ResourceKind", "ExternalId").IsUnique()
            .HasFilter("runtime IS NOT NULL").HasDatabaseName($"ux_{table}_runtime_reference");
    }

    private static void ConfigureLifecycle<T>(EntityTypeBuilder<T> entity, string table) where T : class
    {
        ConfigureTime(entity, "StartedAtTicks");
        ConfigureTime(entity, "EndedAtTicks");
        entity.ToTable(constraints => constraints.HasCheckConstraint($"ck_{table}_lifecycle",
            "(status = 'running' AND ended_at_ticks IS NULL) OR " +
            "(status IN ('completed','failed','interrupted') AND ended_at_ticks IS NOT NULL AND ended_at_ticks >= started_at_ticks)"));
    }

    private static void ConfigureTime<T>(EntityTypeBuilder<T> entity, string property) where T : class
    {
        var column = Regex.Replace(property, "([a-z0-9])([A-Z])", "$1_$2").ToLowerInvariant();
        entity.ToTable(table => table.HasCheckConstraint($"ck_{entity.Metadata.GetTableName()}_{column}",
            $"{column} BETWEEN 0 AND 3155378975999999999"));
    }
}
