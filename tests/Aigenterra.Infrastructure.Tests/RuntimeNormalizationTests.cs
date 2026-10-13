using Aigenterra.Adapters.Codex.Normalization;
using Aigenterra.Application.Recording;
using Aigenterra.Application.Runtime;
using Aigenterra.Infrastructure.Executions.Postgres;
using Microsoft.EntityFrameworkCore;

namespace Aigenterra.Infrastructure.Tests;

public sealed class RuntimeNormalizationTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Attempts_and_facts_are_append_only_versioned_atomic_and_do_not_touch_legacy_records()
    {
        var connection = await fixture.CreateDatabaseAsync();
        var capture = RuntimeCaptureTests.Capture("""{"hook_event_name":"UserPromptSubmit","session_id":"root","turn_id":"turn","unknown":"retained"}"""u8.ToArray());
        capture = capture with { Source = capture.Source with { SourceKind = "hook", EvidenceStage = "HookVisible", Provenance = "hook_stdin" } };
        await using var db = PostgresFixture.Context(connection);
        var archive = new PostgresRawCaptureStore(db);
        var facts = new PostgresRuntimeFactStore(db);
        var normalize = new NormalizeRuntimeCapture(archive, new CodexLifecycleNormalizer(), facts, TimeProvider.System);
        Assert.False((await normalize.ExecuteAsync(capture.CaptureId)).IsSuccess);
        await new ArchiveRuntimeCapture(archive).ExecuteAsync(capture);
        var attempt = (await normalize.ExecuteAsync(capture.CaptureId)).Value!;
        Assert.Equal(2, attempt.Facts.Count);
        await facts.AppendAsync(attempt); // Lost acknowledgement of the same attempt is safe.
        Assert.Single(await facts.ReadAttemptsAsync(capture.CaptureId));
        var newer = attempt with { AttemptId = Guid.NewGuid(), NormalizerVersion = "fixture.next/1", Status = "unsupported", Reason = "future", Facts = [] };
        await facts.AppendAsync(newer);
        var attempts = await facts.ReadAttemptsAsync(capture.CaptureId);
        Assert.Equal(2, attempts.Count);
        Assert.Contains(attempts, a => a.Facts.Count == 0);
        Assert.Null(await new GetRuntimeTurn(facts).ExecuteAsync(capture.Source.RuntimeNamespaceId, "root", "turn", "fixture.next/1"));
        var turn = (await new GetRuntimeTurn(facts).ExecuteAsync(capture.Source.RuntimeNamespaceId, "root", "turn", CodexLifecycleNormalizer.NormalizerVersion))!;
        Assert.Equal("Root", turn.ThreadScope);
        Assert.Equal("Running", turn.Status);
        Assert.Equal(capture.Payload, (await archive.ReadRuntimeAsync(capture.CaptureId))!.Envelope.Payload);
        Assert.Empty(await db.Set<CaptureOutcomeRow>().ToListAsync());
        Assert.Empty(await db.Tasks.ToListAsync());
        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlRawAsync("UPDATE normalization_attempts SET status = 'replaced'"));
        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlRawAsync("DELETE FROM runtime_facts"));
        var broken = attempt with { AttemptId = Guid.NewGuid(), Facts = [attempt.Facts[0] with { ThreadId = null! }] };
        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => facts.AppendAsync(broken));
        Assert.Equal(2, (await facts.ReadAttemptsAsync(capture.CaptureId)).Count);
    }

    [Fact]
    public async Task Unsupported_and_rejected_attempts_leave_raw_evidence_recoverable()
    {
        var connection = await fixture.CreateDatabaseAsync("20261013185755_VersionedRuntimeEvidence");
        await using var db = PostgresFixture.Context(connection);
        var archive = new PostgresRawCaptureStore(db);
        var capture = RuntimeCaptureTests.Capture("not-json"u8.ToArray());
        await new ArchiveRuntimeCapture(archive).ExecuteAsync(capture);
        await db.Database.MigrateAsync();
        var store = new PostgresRuntimeFactStore(db);
        var attempt = (await new NormalizeRuntimeCapture(archive, new CodexLifecycleNormalizer(), store, TimeProvider.System).ExecuteAsync(capture.CaptureId)).Value!;
        Assert.Equal("rejected", attempt.Status);
        Assert.Empty(attempt.Facts);
        Assert.Equal(capture.Payload, (await archive.ReadRuntimeAsync(capture.CaptureId))!.Envelope.Payload);
        Assert.Single(await store.ReadAttemptsAsync(capture.CaptureId));
    }
}
