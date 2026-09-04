using Aigenterra.Collector;

const string defaultIngestUrl = "http://127.0.0.1:5248/ingest/codex/hooks";

var configuredUrl = Environment.GetEnvironmentVariable("AIGENTERRA_INGEST_URL");
if (!Uri.TryCreate(configuredUrl ?? defaultIngestUrl, UriKind.Absolute, out var endpoint))
{
    Console.WriteLine("{}");
    return 0;
}

using var client = new HttpClient
{
    Timeout = TimeSpan.FromMilliseconds(500)
};

return await CollectorCommand.ExecuteAsync(
    Console.OpenStandardInput(),
    Console.Out,
    client,
    endpoint,
    DateTimeOffset.UtcNow);
