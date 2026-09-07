using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;

namespace Aigenterra.Collector;

/// <summary>
/// Forwards one hook payload to Aigenterra without blocking the observed runtime.
/// </summary>
public static class CollectorCommand
{
    internal const string ActivitySourceName = "Aigenterra.Collector";
    public const int MaxPayloadBytes = 1024 * 1024;

    private static readonly ActivitySource ActivitySource = new(ActivitySourceName);

    public static async Task<int> ExecuteAsync(
        Stream input,
        TextWriter output,
        HttpClient client,
        Uri endpoint,
        DateTimeOffset observedAt,
        CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity("forward hook", ActivityKind.Client);

        try
        {
            var payloadRead = await ReadPayloadAsync(input, cancellationToken);
            activity?.SetTag("aigenterra.collector.payload.size", payloadRead.BytesRead);

            if (payloadRead.Payload is not null)
            {
                using var content = new ByteArrayContent(payloadRead.Payload);
                content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
                {
                    Content = content
                };
                request.Headers.TryAddWithoutValidation(
                    "X-Aigenterra-Observed-At",
                    observedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
                using var response = await client.SendAsync(request, cancellationToken);
                activity?.SetTag("http.response.status_code", (int)response.StatusCode);
                activity?.SetTag(
                    "aigenterra.collector.result",
                    response.IsSuccessStatusCode ? "forwarded" : "http_error");

                if (!response.IsSuccessStatusCode)
                {
                    activity?.SetStatus(ActivityStatusCode.Error);
                }
            }
            else
            {
                activity?.SetTag("aigenterra.collector.result", "payload_too_large");
                activity?.SetStatus(ActivityStatusCode.Error);
            }
        }
        catch (Exception exception)
        {
            activity?.SetTag("aigenterra.collector.result", "transport_error");
            activity?.SetTag("error.type", exception.GetType().FullName);
            activity?.SetStatus(ActivityStatusCode.Error);

            // Hook instrumentation is deliberately fail-open and must never block Codex.
        }

        await output.WriteLineAsync("{}");
        return 0;
    }

    private static async Task<PayloadReadResult> ReadPayloadAsync(
        Stream input,
        CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        while (buffer.Length <= MaxPayloadBytes)
        {
            var read = await input.ReadAsync(chunk, cancellationToken);
            if (read == 0)
            {
                return new PayloadReadResult(buffer.ToArray(), buffer.Length);
            }

            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }

        return new PayloadReadResult(null, buffer.Length);
    }

    private readonly record struct PayloadReadResult(byte[]? Payload, long BytesRead);
}
