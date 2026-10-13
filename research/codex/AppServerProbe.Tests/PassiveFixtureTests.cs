using System.Text.Json;

namespace Aigenterra.Research.Tests;

public sealed class PassiveFixtureTests
{
    [Theory]
    [InlineData("0.162.1", "passive-revalidation-20261013")]
    [InlineData("0.160.1", "actual-desktop-revalidation-20261013")]
    public void Observers_send_only_handshake_and_history_reads_and_never_answer_server_requests(string version, string scenario)
    {
        using var fixture = Load(version, scenario);
        foreach (var connection in fixture.RootElement.GetProperty("connections").EnumerateObject())
        {
            foreach (var sent in connection.Value.GetProperty("sent").EnumerateArray())
                Assert.Contains(sent.GetProperty("method").GetString(), new[]
                { "initialize", "initialized", "thread/read", "thread/turns/list", "thread/loaded/list" });
            Assert.Equal(0, connection.Value.GetProperty("summary").GetProperty("serverRequests").GetInt32());
        }
        Assert.Equal("KEEP_GATE_BLOCKED", fixture.RootElement.GetProperty("gate").GetString());
    }

    [Fact]
    public void Shared_CLI_root_status_is_live_but_root_MCP_history_is_not_a_live_MCP_stream()
    {
        using var fixture = Cli();
        var connection = Connection(fixture, "unix-1");
        var root = connection.GetProperty("summary").GetProperty("threadId").GetString();
        var events = connection.GetProperty("events").EnumerateArray().ToArray();
        Assert.Contains(events, x => Method(x) == "thread/status/changed" && Thread(x) == root);
        Assert.DoesNotContain(events, x => Thread(x) == root && Method(x) is "turn/started" or "turn/completed" or "item/started" or "item/completed");
        Assert.Contains(Turns(connection, 7).SelectMany(Items), x => Type(x) == "mcpToolCall" && x.GetProperty("status").GetString() == "failed");
        var childMcp = events.Where(x => Method(x) == "item/completed")
            .Select(x => x.GetProperty("params").GetProperty("item")).Where(x => Type(x) == "mcpToolCall").ToArray();
        Assert.Equal(2, childMcp.Length);
        var failed = Assert.Single(childMcp, x => x.GetProperty("status").GetString() == "failed");
        Assert.Equal(JsonValueKind.Null, failed.GetProperty("error").ValueKind);
        Assert.Equal("PROBE_ERROR", failed.GetProperty("result").GetProperty("structuredContent").GetProperty("result").GetString());
    }

    [Fact]
    public void Reconnect_loses_existing_child_event_delivery_even_though_owner_reuses_it_successfully()
    {
        using var fixture = Cli();
        var first = Connection(fixture, "unix-1");
        var child = first.GetProperty("events").EnumerateArray().First(x => Method(x) == "turn/started");
        var childId = Thread(child);
        var reconnect = Connection(fixture, "unix-2");
        var events = reconnect.GetProperty("events").EnumerateArray().ToArray();
        Assert.Contains(events, x => Thread(x) == childId && Method(x) == "thread/status/changed");
        Assert.DoesNotContain(events, x => Thread(x) == childId && Method(x) is "turn/started" or "turn/completed" or "item/completed");
        Assert.Contains(events, x => Method(x) == "turn/completed" && Thread(x) != childId);
        Assert.Contains(Turns(Connection(fixture, "child-history"), 7).SelectMany(Items), x => Type(x) == "agentMessage" &&
            x.GetProperty("text").GetString() == "CHILD_AFTER_RECONNECT");
        Assert.Equal("inProgress", Turns(first, 7).Last().GetProperty("status").GetString());
        var disconnectedTurn = Turns(first, 7).Last().GetProperty("id").GetString();
        Assert.Equal("completed", Turns(reconnect, 4).Single(x => x.GetProperty("id").GetString() == disconnectedTurn).GetProperty("status").GetString());
        // Reconciliation changes known terminal state; it cannot recover unseen deltas.
        Assert.Contains("close handshake", first.GetProperty("summary").GetProperty("failure").GetString());
    }

    [Fact]
    public void Actual_Desktop_history_contains_MCP_child_reuse_and_interruption_without_live_delivery()
    {
        using var fixture = Load("0.160.1", "actual-desktop-revalidation-20261013");
        Assert.Equal("PASSIVE_HISTORY_ONLY", fixture.RootElement.GetProperty("classification").GetString());
        foreach (var name in new[] { "history-1", "history-2" })
        {
            var connection = Connection(fixture, name);
            Assert.Empty(connection.GetProperty("events").EnumerateArray());
            foreach (var snapshot in connection.GetProperty("snapshots").EnumerateArray().Where(x => Method(x) == "thread/loaded/list"))
                Assert.Empty(snapshot.GetProperty("result").GetProperty("data").EnumerateArray());
        }
        Assert.Single(Turns(Connection(fixture, "history-1"), 4));
        var turns = Turns(Connection(fixture, "history-2"), 7);
        Assert.Equal(5, turns.Length);
        Assert.Single(turns, x => x.GetProperty("status").GetString() == "interrupted");
        var items = turns.SelectMany(Items).ToArray();
        Assert.Contains(items, x => Type(x) == "mcpToolCall" && x.GetProperty("status").GetString() == "completed");
        Assert.Contains(items, x => Type(x) == "mcpToolCall" && x.GetProperty("status").GetString() == "failed");
        Assert.Single(items.Where(x => Type(x) == "subAgentActivity").Select(x => x.GetProperty("agentThreadId").GetString()).Distinct());
        Assert.Equal(2, Turns(Connection(fixture, "child-history"), 7).Length);
    }

    private static JsonDocument Cli() => Load("0.162.1", "passive-revalidation-20261013");
    private static JsonDocument Load(string version, string scenario) => JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", version, scenario, "events.json")));
    private static JsonElement Connection(JsonDocument fixture, string name) => fixture.RootElement.GetProperty("connections").GetProperty(name);
    private static JsonElement[] Turns(JsonElement connection, int responseId) => connection.GetProperty("snapshots").EnumerateArray().Single(x => x.GetProperty("id").GetInt32() == responseId).GetProperty("result").GetProperty("data").EnumerateArray().ToArray();
    private static IEnumerable<JsonElement> Items(JsonElement turn) => turn.GetProperty("items").EnumerateArray();
    private static string? Method(JsonElement message) => message.GetProperty("method").GetString();
    private static string? Thread(JsonElement message) => message.GetProperty("params").GetProperty("threadId").GetString();
    private static string? Type(JsonElement item) => item.GetProperty("type").GetString();
}
