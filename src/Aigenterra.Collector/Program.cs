using System.Text.Json;
using Aigenterra.Adapters.Git;
using Aigenterra.Collector;

var spoolPath = Environment.GetEnvironmentVariable("AIGENTERRA_SPOOL_PATH")
    ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Aigenterra", "spool");
var maxBytes = int.TryParse(Environment.GetEnvironmentVariable("AIGENTERRA_CAPTURE_MAX_BYTES"), out var configuredLimit)
    && configuredLimit > 0 ? configuredLimit : 16 * 1024 * 1024;
var spool = new CaptureSpool(spoolPath, maxBytes);
if (args.FirstOrDefault() == "status")
{
    Console.WriteLine(JsonSerializer.Serialize(spool.Status(), CaptureSpool.JsonOptions));
    return 0;
}
if (args.FirstOrDefault() == "drain")
{
    var configuredUrl = Environment.GetEnvironmentVariable("AIGENTERRA_CAPTURE_URL")
        ?? (Environment.GetEnvironmentVariable("AIGENTERRA_INGEST_URL") ?? "http://127.0.0.1:5248/ingest/codex/hooks")
            .Replace("/ingest/codex/hooks", "/ingest/codex/captures/v1", StringComparison.Ordinal);
    if (!Uri.TryCreate(configuredUrl, UriKind.Absolute, out var endpoint) || endpoint.Scheme is not ("http" or "https"))
    {
        Console.Error.WriteLine("Invalid capture endpoint.");
        return 1;
    }
    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
    using var stop = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
    var delay = 1;
    try
    {
        do
        {
            var result = await spool.DrainAsync(client, endpoint, stop.Token);
            if (result.Delivered > 0 || result.Error is not null)
                Console.WriteLine(JsonSerializer.Serialize(result, CaptureSpool.JsonOptions));
            if (!args.Contains("--watch", StringComparer.Ordinal)) return result.Error is null ? 0 : 1;
            delay = result.Error is null ? 1 : Math.Min(30, delay * 2);
            await Task.Delay(TimeSpan.FromSeconds(delay), stop.Token);
        } while (!stop.IsCancellationRequested);
    }
    catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
    return 0;
}

try
{
    await spool.CaptureAsync(Console.OpenStandardInput(), DateTimeOffset.UtcNow, new GitRepositoryContextReader());
}
catch (Exception)
{
    await spool.RecordCaptureFailureAsync();
    Console.Error.WriteLine("Aigenterra local capture failed; check Collector status and disk access.");
}
// Recording must never control or block the observed agent on failure.
Console.WriteLine("{}");
return 0;
