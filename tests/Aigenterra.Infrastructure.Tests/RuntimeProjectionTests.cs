using System.Text.Json;
using Aigenterra.Adapters.Codex.Diagnostics;
using Aigenterra.Adapters.Codex.Normalization;
using Aigenterra.Application.Recording;
using Aigenterra.Application.Runtime;
using Aigenterra.Infrastructure.Executions.Postgres;
using Microsoft.EntityFrameworkCore;

namespace Aigenterra.Infrastructure.Tests;

public sealed class RuntimeProjectionTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Upgrade_rebuilds_original_diagnostics_and_gaps_without_a_runtime_or_wall_clock_dependent_checksum()
    {
        var connection = await fixture.CreateDatabaseAsync("20261013222426_ScopedRuntimeReconciliation");
        await using var db = PostgresFixture.Context(connection);
        var archive = new PostgresRawCaptureStore(db);
        var source = new RuntimeCaptureSource(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "codex", "diagnostic", "RecorderDiagnostic", "inventory");
        var inventory = RuntimeSourceHealthTests.Capture(source, false);
        await archive.ArchiveAsync(inventory);
        var gap = RuntimeCaptureTests.Capture(JsonSerializer.SerializeToUtf8Bytes(new RuntimeReconciliationInput(1, "gap", DateTimeOffset.UtcNow,
            "history_read_incomplete", "thread"), new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        gap = gap with { Source = source with { Provenance = "reconciliation" } };
        await archive.ArchiveAsync(gap);
        await db.Database.MigrateAsync();
        var store = new PostgresRuntimeProjectionStore(db);
        var rebuild = Rebuilder(db);
        var first = await store.FreezeAsync(source.RuntimeNamespaceId, rebuild.Versions);
        var complete = await rebuild.ExecuteAsync(first.Id);
        var document = (await store.ReadDocumentAsync(first.Id))!;
        Assert.Equal("Disconnected", Assert.Single(document.SourceHealth).ConnectionState);
        Assert.Equal("history_read_incomplete", Assert.Single(document.Reconciliation).Input.Outcome);
        var second = await store.FreezeAsync(source.RuntimeNamespaceId, rebuild.Versions);
        Assert.Equal(complete.ProjectionSha256, (await rebuild.ExecuteAsync(second.Id)).ProjectionSha256);
        Assert.Equal(inventory.Payload, (await archive.ReadRuntimeAsync(inventory.CaptureId))!.Envelope.Payload);
        Assert.Equal(gap.Payload, (await archive.ReadRuntimeAsync(gap.CaptureId))!.Envelope.Payload);
    }

    [Fact]
    public async Task Concurrent_activation_has_one_winner_and_a_committed_contiguous_namespace_revision()
    {
        var connection = await fixture.CreateDatabaseAsync();
        await using var db = PostgresFixture.Context(connection);
        var store = new PostgresRuntimeProjectionStore(db);
        var rebuild = Rebuilder(db);
        var ns = Guid.NewGuid();
        var first = await store.FreezeAsync(ns, rebuild.Versions);
        var second = await store.FreezeAsync(ns, rebuild.Versions);
        await rebuild.ExecuteAsync(first.Id); await rebuild.ExecuteAsync(second.Id);
        var outcomes = await Task.WhenAll(new[] { first.Id, second.Id }.Select(async id =>
        {
            await using var other = PostgresFixture.Context(connection);
            try { await new PostgresRuntimeProjectionStore(other).ActivateAsync(id, null); return true; }
            catch (InvalidOperationException) { return false; }
        }));
        Assert.Single(outcomes, x => x);
        Assert.Equal(1, (await store.ReadActiveAsync(ns))!.Revision);
        Assert.Single(await db.Set<RuntimeProjectionChangeRow>().ToListAsync());
    }

    [Fact]
    public async Task Offline_rebuild_resumes_per_input_matches_reordered_live_inputs_and_atomically_activates_or_rolls_back()
    {
        var connection = await fixture.CreateDatabaseAsync();
        await using var db = PostgresFixture.Context(connection);
        var archive = new PostgresRawCaptureStore(db);
        var capture = RuntimeCaptureTests.Capture("""{"hook_event_name":"UserPromptSubmit","session_id":"root","turn_id":"turn"}"""u8.ToArray());
        capture = capture with { Source = capture.Source with { SourceKind = "hook", EvidenceStage = "HookVisible", Provenance = "hook_stdin" } };
        var pre = WithPayload(capture, """{"hook_event_name":"PreToolUse","session_id":"root","turn_id":"turn","tool_use_id":"call","tool_name":"mcp__server__tool"}""");
        var interrupted = WithPayload(capture, """{"hook_event_name":"Interrupt","session_id":"root","turn_id":"turn"}""");
        foreach (var value in new[] { capture, pre, interrupted }) await archive.ArchiveAsync(value);
        var store = new PostgresRuntimeProjectionStore(db);
        var rebuild = Rebuilder(db);
        var first = await store.FreezeAsync(capture.Source.RuntimeNamespaceId, rebuild.Versions);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ActivateAsync(first.Id, null));
        var life = new CodexHistoryNormalizer().Normalize(pre);
        var evidence = new CodexAgentEvidenceNormalizer().Normalize(pre, null);
        var partial = new RuntimeProjectionInputResult(pre.CaptureId, life, evidence, null, null, null);
        await store.SaveResultAsync(first.Id, partial);
        await store.SaveResultAsync(first.Id, partial); // Lost acknowledgement/retry.
        Assert.Single(await store.ReadResultsAsync(first.Id));
        await using (var reopened = PostgresFixture.Context(connection)) await Rebuilder(reopened).ExecuteAsync(first.Id); // Simulated process crash/restart.
        var complete = (await store.ReadGenerationAsync(first.Id))!;
        var document = (await store.ReadDocumentAsync(first.Id))!;
        Assert.Equal("TerminalUnknown", Assert.Single(document.Activities).Status);
        Assert.Equal("Interrupted", Assert.Single(document.Topology.Segments).Status);
        Assert.Equal("KEEP_GATE_BLOCKED", document.Gate);
        var results = await store.ReadResultsAsync(first.Id);
        var reordered = RuntimeProjectionProjector.Project(complete, (await store.ReadInputsAsync(first.Id)).Reverse().ToArray(), results.Reverse().ToArray());
        Assert.Equal(RuntimeCanonicalJson.Serialize(document), RuntimeCanonicalJson.Serialize(reordered));
        Assert.Equal(complete.ProjectionSha256, (await store.CompleteAsync(first.Id, reordered)).ProjectionSha256);
        Assert.Equal(1, (await store.ActivateAsync(first.Id, null)).Revision);
        Assert.Equal(1, (await store.ActivateAsync(first.Id, null)).Revision);
        var second = await store.FreezeAsync(capture.Source.RuntimeNamespaceId, rebuild.Versions);
        var secondComplete = await rebuild.ExecuteAsync(second.Id);
        Assert.Equal(complete.ManifestSha256, secondComplete.ManifestSha256);
        Assert.Equal(complete.ProjectionSha256, secondComplete.ProjectionSha256);
        Assert.Equal(2, (await store.ActivateAsync(second.Id, first.Id)).Revision);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ActivateAsync(first.Id, null));
        Assert.Equal(3, (await store.ActivateAsync(first.Id, second.Id)).Revision); // Rollback keeps both generations.
        Assert.Equal(3, await db.Set<RuntimeProjectionChangeRow>().CountAsync());
        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE runtime_projection_generations SET document = '{{}}'::jsonb WHERE id = {first.Id}"));
        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlRawAsync("DELETE FROM runtime_projection_changes"));
        Assert.Equal(3, await db.Set<RawCaptureRow>().CountAsync());
        Assert.Empty(await db.Tasks.ToListAsync());
    }

    [Fact]
    public async Task Frozen_missing_context_resolves_only_in_later_generation_and_concurrent_lower_position_is_not_lost()
    {
        var connection = await fixture.CreateDatabaseAsync();
        await using var db = PostgresFixture.Context(connection);
        await using var delayed = PostgresFixture.Context(connection);
        var archive = new PostgresRawCaptureStore(db);
        var store = new PostgresRuntimeProjectionStore(db);
        var rebuild = Rebuilder(db);
        var response = RuntimeCaptureTests.Capture("""{"id":3,"result":{"data":[{"id":"turn","status":"completed","items":[{"type":"mcpToolCall","id":"call","server":"server","tool":"tool","status":"failed","error":null}]}],"nextCursor":null}}"""u8.ToArray());
        response = response with { Source = response.Source with { Provenance = "reader_frame" } };
        var request = WithPayload(response, """{"id":3,"method":"thread/turns/list","params":{"threadId":"child"}}""");
        request = request with { Source = request.Source with { EvidenceStage = "RecorderDiagnostic", Provenance = "history_request" } };
        await using var transaction = await delayed.Database.BeginTransactionAsync();
        await new PostgresRawCaptureStore(delayed).ArchiveAsync(request); // Lower allocated position, still invisible.
        await archive.ArchiveAsync(response);
        var frozen = await store.FreezeAsync(response.Source.RuntimeNamespaceId, rebuild.Versions);
        Assert.Equal(1, frozen.InputCount);
        await transaction.CommitAsync();
        var context = WithPayload(request, JsonSerializer.Serialize(new RuntimeRequestContext(1, request.CaptureId, response.CaptureId, Guid.NewGuid()), new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        context = context with { Source = context.Source with { SourceKind = "diagnostic", Provenance = "request_context" } };
        await archive.ArchiveAsync(context);
        await rebuild.ExecuteAsync(frozen.Id);
        var old = (await store.ReadDocumentAsync(frozen.Id))!;
        Assert.Empty(old.Activities);
        Assert.Equal("pending_context", Assert.Single(old.InputOutcomes).Limitation);
        Assert.True(await store.HasNewInputsAsync(response.Source.RuntimeNamespaceId, frozen.Id));
        var newer = await store.FreezeAsync(response.Source.RuntimeNamespaceId, rebuild.Versions);
        await rebuild.ExecuteAsync(newer.Id);
        Assert.Equal("Failed", Assert.Single((await store.ReadDocumentAsync(newer.Id))!.Activities).Status);
        Assert.Empty((await store.ReadDocumentAsync(frozen.Id))!.Activities); // Never reinterpreted in place.
        var other = await store.FreezeAsync(Guid.NewGuid(), rebuild.Versions);
        Assert.Equal(0, other.InputCount);
        var future = await store.FreezeAsync(response.Source.RuntimeNamespaceId, rebuild.Versions with { Evidence = "future/1" });
        await Assert.ThrowsAsync<InvalidOperationException>(() => rebuild.ExecuteAsync(future.Id));
    }

    internal static RebuildRuntimeProjection Rebuilder(ExecutionDbContext db) => new(new PostgresRuntimeProjectionStore(db), new PostgresRawCaptureStore(db),
        new CodexHistoryNormalizer(), new CodexAgentEvidenceNormalizer(), new CodexRequestContextDecoder(), new CodexCapabilityDetector(), new CodexReconciliationDecoder());
    private static RuntimeCaptureEnvelope WithPayload(RuntimeCaptureEnvelope capture, string json) => RuntimeCaptureTests.Capture(System.Text.Encoding.UTF8.GetBytes(json)) with { Source = capture.Source };
}
