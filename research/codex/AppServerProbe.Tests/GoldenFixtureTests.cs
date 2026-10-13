using System.Text.Json;
using System.Text.Json.Nodes;

namespace Aigenterra.Research.Tests;

public sealed class GoldenFixtureTests
{
    [Theory]
    [InlineData("0.153.4")]
    [InlineData("0.160.1")]
    public void History_recovery_is_not_a_live_subscription_or_a_replayed_event_stream(string version)
    {
        using var document = Load(version, "recovery");
        var evidence = document.RootElement.GetProperty("diagnostics");
        foreach (var key in new[] { "loadedBeforeRead", "loadedAfterRead", "loadedOnReconnect", "loadedAfterHistory" })
            Assert.Empty(evidence.GetProperty(key).GetProperty("result").GetProperty("data").EnumerateArray());
        Assert.True(evidence.GetProperty("readBefore").GetProperty("succeeded").GetBoolean());
        Assert.Equal(0, evidence.GetProperty("observerLifecycleNotifications").GetInt32());
        Assert.Equal(0, evidence.GetProperty("observerLifecycleNotificationsAfterReconnect").GetInt32());
        Assert.Equal(2, evidence.GetProperty("historyWhileConnected").GetProperty("turns").GetArrayLength());
        Assert.Equal(3, evidence.GetProperty("historyAfterReconnect").GetProperty("turns").GetArrayLength());
        Assert.False(evidence.GetProperty("resumeSucceeded").GetBoolean());
        Assert.Contains("active writer", evidence.GetProperty("resumeError").GetProperty("message").GetString());
        // Unsubscribe does not unload the owning thread or release its writer.
        Assert.Single(evidence.GetProperty("ownerLoadedAfterUnsubscribe").GetProperty("result").GetProperty("data").EnumerateArray());
    }

    [Theory]
    [InlineData("0.153.4")]
    [InlineData("0.160.1")]
    public void Interrupted_root_has_no_success_evidence_for_its_unfinished_MCP_item(string version)
    {
        using var document = Load(version, "interrupt-tool");
        var root = document.RootElement;
        var evidence = root.GetProperty("diagnostics");
        Assert.Equal("interrupted", evidence.GetProperty("terminalStatus").GetString());
        var id = evidence.GetProperty("itemId").GetString();
        Assert.DoesNotContain(root.GetProperty("appServer").EnumerateArray(), x => Method(x) == "item/completed"
            && x.GetProperty("params").GetProperty("item").GetProperty("id").GetString() == id);
        Assert.DoesNotContain(root.GetProperty("hooks").EnumerateArray(), x => x.GetProperty("hook_event_name").GetString() == "PostToolUse");
        Assert.Single(root.GetProperty("hooks").EnumerateArray(), x => x.GetProperty("hook_event_name").GetString() == "Interrupt");
    }

    [Theory]
    [InlineData("0.153.4")]
    [InlineData("0.160.1")]
    public void Late_child_tool_success_does_not_erase_interrupted_turn_and_has_explicit_owner(string version)
    {
        using var document = Load(version, "interrupt-child");
        var root = document.RootElement;
        var evidence = root.GetProperty("diagnostics");
        var childId = evidence.GetProperty("threadId").GetString();
        var itemId = evidence.GetProperty("itemId").GetString();
        var messages = root.GetProperty("appServer").EnumerateArray().ToArray();
        var terminal = Assert.Single(messages, x => Method(x) == "turn/completed" && x.GetProperty("params").GetProperty("threadId").GetString() == childId);
        var tool = Assert.Single(messages, x => Method(x) == "item/completed" && x.GetProperty("params").GetProperty("item").GetProperty("id").GetString() == itemId);
        Assert.Equal("interrupted", terminal.GetProperty("params").GetProperty("turn").GetProperty("status").GetString());
        Assert.Equal("completed", tool.GetProperty("params").GetProperty("item").GetProperty("status").GetString());
        Assert.True(tool.GetProperty("emittedAtMs").GetInt64() > terminal.GetProperty("emittedAtMs").GetInt64());
        var hooks = root.GetProperty("hooks").EnumerateArray().ToArray();
        Assert.DoesNotContain(hooks, x => x.GetProperty("hook_event_name").GetString() == "SubagentStop");
        var pre = Assert.Single(hooks, x => x.GetProperty("hook_event_name").GetString() == "PreToolUse" && x.GetProperty("tool_use_id").GetString() == itemId);
        Assert.Equal(childId, pre.GetProperty("agent_id").GetString());
        Assert.NotEqual(childId, pre.GetProperty("session_id").GetString());
        Assert.Equal(evidence.GetProperty("turnId").GetString(), pre.GetProperty("turn_id").GetString());
    }

