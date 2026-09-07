using System.Data;
using Aigenterra.Application.Executions;
using Aigenterra.Application.Executions.Ports;
using Aigenterra.Domain.Executions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Aigenterra.Infrastructure.Executions.Postgres;

/// <summary>
/// Implements execution capabilities with short database operations; no tracked Domain objects escape.
/// </summary>
public sealed class PostgresExecutionStore(ExecutionDbContext db) :
    IExecutionWriteStore, IExecutionRuntimeReferenceReader, ITaskExecutionTimelineReader
{
    public ValueTask<StoreAddResult> AddConversationAsync(Conversation conversation, CancellationToken cancellationToken = default) =>
        AddAsync(ExecutionMapping.ToRow(conversation), "conversations", cancellationToken);

    public ValueTask<StoreAddResult> AddTaskExecutionAsync(TaskExecution taskExecution, CancellationToken cancellationToken = default) =>
        AddAsync(ExecutionMapping.ToRow(taskExecution), "task_executions", cancellationToken);

    public ValueTask<StoreAddResult> AddAgentExecutionAsync(AgentExecution agentExecution, CancellationToken cancellationToken = default) =>
        AddAsync(ExecutionMapping.ToRow(agentExecution), "agent_executions", cancellationToken);

    public async ValueTask<Conversation?> FindConversationAsync(ConversationId conversationId, CancellationToken cancellationToken = default)
    {
        var row = await db.Conversations.AsNoTracking().SingleOrDefaultAsync(row => row.Id == conversationId.Value, cancellationToken);
        return row is null ? null : ExecutionMapping.ToDomain(row);
    }

    public async ValueTask<TaskExecution?> FindTaskExecutionAsync(TaskExecutionId taskExecutionId, CancellationToken cancellationToken = default)
    {
        var row = await db.Tasks.AsNoTracking().SingleOrDefaultAsync(row => row.Id == taskExecutionId.Value, cancellationToken);
        return row is null ? null : ExecutionMapping.ToDomain(row);
    }

    public async ValueTask<AgentExecution?> FindAgentExecutionAsync(AgentExecutionId agentExecutionId, CancellationToken cancellationToken = default)
    {
        var row = await db.Agents.AsNoTracking().SingleOrDefaultAsync(row => row.Id == agentExecutionId.Value, cancellationToken);
        return row is null ? null : ExecutionMapping.ToDomain(row);
    }

    public async ValueTask<bool> TryFinishTaskExecutionAsync(TaskExecution taskExecution, CancellationToken cancellationToken = default)
    {
        var endedAt = RequireTerminal(taskExecution.Status, taskExecution.EndedAt);
        var status = ExecutionMapping.Contract(taskExecution.Status);
        return await db.Tasks.Where(row => row.Id == taskExecution.Id.Value && row.Status == "running")
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Status, status)
                .SetProperty(row => row.EndedAtTicks, endedAt), cancellationToken) == 1;
    }

    public async ValueTask<bool> TryFinishAgentExecutionAsync(AgentExecution agentExecution, CancellationToken cancellationToken = default)
    {
        var endedAt = RequireTerminal(agentExecution.Status, agentExecution.EndedAt);
        var status = ExecutionMapping.Contract(agentExecution.Status);
        return await db.Agents.Where(row => row.Id == agentExecution.Id.Value && row.Status == "running")
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Status, status)
                .SetProperty(row => row.EndedAtTicks, endedAt), cancellationToken) == 1;
    }

    public async ValueTask<AppendObservationStoreResult> AppendObservationAsync(Observation observation,
        SourceEvidence? sourceEvidence, DateTimeOffset recordedAt, CancellationToken cancellationToken = default)
    {
        // Copy before the first await: the caller owns the supplied memory.
        var row = new ObservationRow
        {
            Id = observation.Id.Value,
            TaskExecutionId = observation.TaskExecutionId.Value,
            AgentExecutionId = observation.AgentExecutionId?.Value,
            ObservedAtTicks = observation.ObservedAt.UtcTicks,
            RecordedAtTicks = recordedAt.UtcTicks,
            Kind = ExecutionMapping.Contract(observation.Kind),
            Summary = observation.Summary,
            Runtime = observation.RuntimeReference?.Runtime,
            ResourceKind = observation.RuntimeReference?.ResourceKind,
            ExternalId = observation.RuntimeReference?.ExternalId,
            MediaType = sourceEvidence?.MediaType,
            Evidence = sourceEvidence?.Content.ToArray()
        };
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        try
        {
            // Materialize directly: composing SELECT over UPDATE RETURNING is not valid PostgreSQL.
            var sequence = await db.Database.SqlQuery<long>($"""
                UPDATE task_executions SET last_sequence = last_sequence + 1
                WHERE id = {row.TaskExecutionId} RETURNING last_sequence AS "Value"
                """).ToArrayAsync(cancellationToken);
            if (sequence.Length != 1)
            {
                throw new InvalidOperationException("Cannot append to an unknown task execution.");
            }

            row.Sequence = sequence[0];
            db.Observations.Add(row);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(StoreAddResult.Added, new(observation.Id, row.Sequence, recordedAt.ToUniversalTime()));
        }
        catch (DbUpdateException exception) when (Duplicate(exception, "observations") is not null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new(Duplicate(exception, "observations")!.Value, null);
        }
        finally
        {
            db.Entry(row).State = EntityState.Detached;
        }
    }

    public async ValueTask<ConversationId?> FindConversationIdAsync(RuntimeReference runtimeReference, CancellationToken cancellationToken = default)
    {
        var id = await FindIdAsync(db.Conversations, runtimeReference, cancellationToken);
        return id is { } value ? new ConversationId(value) : null;
    }

    public async ValueTask<TaskExecutionId?> FindTaskExecutionIdAsync(RuntimeReference runtimeReference, CancellationToken cancellationToken = default)
    {
        var id = await FindIdAsync(db.Tasks, runtimeReference, cancellationToken);
        return id is { } value ? new TaskExecutionId(value) : null;
    }

    public async ValueTask<AgentExecutionId?> FindAgentExecutionIdAsync(RuntimeReference runtimeReference, CancellationToken cancellationToken = default)
    {
        var id = await FindIdAsync(db.Agents, runtimeReference, cancellationToken);
        return id is { } value ? new AgentExecutionId(value) : null;
    }

    public async ValueTask<ObservationId?> FindObservationIdAsync(RuntimeReference runtimeReference, CancellationToken cancellationToken = default)
    {
        var id = await FindIdAsync(db.Observations, runtimeReference, cancellationToken);
        return id is { } value ? new ObservationId(value) : null;
    }

    public async ValueTask<TaskExecutionTimeline?> GetTimelineAsync(TaskExecutionId taskExecutionId, CancellationToken cancellationToken = default)
    {
        // Snapshot consistency is a local read strategy, not an Application-port requirement.
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY", cancellationToken);
        var task = await db.Tasks.AsNoTracking().Where(row => row.Id == taskExecutionId.Value)
            .Select(row => new
            {
                row.Id,
                row.ConversationId,
                row.Objective,
                row.Status,
                row.StartedAtTicks,
                row.EndedAtTicks,
                row.Runtime,
                row.ResourceKind,
                row.ExternalId
            }).SingleOrDefaultAsync(cancellationToken);
        if (task is null)
        {
            return null;
        }

        var agents = await db.Agents.AsNoTracking().Where(row => row.TaskExecutionId == taskExecutionId.Value)
            .OrderBy(row => row.StartedAtTicks).ThenBy(row => row.Id)
            .Select(row => new AgentExecutionTimelineItem(row.Id, row.ParentAgentExecutionId, row.Status,
                ExecutionMapping.Time(row.StartedAtTicks), row.EndedAtTicks.HasValue ? ExecutionMapping.Time(row.EndedAtTicks.Value) : null,
                row.Runtime == null ? null : new RuntimeReferenceModel(row.Runtime, row.ResourceKind!, row.ExternalId!)))
            .ToArrayAsync(cancellationToken);
        var observations = await db.Observations.AsNoTracking().Where(row => row.TaskExecutionId == taskExecutionId.Value)
            .OrderBy(row => row.ObservedAtTicks).ThenBy(row => row.Sequence)
            .Select(row => new ObservationTimelineItem(row.Id, row.AgentExecutionId,
                ExecutionMapping.Time(row.ObservedAtTicks), ExecutionMapping.Time(row.RecordedAtTicks), row.Sequence,
                row.Kind, row.Summary,
                row.Runtime == null ? null : new RuntimeReferenceModel(row.Runtime, row.ResourceKind!, row.ExternalId!),
                row.Evidence != null)).ToArrayAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(task.Id, task.ConversationId, task.Objective, task.Status,
            ExecutionMapping.Time(task.StartedAtTicks), task.EndedAtTicks is { } end ? ExecutionMapping.Time(end) : null,
            task.Runtime is null ? null : new(task.Runtime, task.ResourceKind!, task.ExternalId!), agents, observations);
    }

    private async ValueTask<StoreAddResult> AddAsync<T>(T row, string table, CancellationToken cancellationToken) where T : class
    {
        db.Add(row);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return StoreAddResult.Added;
        }
        catch (DbUpdateException exception) when (Duplicate(exception, table) is not null)
        {
            return Duplicate(exception, table)!.Value;
        }
        finally
        {
            db.Entry(row).State = EntityState.Detached;
        }
    }

    private static StoreAddResult? Duplicate(DbUpdateException exception, string table) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } postgres
            ? postgres.ConstraintName switch
            {
                var name when name == $"pk_{table}" => StoreAddResult.DuplicateId,
                var name when name == $"ux_{table}_runtime_reference" => StoreAddResult.DuplicateRuntimeReference,
                _ => null
            }
            : null;

    private static Task<Guid?> FindIdAsync<T>(DbSet<T> rows, RuntimeReference reference, CancellationToken cancellationToken) where T : class =>
        rows.AsNoTracking().Where(row => EF.Property<string>(row, "Runtime") == reference.Runtime
            && EF.Property<string>(row, "ResourceKind") == reference.ResourceKind
            && EF.Property<string>(row, "ExternalId") == reference.ExternalId)
            .Select(row => (Guid?)EF.Property<Guid>(row, "Id")).SingleOrDefaultAsync(cancellationToken);

    private static long RequireTerminal(ExecutionStatus status, DateTimeOffset? endedAt) =>
        status != ExecutionStatus.Running && Enum.IsDefined(status) && endedAt is { } time
            ? time.UtcTicks
            : throw new ArgumentException("A validated terminal execution is required.");
}
