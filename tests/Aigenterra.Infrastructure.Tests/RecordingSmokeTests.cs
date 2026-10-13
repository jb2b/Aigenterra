using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Aigenterra.Application.Executions;
using Aigenterra.Infrastructure.Executions.Postgres;
using Microsoft.EntityFrameworkCore;

namespace Aigenterra.Infrastructure.Tests;

/// <summary>Real processes and two real worktrees; no model, user repository, or user database required.</summary>
public sealed class RecordingSmokeTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Two_worktrees_round_trip_through_collector_and_api_after_outage_and_restart()
    {
        var connection = await fixture.CreateDatabaseAsync();
        var root = Path.Combine(Path.GetTempPath(), "aigenterra-smoke-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var repo = Path.Combine(root, "repository");
        var first = Path.Combine(root, "first worktree");
        var second = Path.Combine(root, "second worktree");
        var spool = Path.Combine(root, "spool");
        var solution = FindSolution();
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var apiDll = Path.Combine(solution, "src", "Aigenterra.Api", "bin", configuration, "net10.0", "Aigenterra.Api.dll");
        var collectorDll = Path.Combine(solution, "src", "Aigenterra.Collector", "bin", configuration, "net10.0", "Aigenterra.Collector.dll");
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var origin = $"http://127.0.0.1:{port}";
        Process? api = null;
        try
        {
            Directory.CreateDirectory(repo);
            await Run("git", repo, ["init", "-b", "main"]);
            await Run("git", repo, ["config", "user.name", "Recording smoke"]);
            await Run("git", repo, ["config", "user.email", "smoke@example.invalid"]);
            await File.WriteAllTextAsync(Path.Combine(repo, "fixture.txt"), "initial\n");
            await Run("git", repo, ["add", "."]);
            await Run("git", repo, ["commit", "-m", "fixture"]);
            await Run("git", repo, ["worktree", "add", "-b", "first", first]);
            await Run("git", repo, ["worktree", "add", "-b", "second", second]);
            await File.WriteAllTextAsync(Path.Combine(second, "untracked.txt"), "dirty\n");
            var environment = new Dictionary<string, string>
            {
                ["AIGENTERRA_SPOOL_PATH"] = spool,
                ["AIGENTERRA_CAPTURE_URL"] = origin + "/ingest/codex/captures/v1"
            };
            var evidence = new List<byte[]>();
            async Task Capture(string worktree, object payload)
            {
                var bytes = JsonSerializer.SerializeToUtf8Bytes(payload);
                evidence.Add(bytes);
                var output = await Run("dotnet", worktree, [collectorDll], environment, bytes);
                Assert.Equal("{}", output.Trim());
            }
            foreach (var (path, turn) in new[] { (first, "turn-first"), (second, "turn-second") })
            {
                await Capture(path, new { hook_event_name = "UserPromptSubmit", session_id = turn, turn_id = turn, prompt = turn, cwd = path, model = "observed-fixture-model" });
                await Capture(path, new { hook_event_name = "PreToolUse", session_id = turn, turn_id = turn, tool_use_id = turn + "-tool", tool_name = "Bash", tool_input = new { command = "read fixture" } });
                await Capture(path, new { hook_event_name = "PostToolUse", session_id = turn, turn_id = turn, tool_use_id = turn + "-tool", tool_name = "Bash", tool_response = "fixture output" });
            }
            await File.AppendAllTextAsync(Path.Combine(first, "fixture.txt"), "completed\n");
            await Run("git", first, ["commit", "-am", "run change"]);
            await Capture(first, new { hook_event_name = "Stop", session_id = "turn-first", turn_id = "turn-first", stop_hook_active = false, cwd = first });
            await Capture(second, new { hook_event_name = "Stop", session_id = "turn-second", turn_id = "turn-second", stop_hook_active = false, cwd = second });

            // The API has never been started. A failed drain must preserve every capture.
            await Run("dotnet", root, [collectorDll, "drain"], environment, expectedExit: 1);
            Assert.Equal(8, Directory.GetDirectories(Path.Combine(spool, "pending")).Length);
            api = StartApi(apiDll, root, origin, connection);
            await Ready(origin);
            await Run("dotnet", root, [collectorDll, "drain"], environment);
            Assert.Empty(Directory.GetDirectories(Path.Combine(spool, "pending")));

            await using var db = PostgresFixture.Context(connection);
            var records = await db.Set<RawCaptureRow>().AsNoTracking().ToListAsync();
            Assert.Equal(8, records.Count);
            foreach (var expected in evidence) Assert.Contains(records, record => record.Payload.SequenceEqual(expected));
            Assert.All(await db.Set<CaptureOutcomeRow>().ToListAsync(), outcome => Assert.Equal("projected", outcome.Disposition));
            using var http = new HttpClient();
            var reader = new PostgresExecutionStore(db);
            var firstId = (await reader.FindTaskExecutionIdAsync(new("codex", "turn", "turn-first")))!.Value;
            var secondId = (await reader.FindTaskExecutionIdAsync(new("codex", "turn", "turn-second")))!.Value;
            var firstTimeline = (await reader.GetTimelineAsync(firstId))!;
            var secondTimeline = (await reader.GetTimelineAsync(secondId))!;
            Assert.Equal("first", firstTimeline.StartContext!.Branch);
            Assert.Equal("second", secondTimeline.StartContext!.Branch);
            Assert.False(firstTimeline.StartContext.IsDirty);
            Assert.True(secondTimeline.StartContext.IsDirty);
            Assert.NotEqual(firstTimeline.StartContext.HeadSha, firstTimeline.EndContext!.HeadSha);
            Assert.NotEqual(firstTimeline.StartContext.WorktreePath, secondTimeline.StartContext.WorktreePath);
            Assert.Equal(firstTimeline.StartContext.CommonGitDirectory, secondTimeline.StartContext.CommonGitDirectory);
            Assert.Equal("completed", firstTimeline.Status);
            var detailUrl = origin + "/task-executions/" + firstId.Value + "/timeline";
            var before = await http.GetStringAsync(detailUrl);
            Assert.Contains("\"startContext\"", before);
            StopApi(api);
            api = StartApi(apiDll, root, origin, connection);
            await Ready(origin);
            Assert.Equal(before, await http.GetStringAsync(detailUrl));
            Assert.Equal(8, await db.Set<RawCaptureRow>().CountAsync());
        }
        finally
        {
            if (api is not null) StopApi(api);
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(root, recursive: true);
        }
    }

    private static Process StartApi(string dll, string cwd, string origin, string connection)
    {
        var start = Info("dotnet", cwd, [dll]);
        start.Environment["ASPNETCORE_URLS"] = origin;
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        start.Environment["ConnectionStrings__aigenterra"] = connection;
        start.Environment["Database__ApplyMigrationsOnStartup"] = "true";
        start.Environment.Remove("OTEL_EXPORTER_OTLP_ENDPOINT");
        var process = Process.Start(start)!;
        _ = process.StandardOutput.BaseStream.CopyToAsync(Stream.Null);
        _ = process.StandardError.BaseStream.CopyToAsync(Stream.Null);
        return process;
    }
    private static void StopApi(Process process)
    {
        if (!process.HasExited) { process.Kill(entireProcessTree: true); process.WaitForExit(); }
        process.Dispose();
    }
    private static async Task Ready(string origin)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(1) };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (true)
        {
            try { using var response = await http.GetAsync(origin + "/health", timeout.Token); if (response.IsSuccessStatusCode) return; }
            catch (HttpRequestException) { }
            catch (TaskCanceledException) when (!timeout.IsCancellationRequested) { }
            await Task.Delay(100, timeout.Token);
        }
    }
    private static ProcessStartInfo Info(string executable, string cwd, string[] arguments)
    {
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = cwd,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        start.Environment["GIT_CONFIG_GLOBAL"] = Path.Combine(cwd, "nonexistent-global-config");
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        return start;
    }
    private static async Task<string> Run(string executable, string cwd, string[] arguments,
        Dictionary<string, string>? environment = null, byte[]? input = null, int expectedExit = 0)
    {
        var start = Info(executable, cwd, arguments);
        if (environment is not null) foreach (var (key, value) in environment) start.Environment[key] = value;
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        try
        {
            if (input is not null) await process.StandardInput.BaseStream.WriteAsync(input);
            process.StandardInput.Close();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            await process.WaitForExitAsync(timeout.Token);
            Assert.True(process.ExitCode == expectedExit, await stderr);
            return await stdout;
        }
        finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
    }
    private static string FindSolution()
    {
        for (var path = new DirectoryInfo(AppContext.BaseDirectory); path is not null; path = path.Parent)
            if (File.Exists(Path.Combine(path.FullName, "Aigenterra.slnx"))) return path.FullName;
        throw new DirectoryNotFoundException();
    }
}
