using System.Text.Json;
using Aigenterra.Application.Runtime;
using Microsoft.EntityFrameworkCore;

namespace Aigenterra.Infrastructure.Executions.Postgres;

public sealed class PostgresRuntimeWorkStore(ExecutionDbContext db) : IRuntimeWorkStore, IRuntimeRequestStore
{
    public async Task<RuntimeWork?> ClaimAsync(bool normalize, bool health, CancellationToken cancellationToken = default)
    {
        var lease = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow.UtcTicks;
        var until = DateTimeOffset.UtcNow.AddMinutes(2).UtcTicks;
        // Identity allocation order is not commit order. Every archive has its own durable work row.
        var claimed = await db.Set<RuntimeWorkRow>().FromSqlInterpolated($"""
            UPDATE runtime_work SET lease_id = {lease}, available_at_ticks = {until}, attempts = attempts + 1
            WHERE capture_id = (SELECT w.capture_id FROM runtime_work w JOIN capture_sources s USING (capture_id)
                WHERE NOT w.complete AND w.available_at_ticks <= {now}
                  AND (CASE WHEN s.envelope->'Source'->>'Provenance' = 'inventory' THEN {health} ELSE {normalize} END)
                ORDER BY w.available_at_ticks, s.ingest_position FOR UPDATE OF w SKIP LOCKED LIMIT 1)
            RETURNING *
            """).AsNoTracking().ToListAsync(cancellationToken);
        return claimed.Count == 0 ? null : new(claimed[0].CaptureId, lease, claimed[0].Attempts);
    }

    public async Task FinishAsync(RuntimeWork work, string outcome, bool retry, CancellationToken cancellationToken = default)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE runtime_work SET complete = {!retry}, outcome = {outcome}, lease_id = NULL,
                available_at_ticks = {DateTimeOffset.UtcNow.AddSeconds(Math.Min(60, Math.Max(1, work.Attempts) * 5)).UtcTicks}
            WHERE capture_id = {work.CaptureId} AND lease_id = {work.LeaseId}
            """, cancellationToken);
    }

    public async Task AppendAsync(VerifiedRuntimeRequest request, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(request);
        var added = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO runtime_requests (response_capture_id, context_capture_id, request_capture_id, request)
            VALUES ({request.ResponseCaptureId}, {request.ContextCaptureId}, {request.RequestCaptureId}, CAST({json} AS jsonb))
            ON CONFLICT (response_capture_id) DO NOTHING
            """, cancellationToken);
        if (added == 0 && await ReadAsync(request.ResponseCaptureId, cancellationToken) != request)
            throw new InvalidOperationException("Conflicting request context.");
    }

    public async Task<VerifiedRuntimeRequest?> ReadAsync(Guid responseCaptureId, CancellationToken cancellationToken = default)
    {
        var row = await db.Set<RuntimeRequestRow>().AsNoTracking().SingleOrDefaultAsync(x => x.ResponseCaptureId == responseCaptureId, cancellationToken);
        return row is null ? null : JsonSerializer.Deserialize<VerifiedRuntimeRequest>(row.Request);
    }
}
