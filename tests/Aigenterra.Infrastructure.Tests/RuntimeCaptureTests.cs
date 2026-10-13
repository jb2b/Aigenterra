using System.Security.Cryptography;
using Aigenterra.Application.Recording;
using Aigenterra.Infrastructure.Executions.Postgres;
using Microsoft.EntityFrameworkCore;

namespace Aigenterra.Infrastructure.Tests;

public sealed class RuntimeCaptureTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Opaque_bytes_and_source_identity_survive_reopen_concurrent_retries_and_conflicts()
    {
        var connection = await fixture.CreateDatabaseAsync();
        var capture = Capture([0xff, 0, 13, 10, 123]); // Intentionally not JSON or valid UTF-8.
        var receipts = await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ =>
        {
            await using var db = PostgresFixture.Context(connection);
            return await new ArchiveRuntimeCapture(new PostgresRawCaptureStore(db)).ExecuteAsync(capture);
        }));
        Assert.All(receipts, result => Assert.True(result.IsSuccess));
        Assert.Single(receipts, result => !result.Value!.Duplicate);
        await using var reopened = PostgresFixture.Context(connection);
        var store = new PostgresRawCaptureStore(reopened);
        var saved = (await store.ReadRuntimeAsync(capture.CaptureId))!;
        Assert.Equal(capture.Payload, saved.Envelope.Payload);
        Assert.Equal(capture.Source, saved.Envelope.Source);
        Assert.Equal(capture.PayloadSha256, Convert.ToHexString(SHA256.HashData(saved.Envelope.Payload)));
        Assert.True(saved.IngestPosition > 0);
        Assert.True(saved.RecordedAt >= capture.CapturedAt);
        Assert.Empty(await reopened.Set<CaptureOutcomeRow>().ToListAsync());
        var useCase = new ArchiveRuntimeCapture(store);
        Assert.Equal("capture_id_conflict", (await useCase.ExecuteAsync(capture with
        { Source = capture.Source with { ConnectionEpochId = Guid.NewGuid() } })).Error!.Code);
        Assert.Equal("invalid_runtime_capture", (await useCase.ExecuteAsync(capture with { Payload = [] })).Error!.Code);
        Assert.Equal("invalid_runtime_capture", (await useCase.ExecuteAsync(capture with { PayloadSha256 = new string('0', 64) })).Error!.Code);
        Assert.Single(await reopened.Set<RawCaptureRow>().ToListAsync());
        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => reopened.Database.ExecuteSqlRawAsync("UPDATE capture_sources SET envelope = envelope"));
    }

    [Fact]
    public async Task Upgrade_preserves_legacy_fingerprint_and_rollback_means_continue_legacy_reads_and_writes()
    {
        var connection = await fixture.CreateDatabaseAsync("20260916150255_RecordingHardening");
        var legacy = new CaptureEnvelope(1, Guid.NewGuid(), DateTimeOffset.UtcNow, "exact legacy bytes\r\n"u8.ToArray());
        await using var db = PostgresFixture.Context(connection);
        var store = new PostgresRawCaptureStore(db);
        Assert.Equal(CaptureArchiveResult.Added, await store.ArchiveAsync(legacy));
        var before = await db.Set<RawCaptureRow>().AsNoTracking().SingleAsync();
        await db.Database.MigrateAsync();
        await new ArchiveRuntimeCapture(store).ExecuteAsync(Capture("{}"u8.ToArray()));
        Assert.Equal(CaptureArchiveResult.Duplicate, await store.ArchiveAsync(legacy));
        var after = await db.Set<RawCaptureRow>().AsNoTracking().SingleAsync(x => x.CaptureId == legacy.CaptureId);
        Assert.Equal(before.Fingerprint, after.Fingerprint);
        Assert.Equal(before.Payload, after.Payload);
        Assert.Null(await store.ReadRuntimeAsync(legacy.CaptureId));
        Assert.Equal(CaptureArchiveResult.Added, await store.ArchiveAsync(legacy with { CaptureId = Guid.NewGuid() }));
        Assert.Single(await db.Set<CaptureSourceRow>().ToListAsync());
    }

    internal static RuntimeCaptureEnvelope Capture(byte[] payload) => new(2, Guid.NewGuid(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "codex", "app_server", "AppServerVisible", "live_notification", RuntimeVersion: "0.162.1"),
        "application/json", payload.Length, Convert.ToHexString(SHA256.HashData(payload)), payload);
}
