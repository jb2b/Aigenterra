using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Aigenterra.Research;

public sealed record ReceivedMessage(DateTimeOffset ReceivedAt, double ElapsedMs, JsonElement Message);

public sealed class ProbeClient : IAsyncDisposable
{
    private readonly Process process;
    private readonly RpcPending pending = new();
    private readonly SemaphoreSlim writer = new(1);
    private readonly SemaphoreSlim changed = new(0);
    private readonly ConcurrentQueue<ReceivedMessage> messages = new();
    private readonly Stopwatch timer = Stopwatch.StartNew();
    private readonly FileStream wire;
    private readonly StreamWriter sent;
    private readonly Task reader;
    private readonly Task stderr;
    private readonly FileStream errorFile;
    private readonly string evidenceDirectory;
    private int nextId;

    public ProbeClient(string executable, string workspace, string evidence, params string[] arguments)
    {
        Directory.CreateDirectory(evidence);
        evidenceDirectory = evidence;
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = Path.Combine(workspace, "repo"),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.Environment["CODEX_HOME"] = Path.Combine(workspace, "home");
        start.ArgumentList.Add("app-server");
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        wire = new FileStream(Path.Combine(evidence, "received.ndjson"), FileMode.CreateNew, FileAccess.Write,
            FileShare.Read, 8192, FileOptions.Asynchronous | FileOptions.WriteThrough);
        errorFile = File.Create(Path.Combine(evidence, "stderr.txt"));
        sent = new StreamWriter(Path.Combine(evidence, "sent.ndjson"), false, new UTF8Encoding(false)) { AutoFlush = true };
        process = Process.Start(start) ?? throw new InvalidOperationException("App Server did not start.");
        stderr = process.StandardError.BaseStream.CopyToAsync(errorFile);
        reader = ReadAsync();
    }

    public ReceivedMessage[] Messages => messages.ToArray();

    private async Task ReadAsync()
    {
        try
        {
            await WireProtocol.ReadAsync(process.StandardOutput.BaseStream, wire, message =>
            {
                messages.Enqueue(new(DateTimeOffset.UtcNow, timer.Elapsed.TotalMilliseconds, message));
                pending.Receive(message);
                changed.Release();
            });
            pending.Fail(new EndOfStreamException("App Server closed stdout."));
        }
        catch (Exception error) { pending.Fail(error); throw; }
        finally { changed.Release(); }
    }

    public async Task<JsonElement> CallAsync(string method, object parameters, int timeoutSeconds = 30)
    {
        if (reader.IsCompleted) { await reader; throw new EndOfStreamException("App Server is no longer connected."); }
        var id = Interlocked.Increment(ref nextId);
        var response = pending.Add(id);
        try
        {
            await SendAsync(new { id, method, @params = parameters });
            return await response.WaitAsync(TimeSpan.FromSeconds(timeoutSeconds));
        }
        finally { pending.Remove(id); }
    }

    public async Task SendAsync(object message)
    {
        var json = JsonSerializer.Serialize(message);
        await writer.WaitAsync();
        try
        {
            await sent.WriteLineAsync(json);
            await process.StandardInput.WriteLineAsync(json);
            await process.StandardInput.FlushAsync();
        }
        finally { writer.Release(); }
    }

    public async Task<JsonElement> WaitForAsync(Func<JsonElement, bool> predicate, int after = 0, int timeoutSeconds = 120)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        while (true)
        {
            foreach (var message in Messages.Skip(after)) if (predicate(message.Message)) return message.Message;
            if (reader.IsCompleted) { await reader; throw new EndOfStreamException("No matching event before EOF."); }
            await changed.WaitAsync(timeout.Token);
        }
    }

    public async ValueTask DisposeAsync()
    {
        process.StandardInput.Close();
        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3)); }
        catch (TimeoutException) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
        try { await reader; }
        finally
        {
            await stderr;
            await File.WriteAllLinesAsync(Path.Combine(evidenceDirectory, "receipt-times.ndjson"), Messages.Select((entry, index) =>
                JsonSerializer.Serialize(new
                {
                    index,
                    entry.ReceivedAt,
                    entry.ElapsedMs,
                    method = entry.Message.TryGetProperty("method", out var method) ? method.GetString() : null
                })));
            await wire.DisposeAsync();
            await errorFile.DisposeAsync();
            await sent.DisposeAsync();
            process.Dispose();
            writer.Dispose();
            changed.Dispose();
        }
    }
}