    [Theory]
    [InlineData("0.153.4")]
    [InlineData("0.160.1")]
    public void Code_Mode_parallel_calls_have_per_call_hooks_and_reload_rechecks_trust(string version)
    {
        using var document = Load(version, "code-mode");
        var root = document.RootElement;
        var inventory = root.GetProperty("diagnostics");
        JsonElement SessionStart(string key) => inventory.GetProperty(key).EnumerateArray().Single(x => x.GetProperty("eventName").GetString() == "sessionStart");
        Assert.Equal("trusted", SessionStart("before").GetProperty("trustStatus").GetString());
        Assert.Equal("modified", SessionStart("changed").GetProperty("trustStatus").GetString());
        Assert.NotEqual(SessionStart("before").GetProperty("currentHash").GetString(), SessionStart("changed").GetProperty("currentHash").GetString());
        Assert.True(Equal(SessionStart("before"), SessionStart("restored")));
        Assert.Contains(root.GetProperty("modelToolCalls").EnumerateArray(), x => x.GetProperty("name").GetString() == "exec"
            && x.GetProperty("input").GetString()!.Contains("Promise.all", StringComparison.Ordinal));
        var messages = root.GetProperty("appServer").EnumerateArray().Where(x => Method(x) is "item/started" or "item/completed"
            && x.GetProperty("params").GetProperty("item").GetProperty("type").GetString() == "mcpToolCall").ToArray();
        var starts = messages.Where(x => Method(x) == "item/started").ToArray();
        var finishes = messages.Where(x => Method(x) == "item/completed").ToArray();
        Assert.Equal(2, starts.Length);
        Assert.Equal(2, finishes.Length);
        Assert.True(starts.Max(x => x.GetProperty("emittedAtMs").GetInt64()) < finishes.Min(x => x.GetProperty("emittedAtMs").GetInt64()));
        // Correlation remains exact when evidence delivery is reversed/duplicated.
        var ids = starts.Reverse().Concat(starts).Select(x => x.GetProperty("params").GetProperty("item").GetProperty("id").GetString()).ToHashSet();
        Assert.Equal(2, ids.Count);
        foreach (var id in ids)
            Assert.Equal(2, root.GetProperty("hooks").EnumerateArray().Count(x => x.TryGetProperty("tool_use_id", out var hookId) && hookId.GetString() == id));
    }

    [Theory]
    [InlineData("0.153.4")]
    [InlineData("0.160.1")]
    public void MCP_identity_is_explicit_and_error_state_survives_missing_PostToolUse(string version)
    {
        using var document = Load(version, "mcp-comparison");
        var root = document.RootElement;
        var messages = root.GetProperty("appServer").EnumerateArray().ToArray();
        var hooks = root.GetProperty("hooks").EnumerateArray().ToArray();
        var completed = messages.Where(x => Method(x) == "item/completed"
            && x.GetProperty("params").GetProperty("item").GetProperty("type").GetString() == "mcpToolCall").ToArray();
        Assert.Equal(2, completed.Length);
        foreach (var message in completed)
        {
            var parameters = message.GetProperty("params");
            var item = parameters.GetProperty("item");
            var id = item.GetProperty("id").GetString();
            var pre = hooks.Single(x => x.GetProperty("hook_event_name").GetString() == "PreToolUse"
                && x.GetProperty("tool_use_id").GetString() == id);
            Assert.Equal(parameters.GetProperty("threadId").GetString(), pre.GetProperty("session_id").GetString());
            Assert.Equal(parameters.GetProperty("turnId").GetString(), pre.GetProperty("turn_id").GetString());
            Assert.True(Equal(item.GetProperty("arguments"), pre.GetProperty("tool_input")));
            var post = hooks.Where(x => x.GetProperty("hook_event_name").GetString() == "PostToolUse"
                && x.GetProperty("tool_use_id").GetString() == id).ToArray();
            var marker = item.GetProperty("arguments").GetProperty("marker").GetString();
            // This controlled marker identifies synthetic server output ONLY;
            // production hook/App Server correlation above uses explicit call IDs.
            var external = root.GetProperty("externalMcp").EnumerateArray().Single(x =>
                x.GetProperty("result").GetProperty("structuredContent").GetProperty("marker").GetString() == marker).GetProperty("result");
            Assert.True(Equal(external.GetProperty("content"), item.GetProperty("result").GetProperty("content")));
            Assert.True(Equal(external.GetProperty("structuredContent"), item.GetProperty("result").GetProperty("structuredContent")));
            Assert.True(Equal(external.GetProperty("_meta"), item.GetProperty("result").GetProperty("_meta")));
            if (external.GetProperty("isError").GetBoolean())
            {
                Assert.Equal("failed", item.GetProperty("status").GetString());
                Assert.Equal(JsonValueKind.Null, item.GetProperty("error").ValueKind);
                Assert.Empty(post);
            }
            else
            {
                Assert.Equal("completed", item.GetProperty("status").GetString());
                Assert.True(Equal(external, Assert.Single(post).GetProperty("tool_response")));
            }
            Assert.False(item.GetProperty("result").TryGetProperty("isError", out _));
        }
    }

