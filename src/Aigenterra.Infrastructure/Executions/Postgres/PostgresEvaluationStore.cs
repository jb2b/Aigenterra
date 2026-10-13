using System.Data;
using Aigenterra.Application.Evaluations;
using Aigenterra.Application.Executions;
using Aigenterra.Domain.Evaluations;
using Aigenterra.Domain.Executions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Aigenterra.Infrastructure.Executions.Postgres;

public sealed class PostgresEvaluationStore(ExecutionDbContext db) : IExecutionEvaluationInputReader, IEvaluationStore
{
    public async ValueTask<EvaluationInput?> ReadAsync(TaskExecutionId taskId, CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY", cancellationToken);
        var watermark = await db.Tasks.Where(t => t.Id == taskId.Value).Select(t => (long?)t.LastSequence).SingleOrDefaultAsync(cancellationToken);
        if (watermark is null) return null;
        var observations = await db.Observations.AsNoTracking().Where(o => o.TaskExecutionId == taskId.Value && o.Sequence <= watermark)
            .OrderBy(o => o.Sequence).Select(o => new
            {
                o.Id,
                o.Sequence,
                o.ObservedAtTicks,
                o.Kind,
                o.Runtime,
                o.ResourceKind,
                o.ExternalId,
                o.MediaType,
                Evidence = o.Kind == "toolinvocation" || o.Kind == "toolresult" || o.Kind == "testexecution" ? o.Evidence : null
            }).ToArrayAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(taskId, watermark.Value, observations.Select(o => new EvaluationObservation(new(o.Id), o.Sequence,
            ExecutionMapping.Time(o.ObservedAtTicks), o.Kind,
            o.Runtime is null ? null : new RuntimeReferenceModel(o.Runtime, o.ResourceKind!, o.ExternalId!),
            o.Evidence is null ? null : new SourceEvidence(o.MediaType!, o.Evidence))).ToArray());
    }

