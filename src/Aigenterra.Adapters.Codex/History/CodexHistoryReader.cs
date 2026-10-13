using System.Diagnostics;
using System.Text.Json;

namespace Aigenterra.Adapters.Codex.History;

public sealed record HistoryReadResult(Guid CycleId, string ThreadId, string? NextCursor, int Pages, string Outcome);

/// <summary>Owns only its independent stdio reader. Never attaches, resumes or controls an owner session.</summary>
public static class CodexHistoryReader
{
    public static async Task<HistoryReadResult> ReadAsync(string executable, string codexHome, string threadId,
        HistoryCapture capture, string temporaryDirectory, string? cursor = null, CancellationToken token = default)
    {
        if (!Path.IsPathFullyQualified(executable) || !File.Exists(executable) || !Path.IsPathFullyQualified(codexHome)
            || !Directory.Exists(codexHome) || string.IsNullOrWhiteSpace(threadId) || threadId.Length > 512)
            throw new ArgumentException("Explicit executable, existing Codex home and thread ID required.");
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = codexHome,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add("app-server");
        start.Environment["CODEX_HOME"] = codexHome;
        using var process = Process.Start(start) ?? throw new IOException("Could not start history reader.");
        using var stderrStop = new CancellationTokenSource();
        var stderr = DrainStderrAsync(process.StandardError.BaseStream, capture, stderrStop.Token);
        try
        {
            var connection = new CodexHistoryConnection(process.StandardInput.BaseStream, process.StandardOutput.BaseStream, capture, temporaryDirectory);
            await connection.InitializeAsync(token);
            return await ReadPagesAsync(connection, threadId, cursor, token);
        }
        finally
        {
            process.StandardInput.Close();
            try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3)); }
            catch (TimeoutException)
            {
                // Only this Process object is ours. Never enumerate or terminate owner/daemon processes.
                if (!process.HasExited) process.Kill(entireProcessTree: false);
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3));
            }
            stderrStop.Cancel();
            try { await stderr; } catch (OperationCanceledException) when (stderrStop.IsCancellationRequested) { }
        }
    }

    public static async Task<HistoryReadResult> ReadPagesAsync(CodexHistoryConnection connection, string threadId,
        string? cursor = null, CancellationToken token = default)
    {
        var cycle = Guid.NewGuid();
        var timer = Stopwatch.StartNew();
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(token);
        budget.CancelAfter(TimeSpan.FromSeconds(30));
        await connection.CallAsync("thread/read", new { threadId, includeTurns = false }, cycle, budget.Token);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (cursor is not null) seen.Add(cursor);
        for (var page = 1; page <= 100; page++)
        {
            HistoryReply reply;
            try { reply = await connection.CallAsync("thread/turns/list", new { threadId, cursor, limit = 100, itemsView = "full", sortDirection = "asc" }, cycle, budget.Token); }
            catch (OperationCanceledException) when (!token.IsCancellationRequested && budget.IsCancellationRequested)
            { return new(cycle, threadId, cursor, page - 1, "budget_limited"); }
            var result = reply.Message.GetProperty("result");
            if (!result.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array
                || !result.TryGetProperty("nextCursor", out var next) || next.ValueKind is not (JsonValueKind.Null or JsonValueKind.String))
                throw new InvalidDataException("Unsupported history page shape; raw response retained.");
            cursor = next.ValueKind == JsonValueKind.Null ? null : next.GetString();
            if (cursor is null) return new(cycle, threadId, null, page, "history_read_partial_coverage");
            if (!seen.Add(cursor)) throw new InvalidDataException("Repeated history cursor.");
            if (page == 100 || timer.Elapsed >= TimeSpan.FromSeconds(30)) return new(cycle, threadId, cursor, page, "budget_limited");
        }
        throw new InvalidOperationException();
    }

    private static async Task DrainStderrAsync(Stream stderr, HistoryCapture capture, CancellationToken token)
    {
        var bytes = new byte[8192];
        int length;
        while ((length = await stderr.ReadAsync(bytes, token)) > 0)
            await capture(new MemoryStream(bytes, 0, length, writable: false), "reader_stderr", token);
    }
}
