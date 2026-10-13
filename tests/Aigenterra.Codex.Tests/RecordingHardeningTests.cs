using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Aigenterra.Adapters.Git;
using Aigenterra.Application.Recording;
using Aigenterra.Collector;

namespace Aigenterra.Codex.Tests;

public sealed class RecordingHardeningTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "aigenterra-recording-test-" + Guid.NewGuid().ToString("N"));
    private static readonly Uri Endpoint = new("http://127.0.0.1/ingest/codex/captures/v1");

    [Fact]
    public async Task Spool_survives_outage_and_lost_ack_with_identical_bytes_id_and_context()
    {
        var payload = Encoding.UTF8.GetBytes("{ \"hook_event_name\":\"UserPromptSubmit\",\"cwd\":\"C:/fixture\",\"prompt\":\"café\" }\r\n");
        var context = new RepositoryContext(DateTimeOffset.UtcNow, "captured", HeadSha: "original");
        var spool = new CaptureSpool(directory);
        var id = await spool.CaptureAsync(new MemoryStream(payload), DateTimeOffset.UtcNow, new FixedContext(context));
        var handler = new DeliveryHandler { Outage = true };
        using var client = new HttpClient(handler);
        Assert.Equal("delivery_failed", (await spool.DrainAsync(client, Endpoint)).Error);
        Assert.Single(Directory.GetDirectories(Path.Combine(directory, "pending")));
        handler.Outage = false;
        handler.LoseAcknowledgement = true;
        Assert.Equal("delivery_failed", (await new CaptureSpool(directory).DrainAsync(client, Endpoint)).Error);
        Assert.Equal(1, (await new CaptureSpool(directory).DrainAsync(client, Endpoint)).Delivered);
        Assert.Equal(2, handler.Captures.Count);
        Assert.All(handler.Captures, capture =>
        {
            Assert.Equal(id, capture.CaptureId);
            Assert.Equal(payload, capture.Payload);
            Assert.Equal(context, capture.RepositoryContext);
        });
        Assert.Empty(Directory.GetDirectories(Path.Combine(directory, "pending")));
    }

    [Fact]
    public async Task Parallel_writers_preserve_all_records_and_oversize_is_retained_in_full()
    {
        var spool = new CaptureSpool(directory, 100);
        var ids = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ =>
            spool.CaptureAsync(new MemoryStream("{}"u8.ToArray()), DateTimeOffset.UtcNow, new FixedContext(null))));
        Assert.Equal(20, ids.Distinct().Count());
        using var client = new HttpClient(new DeliveryHandler());
        Assert.Equal(20, (await spool.DrainAsync(client, Endpoint)).Delivered);
        var oversized = new byte[101];
        await spool.CaptureAsync(new MemoryStream(oversized), DateTimeOffset.UtcNow, new FixedContext(null));
        var retained = Assert.Single(Directory.GetDirectories(Path.Combine(directory, "retained")));
        Assert.Equal(oversized, await File.ReadAllBytesAsync(Path.Combine(retained, "payload.bin")));
        Assert.Equal(1, JsonSerializer.SerializeToElement(spool.Status()).GetProperty("retained").GetInt32());
    }

    [Fact]
    public async Task Worker_lease_prevents_a_second_drain_and_false_ack_does_not_delete()
    {
        var spool = new CaptureSpool(directory);
        await spool.CaptureAsync(new MemoryStream("{}"u8.ToArray()), DateTimeOffset.UtcNow, new FixedContext(null));
        using var client = new HttpClient(new DeliveryHandler { FalseAcknowledgement = true });
        using (var lease = new FileStream(Path.Combine(directory, "drain.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            Assert.Equal("worker_already_running", (await spool.DrainAsync(client, Endpoint)).Error);
        Assert.Equal("delivery_failed", (await spool.DrainAsync(client, Endpoint)).Error);
        Assert.Single(Directory.GetDirectories(Path.Combine(directory, "pending")));
    }

    [Fact]
    public async Task Unwritable_spool_does_not_discard_a_previously_buffered_record()
    {
        var spool = new CaptureSpool(directory);
        await spool.CaptureAsync(new MemoryStream("{}"u8.ToArray()), DateTimeOffset.UtcNow, new FixedContext(null));
        var blocked = Path.Combine(directory, "blocked");
        await File.WriteAllTextAsync(blocked, "file, not directory");
        await Assert.ThrowsAsync<IOException>(() => new CaptureSpool(blocked).CaptureAsync(
            new MemoryStream("{}"u8.ToArray()), DateTimeOffset.UtcNow, new FixedContext(null)));
        Assert.Single(Directory.GetDirectories(Path.Combine(directory, "pending")));
    }

    [Fact]
    public async Task Git_detects_worktrees_branches_head_staged_untracked_and_detached_states()
    {
        Directory.CreateDirectory(directory);
        await Git(directory, "init", "-b", "main");
        await Git(directory, "config", "user.name", "Recording test");
        await Git(directory, "config", "user.email", "recording@example.invalid");
        await File.WriteAllTextAsync(Path.Combine(directory, "tracked.txt"), "initial\n");
        await Git(directory, "add", "tracked.txt");
        await Git(directory, "commit", "-m", "fixture");
        var reader = new GitRepositoryContextReader(TimeSpan.FromSeconds(5));
        var initial = await reader.ReadAsync(directory);
        Assert.Equal("captured", initial.Status);
        Assert.False(initial.IsDirty);
        Assert.Equal("main", initial.Branch);
        Assert.Equal(40, initial.HeadSha!.Length);
        var worktree = Path.Combine(directory, "parallel worktree");
        await Git(directory, "worktree", "add", "-b", "feature", worktree);
        await File.AppendAllTextAsync(Path.Combine(worktree, "tracked.txt"), "change\n");
        await Git(worktree, "add", "tracked.txt");
        await File.WriteAllTextAsync(Path.Combine(worktree, "café file.txt"), "untracked");
        var parallel = await reader.ReadAsync(worktree);
        Assert.Equal("feature", parallel.Branch);
        Assert.Equal(initial.HeadSha, parallel.HeadSha);
        Assert.Equal(initial.RepositoryRoot, parallel.RepositoryRoot);
        Assert.Equal(initial.CommonGitDirectory, parallel.CommonGitDirectory);
        Assert.NotEqual(initial.WorktreePath, parallel.WorktreePath);
        Assert.True(parallel.IsDirty);
        Assert.Contains("tracked.txt", parallel.ChangedFiles!);
        Assert.Contains("café file.txt", parallel.UntrackedFiles!);
        await Git(worktree, "commit", "-m", "changed");
        var completion = await reader.ReadAsync(worktree);
        Assert.NotEqual(parallel.HeadSha, completion.HeadSha);
        await Git(worktree, "checkout", "--detach");
        Assert.Null((await reader.ReadAsync(worktree)).Branch);
    }

    [Fact]
    public async Task Git_handles_empty_repository_non_repository_missing_git_and_timeout_without_clean_guess()
    {
        Directory.CreateDirectory(directory);
        var reader = new GitRepositoryContextReader(TimeSpan.FromSeconds(5));
        var outside = await reader.ReadAsync(directory);
        Assert.Equal("not_repository", outside.Status);
        Assert.Null(outside.IsDirty);
        await Git(directory, "init", "-b", "main");
        var empty = await reader.ReadAsync(directory);
        Assert.Null(empty.HeadSha);
        Assert.Equal("main", empty.Branch);
        var missing = await new GitRepositoryContextReader(executable: Path.Combine(directory, "missing-git")).ReadAsync(directory);
        Assert.Equal("git_unavailable", missing.Status);
        Assert.Null(missing.IsDirty);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        var timedOut = await reader.ReadAsync(directory, canceled.Token);
        Assert.Equal("timeout", timedOut.Status);
        Assert.Null(timedOut.IsDirty);
    }

    private static async Task Git(string cwd, params string[] arguments)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = cwd, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        start.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        start.Environment["GIT_CONFIG_GLOBAL"] = Path.Combine(cwd, "nonexistent-global-config");
        foreach (var arg in arguments) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        await output;
        Assert.True(process.ExitCode == 0, await error);
    }

    public void Dispose()
    {
        if (!Directory.Exists(directory)) return;
        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(directory, recursive: true);
    }
    private sealed class FixedContext(RepositoryContext? context) : IRepositoryContextReader
    {
        public Task<RepositoryContext> ReadAsync(string workingDirectory, CancellationToken cancellationToken = default) =>
            Task.FromResult(context ?? new RepositoryContext(DateTimeOffset.UtcNow, "not_repository"));
    }
    private sealed class DeliveryHandler : HttpMessageHandler
    {
        public bool Outage { get; set; }
        public bool LoseAcknowledgement { get; set; }
        public bool FalseAcknowledgement { get; set; }
        public List<CaptureEnvelope> Captures { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Outage) return new(HttpStatusCode.ServiceUnavailable);
            var capture = (await request.Content!.ReadFromJsonAsync<CaptureEnvelope>(CaptureSpool.JsonOptions, cancellationToken))!;
            Captures.Add(capture);
            if (LoseAcknowledgement) { LoseAcknowledgement = false; throw new HttpRequestException("Response lost after commit"); }
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { capture.CaptureId, archived = !FalseAcknowledgement }) };
        }
    }
}