    [Theory]
    [InlineData("0.153.4")]
    [InlineData("0.160.1")]
    public void Agent_identity_survives_followup_and_child_turns_are_not_parent_turns(string version)
    {
        using var document = Load(version, "agent-topology");
        var items = document.RootElement.GetProperty("appServer").EnumerateArray()
            .Where(x => Method(x) == "item/completed" && x.GetProperty("params").GetProperty("item").GetProperty("type").GetString() == "subAgentActivity")
            .Select(x => x.GetProperty("params")).ToArray();
        var started = items.Where(x => x.GetProperty("item").GetProperty("kind").GetString() == "started").ToArray();
        var agents = started.Select(x => x.GetProperty("item").GetProperty("agentThreadId").GetString()).ToHashSet();
        Assert.Equal(3, agents.Count);
        var followup = Assert.Single(items, x => x.GetProperty("item").GetProperty("kind").GetString() == "interacted");
        Assert.Contains(followup.GetProperty("item").GetProperty("agentThreadId").GetString(), agents);
        var stops = document.RootElement.GetProperty("hooks").EnumerateArray().Where(x => x.GetProperty("hook_event_name").GetString() == "SubagentStop").ToArray();
        Assert.Equal(4, stops.Length);
        var repeated = Assert.Single(stops.GroupBy(x => x.GetProperty("agent_id").GetString()), x => x.Count() == 2);
        Assert.Equal(2, repeated.Select(x => x.GetProperty("turn_id").GetString()).Distinct().Count());
        foreach (var stop in stops)
        {
            Assert.Contains(stop.GetProperty("agent_id").GetString(), agents);
            Assert.Equal(started[0].GetProperty("threadId").GetString(), stop.GetProperty("session_id").GetString());
            Assert.NotEqual(started[0].GetProperty("turnId").GetString(), stop.GetProperty("turn_id").GetString());
        }
    }

    [Theory]
    [InlineData("0.153.4")]
    [InlineData("0.160.1")]
    public void Actual_smoke_streams_prove_thread_turn_hook_identity_and_terminal_status(string version)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", version, "stdio-smoke", "events.json")));
        var messages = document.RootElement.GetProperty("appServer").EnumerateArray().ToArray();
        var thread = messages.Single(x => Method(x) == "thread/started").GetProperty("params").GetProperty("thread").GetProperty("id").GetString();
        var start = messages.Single(x => Method(x) == "turn/started").GetProperty("params");
        var finish = messages.Single(x => Method(x) == "turn/completed").GetProperty("params");
        var prompt = document.RootElement.GetProperty("hooks").EnumerateArray().Single(x => x.GetProperty("hook_event_name").GetString() == "UserPromptSubmit");
        Assert.Equal(thread, start.GetProperty("threadId").GetString());
        Assert.Equal(thread, finish.GetProperty("threadId").GetString());
        Assert.Equal(thread, prompt.GetProperty("session_id").GetString());
        Assert.Equal(start.GetProperty("turn").GetProperty("id").GetString(), prompt.GetProperty("turn_id").GetString());
        Assert.Equal(start.GetProperty("turn").GetProperty("id").GetString(), finish.GetProperty("turn").GetProperty("id").GetString());
        Assert.Equal("completed", finish.GetProperty("turn").GetProperty("status").GetString());
        Assert.DoesNotContain(messages, x => Method(x)?.StartsWith("account/", StringComparison.Ordinal) == true);
    }

    private static string? Method(JsonElement message) => message.TryGetProperty("method", out var method) ? method.GetString() : null;
    private static JsonDocument Load(string version, string scenario) => JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", version, scenario, "events.json")));
    private static bool Equal(JsonElement first, JsonElement second) => JsonNode.DeepEquals(JsonNode.Parse(first.GetRawText()), JsonNode.Parse(second.GetRawText()));
}
