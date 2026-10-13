using System.Text.Json;
using Aigenterra.Research;

if (args.Length < 3)
{
    Console.Error.WriteLine("Usage: AppServerProbe <codex.exe> <isolated-hook-probe-root> <model> [prompt] [scenario]");
    return 2;
}
var executable = Path.GetFullPath(args[0]);
var workspace = Path.GetFullPath(args[1]);
var model = args[2];
var scenario = args.Length > 4 ? args[4] : "stdio-smoke";
var manifestPath = Path.Combine(workspace, "manifest.json");
if (!File.Exists(manifestPath)) throw new InvalidOperationException("First create an isolated workspace with Invoke-HookProbe.ps1.");
using var manifest = JsonDocument.Parse(await File.ReadAllBytesAsync(manifestPath));
if (!string.Equals(manifest.RootElement.GetProperty("outputRoot").GetString(), workspace, StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("Workspace marker does not match; refusing to touch a different home.");
var evidence = Path.Combine(workspace, "evidence", "appserver-" + Guid.NewGuid().ToString("N"));
var auth = Path.Combine(workspace, "home", "auth.json");
var authSource = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "auth.json");
var summary = new Dictionary<string, object?> { ["model"] = model, ["evidence"] = evidence, ["scenario"] = scenario };
try
{
    if (File.Exists(authSource)) File.Copy(authSource, auth, overwrite: false);
    await using var client = new ProbeClient(executable, workspace, evidence,
        scenario == "code-mode" ? ["-c", "features.code_mode=true"] : []);
    var initialize = await client.CallAsync("initialize", new { clientInfo = new { name = "aigenterra_spike", version = "1" }, capabilities = new { experimentalApi = true } });
    summary["initialize"] = initialize;
    RequireResult(initialize);
    await client.SendAsync(new { method = "initialized" });
    summary["hooks"] = await client.CallAsync("hooks/list", new { cwds = new[] { Path.Combine(workspace, "repo") } });
    summary["loadedBefore"] = await client.CallAsync("thread/loaded/list", new { });
    if (scenario is "hook-reload" or "code-mode")
        summary["scenarioEvidence"] = await ProbeScenarios.HookReloadAsync(client, workspace);
    var start = await client.CallAsync("thread/start", new { model, cwd = Path.Combine(workspace, "repo"), approvalPolicy = "never", sandbox = "read-only" });
    summary["threadStart"] = start;
    if (start.TryGetProperty("error", out _)) throw new InvalidOperationException("thread/start rejected; inspect raw evidence.");
    var threadId = start.GetProperty("result").GetProperty("thread").GetProperty("id").GetString()!;
    summary["mcpStatus"] = await client.CallAsync("mcpServerStatus/list", new { });
    var prompt = args.Length > 3 ? args[3] : "Reply exactly AIGENTERRA_APP_SERVER_SMOKE. Do not use tools.";
    var after = client.Messages.Length;
    var turnStart = await client.CallAsync("turn/start", new { threadId, input = new[] { new { type = "text", text = prompt } } });
    summary["turnStart"] = turnStart;
    RequireResult(turnStart);
    if (scenario is "interrupt-tool" or "interrupt-child")
        summary["scenarioEvidence"] = await ProbeScenarios.InterruptAsync(client, threadId, after, scenario == "interrupt-child");
    var completed = await client.WaitForAsync(message => message.TryGetProperty("method", out var method)
        && method.GetString() == "turn/completed" && message.GetProperty("params").GetProperty("threadId").GetString() == threadId, after, timeoutSeconds: 240);
    summary["completed"] = completed;
    summary["turnStatus"] = completed.GetProperty("params").GetProperty("turn").GetProperty("status").GetString();
    summary["read"] = await client.CallAsync("thread/read", new { threadId, includeTurns = true });
    summary["loadedAfter"] = await client.CallAsync("thread/loaded/list", new { });
    if (scenario == "recovery")
        summary["scenarioEvidence"] = await ProbeScenarios.RecoveryAsync(client, executable, workspace, evidence, threadId);
    summary["methods"] = client.Messages.Where(x => x.Message.TryGetProperty("method", out _))
        .GroupBy(x => x.Message.GetProperty("method").GetString()!).ToDictionary(x => x.Key, x => x.Count());
    summary["receivedCount"] = client.Messages.Length;
    summary["status"] = "observed";
}
catch (Exception error)
{
    summary["status"] = "incomplete";
    summary["errorType"] = error.GetType().Name;
    summary["error"] = error.Message;
}
finally
{
    if (File.Exists(auth)) File.Delete(auth);
    Directory.CreateDirectory(evidence);
    await File.WriteAllTextAsync(Path.Combine(evidence, "summary.json"), JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));
}
Console.WriteLine(JsonSerializer.Serialize(new { evidence, status = summary["status"], messages = summary.GetValueOrDefault("receivedCount") }));
return summary["status"] as string == "observed" ? 0 : 1;

static void RequireResult(JsonElement response)
{
    if (response.TryGetProperty("error", out _)) throw new InvalidOperationException("RPC rejected; inspect archived response.");
}
