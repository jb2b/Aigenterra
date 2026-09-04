using System.Globalization;
using Aigenterra.Adapters.Codex;
using Aigenterra.Api.Executions;

namespace Aigenterra.Api.Codex;

internal static class CodexIngestEndpoints
{
    private const int MaxPayloadBytes = 1024 * 1024;
    private const string ObservedAtHeader = "X-Aigenterra-Observed-At";

    public static IEndpointRouteBuilder MapCodexIngestEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/ingest/codex/hooks", IngestAsync)
            .DisableRequestTimeout();
        return endpoints;
    }

    private static async Task<IResult> IngestAsync(
        HttpRequest request,
        ICodexHookIngestor ingestor,
        CancellationToken cancellationToken)
    {
        if (!TryReadObservedAt(request, out var observedAt))
        {
            return ExecutionEndpoints.Problem(
                Application.Common.ApplicationErrorType.Validation,
                "observed_at_header_invalid",
                $"Header {ObservedAtHeader} must contain a UTC ISO-8601 timestamp.");
        }

        var payload = await ReadPayloadAsync(request.Body, cancellationToken);
        if (payload is null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status413PayloadTooLarge,
                title: "Codex hook payload is too large",
                detail: $"The request body cannot exceed {MaxPayloadBytes} bytes.",
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = "codex_hook_payload_too_large"
                });
        }

        var result = await ingestor.IngestAsync(payload, observedAt, cancellationToken);
        return ExecutionEndpoints.ToHttpResult(result, receipt => Results.Ok(new
        {
            disposition = receipt.Disposition.ToString().ToLowerInvariant(),
            receipt.ConversationId,
            receipt.TaskExecutionId,
            receipt.AgentExecutionId,
            receipt.ObservationId
        }));
    }

    private static bool TryReadObservedAt(HttpRequest request, out DateTimeOffset observedAt)
    {
        var value = request.Headers[ObservedAtHeader].ToString();
        if (!DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out observedAt)
            || observedAt.Offset != TimeSpan.Zero)
        {
            observedAt = default;
            return false;
        }

        observedAt = observedAt.ToUniversalTime();
        return true;
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
