using System.Text.Json;
using Aigenterra.Application.Runtime;

namespace Aigenterra.Adapters.Codex.History;

public delegate Task<Guid> HistoryCapture(Stream bytes, string provenance, CancellationToken token);
public sealed record HistoryReply(Guid CaptureId, JsonElement Message);

/// <summary>Sequential, read-only JSON-RPC. Incoming bytes are persisted before parsing.</summary>
public sealed class CodexHistoryConnection(Stream input, Stream output, HistoryCapture capture, string temporaryDirectory,
    TimeSpan? requestTimeout = null, int maximumFrameBytes = 16 * 1024 * 1024)
{
    private readonly byte[] buffer = new byte[8192];
    private int offset;
    private int count;
    private int nextId;
    private bool failed;

    public async Task InitializeAsync(CancellationToken token)
    {
        await CallAsync("initialize", new { clientInfo = new { name = "aigenterra_history_reader", version = "1" }, capabilities = new { experimentalApi = true } }, Guid.Empty, token);
        await SendAsync(new { method = "initialized" }, token);
    }

    public async Task<HistoryReply> CallAsync(string method, object parameters, Guid cycle, CancellationToken token = default)
    {
        if (failed) throw new IOException("Reader must reconnect after a protocol failure.");
        if (method is not ("initialize" or "thread/read" or "thread/turns/list" or "thread/items/list"))
            throw new ArgumentException("Only initialization and passive history reads are permitted.", nameof(method));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(requestTimeout ?? TimeSpan.FromSeconds(10));
        try
        {
            var id = ++nextId;
            var requestId = await SendAsync(new { id, method, @params = parameters }, timeout.Token);
            while (true)
            {
                var reply = await ReceiveAsync(timeout.Token);
                var root = reply.Message;
                if (root.TryGetProperty("method", out _))
                {
                    if (root.TryGetProperty("id", out _)) throw new InvalidDataException("Unexpected server request; closing own reader without answering.");
                    continue; // Reader diagnostics are archived, never treated as owner live observation.
                }
                if (!root.TryGetProperty("id", out var replyId) || replyId.ValueKind != JsonValueKind.Number || !replyId.TryGetInt32(out var number) || number != id)
                    throw new InvalidDataException("Unmatched response ID.");
                if (method != "initialize")
                {
                    var context = JsonSerializer.SerializeToUtf8Bytes(new RuntimeRequestContext(1, requestId, reply.CaptureId, cycle), new JsonSerializerOptions(JsonSerializerDefaults.Web));
                    await capture(new MemoryStream(context), "request_context", timeout.Token);
                }
                if (root.TryGetProperty("error", out _)) throw new InvalidDataException("History RPC error; original response retained.");
                if (!root.TryGetProperty("result", out _)) throw new InvalidDataException("Missing RPC result.");
                return reply;
            }
        }
        catch { failed = true; throw; }
    }

    private async Task<Guid> SendAsync(object value, CancellationToken token)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value).Concat(new byte[] { 10 }).ToArray();
        var id = await capture(new MemoryStream(bytes), "history_request", token);
        await input.WriteAsync(bytes, token);
        await input.FlushAsync(token);
        return id;
    }

    private async Task<HistoryReply> ReceiveAsync(CancellationToken token)
    {
        Directory.CreateDirectory(temporaryDirectory);
        var path = Path.Combine(temporaryDirectory, Guid.NewGuid().ToString("N") + ".frame");
        var persisted = false;
        try
        {
            await using var frame = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 8192, FileOptions.Asynchronous);
            var complete = false;
            try
            {
                while (!complete)
                {
                    if (offset == count)
                    {
                        count = await output.ReadAsync(buffer, token);
                        offset = 0;
                        if (count == 0) throw new EndOfStreamException("History reader closed stdout.");
                    }
                    var end = Array.IndexOf(buffer, (byte)10, offset, count - offset);
                    var length = end < 0 ? count - offset : end - offset + 1;
                    await frame.WriteAsync(buffer.AsMemory(offset, length), token);
                    offset += length;
                    complete = end >= 0;
                }
            }
            finally
            {
                if (frame.Length > 0)
                {
                    await frame.FlushAsync(CancellationToken.None);
                    frame.Position = 0;
                    // Even timeout/EOF and oversized frames survive. Sink retains oversize without acknowledgement.
                    var id = await capture(frame, "reader_frame", CancellationToken.None);
                    persisted = true;
                    LastCaptureId = id;
                }
            }
            if (frame.Length > maximumFrameBytes) throw new InvalidDataException("History frame exceeds parsing/delivery budget; retained locally.");
            frame.Position = 0;
            using var document = await JsonDocument.ParseAsync(frame, cancellationToken: token);
            if (document.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException("Expected object.");
            return new(LastCaptureId, document.RootElement.Clone());
        }
        finally { if (persisted) File.Delete(path); } // On spool failure leave the original local frame recoverable.
    }

    private Guid LastCaptureId { get; set; }
}
