using System.Text.Json;
using Aigenterra.Adapters.Codex.Diagnostics;
using Aigenterra.Adapters.Codex.Normalization;
using Aigenterra.Application.Runtime;
using Aigenterra.Infrastructure.Executions.Postgres;
using Microsoft.EntityFrameworkCore;

namespace Aigenterra.Infrastructure.Tests;

public sealed class RuntimeReconciliationTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Enrollment_scope_health_gaps_and_pagination_survive_recovery_and_out_of_order_processing()
    {
        var connection = await fixture.CreateDatabaseAsync();
        await using var db = PostgresFixture.Context(connection);
        var archive = new PostgresRawCaptureStore(db);
        var store = new PostgresRuntimeReconciliationStore(db);
        var ns = Guid.NewGuid(); var source = Guid.NewGuid(); var hookSource = Guid.NewGuid();
        var inputs = new[]
        {
            new RuntimeReconciliationInput(1, "enrollment", DateTimeOffset.UtcNow, "enrolled_partial", Enrollment: new(new string('A', 64), [hookSource], ["selected"])),
            new RuntimeReconciliationInput(1, "gap", DateTimeOffset.UtcNow, "history_read_incomplete", "selected"),
            new RuntimeReconciliationInput(1, "cycle", DateTimeOffset.UtcNow, "history_read_partial_coverage", "selected")
        };
        var records = new List<RuntimeReconciliationRecord>();
        foreach (var input in inputs)
        {
            var capture = RuntimeCaptureTests.Capture(JsonSerializer.SerializeToUtf8Bytes(input, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            capture = capture with { Source = capture.Source with { RuntimeNamespaceId = ns, SourceId = source, SourceKind = "diagnostic", EvidenceStage = "RecorderDiagnostic", Provenance = "reconciliation" } };
            await archive.ArchiveAsync(capture);
            records.Add(new(capture.CaptureId, ns, source, (await archive.ReadRuntimeAsync(capture.CaptureId))!.IngestPosition, new CodexReconciliationDecoder().Decode(capture)!));
        }
        foreach (var record in records.AsEnumerable().Reverse()) { await store.AppendAsync(record); await store.AppendAsync(record); }
        var health = (await store.ReadHealthAsync(ns, source))!;
        Assert.Equal("history_read_partial_coverage", health.ReaderState);
        Assert.Equal(1, health.GapCount);
        Assert.Equal("Unknown", health.OwnerState);
        Assert.Equal("KEEP_GATE_BLOCKED", health.Gate);
        Assert.Null(await store.ReadHealthAsync(Guid.NewGuid(), source));
        foreach (var id in new[] { hookSource, Guid.NewGuid() })
        {
            var capture = RuntimeCaptureTests.Capture("""{"hook_event_name":"UserPromptSubmit","session_id":"hookRoot","turn_id":"turn"}"""u8.ToArray());
            capture = capture with { Source = capture.Source with { RuntimeNamespaceId = ns, SourceId = id, SourceKind = "hook", EvidenceStage = "HookVisible", Provenance = "hook_stdin" } };
            await archive.ArchiveAsync(capture);
            await new PostgresRuntimeEvidenceStore(db).AppendAsync(new CodexAgentEvidenceNormalizer().Normalize(capture, null));
        }
        var query = new GetRuntimeReconciliationTargets(store);
        var page = (await query.ExecuteAsync(ns, source, null, 1))!;
        Assert.Equal("hookRoot", Assert.Single(page.Targets).ThreadId);
        Assert.Equal("selected", Assert.Single((await query.ExecuteAsync(ns, source, page.Next, 1))!.Targets).ThreadId);
        Assert.Null(await query.ExecuteAsync(Guid.NewGuid(), source, null, 1));
        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlRawAsync("DELETE FROM runtime_reconciliation"));
    }
}
