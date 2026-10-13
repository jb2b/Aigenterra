using System.Security.Cryptography;
using System.Text.Json;
using Aigenterra.Application.Recording;
using Microsoft.EntityFrameworkCore;

namespace Aigenterra.Infrastructure.Executions.Postgres;

/// <summary>Raw content and final interpretation receipts are insert-only and independently durable.</summary>
public sealed class PostgresRawCaptureStore(ExecutionDbContext db) : IRawCaptureStore
{
    public async Task<CaptureArchiveResult> ArchiveAsync(RuntimeCaptureEnvelope capture, CancellationToken cancellationToken = default)
    {
        var fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(capture)));
        var metadata = JsonSerializer.Serialize(capture with { Payload = [] });
        var context = capture.RepositoryContext is null ? null : JsonSerializer.Serialize(capture.RepositoryContext);
        // One statement commits both bytes and source identity. Legacy inserts and fingerprints are unchanged.
        var inserted = await db.Database.ExecuteSqlInterpolatedAsync($"""
            WITH added AS (
                INSERT INTO raw_captures (capture_id, version, captured_at_ticks, recorded_at_ticks, payload, fingerprint, context_phase, repository_context)
                VALUES ({capture.CaptureId}, {capture.Version}, {capture.CapturedAt.UtcTicks}, {DateTimeOffset.UtcNow.UtcTicks},
                    {capture.Payload}, {fingerprint}, {capture.ContextPhase}, CAST({context} AS jsonb))
                ON CONFLICT (capture_id) DO NOTHING RETURNING capture_id
            )
            INSERT INTO capture_sources (capture_id, envelope)
            SELECT capture_id, CAST({metadata} AS jsonb) FROM added
            """, cancellationToken);
        if (inserted == 1) return CaptureArchiveResult.Added;
        var existing = await db.Set<RawCaptureRow>().AsNoTracking().Where(x => x.CaptureId == capture.CaptureId)
            .Select(x => x.Fingerprint).SingleAsync(cancellationToken);
        return existing == fingerprint ? CaptureArchiveResult.Duplicate : CaptureArchiveResult.Conflict;
    }

    public async Task<ArchivedRuntimeCapture?> ReadRuntimeAsync(Guid captureId, CancellationToken cancellationToken = default)
    {
        var record = await (from source in db.Set<CaptureSourceRow>().AsNoTracking()
                            join raw in db.Set<RawCaptureRow>().AsNoTracking() on source.CaptureId equals raw.CaptureId
                            where source.CaptureId == captureId
                            select new { source.Envelope, source.IngestPosition, raw.Payload, raw.RecordedAtTicks })
            .SingleOrDefaultAsync(cancellationToken);
        if (record is null) return null;
        var envelope = JsonSerializer.Deserialize<RuntimeCaptureEnvelope>(record.Envelope)! with { Payload = record.Payload };
        return new(envelope, record.IngestPosition, new DateTimeOffset(record.RecordedAtTicks, TimeSpan.Zero));
    }

    public async Task<CaptureArchiveResult> ArchiveAsync(CaptureEnvelope capture, CancellationToken cancellationToken = default)
    {
        var fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(capture)));
        var context = capture.RepositoryContext is null ? null : JsonSerializer.Serialize(capture.RepositoryContext);
        var inserted = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO raw_captures (capture_id, version, captured_at_ticks, recorded_at_ticks, payload, fingerprint, context_phase, repository_context)
            VALUES ({capture.CaptureId}, {capture.Version}, {capture.CapturedAt.UtcTicks}, {DateTimeOffset.UtcNow.UtcTicks},
                {capture.Payload}, {fingerprint}, {capture.ContextPhase}, CAST({context} AS jsonb))
            ON CONFLICT (capture_id) DO NOTHING
            """, cancellationToken);
        if (inserted == 1) return CaptureArchiveResult.Added;
        var existing = await db.Set<RawCaptureRow>().AsNoTracking().Where(x => x.CaptureId == capture.CaptureId)
            .Select(x => x.Fingerprint).SingleAsync(cancellationToken);
        return existing == fingerprint ? CaptureArchiveResult.Duplicate : CaptureArchiveResult.Conflict;
    }

    public async Task<CaptureOutcome?> FindOutcomeAsync(Guid captureId, CancellationToken cancellationToken = default)
    {
        var row = await db.Set<CaptureOutcomeRow>().AsNoTracking().SingleOrDefaultAsync(x => x.CaptureId == captureId, cancellationToken);
        return row is null ? null : new(row.CaptureId, row.Disposition, row.Reason, row.ConversationId,
            row.TaskExecutionId, row.AgentExecutionId, row.ObservationId);
    }

    public async Task CompleteAsync(CaptureOutcome outcome, CancellationToken cancellationToken = default)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO capture_outcomes (capture_id, disposition, reason, conversation_id, task_execution_id, agent_execution_id, observation_id)
            VALUES ({outcome.CaptureId}, {outcome.Disposition}, {outcome.Reason}, {outcome.ConversationId},
                {outcome.TaskExecutionId}, {outcome.AgentExecutionId}, {outcome.ObservationId})
            ON CONFLICT (capture_id) DO NOTHING
            """, cancellationToken);
    }
}
