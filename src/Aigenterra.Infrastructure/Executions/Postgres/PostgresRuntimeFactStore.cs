using System.Security.Cryptography;
using System.Text.Json;
using Aigenterra.Application.Runtime;
using Microsoft.EntityFrameworkCore;

namespace Aigenterra.Infrastructure.Executions.Postgres;

public sealed class PostgresRuntimeFactStore(ExecutionDbContext db) : IRuntimeFactStore
{
    public async Task AppendAsync(NormalizationAttempt attempt, CancellationToken cancellationToken = default)
    {
        if (attempt.Facts.Select((fact, ordinal) => fact.CaptureId != attempt.CaptureId || fact.Ordinal != ordinal).Any(invalid => invalid))
            throw new ArgumentException("Facts must reference this capture with consecutive ordinals.", nameof(attempt));
        var fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(attempt)));
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var added = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO normalization_attempts (attempt_id, capture_id, normalizer_version, recorded_at_ticks, status, reason, fingerprint)
            VALUES ({attempt.AttemptId}, {attempt.CaptureId}, {attempt.NormalizerVersion}, {attempt.RecordedAt.UtcTicks}, {attempt.Status}, {attempt.Reason}, {fingerprint})
            ON CONFLICT (attempt_id) DO NOTHING
            """, cancellationToken);
        if (added == 0)
        {
            var previous = await db.Set<NormalizationAttemptRow>().AsNoTracking().SingleAsync(x => x.AttemptId == attempt.AttemptId, cancellationToken);
            if (previous.Fingerprint != fingerprint) throw new InvalidOperationException("Normalization attempt ID conflict.");
            return;
        }
        foreach (var fact in attempt.Facts)
        {
            var json = JsonSerializer.Serialize(fact);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO runtime_facts (attempt_id, ordinal, runtime_namespace_id, thread_id, turn_id, fact)
                VALUES ({attempt.AttemptId}, {fact.Ordinal}, {fact.RuntimeNamespaceId}, {fact.ThreadId}, {fact.TurnId}, CAST({json} AS jsonb))
                """, cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<NormalizationAttempt>> ReadAttemptsAsync(Guid captureId, CancellationToken cancellationToken = default)
    {
        var attempts = await db.Set<NormalizationAttemptRow>().AsNoTracking().Where(x => x.CaptureId == captureId)
            .OrderBy(x => x.RecordedAtTicks).ThenBy(x => x.AttemptId).ToListAsync(cancellationToken);
        var facts = await (from fact in db.Set<RuntimeFactRow>().AsNoTracking()
                           join attempt in db.Set<NormalizationAttemptRow>().AsNoTracking() on fact.AttemptId equals attempt.AttemptId
                           where attempt.CaptureId == captureId
                           orderby fact.Ordinal
                           select fact).ToListAsync(cancellationToken);
        return attempts.Select(a => new NormalizationAttempt(a.AttemptId, a.CaptureId, a.NormalizerVersion,
            new(a.RecordedAtTicks, TimeSpan.Zero), a.Status, a.Reason,
            facts.Where(f => f.AttemptId == a.AttemptId).Select(f => JsonSerializer.Deserialize<RuntimeFact>(f.Fact)!).ToArray())).ToArray();
    }

    public async Task<IReadOnlyList<RuntimeFact>> ReadTurnFactsAsync(Guid runtimeNamespaceId, string threadId, string turnId,
        string normalizerVersion, CancellationToken cancellationToken = default)
    {
        var records = await (from fact in db.Set<RuntimeFactRow>().AsNoTracking()
                             join attempt in db.Set<NormalizationAttemptRow>().AsNoTracking() on fact.AttemptId equals attempt.AttemptId
                             where fact.RuntimeNamespaceId == runtimeNamespaceId && fact.ThreadId == threadId && (fact.TurnId == turnId || fact.TurnId == null)
                                 && attempt.NormalizerVersion == normalizerVersion
                             select fact.Fact).ToListAsync(cancellationToken);
        return records.Select(json => JsonSerializer.Deserialize<RuntimeFact>(json)!).ToArray();
    }
}
