using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Aigenterra.Application.Runtime;
using Microsoft.EntityFrameworkCore;

namespace Aigenterra.Infrastructure.Executions.Postgres;

public sealed class PostgresRuntimeSourceHealthStore(ExecutionDbContext db) : IRuntimeSourceHealthStore
{
    public async Task AppendAsync(RuntimeSourceHealth health, long ingestPosition, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(health);
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var added = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO source_health_events (capture_id, detector_version, ingest_position, health, fingerprint)
            VALUES ({health.CaptureId}, {health.DetectorVersion}, {ingestPosition}, CAST({json} AS jsonb), {fingerprint})
            ON CONFLICT (capture_id, detector_version) DO NOTHING
            """, cancellationToken);
        if (added == 0)
        {
            var existing = await db.Set<SourceHealthEventRow>().AsNoTracking()
                .SingleAsync(x => x.CaptureId == health.CaptureId && x.DetectorVersion == health.DetectorVersion, cancellationToken);
            if (existing.Fingerprint != fingerprint || existing.IngestPosition != ingestPosition)
                throw new InvalidOperationException("Source health event conflicts with an existing interpretation.");
            return;
        }
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO observation_sources (runtime_namespace_id, source_id, runtime_instance_id, latest_position, latest_capture_id, detector_version)
            VALUES ({health.RuntimeNamespaceId}, {health.SourceId}, {health.RuntimeInstanceId}, {ingestPosition}, {health.CaptureId}, {health.DetectorVersion})
            ON CONFLICT (runtime_namespace_id, source_id) DO UPDATE
            SET runtime_instance_id = EXCLUDED.runtime_instance_id, latest_position = EXCLUDED.latest_position,
                latest_capture_id = EXCLUDED.latest_capture_id, detector_version = EXCLUDED.detector_version
            WHERE observation_sources.latest_position < EXCLUDED.latest_position
            """, cancellationToken);
        if (health.RuntimeInstanceId is { } instance)
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO runtime_instances (runtime_namespace_id, runtime_instance_id, latest_position, latest_capture_id, detector_version)
                VALUES ({health.RuntimeNamespaceId}, {instance}, {ingestPosition}, {health.CaptureId}, {health.DetectorVersion})
                ON CONFLICT (runtime_namespace_id, runtime_instance_id) DO UPDATE
                SET latest_position = EXCLUDED.latest_position, latest_capture_id = EXCLUDED.latest_capture_id, detector_version = EXCLUDED.detector_version
                WHERE runtime_instances.latest_position < EXCLUDED.latest_position
                """, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<RuntimeSourceHealth?> ReadAsync(Guid runtimeNamespaceId, Guid sourceId, CancellationToken cancellationToken = default)
    {
        var json = await (from source in db.Set<ObservationSourceRow>().AsNoTracking()
                          join health in db.Set<SourceHealthEventRow>().AsNoTracking()
                              on new { CaptureId = source.LatestCaptureId, source.DetectorVersion } equals new { health.CaptureId, health.DetectorVersion }
                          where source.RuntimeNamespaceId == runtimeNamespaceId && source.SourceId == sourceId
                          select health.Health).SingleOrDefaultAsync(cancellationToken);
        return json is null ? null : JsonSerializer.Deserialize<RuntimeSourceHealth>(json);
    }

    public async Task<IReadOnlyList<RuntimeInstanceInventory>> ReadInstancesAsync(Guid runtimeNamespaceId, long after, int limit,
        CancellationToken cancellationToken = default)
    {
        var rows = await (from instance in db.Set<RuntimeInstanceRow>().AsNoTracking()
                          join health in db.Set<SourceHealthEventRow>().AsNoTracking()
                              on new { CaptureId = instance.LatestCaptureId, instance.DetectorVersion } equals new { health.CaptureId, health.DetectorVersion }
                          where instance.RuntimeNamespaceId == runtimeNamespaceId && instance.LatestPosition > after
                          orderby instance.LatestPosition
                          select new { instance.LatestPosition, health.Health }).Take(limit).ToListAsync(cancellationToken);
        return rows.Select(row => new RuntimeInstanceInventory(row.LatestPosition, JsonSerializer.Deserialize<RuntimeSourceHealth>(row.Health)!)).ToArray();
    }
}
