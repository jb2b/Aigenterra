using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Aigenterra.Research;

// Research only. The normal CLI/Desktop creates and owns all observed work.
if (args.Length != 7 || args[5] is not ("proxy" or "history" or "unix") ||
    !int.TryParse(args[6], out var seconds) || seconds is < 0 or > 1800)
{
    Console.Error.WriteLine("Usage: PassiveObserver <codex.exe> <CODEX_HOME> <cwd> <threadId> <new-evidence-dir> <unix|history|proxy> <seconds>");
    return 2;
}
if (args[5] == "unix") return await UnixObserver.RunAsync(args, seconds);

var output = Path.GetFullPath(args[4]);
if (Directory.Exists(output)) throw new IOException("Evidence directory must be new.");
Directory.CreateDirectory(output);
var start = new ProcessStartInfo(Path.GetFullPath(args[0]))
{
    WorkingDirectory = Path.GetFullPath(args[2]),
    UseShellExecute = false,
    CreateNoWindow = true,
    RedirectStandardInput = true,
    RedirectStandardOutput = true,
    RedirectStandardError = true
};
start.Environment["CODEX_HOME"] = Path.GetFullPath(args[1]);
start.ArgumentList.Add("app-server");
if (args[5] == "proxy") start.ArgumentList.Add("proxy");
await using var received = new FileStream(Path.Combine(output, "received.ndjson"), FileMode.CreateNew,
    FileAccess.Write, FileShare.Read, 8192, FileOptions.Asynchronous | FileOptions.WriteThrough);
await using var sent = new StreamWriter(Path.Combine(output, "sent.ndjson")) { AutoFlush = true };
await using var errors = File.Create(Path.Combine(output, "stderr.txt"));
using var process = Process.Start(start) ?? throw new IOException("Cannot start observer transport.");
var startedAt = DateTimeOffset.UtcNow;
var pending = new RpcPending();
var messages = new ConcurrentQueue<ReceivedMessage>();
var watch = Stopwatch.StartNew();
var errorReader = process.StandardError.BaseStream.CopyToAsync(errors);
var reader = ReadAsync();
var nextId = 0;
string? failure = null;
try
{
    await CallAsync("initialize", new
    {
        clientInfo = new { name = "aigenterra_passive_revalidation", version = "6.5" },
        capabilities = new { experimentalApi = true }
    });
    await WriteAsync(new { method = "initialized" });
    await SnapshotAsync();
    Console.WriteLine($"Observer connected ({args[5]}). Waiting {seconds}s; history snapshots are not live events.");
    // A local stop file disconnects only this observer, never the owner/runtime.
    var until = DateTimeOffset.UtcNow.AddSeconds(seconds);
    while (DateTimeOffset.UtcNow < until && !File.Exists(Path.Combine(output, "stop")) && !reader.IsCompleted)
        await Task.Delay(100);
    await SnapshotAsync();
}
catch (Exception error)
{
    failure = error.GetType().Name + ": " + error.Message;
    Console.Error.WriteLine(failure);
}
finally
{
    process.StandardInput.Close();
    try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3)); }
    catch (TimeoutException)
    {
        // Only the process we created. Never kill the shared daemon or its tree.
        process.Kill();
        await process.WaitForExitAsync();
    }
    try { await reader; }
    catch (Exception error) { failure ??= error.GetType().Name + ": " + error.Message; }
    await errorReader;
    await File.WriteAllLinesAsync(Path.Combine(output, "receipts.ndjson"), messages.Select((entry, index) =>
        JsonSerializer.Serialize(new { index, entry.ReceivedAt, entry.ElapsedMs })));
    await File.WriteAllTextAsync(Path.Combine(output, "summary.json"), JsonSerializer.Serialize(new
    {
        transport = args[5],
        threadId = args[3],
        startedAt,
        finishedAt = DateTimeOffset.UtcNow,
        transportPid = process.Id,
        transportExitCode = process.ExitCode,
        failure,
        messageCount = messages.Count,
        notifications = messages.Where(x => x.Message.TryGetProperty("method", out _) &&
            !x.Message.TryGetProperty("id", out _)).Select(x => x.Message.GetProperty("method").GetString()).ToArray(),
        serverRequests = messages.Count(x => x.Message.TryGetProperty("method", out _) &&
            x.Message.TryGetProperty("id", out _))
    }, new JsonSerializerOptions { WriteIndented = true }));
}
return failure is null ? 0 : 1;

async Task ReadAsync()
{
    try
    {
        await WireProtocol.ReadAsync(process.StandardOutput.BaseStream, received, message =>
        {
            messages.Enqueue(new(DateTimeOffset.UtcNow, watch.Elapsed.TotalMilliseconds, message));
            pending.Receive(message);
            // Server requests (including approvals) are captured, never answered.
        });
    }
    finally { pending.Fail(new EndOfStreamException("Observer transport closed stdout.")); }
}

async Task WriteAsync(object message)
{
    var json = JsonSerializer.Serialize(message);
    await sent.WriteLineAsync(json);
    await process.StandardInput.WriteLineAsync(json);
    await process.StandardInput.FlushAsync();
}

async Task CallAsync(string method, object parameters)
{
    if (method is not ("initialize" or "thread/read" or "thread/turns/list" or "thread/loaded/list"))
        throw new InvalidOperationException("Operation is outside the passive research allowlist.");
    if (reader.IsCompleted) { await reader; throw new EndOfStreamException("Transport already closed."); }
    var id = ++nextId;
    var response = pending.Add(id);
    try
    {
        await WriteAsync(new { id, method, @params = parameters });
        var result = await response.WaitAsync(TimeSpan.FromSeconds(30));
        if (result.TryGetProperty("error", out _)) Console.WriteLine($"{method}: RPC error captured.");
    }
    finally { pending.Remove(id); }
}

async Task SnapshotAsync()
{
    await CallAsync("thread/loaded/list", new { });
    await CallAsync("thread/read", new { threadId = args[3], includeTurns = false });
    await CallAsync("thread/turns/list", new { threadId = args[3], limit = 100, itemsView = "full", sortDirection = "asc" });
    // One bounded page: a non-null nextCursor is incomplete history, never full replay.
}
