using System.Text.Json;

namespace Aigenterra.Research.Tests;

public sealed class GoldenFixtureTests
{
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
}
