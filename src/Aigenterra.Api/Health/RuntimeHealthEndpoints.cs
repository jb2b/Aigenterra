using Aigenterra.Application.Runtime;

namespace Aigenterra.Api.Health;

internal static class RuntimeHealthEndpoints
{
    public static void MapRuntimeHealthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/runtime/sources/{id:guid}/health", async (Guid id, Guid runtimeNamespaceId,
            GetRuntimeSourceHealth read, HttpResponse response, CancellationToken token) =>
        {
            if (runtimeNamespaceId == Guid.Empty || id == Guid.Empty) return Results.BadRequest(new { code = "invalid_runtime_identity" });
            response.Headers.CacheControl = "no-store";
            var result = await read.ExecuteAsync(runtimeNamespaceId, id, token);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });
        endpoints.MapGet("/runtime/instances", async (Guid runtimeNamespaceId, long? after, int? limit,
            GetRuntimeInstances read, HttpResponse response, CancellationToken token) =>
        {
            if (runtimeNamespaceId == Guid.Empty || after < 0 || limit is < 1 or > 500) return Results.BadRequest(new { code = "invalid_runtime_page" });
            response.Headers.CacheControl = "no-store";
            var rows = await read.ExecuteAsync(runtimeNamespaceId, after ?? 0, limit ?? 100, token);
            return Results.Ok(new { instances = rows, next = rows.Count == (limit ?? 100) ? rows[^1].Position : (long?)null });
        });
    }
}
