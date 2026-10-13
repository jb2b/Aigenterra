using System.Text.Json;
using Aigenterra.Adapters.Codex;
using Aigenterra.Api.Executions;
using Aigenterra.Application.Recording;
using Microsoft.AspNetCore.Http.Features;

namespace Aigenterra.Api.Codex;

internal static class CaptureEndpoints
{
    public static void MapCaptureEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/ingest/codex/captures/v1", async (HttpRequest request, IConfiguration configuration,
            CodexCaptureIngestor ingestor, CancellationToken cancellationToken) =>
        {
            var maximum = configuration.GetValue<int?>("Capture:MaxPayloadBytes") ?? 16 * 1024 * 1024;
            if (maximum <= 0 || maximum > 256 * 1024 * 1024)
                throw new InvalidOperationException("Capture:MaxPayloadBytes must be between 1 and 268435456.");
            var wireMaximum = (long)maximum * 4 / 3 + 4 * 1024 * 1024;
            var sizeFeature = request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (sizeFeature is { IsReadOnly: false }) sizeFeature.MaxRequestBodySize = wireMaximum;
            using var buffer = new MemoryStream();
            var block = new byte[81920];
            int read;
            while ((read = await request.Body.ReadAsync(block, cancellationToken)) != 0)
            {
                if (buffer.Length + read > wireMaximum) return Results.StatusCode(413);
                await buffer.WriteAsync(block.AsMemory(0, read), cancellationToken);
            }
            CaptureEnvelope? capture;
            try { capture = JsonSerializer.Deserialize<CaptureEnvelope>(buffer.GetBuffer().AsSpan(0, (int)buffer.Length), new JsonSerializerOptions(JsonSerializerDefaults.Web)); }
            catch (JsonException) { return Results.BadRequest(new { code = "invalid_capture_envelope" }); }
            if (capture?.Payload is null) return Results.BadRequest(new { code = "invalid_capture_envelope" });
            if (capture.Payload.Length > maximum) return Results.StatusCode(413);
            var result = await ingestor.IngestAsync(capture, cancellationToken);
            return ExecutionEndpoints.ToHttpResult(result, outcome => Results.Ok(new
            {
                outcome.CaptureId,
                archived = true,
                outcome.Disposition,
                outcome.Reason,
                outcome.ConversationId,
                outcome.TaskExecutionId,
                outcome.AgentExecutionId,
                outcome.ObservationId
            }));
        }).DisableRequestTimeout();
    }
}
