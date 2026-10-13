using System.Text.Json;
using Aigenterra.Application.Runtime;
using Microsoft.EntityFrameworkCore;

namespace Aigenterra.Infrastructure.Executions.Postgres;

internal sealed class RuntimeEvidenceRow
{
    public Guid CaptureId { get; set; }
    public string NormalizerVersion { get; set; } = null!;
    public string Batch { get; set; } = null!;
    public string Fingerprint { get; set; } = null!;
}
public sealed class PostgresRuntimeEvidenceStore(ExecutionDbContext db) : IRuntimeEvidenceStore
{
    public async Task AppendAsync(RuntimeEvidenceBatch batch, CancellationToken token = default)
    {
        if (batch.Facts.Select((f, i) => f.CaptureId != batch.CaptureId || f.Ordinal != i).Any(x => x)) throw new ArgumentException("Invalid evidence ordinals.");
        var json = JsonSerializer.Serialize(batch);
        var fingerprint = RuntimeIdentity.Key(json);
        var added = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO runtime_evidence (capture_id, normalizer_version, batch, fingerprint)
            VALUES ({batch.CaptureId}, {batch.NormalizerVersion}, CAST({json} AS jsonb), {fingerprint})
            ON CONFLICT (capture_id, normalizer_version) DO NOTHING
            """, token);
        if (added == 0 && (await db.Set<RuntimeEvidenceRow>().AsNoTracking().SingleAsync(x => x.CaptureId == batch.CaptureId && x.NormalizerVersion == batch.NormalizerVersion, token)).Fingerprint != fingerprint)
            throw new InvalidOperationException("Evidence interpretation conflict; use a new normalizer version.");
    }
    public async Task<IReadOnlyList<RuntimeEvidenceBatch>> ReadAsync(Guid runtimeNamespaceId, string normalizerVersion, CancellationToken token = default)
    {
        var json = await db.Set<RuntimeEvidenceRow>().FromSqlInterpolated($"""
            SELECT e.* FROM runtime_evidence e JOIN capture_sources s USING (capture_id)
            WHERE e.normalizer_version = {normalizerVersion} AND s.envelope->'Source'->>'RuntimeNamespaceId' = {runtimeNamespaceId.ToString()}
            """).AsNoTracking().Select(x => x.Batch).ToListAsync(token);
        return json.Select(x => JsonSerializer.Deserialize<RuntimeEvidenceBatch>(x)!).ToArray();
    }
    internal static void Configure(ModelBuilder builder)
    {
        var row = builder.Entity<RuntimeEvidenceRow>();
        row.ToTable("runtime_evidence");
        row.HasKey(x => new { x.CaptureId, x.NormalizerVersion });
        row.Property(x => x.Batch).HasColumnType("jsonb");
        row.HasOne<CaptureSourceRow>().WithMany().HasForeignKey(x => x.CaptureId).OnDelete(DeleteBehavior.Restrict);
    }
}
