using System.Net.Http.Json;
using System.Text.Json;
using Aigenterra.Adapters.Codex.Diagnostics;
using Aigenterra.Adapters.Codex.History;
using Aigenterra.Application.Recording;
using Aigenterra.Application.Runtime;

namespace Aigenterra.Collector;

public sealed record CodexReconciliationEnrollment(int Version, Guid RuntimeNamespaceId, Guid SourceId,
    string Executable, string CodexHome, string ApiBaseUrl, string StateDirectory, Guid[] HookSourceIds, string[] ThreadIds);

public static class CodexReconciliationWatch
{
    public static async Task<int> ExecuteAsync(string path, bool watch, CaptureSpool spool, TextWriter output, CancellationToken token)
    {
        if (!Path.IsPathFullyQualified(path) || new FileInfo(path).Length > 256 * 1024) throw new ArgumentException("Bounded absolute enrollment file required.");
        var config = JsonSerializer.Deserialize<CodexReconciliationEnrollment>(await File.ReadAllBytesAsync(path, token), CaptureSpool.JsonOptions)
            ?? throw new ArgumentException("Invalid enrollment.");
        Validate(config);
        var fingerprint = RuntimeIdentity.Key(JsonSerializer.Serialize(config, CaptureSpool.JsonOptions));
        Directory.CreateDirectory(config.StateDirectory);
        using var lease = new FileStream(Path.Combine(config.StateDirectory, "watch.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var binding = Path.Combine(config.StateDirectory, "enrollment.sha256");
        if (File.Exists(binding) && await File.ReadAllTextAsync(binding, token) != fingerprint) throw new InvalidDataException("Enrollment changed; use a new state directory.");
        if (!File.Exists(binding))
        {
            using var bindingFile = new FileStream(binding, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough);
            bindingFile.Write(System.Text.Encoding.UTF8.GetBytes(fingerprint)); bindingFile.Flush(true);
        }
        var checkpoints = new ReconciliationCheckpointStore(Path.Combine(config.StateDirectory, "threads"));
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        var endpoint = new Uri(config.ApiBaseUrl);
        var inventory = await CodexDoctorProbe.ReadAsync(config.Executable, null, null, "unknown", token);
        var run = Guid.NewGuid();
        await Diagnostic(new(1, "enrollment", DateTimeOffset.UtcNow, "enrolled_partial", Enrollment: new(fingerprint, config.HookSourceIds, config.ThreadIds)), run, token);
        await Diagnostic(new(1, "gap", DateTimeOffset.UtcNow, "reader_started_without_live_replay"), run, token);
        var unavailable = false;
        var pressure = false;
        var discovered = new Dictionary<string, RuntimeReconciliationTarget>(StringComparer.Ordinal);
        DateTimeOffset refreshAt = DateTimeOffset.MinValue;
        do
        {
            token.ThrowIfCancellationRequested();
            var drain = await spool.DrainAsync(http, endpoint, token);
            if (drain.Error is not null)
            {
                if (!unavailable) await Diagnostic(new(1, "gap", DateTimeOffset.UtcNow, "archive_unavailable"), run, token);
                unavailable = true;
                if (!watch) return 1;
                await Task.Delay(1000, token); continue;
            }
            try
            {
                if (DateTimeOffset.UtcNow >= refreshAt)
                {
                    var current = new Dictionary<string, RuntimeReconciliationTarget>(StringComparer.Ordinal);
                    string? after = null;
                    do
                    {
                        var uri = new Uri(endpoint, $"/runtime/reconciliation/targets?runtimeNamespaceId={config.RuntimeNamespaceId}&sourceId={config.SourceId}&limit=500" + (after is null ? "" : "&after=" + Uri.EscapeDataString(after)));
                        var page = await http.GetFromJsonAsync<RuntimeReconciliationTargets>(uri, CaptureSpool.JsonOptions, token) ?? throw new IOException("Missing target page.");
                        if (page.EnrollmentFingerprint != fingerprint) throw new InvalidDataException("Enrollment has not yet been reconciled.");
                        foreach (var target in page.Targets) current[target.ThreadId] = target;
                        if (current.Count > 10000) throw new InvalidDataException("Target budget exceeded.");
                        if (page.Next is not null && string.CompareOrdinal(page.Next, after) <= 0) throw new InvalidDataException("Invalid target cursor.");
                        after = page.Next;
                    } while (after is not null);
                    discovered = current;
                    refreshAt = DateTimeOffset.UtcNow.AddSeconds(5);
                }
                unavailable = false;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException or TaskCanceledException && !token.IsCancellationRequested)
            {
                if (!unavailable) await Diagnostic(new(1, "gap", DateTimeOffset.UtcNow, "target_discovery_unavailable"), run, token);
                unavailable = true;
                if (!watch) return 1;
                await Task.Delay(1000, token); continue;
            }
            var status = JsonSerializer.SerializeToElement(spool.Status());
            if (status.GetProperty("pending").GetInt32() >= 10000 || status.GetProperty("retained").GetInt32() > 0)
            {
                if (!pressure) await Diagnostic(new(1, "gap", DateTimeOffset.UtcNow, "history_paused_spool_pressure"), run, token);
                pressure = true;
                if (!watch) return 1;
                await Task.Delay(1000, token); continue;
            }
            pressure = false;
            var due = new List<(RuntimeReconciliationTarget Target, ReconciliationCheckpoint Checkpoint)>();
            foreach (var target in discovered.Values)
            {
                var checkpoint = checkpoints.Read(target.ThreadId);
                if (checkpoint is not null && checkpoint.EnrollmentFingerprint != fingerprint) throw new InvalidDataException("Checkpoint enrollment mismatch.");
                if (checkpoint is null || checkpoint.EvidenceKey != target.EvidenceKey)
                {
                    checkpoint = new(target.ThreadId, fingerprint, target.EvidenceKey, checkpoint?.Cursor,
                        DateTimeOffset.UtcNow.AddSeconds(watch ? 1 : 0), checkpoint?.InProgress ?? false, "scheduled");
                    await checkpoints.SaveAsync(checkpoint, token);
                }
                if (checkpoint.DueAt <= DateTimeOffset.UtcNow) due.Add((target, checkpoint));
            }
            // Two independent owned readers at most; least-due ordering prevents a hot thread starving others.
            await Task.WhenAll(due.OrderBy(x => x.Checkpoint.DueAt).ThenBy(x => x.Target.ThreadId, StringComparer.Ordinal).Take(2).Select(Read));
            await spool.DrainAsync(http, endpoint, token);
            if (watch) await Task.Delay(1000, token);
        } while (watch);
        return 0;

        async Task Read((RuntimeReconciliationTarget Target, ReconciliationCheckpoint Checkpoint) item)
        {
            var epoch = Guid.NewGuid();
            var state = item.Checkpoint;
            if (state.InProgress) await Diagnostic(new(1, "gap", DateTimeOffset.UtcNow, "reader_restart", state.ThreadId), epoch, token);
            await checkpoints.SaveAsync(state with { InProgress = true, Outcome = "reading_history" }, token);
            try
            {
                Task<Guid> Capture(Stream bytes, string provenance, CancellationToken ct) => spool.CaptureRuntimeAsync(bytes, DateTimeOffset.UtcNow,
                    new NoContext(), new(config.RuntimeNamespaceId, config.SourceId, epoch, "codex", provenance == "request_context" ? "diagnostic" : "app_server",
                        provenance == "reader_frame" ? "AppServerVisible" : "RecorderDiagnostic", provenance, RuntimeVersion: inventory.ExecutableVersion), ct,
                    provenance == "reader_stderr" ? "application/octet-stream" : "application/json");
                var result = await CodexHistoryReader.ReadAsync(config.Executable, config.CodexHome, state.ThreadId, Capture,
                    Path.Combine(config.StateDirectory, "frames", epoch.ToString("N")), state.Cursor, token);
                await Diagnostic(new(1, result.Outcome == "budget_limited" ? "gap" : "cycle", DateTimeOffset.UtcNow, result.Outcome, state.ThreadId, result.CycleId, result.NextCursor), epoch, token);
                await checkpoints.SaveAsync(state with
                {
                    Cursor = result.NextCursor,
                    InProgress = false,
                    Outcome = result.Outcome,
                    DueAt = DateTimeOffset.UtcNow.Add(result.NextCursor is null ? RuntimeReconciliationPolicy.Interval(item.Target.ActiveOrUnresolved) : TimeSpan.FromSeconds(1))
                }, token);
                await output.WriteLineAsync(JsonSerializer.Serialize(result, CaptureSpool.JsonOptions));
            }
            catch (Exception ex) when (ex is IOException or JsonException or OperationCanceledException or System.ComponentModel.Win32Exception)
            {
                await Diagnostic(new(1, "gap", DateTimeOffset.UtcNow, "history_read_incomplete", state.ThreadId, NextCursor: state.Cursor), epoch, CancellationToken.None);
                await checkpoints.SaveAsync(state with { Cursor = null, InProgress = false, Outcome = "history_read_incomplete", DueAt = DateTimeOffset.UtcNow.AddSeconds(5) }, CancellationToken.None);
                if (token.IsCancellationRequested) throw;
            }
        }
        Task<Guid> Diagnostic(RuntimeReconciliationInput input, Guid epoch, CancellationToken ct) => spool.CaptureRuntimeAsync(
            new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(input, CaptureSpool.JsonOptions)), DateTimeOffset.UtcNow, new NoContext(),
            new(config.RuntimeNamespaceId, config.SourceId, epoch, "codex", "diagnostic", "RecorderDiagnostic", "reconciliation", RuntimeVersion: inventory.ExecutableVersion), ct);
    }

    public static void Validate(CodexReconciliationEnrollment config)
    {
        if (config.Version != 1 || config.RuntimeNamespaceId == Guid.Empty || config.SourceId == Guid.Empty
            || !Path.IsPathFullyQualified(config.Executable) || !File.Exists(config.Executable)
            || !Path.IsPathFullyQualified(config.CodexHome) || !Directory.Exists(config.CodexHome)
            || !Path.IsPathFullyQualified(config.StateDirectory) || !Uri.TryCreate(config.ApiBaseUrl, UriKind.Absolute, out var uri)
            || !uri.IsLoopback || uri.Scheme is not ("http" or "https") || config.HookSourceIds is null || config.ThreadIds is null
            || config.HookSourceIds.Length > 32 || config.ThreadIds.Length > 1000 || config.HookSourceIds.Any(id => id == Guid.Empty || id == config.SourceId)
            || config.ThreadIds.Any(id => string.IsNullOrWhiteSpace(id) || id.Length > 512)) throw new ArgumentException("Invalid explicit local reconciliation enrollment.");
    }
    private sealed class NoContext : IRepositoryContextReader
    {
        public Task<RepositoryContext> ReadAsync(string workingDirectory, CancellationToken cancellationToken = default) => throw new InvalidOperationException();
    }
}
