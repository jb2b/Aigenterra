using System.Text.Json;
using Aigenterra.Adapters.Codex.Diagnostics;
using Aigenterra.Adapters.Codex.History;
using Aigenterra.Application.Recording;

namespace Aigenterra.Collector;

public static class CodexReconcileCommand
{
    public static async Task<int> ExecuteAsync(string[] args, TextWriter output, TextWriter error, CaptureSpool spool, CancellationToken token = default)
    {
        try
        {
            if (args.Length == 3 && args[0] == "--enrollment" && args[2] == "--watch")
                return await CodexReconciliationWatch.ExecuteAsync(args[1], true, spool, output, token);
            var options = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var i = 0; i < args.Length; i++)
                if (args[i] is not ("--executable" or "--home" or "--namespace" or "--source" or "--thread" or "--cursor")
                    || i + 1 == args.Length || !options.TryAdd(args[i], args[++i])) throw new ArgumentException("Invalid reconcile options.");
            var ns = Guid.Parse(options["--namespace"]);
            var source = Guid.Parse(options["--source"]);
            if (ns == Guid.Empty || source == Guid.Empty) throw new ArgumentException("Explicit enrollment required.");
            var inventory = await CodexDoctorProbe.ReadAsync(options["--executable"], null, null, "unknown", token);
            var epoch = Guid.NewGuid();
            var temporary = Path.Combine(Path.GetTempPath(), "aigenterra-history-" + epoch.ToString("N"));
            Task<Guid> Capture(Stream bytes, string provenance, CancellationToken ct) => spool.CaptureRuntimeAsync(bytes, DateTimeOffset.UtcNow,
                new NoContext(), new(ns, source, epoch, "codex", provenance == "request_context" ? "diagnostic" : "app_server",
                    provenance == "reader_frame" ? "AppServerVisible" : "RecorderDiagnostic", provenance, RuntimeVersion: inventory.ExecutableVersion), ct,
                mediaType: provenance == "reader_stderr" ? "application/octet-stream" : "application/json");
            var result = await CodexHistoryReader.ReadAsync(options["--executable"], options["--home"], options["--thread"], Capture, temporary,
                options.GetValueOrDefault("--cursor"), token);
            await output.WriteLineAsync(JsonSerializer.Serialize(new { gate = "KEEP_GATE_BLOCKED", mode = "PASSIVE_HISTORY_ONLY", result }, CaptureSpool.JsonOptions));
            if (Directory.Exists(temporary) && !Directory.EnumerateFileSystemEntries(temporary).Any()) Directory.Delete(temporary);
            return 0;
        }
        catch (Exception ex) when (ex is ArgumentException or KeyNotFoundException or FormatException or IOException or JsonException or InvalidOperationException or OperationCanceledException or System.ComponentModel.Win32Exception)
        {
            await error.WriteLineAsync("Passive history read incomplete; original received evidence remains in the local spool. " + ex.GetType().Name);
            return 1;
        }
    }

    private sealed class NoContext : IRepositoryContextReader
    {
        public Task<RepositoryContext> ReadAsync(string workingDirectory, CancellationToken cancellationToken = default) => throw new InvalidOperationException();
    }
}
