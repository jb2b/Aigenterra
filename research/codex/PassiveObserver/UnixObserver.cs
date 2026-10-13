using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Aigenterra.Research;

internal static class UnixObserver
{
    public static async Task<int> RunAsync(string[] args, int seconds)
    {
        var output = Path.GetFullPath(args[4]);
        if (Directory.Exists(output)) throw new IOException("Evidence directory must be new.");
        Directory.CreateDirectory(output);
        var socketPath = Path.Combine(Path.GetFullPath(args[1]), "app-server-control", "app-server-control.sock");
        using var handler = new SocketsHttpHandler
        {
            ConnectCallback = async (_, token) =>
            {
                var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                try { await socket.ConnectAsync(new UnixDomainSocketEndPoint(socketPath), token); }
                catch { socket.Dispose(); throw; }
                return new NetworkStream(socket, ownsSocket: true);
            }
        };
        using var http = new HttpMessageInvoker(handler);
        using var connection = new ClientWebSocket();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(seconds + 120));
        await using var sent = new StreamWriter(Path.Combine(output, "sent.ndjson")) { AutoFlush = true };
        await using var transcript = new StreamWriter(Path.Combine(output, "received.ndjson")) { AutoFlush = true };
        var pending = new RpcPending();
        var messages = new ConcurrentQueue<object>();
        var notifications = new ConcurrentQueue<string>();
        var serverRequests = 0;
        var nextId = 0;
        var startedAt = DateTimeOffset.UtcNow;
        string? failure = null;
        Task reader = Task.CompletedTask;
        try
        {
            await connection.ConnectAsync(new Uri("ws://localhost/"), http, timeout.Token);
            reader = ReadAsync();
            await CallAsync("initialize", new
            {
                clientInfo = new { name = "aigenterra_passive_revalidation", version = "6.5" },
                capabilities = new { experimentalApi = true }
            });
            await SendAsync(new { method = "initialized" });
            await SnapshotAsync();
            Console.WriteLine($"Connected to existing Unix WebSocket. Listening {seconds}s without resume.");
            var until = DateTimeOffset.UtcNow.AddSeconds(seconds);
            while (DateTimeOffset.UtcNow < until && !File.Exists(Path.Combine(output, "stop")) && !reader.IsCompleted)
                await Task.Delay(100, timeout.Token);
            await SnapshotAsync();
            await connection.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "research disconnect", timeout.Token);
            await reader.WaitAsync(TimeSpan.FromSeconds(3));
        }
        catch (Exception error)
        {
            failure = error.GetType().Name + ": " + error.Message;
            Console.Error.WriteLine(failure);
        }
        finally
        {
            connection.Abort();
            try { await reader; }
            catch (Exception error) { failure ??= error.GetType().Name + ": " + error.Message; }
            await File.WriteAllLinesAsync(Path.Combine(output, "receipts.ndjson"), messages.Select(x => JsonSerializer.Serialize(x)));
            await File.WriteAllTextAsync(Path.Combine(output, "summary.json"), JsonSerializer.Serialize(new
            {
                transport = "unix-websocket",
                threadId = args[3],
                startedAt,
                finishedAt = DateTimeOffset.UtcNow,
                failure,
                messageCount = messages.Count,
                notifications,
                serverRequests
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        return failure is null ? 0 : 1;

        async Task ReadAsync()
        {
            var buffer = new byte[8192];
            var index = 0;
            try
            {
                while (connection.State is WebSocketState.Open or WebSocketState.CloseSent)
                {
                    using var message = new MemoryStream();
                    var path = Path.Combine(output, $"payload-{index:D6}.bin");
                    await using var original = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
                    ValueWebSocketReceiveResult part;
                    do
                    {
                        part = await connection.ReceiveAsync(buffer.AsMemory(), timeout.Token);
                        if (part.MessageType == WebSocketMessageType.Close) return;
                        // Preserve decoded application payload bytes BEFORE parsing, including partial messages.
                        await original.WriteAsync(buffer.AsMemory(0, part.Count), timeout.Token);
                        await original.FlushAsync(timeout.Token);
                        if (message.Length + part.Count > 64 * 1024 * 1024) throw new InvalidDataException("Research payload limit exceeded.");
                        message.Write(buffer, 0, part.Count);
                    } while (!part.EndOfMessage);
                    var json = Encoding.UTF8.GetString(message.ToArray());
                    await transcript.WriteLineAsync(json); // Derived framing, originals are payload-*.bin.
                    using var document = JsonDocument.Parse(json);
                    var value = document.RootElement.Clone();
                    messages.Enqueue(new { index = index++, receivedAt = DateTimeOffset.UtcNow, bytes = message.Length });
                    pending.Receive(value);
                    if (value.TryGetProperty("method", out var method))
                    {
                        if (value.TryGetProperty("id", out _)) Interlocked.Increment(ref serverRequests);
                        else notifications.Enqueue(method.GetString()!);
                    }
                }
            }
            finally { pending.Fail(new EndOfStreamException("Unix observer connection closed.")); }
        }

        async Task SendAsync(object value)
        {
            var json = JsonSerializer.Serialize(value);
            await sent.WriteLineAsync(json);
            await connection.SendAsync(Encoding.UTF8.GetBytes(json).AsMemory(), WebSocketMessageType.Text, true, timeout.Token);
        }

        async Task CallAsync(string method, object parameters)
        {
            if (method is not ("initialize" or "thread/read" or "thread/turns/list" or "thread/loaded/list"))
                throw new InvalidOperationException("Outside passive allowlist.");
            if (reader.IsCompleted) { await reader; throw new EndOfStreamException("Observer reader closed."); }
            var id = ++nextId;
            var response = pending.Add(id);
            try
            {
                await SendAsync(new { id, method, @params = parameters });
                await response.WaitAsync(TimeSpan.FromSeconds(30));
            }
            finally { pending.Remove(id); }
        }

        async Task SnapshotAsync()
        {
            await CallAsync("thread/loaded/list", new { });
            await CallAsync("thread/read", new { threadId = args[3], includeTurns = false });
            await CallAsync("thread/turns/list", new { threadId = args[3], limit = 100, itemsView = "full", sortDirection = "asc" });
        }
    }
}
