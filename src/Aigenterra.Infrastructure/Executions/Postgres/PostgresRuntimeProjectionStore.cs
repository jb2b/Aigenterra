using System.Data;
using System.Text.Json;
using Aigenterra.Application.Recording;
using Aigenterra.Application.Runtime;
using Microsoft.EntityFrameworkCore;

namespace Aigenterra.Infrastructure.Executions.Postgres;

public sealed class PostgresRuntimeProjectionStore(ExecutionDbContext db) : IRuntimeProjectionStore
{
    public async Task<RuntimeProjectionGeneration> FreezeAsync(Guid ns, RuntimeProjectionVersions versions, CancellationToken token = default)
    {
        if (ns == Guid.Empty) throw new ArgumentException("Explicit namespace required.");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, token);
        var rows = await db.Set<CaptureSourceRow>().FromSqlInterpolated($"""
            SELECT * FROM capture_sources WHERE envelope->'Source'->>'RuntimeNamespaceId' = {ns.ToString()}
            """).AsNoTracking().Join(db.Set<RawCaptureRow>(), s => s.CaptureId, r => r.CaptureId,
                (s, r) => new { s.CaptureId, r.Fingerprint }).ToListAsync(token);
        var manifest = rows.OrderBy(x => x.CaptureId).ToArray();
        var active = await ReadActiveAsync(ns, token);
        var id = Guid.NewGuid();
        var versionJson = RuntimeCanonicalJson.Serialize(versions);
        var hash = RuntimeCanonicalJson.Hash(RuntimeCanonicalJson.Serialize(manifest));
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO runtime_projection_generations (id, runtime_namespace_id, base_generation_id, versions, state,
                input_count, manifest_sha256, created_at_ticks)
            VALUES ({id}, {ns}, {active?.GenerationId}, CAST({versionJson} AS jsonb), 'Building',
                {manifest.Length}, {hash}, {DateTimeOffset.UtcNow.UtcTicks})
            """, token);
        // This transaction's snapshot fixes membership. Later commits, including lower allocated IDs, belong to a later manifest.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO runtime_projection_inputs (generation_id, capture_id, envelope_fingerprint, complete)
            SELECT {id}, s.capture_id, r.fingerprint, false FROM capture_sources s JOIN raw_captures r USING (capture_id)
            WHERE s.envelope->'Source'->>'RuntimeNamespaceId' = {ns.ToString()}
            """, token);
        await transaction.CommitAsync(token);
        return new(id, ns, active?.GenerationId, versions, "Building", manifest.Length, hash, null);
    }
    public async Task<RuntimeProjectionGeneration?> ReadGenerationAsync(Guid id, CancellationToken token = default)
    {
        var row = await db.Set<RuntimeProjectionGenerationRow>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, token);
        return row is null ? null : Generation(row);
    }
    public async Task<IReadOnlyList<RuntimeProjectionInput>> ReadInputsAsync(Guid id, CancellationToken token = default)
    {
        var rows = await db.Set<RuntimeProjectionInputRow>().AsNoTracking().Where(x => x.GenerationId == id)
            .Join(db.Set<CaptureSourceRow>(), i => i.CaptureId, c => c.CaptureId, (i, c) => new { Input = i, c.Envelope }).ToListAsync(token);
        return rows.Select(x => new RuntimeProjectionInput(x.Input.CaptureId, x.Input.EnvelopeFingerprint,
            JsonSerializer.Deserialize<RuntimeCaptureEnvelope>(x.Envelope)!.Source, x.Input.Complete)).OrderBy(x => x.CaptureId).ToArray();
    }
    public async Task SaveResultAsync(Guid id, RuntimeProjectionInputResult result, CancellationToken token = default)
    {
        var json = RuntimeCanonicalJson.Serialize(result);
        var hash = RuntimeCanonicalJson.Hash(json);
        var changed = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE runtime_projection_inputs SET result = CAST({json} AS jsonb), result_sha256 = {hash}, complete = true
            WHERE generation_id = {id} AND capture_id = {result.CaptureId} AND NOT complete
            """, token);
        if (changed == 0)
        {
            var previous = await db.Set<RuntimeProjectionInputRow>().AsNoTracking().SingleOrDefaultAsync(x => x.GenerationId == id && x.CaptureId == result.CaptureId, token);
            if (previous?.ResultSha256 != hash) throw new InvalidOperationException("Frozen projection input result conflict.");
        }
    }
    public async Task<IReadOnlyList<RuntimeProjectionInputResult>> ReadResultsAsync(Guid id, CancellationToken token = default)
    {
        var rows = await db.Set<RuntimeProjectionInputRow>().AsNoTracking().Where(x => x.GenerationId == id && x.Complete)
            .Select(x => x.Result!).ToListAsync(token);
        return rows.Select(x => JsonSerializer.Deserialize<RuntimeProjectionInputResult>(x)!).OrderBy(x => x.CaptureId).ToArray();
    }
    public async Task<RuntimeProjectionGeneration> CompleteAsync(Guid id, RuntimeProjectionDocument document, CancellationToken token = default)
    {
        var generation = await ReadGenerationAsync(id, token) ?? throw new ArgumentException("Unknown generation.");
        if (document.RuntimeNamespaceId != generation.RuntimeNamespaceId) throw new InvalidOperationException("Projection namespace mismatch.");
        var inputRows = await db.Set<RuntimeProjectionInputRow>().AsNoTracking().Where(x => x.GenerationId == id).ToListAsync(token);
        var manifestHash = RuntimeCanonicalJson.Hash(RuntimeCanonicalJson.Serialize(inputRows.OrderBy(x => x.CaptureId).Select(x => new { x.CaptureId, Fingerprint = x.EnvelopeFingerprint }).ToArray()));
        if (inputRows.Count != generation.InputCount || inputRows.Any(x => !x.Complete) || manifestHash != generation.ManifestSha256)
            throw new InvalidOperationException("Frozen manifest is incomplete or changed.");
        var json = RuntimeCanonicalJson.Serialize(document);
        var hash = RuntimeCanonicalJson.Hash(RuntimeCanonicalJson.Serialize(new { generation.ManifestSha256, generation.Versions, Document = document }));
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE runtime_projection_generations SET state = 'Ready', document = CAST({json} AS jsonb), projection_sha256 = {hash}
            WHERE id = {id} AND state = 'Building'
            """, token);
        var completed = (await ReadGenerationAsync(id, token))!;
        if (completed.ProjectionSha256 != hash) throw new InvalidOperationException("Projection checksum conflict.");
        return completed;
    }
    public async Task<RuntimeProjectionDocument?> ReadDocumentAsync(Guid id, CancellationToken token = default)
    {
        var json = await db.Set<RuntimeProjectionGenerationRow>().AsNoTracking().Where(x => x.Id == id && x.State == "Ready").Select(x => x.Document).SingleOrDefaultAsync(token);
        return json is null ? null : JsonSerializer.Deserialize<RuntimeProjectionDocument>(json);
    }
    public async Task<RuntimeProjectionActivation> ActivateAsync(Guid id, Guid? expectedActiveId, CancellationToken token = default)
    {
        var generation = await ReadGenerationAsync(id, token) ?? throw new ArgumentException("Unknown generation.");
        if (generation.State != "Ready") throw new InvalidOperationException("Only a ready generation may be activated.");
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        // Revision is allocated under a per-namespace transaction lock, so allocation and commit order cannot diverge.
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({generation.RuntimeNamespaceId.ToString()}, 0))", token);
        var active = await ReadActiveAsync(generation.RuntimeNamespaceId, token);
        if (active?.GenerationId == id) { await transaction.CommitAsync(token); return active; }
        if (active?.GenerationId != expectedActiveId) throw new InvalidOperationException("Active generation changed; refresh before activation or rollback.");
        var revision = (active?.Revision ?? 0) + 1;
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO runtime_projection_changes (runtime_namespace_id, revision, generation_id, previous_generation_id, projection_sha256)
            VALUES ({generation.RuntimeNamespaceId}, {revision}, {id}, {expectedActiveId}, {generation.ProjectionSha256});
            INSERT INTO runtime_projection_active (runtime_namespace_id, revision, generation_id)
            VALUES ({generation.RuntimeNamespaceId}, {revision}, {id})
            ON CONFLICT (runtime_namespace_id) DO UPDATE SET revision = EXCLUDED.revision, generation_id = EXCLUDED.generation_id;
            """, token);
        await transaction.CommitAsync(token);
        return new(generation.RuntimeNamespaceId, revision, id, expectedActiveId, generation.ProjectionSha256!);
    }
    public async Task<RuntimeProjectionActivation?> ReadActiveAsync(Guid ns, CancellationToken token = default)
    {
        var row = await db.Set<RuntimeProjectionActiveRow>().AsNoTracking().Where(x => x.RuntimeNamespaceId == ns)
            .Join(db.Set<RuntimeProjectionChangeRow>(), a => new { a.RuntimeNamespaceId, a.Revision }, c => new { c.RuntimeNamespaceId, c.Revision }, (a, c) => c).SingleOrDefaultAsync(token);
        return row is null ? null : new(row.RuntimeNamespaceId, row.Revision, row.GenerationId, row.PreviousGenerationId, row.ProjectionSha256);
    }
    public Task<bool> HasNewInputsAsync(Guid ns, Guid generationId, CancellationToken token = default) =>
        db.Set<CaptureSourceRow>().FromSqlInterpolated($"""
            SELECT s.* FROM capture_sources s WHERE s.envelope->'Source'->>'RuntimeNamespaceId' = {ns.ToString()}
            AND NOT EXISTS (SELECT 1 FROM runtime_projection_inputs i WHERE i.generation_id = {generationId} AND i.capture_id = s.capture_id)
            """).AsNoTracking().AnyAsync(token);
    public async Task<Guid?> FindBuildingAsync(Guid ns, RuntimeProjectionVersions versions, CancellationToken token = default)
    {
        var rows = await db.Set<RuntimeProjectionGenerationRow>().AsNoTracking().Where(x => x.RuntimeNamespaceId == ns && x.State == "Building")
            .OrderBy(x => x.CreatedAtTicks).ToListAsync(token);
        return rows.FirstOrDefault(x => JsonSerializer.Deserialize<RuntimeProjectionVersions>(x.Versions) == versions)?.Id;
    }
    private static RuntimeProjectionGeneration Generation(RuntimeProjectionGenerationRow row) => new(row.Id, row.RuntimeNamespaceId, row.BaseGenerationId,
        JsonSerializer.Deserialize<RuntimeProjectionVersions>(row.Versions)!, row.State, row.InputCount, row.ManifestSha256, row.ProjectionSha256);
}
