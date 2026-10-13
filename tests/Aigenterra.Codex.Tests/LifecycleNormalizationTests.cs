using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Aigenterra.Adapters.Codex.Normalization;
using Aigenterra.Application.Recording;
using Aigenterra.Application.Runtime;

namespace Aigenterra.Codex.Tests;

public sealed class LifecycleNormalizationTests
{
    private readonly CodexLifecycleNormalizer normalizer = new();

    [Theory]
    [InlineData("0.153.4")]
    [InlineData("0.160.1")]
    public void Research_fixtures_normalize_root_child_reuse_and_interruptions_without_rewriting_raw(string version)
    {
        foreach (var scenario in new[] { "stdio-smoke", "agent-topology", "interrupt-tool", "interrupt-child" })
        {
            using var doc = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(FindRoot(), "research", "codex", "fixtures", version, scenario, "events.json")));
            var facts = new List<RuntimeFact>();
            var namespaceId = Guid.NewGuid();
            foreach (var message in doc.RootElement.GetProperty("appServer").EnumerateArray())
            {
                var capture = Capture(message.GetRawText(), version) with { Source = Source(version) with { RuntimeNamespaceId = namespaceId } };
                var original = capture.Payload.ToArray();
                var result = normalizer.Normalize(capture);
                Assert.NotEqual("rejected", result.Status);
                Assert.Equal(original, capture.Payload);
                facts.AddRange(result.Facts);
            }
            Assert.Contains(facts, fact => fact.Kind == "turn" && fact.Status != "Running");
            if (scenario == "agent-topology")
            {
                Assert.True(facts.Where(f => f.Kind == "turn").Select(f => f.ThreadId).Distinct().Count() >= 4);
                Assert.Contains(facts.Where(f => f.Kind == "turn" && f.Status == "Completed").GroupBy(f => f.ThreadId), group => group.Select(f => f.TurnId).Distinct().Count() >= 2);
            }
            if (scenario.StartsWith("interrupt", StringComparison.Ordinal)) Assert.Contains(facts, f => f.Status == "Interrupted");
        }
    }

    [Fact]
    public void Current_cli_notifications_and_actual_desktop_self_identifying_history_are_supported_without_live_claim()
    {
        foreach (var (version, scenario, key) in new[] {
            ("0.162.1", "passive-revalidation-20261013", "unix-1"),
            ("0.160.1", "actual-desktop-revalidation-20261013", "child-history") })
        {
            using var doc = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(FindRoot(), "research", "codex", "fixtures", version, scenario, "events.json")));
            var connection = doc.RootElement.GetProperty("connections").GetProperty(key);
            var captures = new List<RuntimeFact>();
            Walk(connection, item =>
            {
                var history = item.TryGetProperty("result", out _);
                var capture = Capture(item.GetRawText(), version);
                if (history) capture = capture with { Source = capture.Source with { Provenance = "history_response" } };
                captures.AddRange(normalizer.Normalize(capture).Facts);
            });
            if (key == "unix-1") Assert.Contains(captures, f => f.Kind == "turn" && f.Status == "Completed");
            if (key == "child-history")
            {
                Assert.All(captures, f => Assert.Equal("Reconciled", f.Derivation));
                Assert.Contains(captures, f => f.ThreadScope == "Child");
            }
        }
    }

    [Fact]
    public void History_requires_self_identifying_thread_and_can_yield_several_reconciled_turn_facts()
    {
        var capture = Capture("""{"id":1,"result":{"thread":{"id":"root","parentThreadId":null,"source":"cli","status":{"type":"idle"},"turns":[{"id":"a","status":"completed","completedAt":1791898138},{"id":"b","status":"interrupted"}]}}}""");
        capture = capture with { Source = capture.Source with { Provenance = "history_response" } };
        var facts = normalizer.Normalize(capture).Facts;
        Assert.Equal(3, facts.Count);
        Assert.All(facts, f => Assert.Equal("Reconciled", f.Derivation));
        Assert.Equal("unix_seconds", facts[1].OccurredAt!.Unit);
        Assert.Equal("Root", facts[2].ThreadScope);
        var orphanPage = capture with { Payload = """{"id":1,"result":{"data":[{"id":"a","status":"completed"}]}}"""u8.ToArray() };
        Assert.Equal("unsupported", normalizer.Normalize(orphanPage).Status);
    }

    [Theory]
    [InlineData("{\"method\":\"future/event\",\"params\":{\"extra\":true}}", "unsupported")]
    [InlineData("{\"id\":1,\"method\":\"turn/completed\",\"params\":{}}", "unsupported")]
    [InlineData("{\"method\":\"turn/completed\",\"params\":{}}", "rejected")]
    [InlineData("{\"method\":\"turn/started\"}", "rejected")]
    [InlineData("not json", "rejected")]
    public void Unknown_requests_and_malformed_payloads_produce_zero_facts(string json, string status)
    {
        var result = normalizer.Normalize(Capture(json));
        Assert.Equal(status, result.Status);
        Assert.Empty(result.Facts);
    }

    [Fact]
    public void Unknown_version_is_not_assumed_compatible_and_unknown_status_is_not_success()
    {
        const string json = """{"method":"turn/completed","params":{"threadId":"t","turn":{"id":"u","status":"futureState","error":null,"extra":{"new":true}}},"emittedAtMs":123}""";
        Assert.Equal("unverified_runtime_version", normalizer.Normalize(Capture(json, "99.0.0")).Reason);
        var fact = Assert.Single(normalizer.Normalize(Capture(json)).Facts);
        Assert.Equal("TerminalUnknown", fact.Status);
        Assert.Null(fact.OccurredAt);
        Assert.Equal(123, fact.SourceEmittedAtMilliseconds);
    }

    [Fact]
    public void Hooks_keep_child_owner_and_root_context_separate_and_stop_is_versioned_inference()
    {
        var capture = Capture("""{"hook_event_name":"SubagentStop","session_id":"root","agent_id":"child","turn_id":"child-turn","stop_hook_active":false} """);
        capture = capture with { Source = capture.Source with { SourceKind = "hook", EvidenceStage = "HookVisible", Provenance = "hook_stdin" } };
        var fact = Assert.Single(normalizer.Normalize(capture).Facts);
        Assert.Equal("child", fact.ThreadId);
        Assert.Equal("root", fact.ConversationThreadId);
        Assert.Equal("Child", fact.ThreadScope);
        Assert.Equal("Inferred", fact.Derivation);
        Assert.Equal("Completed", fact.Status);
        Assert.Null(fact.OccurredAt);
        Assert.Equal("codex.lifecycle/1", normalizer.Version);
        var start = capture with { Payload = Encoding.UTF8.GetBytes("""{"hook_event_name":"UserPromptSubmit","session_id":"root","turn_id":"root-turn"}""") };
        Assert.Equal(2, normalizer.Normalize(start).Facts.Count);
    }

    private static RuntimeCaptureSource Source(string version) => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "codex", "app_server", "AppServerVisible", "live_notification", RuntimeVersion: version);
    internal static RuntimeCaptureEnvelope Capture(string json, string version = "0.162.1")
    {
        var payload = Encoding.UTF8.GetBytes(json);
        return new(2, Guid.NewGuid(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, Source(version), "application/json",
            payload.Length, Convert.ToHexString(SHA256.HashData(payload)), payload);
    }
    private static void Walk(JsonElement element, Action<JsonElement> action)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("method", out _) || element.TryGetProperty("result", out _)) action(element);
            foreach (var property in element.EnumerateObject()) Walk(property.Value, action);
        }
        else if (element.ValueKind == JsonValueKind.Array) foreach (var item in element.EnumerateArray()) Walk(item, action);
    }
    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Aigenterra.slnx"))) return directory.FullName;
        throw new DirectoryNotFoundException();
    }
}
