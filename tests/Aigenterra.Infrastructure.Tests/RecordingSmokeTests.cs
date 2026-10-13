using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Aigenterra.Application.Executions;
using Aigenterra.Application.Recording;
using Aigenterra.Infrastructure.Executions.Postgres;
using Microsoft.EntityFrameworkCore;

namespace Aigenterra.Infrastructure.Tests;

/// <summary>Real processes and two real worktrees; no model, user repository, or user database required.</summary>
public sealed class RecordingSmokeTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Opted_in_projection_worker_and_offline_maintenance_command_produce_identical_checksums()
    {
        var connection = await fixture.CreateDatabaseAsync();
        var root = FindSolution();
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var dll = Path.Combine(root, "src", "Aigenterra.Api", "bin", configuration, "net10.0", "Aigenterra.Api.dll");
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var origin = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}"; listener.Stop();
        var capture = RuntimeCaptureTests.Capture("""{"hook_event_name":"UserPromptSubmit","session_id":"root","turn_id":"turn"}"""u8.ToArray());
        capture = capture with { Source = capture.Source with { SourceKind = "hook", EvidenceStage = "HookVisible", Provenance = "hook_stdin" } };
        var api = StartApi(dll, root, origin, connection, projectionNamespace: capture.Source.RuntimeNamespaceId);
        string? liveHash = null;
        try
        {
            await Ready(origin);
            using var http = new HttpClient();
            using var response = await http.PostAsJsonAsync(origin + "/ingest/runtime/captures/v2", capture);
            response.EnsureSuccessStatusCode();
            await using var db = PostgresFixture.Context(connection);
            var store = new PostgresRuntimeProjectionStore(db);
            for (var i = 0; i < 150; i++)
            {
                var active = await store.ReadActiveAsync(capture.Source.RuntimeNamespaceId);
                if (active is not null && (await store.ReadGenerationAsync(active.GenerationId))!.InputCount == 1) { liveHash = active.ProjectionSha256; break; }
                await Task.Delay(100);
            }
            Assert.NotNull(liveHash);
        }
        finally { StopApi(api); }
        var environment = new Dictionary<string, string> { ["ConnectionStrings__aigenterra"] = connection };
        var frozen = JsonSerializer.Deserialize<JsonElement>(await Run("dotnet", root, [dll, "runtime-projection", "freeze", capture.Source.RuntimeNamespaceId.ToString()], environment));
        var rebuilt = JsonSerializer.Deserialize<JsonElement>(await Run("dotnet", root, [dll, "runtime-projection", "rebuild", frozen.GetProperty("id").GetString()!], environment));
        Assert.Equal(liveHash, rebuilt.GetProperty("projectionSha256").GetString());
    }

    [Fact]
    public async Task Health_ingest_and_bounded_read_endpoints_preserve_namespace_and_unknown_fields()
    {
        var connection = await fixture.CreateDatabaseAsync();
        var solution = FindSolution();
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var apiDll = Path.Combine(solution, "src", "Aigenterra.Api", "bin", configuration, "net10.0", "Aigenterra.Api.dll");
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var origin = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}";
        listener.Stop();
        using var api = StartApi(apiDll, solution, origin, connection, recordHealth: true);
        try
        {
            await Ready(origin);
            using var http = new HttpClient();
            var source = new RuntimeCaptureSource(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "codex", "diagnostic", "RecorderDiagnostic", "inventory", Guid.NewGuid());
            using var receipt = await http.PostAsJsonAsync(origin + "/ingest/runtime/captures/v2", RuntimeSourceHealthTests.Capture(source, false));
            Assert.Equal("pending", (await receipt.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("normalization").GetString());
            await using var db = PostgresFixture.Context(connection);
            for (var i = 0; i < 100 && !await db.Set<SourceHealthEventRow>().AnyAsync(); i++) await Task.Delay(100);
            using var response = await http.GetAsync($"{origin}/runtime/sources/{source.SourceId}/health?runtimeNamespaceId={source.RuntimeNamespaceId}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(response.Headers.CacheControl!.NoStore);
            var health = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("snapshot");
            Assert.Equal("Disconnected", health.GetProperty("connectionState").GetString());
            Assert.Equal("Untrusted", health.GetProperty("hooks")[0].GetProperty("trust").GetString());
            Assert.Equal(JsonValueKind.Null, health.GetProperty("binary").GetProperty("executableVersion").ValueKind);
            using var wrongNamespace = await http.GetAsync($"{origin}/runtime/sources/{source.SourceId}/health?runtimeNamespaceId={Guid.NewGuid()}");
            Assert.Equal(HttpStatusCode.NotFound, wrongNamespace.StatusCode);
            using var page = await http.GetAsync($"{origin}/runtime/instances?runtimeNamespaceId={source.RuntimeNamespaceId}&limit=1");
            var instances = await page.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(1, instances.GetProperty("instances").GetArrayLength());
            var next = instances.GetProperty("next").GetInt64();
            using var finalPage = await http.GetAsync($"{origin}/runtime/instances?runtimeNamespaceId={source.RuntimeNamespaceId}&after={next}");
            Assert.Equal(0, (await finalPage.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("instances").GetArrayLength());
            using var excessive = await http.GetAsync($"{origin}/runtime/instances?runtimeNamespaceId={source.RuntimeNamespaceId}&limit=501");
            Assert.Equal(HttpStatusCode.BadRequest, excessive.StatusCode);
        }
        finally { StopApi(api); }
    }

    [Fact]
    public async Task Runtime_ingress_acknowledges_raw_bytes_rejects_conflicts_and_enforces_size_without_false_ack()
    {
        var connection = await fixture.CreateDatabaseAsync();
        var solution = FindSolution();
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var apiDll = Path.Combine(solution, "src", "Aigenterra.Api", "bin", configuration, "net10.0", "Aigenterra.Api.dll");
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var origin = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}";
        listener.Stop();
        using var api = StartApi(apiDll, solution, origin, connection, maxPayloadBytes: 1024, normalizeRuntime: true);
        try
        {
            await Ready(origin);
            using var http = new HttpClient();
            var capture = RuntimeCaptureTests.Capture([0xff, 0, 13, 10]);
            var url = origin + "/ingest/runtime/captures/v2";
            using var first = await http.PostAsJsonAsync(url, capture);
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            Assert.Equal("pending", (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("normalization").GetString());
            using var retry = await http.PostAsJsonAsync(url, capture);
            Assert.True((await retry.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("duplicate").GetBoolean());
            using var conflict = await http.PostAsJsonAsync(url, capture with { CapturedAt = capture.CapturedAt.AddTicks(1) });
            Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
            using var invalid = await http.PostAsJsonAsync(url, capture with { PayloadLength = 99 });
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            using var oversized = await http.PostAsJsonAsync(url, RuntimeCaptureTests.Capture(new byte[1025]));
            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, oversized.StatusCode);
            await using var db = PostgresFixture.Context(connection);
            Assert.Equal(capture.Payload, (await db.Set<RawCaptureRow>().SingleAsync()).Payload);
            for (var i = 0; i < 100 && !await db.Set<RuntimeWorkRow>().AnyAsync(x => x.Complete); i++) await Task.Delay(100);
            Assert.Equal("rejected", (await db.Set<RuntimeWorkRow>().SingleAsync()).Outcome);
        }
        finally { StopApi(api); }
    }

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

    private static Process StartApi(string dll, string cwd, string origin, string connection, int? maxPayloadBytes = null, bool normalizeRuntime = false, bool recordHealth = false, Guid? projectionNamespace = null)
    {
        var start = Info("dotnet", cwd, [dll]);
        start.Environment["ASPNETCORE_URLS"] = origin;
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        start.Environment["ConnectionStrings__aigenterra"] = connection;
        start.Environment["Database__ApplyMigrationsOnStartup"] = "true";
        if (maxPayloadBytes is not null) start.Environment["Capture__MaxPayloadBytes"] = maxPayloadBytes.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        start.Environment["Runtime__NormalizeCaptures"] = normalizeRuntime.ToString();
        start.Environment["Runtime__RecordSourceHealth"] = recordHealth.ToString();
        if (projectionNamespace is not null) start.Environment["Runtime__ProjectionNamespaces__0"] = projectionNamespace.Value.ToString();
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
