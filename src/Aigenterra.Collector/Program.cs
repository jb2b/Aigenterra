using Aigenterra.Collector;
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

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

using var tracerProvider = CreateTracerProvider();

var exitCode = await CollectorCommand.ExecuteAsync(
    Console.OpenStandardInput(),
    Console.Out,
    client,
    endpoint,
    DateTimeOffset.UtcNow);

try
{
    tracerProvider?.ForceFlush(250);
}
catch
{
    // Collector telemetry is best-effort and must preserve fail-open hook behavior.
}

return exitCode;

static TracerProvider? CreateTracerProvider()
{
    if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT")))
    {
        return null;
    }

    try
    {
        return Sdk.CreateTracerProviderBuilder()
            .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("aigenterra-collector"))
            .AddSource(CollectorCommand.ActivitySourceName)
            .AddOtlpExporter(options => options.TimeoutMilliseconds = 200)
            .Build();
    }
    catch
    {
        return null;
    }
}
