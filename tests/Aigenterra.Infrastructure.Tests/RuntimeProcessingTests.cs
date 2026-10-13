using System.Text.Json;
using Aigenterra.Adapters.Codex.Diagnostics;
using Aigenterra.Adapters.Codex.Normalization;
using Aigenterra.Application.Recording;
using Aigenterra.Application.Runtime;
using Aigenterra.Infrastructure.Executions.Postgres;
using Microsoft.EntityFrameworkCore;

namespace Aigenterra.Infrastructure.Tests;

public sealed class RuntimeProcessingTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Archive_and_queue_are_atomic_with_backfill_expired_leases_and_idempotent_processing()
    {
        var connection = await fixture.CreateDatabaseAsync("20261013191550_RuntimeSourceHealth");
        await using var db = PostgresFixture.Context(connection);
        var archive = new PostgresRawCaptureStore(db);
        var capture = RuntimeCaptureTests.Capture("""{"hook_event_name":"UserPromptSubmit","session_id":"root","turn_id":"turn"}"""u8.ToArray());
        capture = capture with { Source = capture.Source with { SourceKind = "hook", EvidenceStage = "HookVisible", Provenance = "hook_stdin" } };
        await archive.ArchiveAsync(capture);
        await db.Database.MigrateAsync();
        var queue = new PostgresRuntimeWorkStore(db);
        Assert.Null(await queue.ClaimAsync(false, true));
        var first = (await queue.ClaimAsync(true, false))!;
        Assert.Null(await queue.ClaimAsync(true, false));
        await db.Database.ExecuteSqlRawAsync("UPDATE runtime_work SET available_at_ticks = 0"); // Simulate worker dying after claim.
        var recovered = (await queue.ClaimAsync(true, false))!;
        Assert.NotEqual(first.LeaseId, recovered.LeaseId);
        await queue.FinishAsync(first, "stale_worker", false);
        Assert.False((await db.Set<RuntimeWorkRow>().AsNoTracking().SingleAsync()).Complete);
        var processor = Processor(db);
        Assert.Equal("normalized", await processor.ExecuteAsync(capture.CaptureId));
        Assert.Equal("normalized", await processor.ExecuteAsync(capture.CaptureId)); // Crash after writing facts, before completion.
        Assert.Single(await new PostgresRuntimeFactStore(db).ReadAttemptsAsync(capture.CaptureId));
        await queue.FinishAsync(recovered, "normalized", false);
        Assert.Null(await queue.ClaimAsync(true, true));
        await archive.ArchiveAsync(capture);
        Assert.Single(await db.Set<RuntimeWorkRow>().ToListAsync());
        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            await archive.ArchiveAsync(capture with { CaptureId = Guid.NewGuid() });
            Assert.Equal(2, await db.Set<RuntimeWorkRow>().CountAsync());
            await transaction.RollbackAsync();
        }
        Assert.Single(await db.Set<RuntimeWorkRow>().ToListAsync());
    }

    [Fact]
    public async Task Uncommitted_lower_position_is_not_skipped_when_later_capture_commits_first()
    {
        var connection = await fixture.CreateDatabaseAsync();
        await using var first = PostgresFixture.Context(connection);
        await using var second = PostgresFixture.Context(connection);
        await using var transaction = await first.Database.BeginTransactionAsync();
        var a = RuntimeCaptureTests.Capture("{}"u8.ToArray());
        var b = RuntimeCaptureTests.Capture("{}"u8.ToArray());
        await new PostgresRawCaptureStore(first).ArchiveAsync(a);
        await new PostgresRawCaptureStore(second).ArchiveAsync(b);
        var queue = new PostgresRuntimeWorkStore(second);
        var work = (await queue.ClaimAsync(true, true))!;
        Assert.Equal(b.CaptureId, work.CaptureId);
        await queue.FinishAsync(work, "unsupported", false);
        await transaction.CommitAsync();
        Assert.Equal(a.CaptureId, (await queue.ClaimAsync(true, true))!.CaptureId);
    }

    [Fact]
    public async Task Out_of_order_context_waits_for_original_messages_and_links_only_valid_same_scope_pairs()
    {
        var connection = await fixture.CreateDatabaseAsync();
        await using var db = PostgresFixture.Context(connection);
        var archive = new PostgresRawCaptureStore(db);
        var request = RuntimeCaptureTests.Capture("""{"id":1,"method":"thread/turns/list","params":{"threadId":"child","cursor":"page2"}}"""u8.ToArray());
        request = request with { Source = request.Source with { EvidenceStage = "RecorderDiagnostic", Provenance = "history_request" } };
        var response = RuntimeCaptureTests.Capture("""{"id":1,"result":{"data":[],"nextCursor":null}}"""u8.ToArray());
        response = response with { Source = request.Source with { EvidenceStage = "AppServerVisible", Provenance = "history_response" } };
        var payload = JsonSerializer.SerializeToUtf8Bytes(new RuntimeRequestContext(1, request.CaptureId, response.CaptureId, Guid.NewGuid()), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var context = RuntimeCaptureTests.Capture(payload) with { Source = request.Source with { SourceKind = "diagnostic", Provenance = "request_context" } };
        await archive.ArchiveAsync(context);
        var processor = Processor(db);
        Assert.Equal("pending_context", await processor.ExecuteAsync(context.CaptureId));
        await archive.ArchiveAsync(response);
        Assert.Equal("pending_context", await processor.ExecuteAsync(response.CaptureId));
        await archive.ArchiveAsync(request);
        Assert.Equal("context_recorded", await processor.ExecuteAsync(context.CaptureId));
        Assert.Equal("context_recorded", await processor.ExecuteAsync(context.CaptureId));
        var linked = (await new PostgresRuntimeWorkStore(db).ReadAsync(response.CaptureId))!;
        Assert.Equal("child", linked.ThreadId);
        Assert.Equal("\"page2\"", linked.Cursor);
        Assert.Null(new CodexRequestContextDecoder().Decode(context, request, response with { Source = response.Source with { ConnectionEpochId = Guid.NewGuid() } }));
        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlRawAsync("DELETE FROM runtime_requests"));
        Assert.Equal(response.Payload, (await archive.ReadRuntimeAsync(response.CaptureId))!.Envelope.Payload);
    }

    internal static ProcessRuntimeCapture Processor(ExecutionDbContext db)
    {
        var archive = new PostgresRawCaptureStore(db);
        return new(archive, new PostgresRuntimeFactStore(db), new CodexLifecycleNormalizer(), new CodexRequestContextDecoder(),
            new PostgresRuntimeWorkStore(db), new RecordRuntimeSourceHealth(archive, new CodexCapabilityDetector(), new PostgresRuntimeSourceHealthStore(db)),
            new CodexEvidenceNormalizer(), new PostgresRuntimeEvidenceStore(db));
    }
}
