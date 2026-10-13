using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Aigenterra.Application.Recording;
using Aigenterra.Collector;

namespace Aigenterra.Codex.Tests;

public sealed class RuntimeSpoolTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "aigenterra-v2-spool-" + Guid.NewGuid().ToString("N"));
    private readonly RuntimeCaptureSource source = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "codex", "hook", "HookVisible", "hook_stdin");

    [Fact]
    public async Task Mixed_spool_routes_each_version_and_preserves_evidence_on_lost_ack_and_oversize()
    {
        var spool = new CaptureSpool(root, 100);
        byte[] original = [0xff, 0, 13, 10];
        var id = await spool.CaptureRuntimeAsync(new MemoryStream(original), DateTimeOffset.UtcNow, new NoContext(), source);
        var handler = new Handler { LoseAck = true };
        using var http = new HttpClient(handler);
        var endpoint = new Uri("https://recorder.invalid/ingest/codex/captures/v1");
        Assert.Equal("delivery_failed", (await spool.DrainAsync(http, endpoint)).Error);
        Assert.Equal(id, handler.Last!.CaptureId);
        Assert.Equal(original, handler.Last.Payload);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(original)), handler.Last.PayloadSha256);
        Assert.Equal(source, handler.Last.Source);
        await spool.CaptureAsync(new MemoryStream("{}"u8.ToArray()), DateTimeOffset.UtcNow, new NoContext());
        Assert.Equal(2, (await spool.DrainAsync(http, endpoint)).Delivered);
        Assert.Contains("/ingest/codex/captures/v1", handler.Paths);
        Assert.Contains("/ingest/runtime/captures/v2", handler.Paths);
        var oversized = new byte[101];
        await spool.CaptureRuntimeAsync(new MemoryStream(oversized), DateTimeOffset.UtcNow, new NoContext(), source);
        var retained = Assert.Single(Directory.GetDirectories(Path.Combine(root, "retained")));
        Assert.Equal(oversized, await File.ReadAllBytesAsync(Path.Combine(retained, "payload.bin")));
    }

    [Fact]
    public async Task Damaged_payload_is_retained_without_delivery()
    {
        var spool = new CaptureSpool(root);
        await spool.CaptureRuntimeAsync(new MemoryStream("{}"u8.ToArray()), DateTimeOffset.UtcNow, new NoContext(), source);
        var pending = Assert.Single(Directory.GetDirectories(Path.Combine(root, "pending")));
        await File.WriteAllBytesAsync(Path.Combine(pending, "payload.bin"), "xx"u8.ToArray());
        var handler = new Handler();
        using var http = new HttpClient(handler);
        Assert.Equal(0, (await spool.DrainAsync(http, new Uri("https://recorder.invalid/ingest/codex/captures/v1"))).Delivered);
        Assert.Empty(handler.Paths);
        Assert.Single(Directory.GetDirectories(Path.Combine(root, "retained")));
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    private sealed class NoContext : IRepositoryContextReader
    {
        public Task<RepositoryContext> ReadAsync(string workingDirectory, CancellationToken cancellationToken = default) => throw new InvalidOperationException();
    }
    private sealed class Handler : HttpMessageHandler
    {
        public bool LoseAck { get; set; }
        public RuntimeCaptureEnvelope? Last { get; private set; }
        public List<string> Paths { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Paths.Add(request.RequestUri!.AbsolutePath);
            Guid id;
            if (request.RequestUri.AbsolutePath.EndsWith("/v2", StringComparison.Ordinal))
            {
                Last = await request.Content!.ReadFromJsonAsync<RuntimeCaptureEnvelope>(CaptureSpool.JsonOptions, token);
                id = Last!.CaptureId;
            }
            else id = (await request.Content!.ReadFromJsonAsync<CaptureEnvelope>(CaptureSpool.JsonOptions, token))!.CaptureId;
            if (LoseAck) { LoseAck = false; throw new HttpRequestException(); }
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { captureId = id, archived = true }) };
        }
    }
}
