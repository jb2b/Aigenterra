using System.Text.Json;
using Aigenterra.Adapters.Codex.Diagnostics;
using Aigenterra.Application.Recording;
using Aigenterra.Application.Runtime;
using Aigenterra.Infrastructure.Executions.Postgres;
using Microsoft.EntityFrameworkCore;

namespace Aigenterra.Infrastructure.Tests;

public sealed class RuntimeSourceHealthTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Diagnostic_history_is_append_only_and_out_of_order_processing_does_not_regress_latest_source_or_instance()
    {
        var connection = await fixture.CreateDatabaseAsync("20261013190506_RuntimeLifecycleFacts");
        await using var db = PostgresFixture.Context(connection);
        var source = new RuntimeCaptureSource(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "codex", "diagnostic", "RecorderDiagnostic", "inventory", Guid.NewGuid());
        var older = Capture(source, true);
        var later = Capture(source, false);
        var archive = new PostgresRawCaptureStore(db);
        await new ArchiveRuntimeCapture(archive).ExecuteAsync(older);
        await new ArchiveRuntimeCapture(archive).ExecuteAsync(later);
        await db.Database.MigrateAsync();
        var store = new PostgresRuntimeSourceHealthStore(db);
        var record = new RecordRuntimeSourceHealth(archive, new CodexCapabilityDetector(), store);
        Assert.True((await record.ExecuteAsync(later.CaptureId)).IsSuccess);
        Assert.True((await record.ExecuteAsync(older.CaptureId)).IsSuccess);
        Assert.True((await record.ExecuteAsync(later.CaptureId)).IsSuccess);
        Assert.Equal(2, await db.Set<SourceHealthEventRow>().CountAsync());
        var read = (await new GetRuntimeSourceHealth(store, TimeProvider.System).ExecuteAsync(source.RuntimeNamespaceId, source.SourceId))!;
        Assert.Equal(later.CaptureId, read.Snapshot.CaptureId);
        Assert.Equal("Disconnected", read.Snapshot.ConnectionState);
        Assert.True(read.InventoryAgeSeconds >= 0);
        Assert.Null(await store.ReadAsync(Guid.NewGuid(), source.SourceId));
        var instance = Assert.Single(await new GetRuntimeInstances(store).ExecuteAsync(source.RuntimeNamespaceId));
        Assert.Equal(source.RuntimeInstanceId, instance.LatestDiagnostic.RuntimeInstanceId);
        Assert.Equal(later.CaptureId, instance.LatestDiagnostic.CaptureId);
        Assert.Empty(await new GetRuntimeInstances(store).ExecuteAsync(source.RuntimeNamespaceId, instance.Position));
        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlRawAsync("UPDATE source_health_events SET health = health"));
        Assert.Equal(older.Payload, (await archive.ReadRuntimeAsync(older.CaptureId))!.Envelope.Payload);
        Assert.Empty(await db.Set<CaptureOutcomeRow>().ToListAsync());
        Assert.Empty(await db.Tasks.ToListAsync());
        // Rollback mode: keep new evidence and continue the existing v1 archive contract.
        Assert.Equal(CaptureArchiveResult.Added, await archive.ArchiveAsync(new CaptureEnvelope(1, Guid.NewGuid(), DateTimeOffset.UtcNow, "legacy"u8.ToArray())));
    }

    [Fact]
    public async Task Idle_source_keeps_snapshot_state_and_health_age_does_not_invent_failure()
    {
        var connection = await fixture.CreateDatabaseAsync();
        await using var db = PostgresFixture.Context(connection);
        var source = new RuntimeCaptureSource(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "codex", "diagnostic", "RecorderDiagnostic", "inventory");
        var capture = Capture(source, true);
        var archive = new PostgresRawCaptureStore(db);
        var store = new PostgresRuntimeSourceHealthStore(db);
        await new ArchiveRuntimeCapture(archive).ExecuteAsync(capture);
        await new RecordRuntimeSourceHealth(archive, new CodexCapabilityDetector(), store).ExecuteAsync(capture.CaptureId);
        var read = (await new GetRuntimeSourceHealth(store, new FutureClock()).ExecuteAsync(source.RuntimeNamespaceId, source.SourceId))!;
        Assert.True(read.InventoryAgeSeconds > 80000);
        Assert.Equal("Connected", read.Snapshot.ConnectionState);
        Assert.DoesNotContain(read.Snapshot.Checks, check => check.Capability == "connection" && check.Status is "WARN" or "FAIL");
        Assert.Empty(await store.ReadInstancesAsync(source.RuntimeNamespaceId, 0, 100)); // Unknown instance stays unknown.
    }

    internal static RuntimeCaptureEnvelope Capture(RuntimeCaptureSource source, bool connected)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(new CodexDiagnosticInventory(1, DateTimeOffset.UtcNow, Connected: connected,
            Hooks: [new("preToolUse", true, true, "untrusted")]), CodexCapabilityDetector.JsonOptions);
        return RuntimeCaptureTests.Capture(payload) with { Source = source };
    }
    private sealed class FutureClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow.AddDays(1);
    }
}
