using System.Security.Cryptography;
using System.Text.Json;
using Aigenterra.Adapters.Codex.Normalization;
using Aigenterra.Application.Recording;
using Aigenterra.Application.Runtime;

namespace Aigenterra.Codex.Tests;

public sealed class RuntimeActivityTests
{
    [Theory]
    [InlineData("commandExecution")]
    [InlineData("fileChange")]
    public void Supported_history_items_keep_explicit_terminal_conflicts(string kind)
    {
        var capture = Capture(JsonSerializer.Serialize(new { id = 3, result = new { data = new[] { new { id = "turn", items = new[] { new { id = "item", type = kind, status = "completed" } } } } } }), "reader_frame");
        var context = Context(capture, "thread");
        var normalizer = new CodexEvidenceNormalizer();
        var first = normalizer.Normalize(capture, context);
        var changed = Capture(System.Text.Encoding.UTF8.GetString(capture.Payload).Replace("completed", "failed", StringComparison.Ordinal), "reader_frame", capture.Source);
        var second = normalizer.Normalize(changed, context with { ResponseCaptureId = changed.CaptureId });
        var activity = Assert.Single(RuntimeActivityProjector.Project([first, second]));
        Assert.Equal(kind, activity.Kind);
        Assert.True(activity.Conflict);
        Assert.Equal("TerminalUnknown", activity.Status);
    }

    [Fact]
    public void Research_history_and_hook_evidence_converge_without_duplicate_calls_or_false_success()
    {
        using var fixture = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "runtime-history-activities.json")));
        var response = fixture.RootElement.GetProperty("response");
        var capture = Capture(response.GetRawText(), "reader_frame");
        var thread = fixture.RootElement.GetProperty("threadId").GetString()!;
        var context = Context(capture, thread);
        var normalizer = new CodexEvidenceNormalizer();
        var history = normalizer.Normalize(capture, context);
        Assert.Equal(2, history.Facts.Count);
        var success = Assert.Single(history.Facts, f => f.Status == "Completed");
        Assert.Single(history.Facts, f => f.Status == "Failed"); // Original error:null never overrides explicit failed.
        var pre = Hook("PreToolUse", success, capture.Source, false);
        var post = Hook("PostToolUse", success, capture.Source, false);
        var duplicate = capture with { CaptureId = Guid.NewGuid() };
        RuntimeEvidenceBatch[] inputs = [normalizer.Normalize(pre, null), normalizer.Normalize(post, null), history,
            normalizer.Normalize(duplicate, context with { ResponseCaptureId = duplicate.CaptureId })];
        var activities = RuntimeActivityProjector.Project(inputs);
        Assert.Equal(2, activities.Count);
        var completed = Assert.Single(activities, a => a.Status == "Completed");
        Assert.Equal(4, completed.Evidence.Count);
        Assert.False(completed.Conflict);
        Assert.Equal(new[] { "Observed", "Reconciled" }, completed.Derivations);
        Assert.Equal(JsonSerializer.Serialize(activities), JsonSerializer.Serialize(RuntimeActivityProjector.Project(inputs.Reverse().Concat(inputs))));
        Assert.Contains(success.Fields, field => field.CaptureId == context.RequestCaptureId);
    }

    [Fact]
    public void Missing_results_unknown_ids_child_ownership_and_namespaces_remain_explicit()
    {
        var normalizer = new CodexEvidenceNormalizer();
        var pre = Capture("""{"hook_event_name":"PreToolUse","session_id":"root","agent_id":"child","turn_id":"turn","tool_use_id":"call","tool_name":"mcp__server__tool"}""", "hook_stdin");
        var requested = Assert.Single(RuntimeActivityProjector.Project([normalizer.Normalize(pre, null)]));
        Assert.Equal("child", requested.ThreadId);
        Assert.Equal("Requested", requested.Status);
        Assert.Equal("Partial", requested.Coverage);
        var missing = Capture("""{"hook_event_name":"PostToolUse","session_id":"root","turn_id":"turn","tool_response":{"error":null}}""", "hook_stdin", pre.Source);
        var secondMissing = missing with { CaptureId = Guid.NewGuid() };
        var activities = RuntimeActivityProjector.Project([normalizer.Normalize(missing, null), normalizer.Normalize(secondMissing, null)]);
        Assert.Equal(2, activities.Count);
        Assert.All(activities, a => { Assert.Equal("Unresolved", a.Identity); Assert.Equal("ResultObserved", a.Status); });
        var other = pre with { Source = pre.Source with { RuntimeNamespaceId = Guid.NewGuid() } };
        Assert.Equal(2, RuntimeActivityProjector.Project([normalizer.Normalize(pre, null), normalizer.Normalize(other, null)]).Count);
        Assert.Empty(normalizer.Normalize(pre with { Source = pre.Source with { RuntimeVersion = "future" } }, null).Facts);
    }

    internal static RuntimeCaptureEnvelope Capture(string json, string provenance, RuntimeCaptureSource? scope = null)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        var source = (scope ?? new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "codex", "", "", "", RuntimeVersion: "0.162.1")) with
        { SourceKind = provenance == "hook_stdin" ? "hook" : "app_server", EvidenceStage = provenance == "hook_stdin" ? "HookVisible" : "AppServerVisible", Provenance = provenance };
        return new(2, Guid.NewGuid(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, source, "application/json", bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)), bytes);
    }
    internal static VerifiedRuntimeRequest Context(RuntimeCaptureEnvelope capture, string thread) => new(Guid.NewGuid(), Guid.NewGuid(), capture.CaptureId,
        capture.Source.RuntimeNamespaceId, capture.Source.SourceId, capture.Source.ConnectionEpochId, Guid.NewGuid(), "thread/turns/list", thread, null, null);
    private static RuntimeCaptureEnvelope Hook(string name, RuntimeEvidenceFact activity, RuntimeCaptureSource source, bool failed) => Capture(JsonSerializer.Serialize(new
    {
        hook_event_name = name,
        session_id = activity.ThreadId,
        turn_id = activity.TurnId,
        tool_use_id = activity.ExternalId,
        tool_name = "mcp__" + activity.Values["server"] + "__" + activity.Values["tool"],
        tool_response = new { isError = failed }
    }), "hook_stdin", source);
}
