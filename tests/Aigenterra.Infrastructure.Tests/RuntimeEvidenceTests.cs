using Aigenterra.Application.Runtime;
using Aigenterra.Infrastructure.Executions.Postgres;
using Microsoft.EntityFrameworkCore;

namespace Aigenterra.Infrastructure.Tests;

public sealed class RuntimeEvidenceTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Versioned_activity_evidence_is_immutable_idempotent_namespace_scoped_and_survives_retry()
    {
        var connection = await fixture.CreateDatabaseAsync();
        await using var db = PostgresFixture.Context(connection);
        var capture = RuntimeCaptureTests.Capture("""{"hook_event_name":"PreToolUse","session_id":"root","turn_id":"turn","tool_use_id":"call","tool_name":"mcp__server__tool"}"""u8.ToArray());
        capture = capture with { Source = capture.Source with { SourceKind = "hook", EvidenceStage = "HookVisible", Provenance = "hook_stdin" } };
        await new PostgresRawCaptureStore(db).ArchiveAsync(capture);
        var processor = RuntimeProcessingTests.Processor(db);
        await processor.ExecuteAsync(capture.CaptureId);
        await processor.ExecuteAsync(capture.CaptureId);
        var store = new PostgresRuntimeEvidenceStore(db);
        var batch = Assert.Single(await store.ReadAsync(capture.Source.RuntimeNamespaceId, "codex.evidence/1"));
        Assert.Equal("Requested", Assert.Single(RuntimeActivityProjector.Project([batch])).Status);
        Assert.Empty(await store.ReadAsync(Guid.NewGuid(), "codex.evidence/1"));
        Assert.Empty(await store.ReadAsync(capture.Source.RuntimeNamespaceId, "future"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.AppendAsync(batch with { Outcome = "changed" }));
        await store.AppendAsync(batch with { NormalizerVersion = "future" });
        Assert.Equal(2, await db.Set<RuntimeEvidenceRow>().CountAsync());
        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlRawAsync("DELETE FROM runtime_evidence"));
        Assert.Empty(await db.Tasks.ToListAsync());
    }
}
