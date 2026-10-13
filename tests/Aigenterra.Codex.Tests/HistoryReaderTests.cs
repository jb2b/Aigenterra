using System.Text;
using System.Text.Json;
using Aigenterra.Adapters.Codex.History;
using Aigenterra.Adapters.Codex.Normalization;
using Aigenterra.Application.Recording;
using Aigenterra.Application.Runtime;

namespace Aigenterra.Codex.Tests;

public sealed class HistoryReaderTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "aigenterra-history-test-" + Guid.NewGuid().ToString("N"));
    private readonly List<(Guid Id, string Provenance, byte[] Bytes)> evidence = [];
    private async Task<Guid> Capture(Stream bytes, string provenance, CancellationToken token)
    {
        using var buffer = new MemoryStream();
        await bytes.CopyToAsync(buffer, token);
        var id = Guid.NewGuid();
        evidence.Add((id, provenance, buffer.ToArray()));
        return id;
    }

    [Fact]
    public async Task Paged_read_preserves_exact_bytes_and_context_without_any_session_control()
    {
        var wire = Encoding.UTF8.GetBytes("{\"id\":1,\"result\":{}}\r\n{\"method\":\"account/updated\",\"params\":{}}\n{\"id\":2,\"result\":{\"thread\":{\"id\":\"child\"}}}\n{\"id\":3,\"result\":{\"data\":[],\"nextCursor\":\"next\"}}\n{\"id\":4,\"result\":{\"data\":[],\"nextCursor\":null}}\n");
        using var output = new MemoryStream(wire);
        using var input = new MemoryStream();
        var client = new CodexHistoryConnection(input, output, Capture, root);
        await client.InitializeAsync(default);
        var result = await CodexHistoryReader.ReadPagesAsync(client, "child");
        Assert.Equal(2, result.Pages);
        Assert.Null(result.NextCursor);
        Assert.Equal(wire, evidence.Where(x => x.Provenance == "reader_frame").SelectMany(x => x.Bytes).ToArray());
        Assert.Equal(3, evidence.Count(x => x.Provenance == "request_context"));
        var sent = evidence.Where(x => x.Provenance == "history_request").Select(x => JsonDocument.Parse(x.Bytes)).ToArray();
        Assert.Equal(new[] { "initialize", "initialized", "thread/read", "thread/turns/list", "thread/turns/list" }, sent.Select(x => x.RootElement.GetProperty("method").GetString()));
        Assert.Equal("next", sent[^1].RootElement.GetProperty("params").GetProperty("cursor").GetString());
        foreach (var document in sent) document.Dispose();
    }

    [Theory]
    [InlineData("{\"id\":1,\"method\":\"item/commandExecution/requestApproval\",\"params\":{}}\n")]
    [InlineData("{\"id\":99,\"result\":{}}\n")]
    [InlineData("{\"id\":1,\"error\":{}}\n")]
    [InlineData("not-json\r\n")]
    [InlineData("{\"id\":1")]
    public async Task Unexpected_requests_errors_malformed_and_truncated_frames_are_preserved_and_poison_connection(string wire)
    {
        using var input = new MemoryStream();
        using var output = new MemoryStream(Encoding.UTF8.GetBytes(wire));
        var client = new CodexHistoryConnection(input, output, Capture, root);
        await Assert.ThrowsAnyAsync<Exception>(() => client.InitializeAsync(default));
        Assert.Equal(Encoding.UTF8.GetBytes(wire), Assert.Single(evidence, x => x.Provenance == "reader_frame").Bytes);
        Assert.Single(evidence, x => x.Provenance == "history_request"); // No server-request answer was sent.
        await Assert.ThrowsAsync<IOException>(() => client.InitializeAsync(default));
    }

    [Fact]
    public async Task Control_methods_are_rejected_before_writing_and_oversize_is_retained()
    {
        using var input = new MemoryStream();
        using var output = new MemoryStream("{\"id\":1,\"result\":{}}\n"u8.ToArray());
        var client = new CodexHistoryConnection(input, output, Capture, root, maximumFrameBytes: 8);
        await Assert.ThrowsAsync<ArgumentException>(() => client.CallAsync("thread/resume", new { threadId = "owner" }, Guid.NewGuid()));
        Assert.Empty(evidence);
        await Assert.ThrowsAsync<InvalidDataException>(() => client.InitializeAsync(default));
        Assert.Equal(output.ToArray(), Assert.Single(evidence, x => x.Provenance == "reader_frame").Bytes);
    }

    [Fact]
    public async Task Timeout_preserves_partial_bytes_and_connection_cannot_accept_a_late_response()
    {
        using var input = new MemoryStream();
        using var output = new HangingStream();
        var client = new CodexHistoryConnection(input, output, Capture, root, TimeSpan.FromMilliseconds(50));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.InitializeAsync(default));
        Assert.Equal("partial"u8.ToArray(), Assert.Single(evidence, x => x.Provenance == "reader_frame").Bytes);
        await Assert.ThrowsAsync<IOException>(() => client.InitializeAsync(default));
    }

    [Fact]
    public async Task Repeated_cursor_is_a_visible_failure_not_a_completed_history_scan()
    {
        using var input = new MemoryStream();
        using var output = new MemoryStream("{\"id\":1,\"result\":{}}\n{\"id\":2,\"result\":{\"data\":[],\"nextCursor\":\"same\"}}\n{\"id\":3,\"result\":{\"data\":[],\"nextCursor\":\"same\"}}\n"u8.ToArray());
        var client = new CodexHistoryConnection(input, output, Capture, root);
        await Assert.ThrowsAsync<InvalidDataException>(() => CodexHistoryReader.ReadPagesAsync(client, "child"));
        Assert.Equal(3, evidence.Count(x => x.Provenance == "request_context"));
    }

    [Fact]
    public void Page_requires_verified_context_and_never_claims_live_coverage()
    {
        var source = new RuntimeCaptureSource(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "codex", "app_server", "AppServerVisible", "reader_frame", RuntimeVersion: "0.162.1");
        var bytes = """{"id":3,"result":{"data":[{"id":"turn","status":"interrupted","items":[]}],"nextCursor":null}}"""u8.ToArray();
        var capture = new RuntimeCaptureEnvelope(2, Guid.NewGuid(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, source, "application/json", bytes.Length, "unused", bytes);
        var normalizer = new CodexHistoryNormalizer();
        Assert.True(normalizer.RequiresRequestContext(capture));
        Assert.Empty(normalizer.Normalize(capture).Facts);
        var context = new VerifiedRuntimeRequest(Guid.NewGuid(), Guid.NewGuid(), capture.CaptureId, source.RuntimeNamespaceId, source.SourceId, source.ConnectionEpochId, Guid.NewGuid(), "thread/turns/list", "child", null, null);
        var fact = Assert.Single(normalizer.Normalize(capture, context).Facts);
        Assert.Equal("child", fact.ThreadId);
        Assert.Equal("Interrupted", fact.Status);
        Assert.Equal("Reconciled", fact.Derivation);
        Assert.Equal("Unknown", fact.ThreadScope);
        Assert.Contains(fact.Fields, x => x.Pointer.Contains(context.RequestCaptureId.ToString(), StringComparison.Ordinal));
        Assert.Empty(normalizer.Normalize(capture, context with { RuntimeNamespaceId = Guid.NewGuid() }).Facts);
    }

    private sealed class HangingStream : MemoryStream
    {
        private bool sent;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!sent) { sent = true; "partial"u8.CopyTo(buffer.Span); return 7; }
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
}
