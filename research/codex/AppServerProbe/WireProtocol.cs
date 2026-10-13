using System.Buffers;
using System.Collections.Concurrent;
using System.Text.Json;

namespace Aigenterra.Research;

public static class WireProtocol
{
    // Archive the original bytes before interpreting frames. StreamReader alone
    // would lose newline/encoding fidelity. This is a bounded research reader.
    public static async Task ReadAsync(Stream input, Stream archive, Action<JsonElement> receive,
        CancellationToken cancellationToken = default, int maxFrameBytes = 64 * 1024 * 1024)
    {
        var buffer = new byte[8192];
        var frame = new ArrayBufferWriter<byte>();
        int count;
        while ((count = await input.ReadAsync(buffer, cancellationToken)) != 0)
        {
            await archive.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
            await archive.FlushAsync(cancellationToken);
            for (var index = 0; index < count; index++)
            {
                if (buffer[index] == '\n')
                {
                    Emit(frame, receive);
                    frame.Clear();
                }
                else
                {
                    if (frame.WrittenCount >= maxFrameBytes) throw new InvalidDataException("Frame exceeds research memory limit; inspect archived bytes.");
                    frame.GetSpan(1)[0] = buffer[index];
                    frame.Advance(1);
                }
            }
        }
        Emit(frame, receive);
    }

    private static void Emit(ArrayBufferWriter<byte> frame, Action<JsonElement> receive)
    {
        if (frame.WrittenCount == 0) return;
        using var document = JsonDocument.Parse(frame.WrittenMemory);
        receive(document.RootElement.Clone());
    }
}

public sealed class RpcPending
{
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> requests = new();

    public Task<JsonElement> Add(int id)
    {
        var source = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!requests.TryAdd(id, source)) throw new InvalidOperationException("Duplicate local request ID.");
        return source.Task;
    }

    public void Receive(JsonElement message)
    {
        // Server-originated requests are evidence, never implicit approvals.
        if (message.TryGetProperty("method", out _) || !message.TryGetProperty("id", out var id)
            || id.ValueKind != JsonValueKind.Number || !id.TryGetInt32(out var number) || !requests.TryRemove(number, out var pending)) return;
        pending.TrySetResult(message);
    }

    public void Remove(int id) => requests.TryRemove(id, out _);

    public void Fail(Exception error)
    {
        foreach (var id in requests.Keys)
            if (requests.TryRemove(id, out var pending)) pending.TrySetException(error);
    }
}
