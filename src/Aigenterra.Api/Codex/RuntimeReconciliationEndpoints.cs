using Aigenterra.Application.Runtime;

namespace Aigenterra.Api.Codex;

internal static class RuntimeReconciliationEndpoints
{
    public static void MapRuntimeReconciliationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/runtime/reconciliation/targets", async (Guid runtimeNamespaceId, Guid sourceId, string? after, int? limit,
            GetRuntimeReconciliationTargets query, HttpResponse response, CancellationToken token) =>
        {
            if (runtimeNamespaceId == Guid.Empty || sourceId == Guid.Empty || limit is < 1 or > 500 || after?.Length > 512)
                return Results.BadRequest(new { code = "invalid_reconciliation_scope" });
            response.Headers.CacheControl = "no-store";
            var result = await query.ExecuteAsync(runtimeNamespaceId, sourceId, after, limit ?? 100, token);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });
        endpoints.MapGet("/runtime/sources/{sourceId:guid}/reconciliation", async (Guid runtimeNamespaceId, Guid sourceId,
            IRuntimeReconciliationStore store, HttpResponse response, CancellationToken token) =>
        {
            if (runtimeNamespaceId == Guid.Empty || sourceId == Guid.Empty) return Results.BadRequest();
            response.Headers.CacheControl = "no-store";
            var health = await store.ReadHealthAsync(runtimeNamespaceId, sourceId, token);
            return health is null ? Results.NotFound() : Results.Ok(health);
        });
    }
}
