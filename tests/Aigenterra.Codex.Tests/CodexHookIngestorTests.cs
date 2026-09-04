using System.Text;
using Aigenterra.Adapters.Codex;
using Aigenterra.Application.Executions;
using Aigenterra.Domain.Executions;
using Aigenterra.Infrastructure.Executions;

namespace Aigenterra.Codex.Tests;

/// <summary>
/// Verifies Codex hook translation, correlation and idempotency end to end.
/// </summary>
public sealed class CodexHookIngestorTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Session_start_is_idempotent_and_does_not_create_an_observation()
    {
        var fixture = new IngestFixture();
        const string sessionStart =
            """{"hook_event_name":"SessionStart","session_id":"session-only"}""";

        var first = await fixture.IngestAsync(sessionStart, Now);
        var duplicate = await fixture.IngestAsync(sessionStart, Now.AddSeconds(1));

        Assert.Equal(CodexIngestDisposition.Recorded, first.Value!.Disposition);
        Assert.Equal(CodexIngestDisposition.Duplicate, duplicate.Value!.Disposition);
        Assert.Equal(first.Value.ConversationId, duplicate.Value.ConversationId);
        Assert.Null(first.Value.TaskExecutionId);
        Assert.Null(first.Value.ObservationId);
    }

    [Fact]
    public async Task Normal_turn_records_prompt_tools_and_stop_without_completing_execution()
    {
        var fixture = new IngestFixture();

        var prompt = await fixture.IngestAsync(
            """{"hook_event_name":"UserPromptSubmit","session_id":"session-1","turn_id":"turn-1","prompt":"Run dotnet --version"}""",
            Now);
        await fixture.IngestAsync(
            """{"hook_event_name":"PreToolUse","session_id":"session-1","turn_id":"turn-1","tool_name":"exec_command","tool_use_id":"tool-1"}""",
            Now.AddSeconds(1));
        await fixture.IngestAsync(
            """{"hook_event_name":"PostToolUse","session_id":"session-1","turn_id":"turn-1","tool_name":"exec_command","tool_use_id":"tool-1"}""",
            Now.AddSeconds(2));
        var stop = await fixture.IngestAsync(
            """{"hook_event_name":"Stop","session_id":"session-1","turn_id":"turn-1","stop_hook_active":false,"last_assistant_message":"10.0.400"}""",
            Now.AddSeconds(3));

        Assert.Equal(CodexIngestDisposition.Recorded, stop.Value!.Disposition);
        var timeline = await fixture.GetTimelineAsync(prompt.Value!.TaskExecutionId!.Value);
        Assert.Equal("Run dotnet --version", timeline.Objective);
        Assert.Equal("running", timeline.Status);
        Assert.Single(timeline.Agents);
        Assert.Equal("running", timeline.Agents[0].Status);
        Assert.Equal(4, timeline.Observations.Count);
        Assert.All(timeline.Observations, observation => Assert.True(observation.HasRawEvidence));
        Assert.Null(timeline.Observations.Single(item => item.Kind == "toolinvocation").AgentExecutionId);
        Assert.Null(timeline.Observations.Single(item => item.Kind == "toolresult").AgentExecutionId);
    }

    [Fact]
    public async Task Interrupt_is_the_only_hook_that_terminates_the_task_and_root_agent()
    {
        var fixture = new IngestFixture();
        var prompt = await fixture.IngestAsync(PromptJson("turn-interrupt"), Now);
        var interrupt = """{"hook_event_name":"Interrupt","session_id":"session-1","turn_id":"turn-interrupt"}""";

        var first = await fixture.IngestAsync(interrupt, Now.AddSeconds(1));
        var duplicate = await fixture.IngestAsync(interrupt, Now.AddSeconds(2));

        Assert.Equal(CodexIngestDisposition.Recorded, first.Value!.Disposition);
        Assert.Equal(CodexIngestDisposition.Duplicate, duplicate.Value!.Disposition);
        var timeline = await fixture.GetTimelineAsync(prompt.Value!.TaskExecutionId!.Value);
        Assert.Equal("interrupted", timeline.Status);
        Assert.Equal("interrupted", Assert.Single(timeline.Agents).Status);
        Assert.Equal(2, timeline.Observations.Count);
    }

    [Fact]
    public async Task Subagent_stop_before_start_reconstructs_one_running_agent_and_keeps_both_signals()
    {
        var fixture = new IngestFixture();
        var prompt = await fixture.IngestAsync(PromptJson("turn-subagent"), Now);
        var stop = await fixture.IngestAsync(
            """{"hook_event_name":"SubagentStop","session_id":"session-1","turn_id":"turn-subagent","agent_id":"agent-1","agent_type":"worker","stop_hook_active":false}""",
            Now.AddSeconds(1));
        var start = await fixture.IngestAsync(
            """{"hook_event_name":"SubagentStart","session_id":"session-1","turn_id":"turn-subagent","agent_id":"agent-1","agent_type":"worker"}""",
            Now.AddSeconds(2));

        Assert.Equal(stop.Value!.AgentExecutionId, start.Value!.AgentExecutionId);
        var timeline = await fixture.GetTimelineAsync(prompt.Value!.TaskExecutionId!.Value);
        var subagent = Assert.Single(
            timeline.Agents,
            item => item.RuntimeReference?.ResourceKind == "subagent");
        Assert.Null(subagent.ParentAgentExecutionId);
        Assert.Equal("running", subagent.Status);
        Assert.Equal(2, timeline.Observations.Count(item => item.AgentExecutionId == subagent.AgentExecutionId));
    }

    [Fact]
    public async Task Duplicate_delivery_does_not_allocate_a_second_observation_or_sequence()
    {
        var fixture = new IngestFixture();
        var prompt = await fixture.IngestAsync(PromptJson("turn-duplicate"), Now);
        const string tool = """{"hook_event_name":"PreToolUse","session_id":"session-1","turn_id":"turn-duplicate","tool_name":"exec_command","tool_use_id":"tool-duplicate"}""";

        var first = await fixture.IngestAsync(tool, Now.AddSeconds(1));
        var duplicate = await fixture.IngestAsync(tool, Now.AddSeconds(2));

        Assert.Equal(CodexIngestDisposition.Recorded, first.Value!.Disposition);
        Assert.Equal(CodexIngestDisposition.Duplicate, duplicate.Value!.Disposition);
        Assert.Equal(first.Value.ObservationId, duplicate.Value.ObservationId);
        var timeline = await fixture.GetTimelineAsync(prompt.Value!.TaskExecutionId!.Value);
        Assert.Equal([1L, 2L], timeline.Observations.Select(item => item.Sequence).Order());
    }

    [Fact]
    public async Task Timeline_preserves_collector_time_and_ingest_sequence_without_claiming_source_order()
    {
        var fixture = new IngestFixture();
        var prompt = await fixture.IngestAsync(PromptJson("turn-order"), Now);
        await fixture.IngestAsync(
            """{"hook_event_name":"PreToolUse","session_id":"session-1","turn_id":"turn-order","tool_name":"first-arrival","tool_use_id":"tool-later-time"}""",
            Now.AddSeconds(2));
        await fixture.IngestAsync(
            """{"hook_event_name":"PostToolUse","session_id":"session-1","turn_id":"turn-order","tool_name":"second-arrival","tool_use_id":"tool-earlier-time"}""",
            Now.AddSeconds(1));

        var timeline = await fixture.GetTimelineAsync(prompt.Value!.TaskExecutionId!.Value);
        Assert.Equal([Now, Now.AddSeconds(1), Now.AddSeconds(2)], timeline.Observations.Select(item => item.ObservedAt));
        Assert.Equal([1L, 3L, 2L], timeline.Observations.Select(item => item.Sequence));
    }

    [Fact]
    public async Task Raw_payload_is_stored_byte_for_byte_and_unknown_events_are_ignored()
    {
        var fixture = new IngestFixture();
        var json = PromptJson("turn-evidence");
        var result = await fixture.IngestAsync(json, Now);
        var stored = fixture.Store.GetRawEvidenceSnapshot(
            new ObservationId(result.Value!.ObservationId!.Value));

        Assert.Equal(Encoding.UTF8.GetBytes(json), stored);

        var unknown = await fixture.IngestAsync(
            """{"hook_event_name":"FutureHook","arbitrary":true}""",
            Now);
        Assert.True(unknown.IsSuccess);
        Assert.Equal(CodexIngestDisposition.Ignored, unknown.Value!.Disposition);
    }

    [Fact]
    public async Task Dependent_event_without_a_known_task_returns_a_correlation_conflict()
    {
        var fixture = new IngestFixture();
        var result = await fixture.IngestAsync(
            """{"hook_event_name":"SubagentStop","session_id":"session-1","turn_id":"missing","agent_id":"agent-1","agent_type":"worker","stop_hook_active":false}""",
            Now);

        Assert.False(result.IsSuccess);
        Assert.Equal("codex_execution_context_not_found", result.Error!.Code);
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("{}")]
    [InlineData("{\"hook_event_name\":\"UserPromptSubmit\",\"session_id\":\"s\"}")]
    public async Task Malformed_or_incomplete_payload_is_rejected(string payload)
    {
        var result = await new IngestFixture().IngestAsync(payload, Now);

        Assert.False(result.IsSuccess);
        Assert.Equal("invalid_codex_hook_payload", result.Error!.Code);
    }

    private static string PromptJson(string turnId) =>
        $$"""{"hook_event_name":"UserPromptSubmit","session_id":"session-1","turn_id":"{{turnId}}","prompt":"Observe this task"}""";

    /// <summary>
    /// Composes the real Codex adapter with in-memory execution ports for tests.
    /// </summary>
    private sealed class IngestFixture
    {
        private readonly GetTaskExecutionTimeline getTimeline;

        public IngestFixture()
        {
            Store = new InMemoryExecutionStore();
            var timeProvider = TimeProvider.System;
            Ingestor = new CodexHookIngestor(
                new CreateConversation(Store, timeProvider),
                new StartTaskExecution(Store, timeProvider),
                new StartAgentExecution(Store, timeProvider),
                new RecordObservation(Store, timeProvider),
                new FinishAgentExecution(Store, timeProvider),
                new FinishTaskExecution(Store, timeProvider),
                new ResolveExecutionRuntimeReference(Store));
            getTimeline = new GetTaskExecutionTimeline(Store);
        }

        public InMemoryExecutionStore Store { get; }

        private CodexHookIngestor Ingestor { get; }

        public ValueTask<Application.Common.UseCaseResult<CodexHookIngestReceipt>> IngestAsync(
            string json,
            DateTimeOffset observedAt) =>
            Ingestor.IngestAsync(Encoding.UTF8.GetBytes(json), observedAt);

        public async Task<TaskExecutionTimeline> GetTimelineAsync(Guid taskExecutionId)
        {
            var result = await getTimeline.ExecuteAsync(
                new GetTaskExecutionTimelineQuery(taskExecutionId));
            return result.Value!;
        }
    }
}
