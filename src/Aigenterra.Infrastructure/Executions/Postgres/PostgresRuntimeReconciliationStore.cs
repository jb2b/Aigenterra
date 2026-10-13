using System.Text.Json;
using Aigenterra.Application.Runtime;
using Microsoft.EntityFrameworkCore;

namespace Aigenterra.Infrastructure.Executions.Postgres;

internal sealed class RuntimeReconciliationRow
{
    public Guid CaptureId { get; set; }
    public Guid RuntimeNamespaceId { get; set; }
    public Guid SourceId { get; set; }
    public long IngestPosition { get; set; }
    public string Kind { get; set; } = null!;
    public string Record { get; set; } = null!;
    public string Fingerprint { get; set; } = null!;
}
public sealed class PostgresRuntimeReconciliationStore(ExecutionDbContext db) : IRuntimeReconciliationStore
{
    public async Task AppendAsync(RuntimeReconciliationRecord record, CancellationToken token = default)
    {
        var json = JsonSerializer.Serialize(record);
        var fingerprint = RuntimeIdentity.Key(json);
        var added = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO runtime_reconciliation (capture_id, runtime_namespace_id, source_id, ingest_position, kind, record, fingerprint)
            VALUES ({record.CaptureId}, {record.RuntimeNamespaceId}, {record.SourceId}, {record.IngestPosition}, {record.Input.Kind}, CAST({json} AS jsonb), {fingerprint})
            ON CONFLICT (capture_id) DO NOTHING
            """, token);
        if (added == 0 && (await db.Set<RuntimeReconciliationRow>().AsNoTracking().SingleAsync(x => x.CaptureId == record.CaptureId, token)).Fingerprint != fingerprint)
            throw new InvalidOperationException("Reconciliation input conflict.");
    }
    public async Task<RuntimeEnrollment?> ReadEnrollmentAsync(Guid runtimeNamespaceId, Guid sourceId, CancellationToken token = default)
    {
        var row = await Query(runtimeNamespaceId, sourceId).Where(x => x.Kind == "enrollment").OrderByDescending(x => x.IngestPosition).FirstOrDefaultAsync(token);
        return row is null ? null : JsonSerializer.Deserialize<RuntimeReconciliationRecord>(row.Record)!.Input.Enrollment;
    }
    public async Task<IReadOnlyList<ScopedRuntimeEvidence>> ReadEvidenceAsync(Guid runtimeNamespaceId, IReadOnlyList<Guid> sources, CancellationToken token = default)
    {
        var ids = sources.Select(x => x.ToString()).ToArray();
        var rows = await db.Set<RuntimeEvidenceRow>().FromSqlInterpolated($"""
            SELECT e.* FROM runtime_evidence e JOIN capture_sources s USING (capture_id)
            WHERE e.normalizer_version = 'codex.evidence/2' AND s.envelope->'Source'->>'RuntimeNamespaceId' = {runtimeNamespaceId.ToString()}
              AND s.envelope->'Source'->>'SourceId' = ANY ({ids})
            """).AsNoTracking().Join(db.Set<CaptureSourceRow>(), e => e.CaptureId, s => s.CaptureId,
                (e, s) => new { e.Batch, s.Envelope }).ToListAsync(token);
        return rows.Select(row =>
        {
            var envelope = JsonSerializer.Deserialize<Application.Recording.RuntimeCaptureEnvelope>(row.Envelope)!;
            return new ScopedRuntimeEvidence(envelope.Source.SourceId, envelope.Source.SourceKind, JsonSerializer.Deserialize<RuntimeEvidenceBatch>(row.Batch)!);
        }).ToArray();
    }
    public async Task<RuntimeReconciliationHealth?> ReadHealthAsync(Guid runtimeNamespaceId, Guid sourceId, CancellationToken token = default)
    {
        var query = Query(runtimeNamespaceId, sourceId);
        var latest = await query.OrderByDescending(x => x.IngestPosition).FirstOrDefaultAsync(token);
        if (latest is null) return null;
        var gaps = await query.Where(x => x.Kind == "gap").OrderByDescending(x => x.IngestPosition).Take(100).ToListAsync(token);
        var record = JsonSerializer.Deserialize<RuntimeReconciliationRecord>(latest.Record)!;
        return new(record.Input.Outcome, "Unknown", "Partial", "KEEP_GATE_BLOCKED", record,
            await query.LongCountAsync(x => x.Kind == "gap", token), gaps.Select(x => JsonSerializer.Deserialize<RuntimeReconciliationRecord>(x.Record)!).ToArray());
    }
    private IQueryable<RuntimeReconciliationRow> Query(Guid ns, Guid source) => db.Set<RuntimeReconciliationRow>().AsNoTracking()
        .Where(x => x.RuntimeNamespaceId == ns && x.SourceId == source);
    internal static void Configure(ModelBuilder builder)
    {
        var row = builder.Entity<RuntimeReconciliationRow>();
        row.ToTable("runtime_reconciliation"); row.HasKey(x => x.CaptureId); row.Property(x => x.CaptureId).ValueGeneratedNever();
        row.Property(x => x.Record).HasColumnType("jsonb");
        row.HasIndex(x => new { x.RuntimeNamespaceId, x.SourceId, x.IngestPosition });
        row.HasOne<CaptureSourceRow>().WithMany().HasForeignKey(x => x.CaptureId).OnDelete(DeleteBehavior.Restrict);
    }
}
