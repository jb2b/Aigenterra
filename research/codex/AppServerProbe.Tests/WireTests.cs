using System.Text;
using System.Text.Json;
using Aigenterra.Research;

namespace Aigenterra.Research.Tests;

public sealed class WireTests
{
    [Fact]
    public async Task Fragmented_multibyte_CRLF_and_final_line_preserve_all_source_bytes()
    {
        var bytes = Encoding.UTF8.GetBytes("{\"id\":2,\"result\":\"é😀\"}\r\n{\"method\":\"item/started\"}");
        await using var input = new FragmentedStream(bytes);
        await using var archive = new MemoryStream();
        var messages = new List<JsonElement>();
        await WireProtocol.ReadAsync(input, archive, messages.Add);
        Assert.Equal(bytes, archive.ToArray());
        Assert.Equal(2, messages.Count);
        Assert.Equal("é😀", messages[0].GetProperty("result").GetString());
    }

    [Fact]
    public async Task Invalid_JSON_is_archived_even_when_interpretation_fails()
    {
        var bytes = Encoding.UTF8.GetBytes("invalid\n");
        await using var archive = new MemoryStream();
        await Assert.ThrowsAnyAsync<JsonException>(() => WireProtocol.ReadAsync(new MemoryStream(bytes), archive, _ => { }));
        Assert.Equal(bytes, archive.ToArray());
    }

    [Fact]
    public async Task Out_of_order_responses_and_server_requests_do_not_cross_correlate()
    {
        var pending = new RpcPending();
        var first = pending.Add(1);
        var second = pending.Add(2);
        pending.Receive(Json("{\"id\":\"foreign-response\",\"result\":null}"));
        pending.Receive(Json("{\"id\":1,\"method\":\"approval/request\"}"));
        Assert.False(first.IsCompleted);
        pending.Receive(Json("{\"id\":2,\"result\":\"second\"}"));
        pending.Receive(Json("{\"id\":2,\"result\":\"duplicate\"}"));
        Assert.Equal("second", (await second).GetProperty("result").GetString());
        Assert.False(first.IsCompleted);
        pending.Receive(Json("{\"id\":1,\"error\":{\"code\":-1}}"));
        Assert.Equal(-1, (await first).GetProperty("error").GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Disconnect_fails_pending_requests_instead_of_inventing_success()
    {
        var pending = new RpcPending();
        var task = pending.Add(1);
        pending.Fail(new EndOfStreamException());
        await Assert.ThrowsAsync<EndOfStreamException>(async () => await task);
    }

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private sealed class FragmentedStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            base.ReadAsync(buffer[..Math.Min(1, buffer.Length)], cancellationToken);
    }
}
