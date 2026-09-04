using System.Globalization;
using System.Net.Http.Headers;

namespace Aigenterra.Collector;

/// <summary>Forwards one hook payload to Aigenterra without blocking the observed runtime.</summary>
public static class CollectorCommand
{
    public const int MaxPayloadBytes = 1024 * 1024;

    public static async Task<int> ExecuteAsync(
        Stream input,
        TextWriter output,
        HttpClient client,
        Uri endpoint,
        DateTimeOffset observedAt,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var payload = await ReadPayloadAsync(input, cancellationToken);
            if (payload is not null)
            {
                using var content = new ByteArrayContent(payload);
                content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
                {
                    Content = content
                };
                request.Headers.TryAddWithoutValidation(
                    "X-Aigenterra-Observed-At",
                    observedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
                using var response = await client.SendAsync(request, cancellationToken);
            }
        }
        catch
        {
            // Hook instrumentation is deliberately fail-open and must never block Codex.
        }

        await output.WriteLineAsync("{}");
        return 0;
    }

    private static async Task<byte[]?> ReadPayloadAsync(
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
                return buffer.ToArray();
            }

            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }

        return null;
    }
}
