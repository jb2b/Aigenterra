using System.Text;
using Aigenterra.Adapters.Codex;
using Aigenterra.Application.Executions;
using Aigenterra.Application.Recording;
using Aigenterra.Domain.Executions;
using Aigenterra.Infrastructure.Executions.Postgres;
using Microsoft.EntityFrameworkCore;

namespace Aigenterra.Infrastructure.Tests;

public sealed class RawCaptureTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    [Theory]
    [InlineData("{ \"hook_event_name\":\"SessionStart\",\"session_id\":\"s\",\"model\":\"observed-model\",\"extra\":true }\r\n", "projected", null)]
    [InlineData("{\"hook_event_name\":\"FutureHook\",\"extra\":123}", "archived_only", "unsupported_hook")]
    [InlineData("{\"hook_event_name\":\"PreToolUse\",\"session_id\":\"s\",\"turn_id\":\"missing\",\"tool_use_id\":\"x\",\"tool_name\":\"Bash\"}", "archived_only", "codex_execution_context_not_found")]
    [InlineData("not json café", "archived_only", "invalid_codex_hook_payload")]
    public async Task All_source_bytes_survive_projection_and_retries(string json, string disposition, string? reason)
    {
        var connection = await fixture.CreateDatabaseAsync();
        var capture = new CaptureEnvelope(1, Guid.NewGuid(), DateTimeOffset.UtcNow, Encoding.UTF8.GetBytes(json));
        await using (var db = PostgresFixture.Context(connection))
        {
            var ingestor = Ingestor(db);
            var outcome = await ingestor.IngestAsync(capture);
            Assert.True(outcome.IsSuccess);
            Assert.Equal(disposition, outcome.Value!.Disposition);
            Assert.Equal(reason, outcome.Value.Reason);
            Assert.Equal(outcome.Value, (await ingestor.IngestAsync(capture)).Value);
            var conflict = await ingestor.IngestAsync(capture with { Payload = "different"u8.ToArray() });
            Assert.Equal("capture_id_conflict", conflict.Error!.Code);
        }
        await using var reopened = PostgresFixture.Context(connection);
        var raw = await reopened.Set<RawCaptureRow>().SingleAsync();
        Assert.Equal(capture.Payload, raw.Payload);
        Assert.Equal(capture.CapturedAt.UtcTicks, raw.CapturedAtTicks);
        Assert.Single(await reopened.Set<CaptureOutcomeRow>().ToListAsync());
        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => reopened.Database.ExecuteSqlRawAsync("UPDATE raw_captures SET payload = decode('00','hex')"));
    }

    [Fact]
    public async Task Start_end_context_and_existing_recordings_survive_additive_migration()
    {
        var connection = await fixture.CreateDatabaseAsync("20260909161028_EvidenceBackedEvaluations");
        var oldConversation = new Conversation(ConversationId.New(), DateTimeOffset.UtcNow);
        var oldTask = new TaskExecution(TaskExecutionId.New(), oldConversation.Id, "Old recording", DateTimeOffset.UtcNow);
        await using (var previous = PostgresFixture.Context(connection))
        {
            var store = new PostgresExecutionStore(previous);
            await store.AddConversationAsync(oldConversation);
            await store.AddTaskExecutionAsync(oldTask);
            await previous.Database.MigrateAsync();
        }
        await using var db = PostgresFixture.Context(connection);
        var ingestor = Ingestor(db);
        var now = DateTimeOffset.UtcNow;
        var startContext = new RepositoryContext(now, "captured", "repo", "common", "worktree", "feature", "start-sha", true, ["changed"], ["untracked"]);
        var endContext = new RepositoryContext(now.AddSeconds(1), "captured", HeadSha: "end-sha");
        var start = await ingestor.IngestAsync(new(1, Guid.NewGuid(), now,
            "{\"hook_event_name\":\"UserPromptSubmit\",\"session_id\":\"s\",\"turn_id\":\"t\",\"prompt\":\"record\"}"u8.ToArray(), "start", startContext));
        await ingestor.IngestAsync(new(1, Guid.NewGuid(), now.AddSeconds(1),
            "{\"hook_event_name\":\"Stop\",\"session_id\":\"s\",\"turn_id\":\"t\",\"stop_hook_active\":false}"u8.ToArray(), "end", endContext));
        var reader = new PostgresExecutionStore(db);
        var source = await reader.GetEvidenceAsync(start.Value!.ObservationId!.Value);
        Assert.NotNull(source);
        Assert.Equal("{\"hook_event_name\":\"UserPromptSubmit\",\"session_id\":\"s\",\"turn_id\":\"t\",\"prompt\":\"record\"}",
            Encoding.UTF8.GetString(source.Content.Span));
        Assert.Null(await reader.GetEvidenceAsync(Guid.NewGuid()));
        var timeline = (await reader.GetTimelineAsync(new(start.Value!.TaskExecutionId!.Value)))!;
        Assert.Equal("completed", timeline.Status);
        Assert.Equal("start-sha", timeline.StartContext!.HeadSha);
        Assert.Equal("end-sha", timeline.EndContext!.HeadSha);
        Assert.Equal(startContext.ChangedFiles, timeline.StartContext.ChangedFiles);
        Assert.Equal(2, timeline.Observations.Count);
        var old = (await reader.GetTimelineAsync(oldTask.Id))!;
        Assert.Equal("Old recording", old.Objective);
        Assert.Null(old.StartContext);
        Assert.Null(old.EndContext);
    }

    [Fact]
    public async Task Raw_insert_survives_a_projection_crash_and_a_retry_can_finish()
    {
        var connection = await fixture.CreateDatabaseAsync();
        var capture = new CaptureEnvelope(1, Guid.NewGuid(), DateTimeOffset.UtcNow,
            "{\"hook_event_name\":\"SessionStart\",\"session_id\":\"crash\"}"u8.ToArray());
        await using (var db = PostgresFixture.Context(connection))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new CodexCaptureIngestor(new PostgresRawCaptureStore(db), new FailingIngestor()).IngestAsync(capture));
        }
        await using var reopened = PostgresFixture.Context(connection);
        Assert.Single(await reopened.Set<RawCaptureRow>().ToListAsync());
        Assert.True((await Ingestor(reopened).IngestAsync(capture)).IsSuccess);
    }

    private static CodexCaptureIngestor Ingestor(ExecutionDbContext db)
    {
        var store = new PostgresExecutionStore(db);
        var clock = TimeProvider.System;
        return new(new PostgresRawCaptureStore(db), new CodexHookIngestor(new CreateConversation(store, clock),
            new StartTaskExecution(store, clock), new StartAgentExecution(store, clock), new RecordObservation(store, clock),
            new FinishAgentExecution(store, clock), new FinishTaskExecution(store, clock), new ResolveExecutionRuntimeReference(store)));
    }
    private sealed class FailingIngestor : ICodexHookIngestor
    {
        public ValueTask<Application.Common.UseCaseResult<CodexHookIngestReceipt>> IngestAsync(ReadOnlyMemory<byte> payload,
            DateTimeOffset observedAt, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Projection interrupted");
    }
}
