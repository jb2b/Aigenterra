using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Aigenterra.Adapters.Codex;
using Aigenterra.Application.Evaluations;
using Aigenterra.Application.Executions;
using Aigenterra.Domain.Evaluations;
using Aigenterra.Domain.Executions;

namespace Aigenterra.Codex.Tests;

public sealed class CommandEvidenceTests
{
    [Theory]
    [InlineData("dotnet build", "PreToolUse", EvaluationCheck.Build, CommandVerdict.RequestedOnly)]
    [InlineData("dotnet test", "PreToolUse", EvaluationCheck.Tests, CommandVerdict.RequestedOnly)]
    [InlineData("dotnet build", "PostToolUse", EvaluationCheck.Build, CommandVerdict.Unknown)]
    [InlineData("dotnet test", "PostToolUse", EvaluationCheck.Tests, CommandVerdict.Unknown)]
    public void Automatic_hooks_establish_attempts_but_never_an_outcome_from_text(string command, string hook, EvaluationCheck check, CommandVerdict verdict)
    {
        var payload = new JsonObject
        {
            ["session_id"] = "s",
            ["turn_id"] = "t",
            ["tool_use_id"] = "c",
            ["hook_event_name"] = hook,
            ["tool_name"] = "Bash",
            ["tool_input"] = new JsonObject { ["command"] = command },
            ["tool_response"] = "Build succeeded. All tests passed. Exit code: 0"
        };
        var input = new EvaluationInput(TaskExecutionId.New(), 1, [Observation(payload, hook == "PreToolUse" ? "tool-invocation" : "tool-result")]);
        var result = new BuildTestEvaluator().Evaluate(input, new CodexCommandEvidenceDecoder().Decode(input), DateTimeOffset.UtcNow);
        var finding = result.Findings.Single(f => f.Check == check);
        Assert.Equal(Detection.Observed, finding.Detection);
        Assert.Equal(verdict, Assert.Single(finding.Attempts).Verdict);
        Assert.Null(finding.Attempts[0].ExitCode);
    }
    [Theory]
    [InlineData("missing_payload")]
    [InlineData("missing_identity")]
    [InlineData("unsupported_tool")]
    [InlineData("vehicle_commands")]
    public void Incomplete_or_ambiguous_automatic_evidence_remains_unknown(string scenario)
    {
        var payload = JsonNode.Parse("""{"session_id":"s","turn_id":"t","tool_use_id":"c","hook_event_name":"PreToolUse","tool_name":"Bash","tool_input":{"command":"dotnet build"}}""")!;
        if (scenario == "missing_identity") payload.AsObject().Remove("tool_use_id");
        if (scenario == "unsupported_tool") payload["tool_name"] = "apply_patch";
        var observations = new List<EvaluationObservation> { Observation(payload, "tool-invocation") };
        if (scenario == "missing_payload") observations[0] = observations[0] with { Evidence = null };
        if (scenario == "vehicle_commands")
        {
            observations.Clear();
            foreach (var command in new[] { "pwsh -NoProfile -File 'Signal-Phase.ps1' -Role vehicle -Phase ready",
                "[System.IO.File]::WriteAllText('vehicle.txt', 'acceptance')\ngit rev-parse HEAD\ngit status --porcelain",
                "pwsh -NoProfile -File 'Signal-Phase.ps1' -Role vehicle -Phase committed" })
            {
                var copy = payload.DeepClone();
                copy["tool_use_id"] = Guid.NewGuid().ToString();
                copy["tool_input"]!["command"] = command;
                observations.Add(Observation(copy, "tool-invocation"));
            }
        }
        var input = new EvaluationInput(TaskExecutionId.New(), 10, observations);
        var result = new BuildTestEvaluator().Evaluate(input, new CodexCommandEvidenceDecoder().Decode(input), DateTimeOffset.UtcNow);
        Assert.All(result.Findings, f => { Assert.Equal(Detection.Unknown, f.Detection); Assert.Empty(f.Attempts); });
    }
    [Fact]
    public void Real_hooks_never_become_success_from_stdout_and_real_runtime_items_supply_exit_codes()
    {
        var hooks = JsonNode.Parse(File.ReadAllText(Fixture("codex-0.153.4-command-hooks.json")))!["events"]!.AsArray();
        var observations = hooks.Select(h =>
        {
            var payload = h!.DeepClone().AsObject();
            payload.Remove("keys"); payload["session_id"] = "probe-session"; payload["turn_id"] = "probe-turn";
            return Observation(payload, payload["hook_event_name"]!.GetValue<string>() == "PreToolUse" ? "tool-invocation" : "tool-result");
        }).ToList();
        var input = new EvaluationInput(TaskExecutionId.New(), 8, observations);
        var evaluator = new BuildTestEvaluator();
        var hookOnly = evaluator.Evaluate(input, new CodexCommandEvidenceDecoder().Decode(input), DateTimeOffset.UtcNow);
        Assert.All(hookOnly.Findings, f => Assert.All(f.Attempts, a => Assert.Equal(CommandVerdict.Unknown, a.Verdict)));
        Assert.Equal(4, hookOnly.Findings.Sum(f => f.Attempts.Count));
        var results = JsonNode.Parse(File.ReadAllText(Fixture("codex-0.153.4-command-results.json")))!.AsArray();
        observations.AddRange(results.Select(r => Observation(r!, "command-result")));
        input = input with { Watermark = 12 };
        var captured = evaluator.Evaluate(input, new CodexCommandEvidenceDecoder().Decode(input), DateTimeOffset.UtcNow);
        Assert.All(captured.Findings, f =>
        {
            Assert.Equal([CommandVerdict.Succeeded, CommandVerdict.Failed], f.Attempts.Select(a => a.Verdict));
            Assert.All(f.Attempts, a => Assert.Equal(3, a.Evidence.Count));
        });
    }
    [Theory]
    [InlineData("completed", null)]
    [InlineData("completed", 1)]
    [InlineData("failed", 0)]
    [InlineData("inProgress", 0)]
    [InlineData("declined", null)]
    public void Invalid_terminal_metadata_never_produces_success(string status, int? exit)
    {
        var node = JsonNode.Parse(File.ReadAllText(Fixture("codex-0.153.4-command-results.json")))![0]!.DeepClone();
        node["item"]!["status"] = status; node["item"]!["exitCode"] = exit;
        var input = new EvaluationInput(TaskExecutionId.New(), 1, [Observation(node, "command-result")]);
        var result = new BuildTestEvaluator().Evaluate(input, new CodexCommandEvidenceDecoder().Decode(input), DateTimeOffset.UtcNow);
        Assert.Equal(CommandVerdict.Unknown, Assert.Single(result.Findings[0].Attempts).Verdict);
    }
    [Fact]
    public void Missing_or_malformed_source_is_unknown()
    {
        var observation = new EvaluationObservation(ObservationId.New(), 1, DateTimeOffset.UtcNow, "toolresult", new("codex", "tool-result", "a"), new("application/json", Encoding.UTF8.GetBytes("not json")));
        var decoded = new CodexCommandEvidenceDecoder().Decode(new(TaskExecutionId.New(), 1, [observation]));
        Assert.True(decoded.HasUninterpretableEvidence);
        Assert.Empty(decoded.Commands);
    }
    private static long sequence;
    private static EvaluationObservation Observation(JsonNode payload, string kind) => new(ObservationId.New(), Interlocked.Increment(ref sequence), DateTimeOffset.UnixEpoch,
        kind == "tool-invocation" ? "toolinvocation" : "toolresult", new("codex", kind, Guid.NewGuid().ToString()),
        new("application/json", JsonSerializer.SerializeToUtf8Bytes(payload)));
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
}
