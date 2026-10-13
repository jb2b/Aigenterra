using System.Text.Json;

namespace Aigenterra.Research;

// Control operations below target only threads created by this disposable probe.
// They are experiments, never an approved production attachment strategy.
public static class ProbeScenarios
{
    public static async Task<object> HookReloadAsync(ProbeClient client, string workspace)
    {
        var path = Path.Combine(workspace, "home", "hooks.json");
        var original = await File.ReadAllBytesAsync(path);
        var parameters = new { cwds = new[] { Path.Combine(workspace, "repo") } };
        static object Inventory(JsonElement response) => response.GetProperty("result").GetProperty("data")[0]
            .GetProperty("hooks").EnumerateArray().Select(x => new
            {
                eventName = x.GetProperty("eventName"),
                enabled = x.GetProperty("enabled"),
                trustStatus = x.GetProperty("trustStatus"),
                currentHash = x.GetProperty("currentHash")
            }).ToArray();
        var before = Inventory(await client.CallAsync("hooks/list", parameters));
        object changed;
        try
        {
            var config = System.Text.Json.Nodes.JsonNode.Parse(original)!;
            var command = config["hooks"]!["SessionStart"]![0]!["hooks"]![0]!;
            command["commandWindows"] = command["commandWindows"]!.GetValue<string>() + " ";
            await File.WriteAllTextAsync(path, config.ToJsonString());
            changed = Inventory(await client.CallAsync("hooks/list", parameters));
        }
        finally { await File.WriteAllBytesAsync(path, original); }
        var restored = Inventory(await client.CallAsync("hooks/list", parameters));
        return new { before, changed, restored };
    }

    public static async Task<object> InterruptAsync(ProbeClient owner, string rootId, int after, bool child)
    {
        var started = await owner.WaitForAsync(message => Method(message) == "item/started"
            && message.GetProperty("params").GetProperty("item").GetProperty("type").GetString() == "mcpToolCall"
            && (message.GetProperty("params").GetProperty("threadId").GetString() != rootId) == child,
            after, timeoutSeconds: 180);
        var parameters = started.GetProperty("params");
        var threadId = parameters.GetProperty("threadId").GetString()!;
        var turnId = parameters.GetProperty("turnId").GetString()!;
        // The runtime emits item/started before the PreToolUse hook. Give the
        // synthetic delayed tool time to enter its server without awaiting it.
        await Task.Delay(2000);
        var interrupted = await owner.CallAsync("turn/interrupt", new { threadId, turnId });
        var terminal = await owner.WaitForAsync(message => Method(message) == "turn/completed"
            && message.GetProperty("params").GetProperty("threadId").GetString() == threadId,
            after, timeoutSeconds: 60);
        return new
        {
            threadId,
            turnId,
            itemId = parameters.GetProperty("item").GetProperty("id"),
            request = interrupted,
            terminalStatus = terminal.GetProperty("params").GetProperty("turn").GetProperty("status")
        };
    }