    public async ValueTask<StoredEvaluation> SaveAsync(Evaluation evaluation, CancellationToken cancellationToken = default)
    {
        var duplicateId = await FindSnapshotAsync(evaluation, cancellationToken);
        if (duplicateId is { } known) return (await GetAsync(evaluation.TaskExecutionId, known, cancellationToken))!;
        var ids = evaluation.Findings.SelectMany(f => f.Attempts).SelectMany(a => a.Evidence).Select(id => id.Value).Distinct().ToArray();
        var valid = await db.Observations.CountAsync(o => ids.Contains(o.Id) && o.TaskExecutionId == evaluation.TaskExecutionId.Value
            && o.Sequence <= evaluation.Watermark, cancellationToken);
        if (valid != ids.Length) throw new InvalidOperationException("Evaluation evidence must belong to its task and snapshot.");
        var watermark = await db.Tasks.Where(t => t.Id == evaluation.TaskExecutionId.Value).Select(t => t.LastSequence).SingleAsync(cancellationToken);
        if (evaluation.Watermark > watermark) throw new InvalidOperationException("Evaluation watermark exceeds captured evidence.");
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        db.Add(new EvaluationRow
        {
            Id = evaluation.Id,
            TaskExecutionId = evaluation.TaskExecutionId.Value,
            CreatedAtTicks = evaluation.CreatedAt.UtcTicks,
            EvaluatorId = evaluation.EvaluatorId,
            Version = evaluation.Version,
            Watermark = evaluation.Watermark
        });
        foreach (var finding in evaluation.Findings)
        {
            db.Add(new EvaluationFindingRow
            {
                TaskExecutionId = evaluation.TaskExecutionId.Value,
                EvaluationId = evaluation.Id,
                Check = finding.Check.ToString(),
                Detection = finding.Detection.ToString(),
                Limitations = finding.Limitations
            });
            foreach (var attempt in finding.Attempts)
            {
                db.Add(new EvaluationAttemptRow
                {
                    TaskExecutionId = evaluation.TaskExecutionId.Value,
                    EvaluationId = evaluation.Id,
                    Check = finding.Check.ToString(),
                    Number = attempt.Number,
                    Verdict = attempt.Verdict.ToString(),
                    ExitCode = attempt.ExitCode,
                    Explanation = attempt.Explanation
                });
                for (var i = 0; i < attempt.Evidence.Count; i++)
                    db.Add(new EvaluationEvidenceRow
                    {
                        TaskExecutionId = evaluation.TaskExecutionId.Value,
                        EvaluationId = evaluation.Id,
                        Check = finding.Check.ToString(),
                        Number = attempt.Number,
                        ObservationId = attempt.Evidence[i].Value,
                        Position = i
                    });
            }
        }
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            await transaction.RollbackAsync(cancellationToken);
            db.ChangeTracker.Clear();
            await transaction.DisposeAsync();
            duplicateId = await FindSnapshotAsync(evaluation, cancellationToken);
            if (duplicateId is null) throw;
            return (await GetAsync(evaluation.TaskExecutionId, duplicateId.Value, cancellationToken))!;
        }
        db.ChangeTracker.Clear();
        return new(evaluation, true, watermark > evaluation.Watermark);
    }

    private Task<Guid?> FindSnapshotAsync(Evaluation e, CancellationToken ct) => db.Set<EvaluationRow>()
        .Where(r => r.TaskExecutionId == e.TaskExecutionId.Value && r.EvaluatorId == e.EvaluatorId && r.Version == e.Version && r.Watermark == e.Watermark)
        .Select(r => (Guid?)r.Id).SingleOrDefaultAsync(ct);

    public async ValueTask<StoredEvaluation?> GetAsync(TaskExecutionId taskId, Guid evaluationId, CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY", cancellationToken);
        var row = await db.Set<EvaluationRow>().AsNoTracking().SingleOrDefaultAsync(e => e.Id == evaluationId && e.TaskExecutionId == taskId.Value, cancellationToken);
        if (row is null) return null;
        var findings = await db.Set<EvaluationFindingRow>().AsNoTracking().Where(e => e.EvaluationId == evaluationId).ToArrayAsync(cancellationToken);
        var attempts = await db.Set<EvaluationAttemptRow>().AsNoTracking().Where(e => e.EvaluationId == evaluationId).OrderBy(e => e.Number).ToArrayAsync(cancellationToken);
        var evidence = await db.Set<EvaluationEvidenceRow>().AsNoTracking().Where(e => e.EvaluationId == evaluationId).OrderBy(e => e.Position).ToArrayAsync(cancellationToken);
        var watermark = await db.Tasks.Where(t => t.Id == taskId.Value).Select(t => t.LastSequence).SingleAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(new(row.Id, taskId, ExecutionMapping.Time(row.CreatedAtTicks), row.EvaluatorId, row.Version, row.Watermark,
            findings.Select(f => new EvaluationFinding(Enum.Parse<EvaluationCheck>(f.Check), Enum.Parse<Detection>(f.Detection), f.Limitations,
                attempts.Where(a => a.Check == f.Check).Select(a => new EvaluationAttempt(a.Number, Enum.Parse<CommandVerdict>(a.Verdict), a.ExitCode,
                    a.Explanation, evidence.Where(e => e.Check == f.Check && e.Number == a.Number).Select(e => new ObservationId(e.ObservationId))))))), false, watermark > row.Watermark);
    }

    public async ValueTask<EvaluationPage?> ListAsync(TaskExecutionId taskId, Guid? beforeId, CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY", cancellationToken);
        var watermark = await db.Tasks.Where(t => t.Id == taskId.Value).Select(t => (long?)t.LastSequence).SingleOrDefaultAsync(cancellationToken);
        if (watermark is null) return null;
        var query = db.Set<EvaluationRow>().AsNoTracking().Where(e => e.TaskExecutionId == taskId.Value);
        if (beforeId is { } before) query = query.Where(e => e.Id.CompareTo(before) < 0);
        var rows = await query.OrderByDescending(e => e.Id).Take(21).ToArrayAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        var items = rows.Take(20).Select(e => new EvaluationSummary(e.Id, ExecutionMapping.Time(e.CreatedAtTicks), e.EvaluatorId,
            e.Version, e.Watermark, watermark > e.Watermark)).ToArray();
        return new(items, rows.Length > 20 ? Convert.ToBase64String(items[^1].Id.ToByteArray()) : null);
    }
}
