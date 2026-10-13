using System.Text.Json;
using Aigenterra.Api.Executions;
using Aigenterra.Application.Recording;
using Microsoft.AspNetCore.Http.Features;

namespace Aigenterra.Api.Codex;

internal static class RuntimeCaptureEndpoints
{
    public static void MapRuntimeCaptureEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/ingest/runtime/captures/v2", async (HttpRequest request, IConfiguration configuration,
            ArchiveRuntimeCapture archive, CancellationToken cancellationToken) =>
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
            RuntimeCaptureEnvelope? capture;
            try { capture = JsonSerializer.Deserialize<RuntimeCaptureEnvelope>(buffer.GetBuffer().AsSpan(0, (int)buffer.Length), new JsonSerializerOptions(JsonSerializerDefaults.Web)); }
            catch (JsonException) { return Results.BadRequest(new { code = "invalid_capture_envelope" }); }
            if (capture?.Payload is null) return Results.BadRequest(new { code = "invalid_capture_envelope" });
            if (capture.Payload.Length > maximum) return Results.StatusCode(413);
            return ExecutionEndpoints.ToHttpResult(await archive.ExecuteAsync(capture, cancellationToken), Results.Ok);
        }).DisableRequestTimeout();
    }
}