    public static async Task<object> RecoveryAsync(ProbeClient owner, string executable, string workspace, string evidence, string threadId)
    {
        var result = new Dictionary<string, object?>();
        await using (var observer = new ProbeClient(executable, workspace, Path.Combine(evidence, "observer-before-disconnect")))
        {
            await InitializeAsync(observer);
            result["loadedBeforeRead"] = await observer.CallAsync("thread/loaded/list", new { });
            result["readBefore"] = Metadata(await observer.CallAsync("thread/read", new { threadId, includeTurns = false }));
            result["loadedAfterRead"] = await observer.CallAsync("thread/loaded/list", new { });
            await HarmlessTurnAsync(owner, threadId, "AIGENTERRA_OBSERVER_CONNECTED");
            result["historyWhileConnected"] = History(await observer.CallAsync("thread/turns/list", new { threadId, limit = 20, itemsView = "notLoaded" }));
            result["observerLifecycleNotifications"] = observer.Messages.Count(x => HasThread(x.Message, threadId));
        }
        await HarmlessTurnAsync(owner, threadId, "AIGENTERRA_OBSERVER_DISCONNECTED");
        await using (var observer = new ProbeClient(executable, workspace, Path.Combine(evidence, "observer-reconnected")))
        {
            await InitializeAsync(observer);
            result["loadedOnReconnect"] = await observer.CallAsync("thread/loaded/list", new { });
            result["historyAfterReconnect"] = History(await observer.CallAsync("thread/turns/list", new { threadId, limit = 20, itemsView = "notLoaded" }));
            var items = await observer.CallAsync("thread/items/list", new { threadId, limit = 20 });
            result["itemsRead"] = new
            {
                succeeded = !items.TryGetProperty("error", out _),
                count = items.TryGetProperty("result", out var itemResult) && itemResult.TryGetProperty("data", out var data) ? data.GetArrayLength() : -1
            };
            result["loadedAfterHistory"] = await observer.CallAsync("thread/loaded/list", new { });
            result["observerLifecycleNotificationsAfterReconnect"] = observer.Messages.Count(x => HasThread(x.Message, threadId));
            // Explicitly unsafe as a general observer action; disposable thread only.
            var resumed = await observer.CallAsync("thread/resume", new { threadId, excludeTurns = true });
            result["resumeSucceeded"] = !resumed.TryGetProperty("error", out _);
            result["resumeError"] = resumed.TryGetProperty("error", out var resumeError) ? resumeError : (JsonElement?)null;
            result["loadedAfterResume"] = await observer.CallAsync("thread/loaded/list", new { });
            result["unsubscribe"] = await observer.CallAsync("thread/unsubscribe", new { threadId });
            result["loadedAfterUnsubscribe"] = await observer.CallAsync("thread/loaded/list", new { });
        }
        result["ownerUnsubscribe"] = await owner.CallAsync("thread/unsubscribe", new { threadId });
        await Task.Delay(1000);
        result["ownerLoadedAfterUnsubscribe"] = await owner.CallAsync("thread/loaded/list", new { });
        await using (var observer = new ProbeClient(executable, workspace, Path.Combine(evidence, "observer-after-owner-unsubscribe")))
        {
            await InitializeAsync(observer);
            var resumed = await observer.CallAsync("thread/resume", new { threadId, excludeTurns = true });
            result["resumeAfterUnsubscribeSucceeded"] = !resumed.TryGetProperty("error", out _);
            result["resumeAfterUnsubscribeError"] = resumed.TryGetProperty("error", out var error) ? error : (JsonElement?)null;
            result["loadedAfterSecondResume"] = await observer.CallAsync("thread/loaded/list", new { });
            // Ensure resume hooks have had a chance to finish before closing.
            await Task.Delay(1000);
        }
        return result;
    }

    private static async Task InitializeAsync(ProbeClient client)
    {
        var response = await client.CallAsync("initialize", new { clientInfo = new { name = "aigenterra_observer_probe", version = "1" }, capabilities = new { experimentalApi = true } });
        if (response.TryGetProperty("error", out _)) throw new InvalidOperationException("Observer initialization rejected.");
        await client.SendAsync(new { method = "initialized" });
    }

    private static async Task HarmlessTurnAsync(ProbeClient owner, string threadId, string marker)
    {
        var after = owner.Messages.Length;
        var started = await owner.CallAsync("turn/start", new { threadId, input = new[] { new { type = "text", text = $"Reply exactly {marker}. Do not use tools." } } });
        if (started.TryGetProperty("error", out _)) throw new InvalidOperationException("Owned turn rejected.");
        await owner.WaitForAsync(message => Method(message) == "turn/completed" && HasThread(message, threadId), after);
    }

    private static object Metadata(JsonElement response) => response.TryGetProperty("result", out var result)
        ? new { succeeded = true, threadId = result.GetProperty("thread").GetProperty("id").GetString() }
        : new { succeeded = false, threadId = (string?)null };

    private static object History(JsonElement response)
    {
        if (!response.TryGetProperty("result", out var result)) return new { succeeded = false, error = response.GetProperty("error") };
        return new
        {
            succeeded = true,
            turns = result.GetProperty("data").EnumerateArray()
            .Select(x => new { id = x.GetProperty("id"), status = x.GetProperty("status") }).ToArray(),
            nextCursor = result.GetProperty("nextCursor")
        };
    }

    private static string? Method(JsonElement message) => message.TryGetProperty("method", out var method) ? method.GetString() : null;
    private static bool HasThread(JsonElement message, string threadId) => Method(message) != null
        && message.TryGetProperty("params", out var parameters) && parameters.TryGetProperty("threadId", out var id) && id.GetString() == threadId;
}
