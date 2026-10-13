using System.Text.Json;
using System.Text.Json.Nodes;

namespace Aigenterra.Research.Tests;

public sealed class GoldenFixtureTests
{
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
