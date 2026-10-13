using System.Text;
using Aigenterra.Adapters.Codex;
using Aigenterra.Application.Executions;
using Aigenterra.Infrastructure.Executions;

namespace Aigenterra.Codex.Tests;

public sealed class CommandResultIngestorTests
{
    [Fact]
    public async Task Capture_requires_matching_session_and_turn_and_replay_preserves_evidence()
    {
        var store = new InMemoryExecutionStore();
        var clock = TimeProvider.System;
        var resolve = new ResolveExecutionRuntimeReference(store);
        var record = new RecordObservation(store, clock);
        var timeline = new GetTaskExecutionTimeline(store);
        var hooks = new CodexHookIngestor(new(store, clock), new(store, clock), new(store, clock), record,
            new(store, clock), new(store, clock), resolve);
        var ingestor = new CodexCommandResultIngestor(resolve, timeline, record);
        var payload = Encoding.UTF8.GetBytes("""{"source":"codex-app-server-command/v1","runtime_version":"0.153.4","session_id":"session","turn_id":"turn","item":{"type":"commandExecution","id":"call","status":"completed","exitCode":0,"commandActions":[{"type":"unknown","command":"dotnet build"}]}}""");
        Assert.Equal("codex_execution_context_not_found", (await ingestor.IngestAsync(payload, clock.GetUtcNow())).Error!.Code);
        var prompt = await hooks.IngestAsync(Encoding.UTF8.GetBytes("""{"hook_event_name":"UserPromptSubmit","session_id":"session","turn_id":"turn","prompt":"Record command"}"""), clock.GetUtcNow());
        await hooks.IngestAsync(Encoding.UTF8.GetBytes("""{"hook_event_name":"SessionStart","session_id":"foreign"}"""), clock.GetUtcNow());
        var foreign = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(payload).Replace("\"session\"", "\"foreign\"", StringComparison.Ordinal));
        Assert.Equal("codex_execution_context_not_found", (await ingestor.IngestAsync(foreign, clock.GetUtcNow())).Error!.Code);
        var first = (await ingestor.IngestAsync(payload, clock.GetUtcNow())).Value!;
        var duplicate = (await ingestor.IngestAsync(payload, clock.GetUtcNow())).Value!;
        Assert.Equal(CodexIngestDisposition.Recorded, first.Disposition);
        Assert.Equal(CodexIngestDisposition.Duplicate, duplicate.Disposition);
        Assert.Equal(first.ObservationId, duplicate.ObservationId);
        Assert.Equal(payload, store.GetRawEvidenceSnapshot(new(first.ObservationId!.Value)));
        Assert.Equal(2, (await timeline.ExecuteAsync(new(prompt.Value!.TaskExecutionId!.Value))).Value!.Observations.Count);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("not json")]
    [InlineData("{\"source\":\"codex-app-server-command/v1\",\"session_id\":\"s\",\"turn_id\":\"t\",\"runtime_version\":\"0.153.4\",\"item\":{\"type\":\"commandExecution\",\"id\":\"call\",\"status\":\"inProgress\"}}")]
    public async Task Malformed_or_nonterminal_capture_is_rejected(string json)
    {
        var store = new InMemoryExecutionStore();
        var ingestor = new CodexCommandResultIngestor(new(store), new(store), new(store, TimeProvider.System));
        Assert.Equal("invalid_codex_command_result", (await ingestor.IngestAsync(Encoding.UTF8.GetBytes(json), DateTimeOffset.UtcNow)).Error!.Code);
    }
}
