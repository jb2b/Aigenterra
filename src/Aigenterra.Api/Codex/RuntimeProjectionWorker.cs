using Aigenterra.Application.Runtime;

namespace Aigenterra.Api.Codex;

internal sealed class RuntimeProjectionWorker(IServiceScopeFactory scopes, IConfiguration configuration, ILogger<RuntimeProjectionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Explicit namespace opt-in, independent from capture acceptance and the later public inspection API.
        var namespaces = configuration.GetSection("Runtime:ProjectionNamespaces").Get<Guid[]>() ?? [];
        while (!stoppingToken.IsCancellationRequested)
        {
            foreach (var ns in namespaces.Where(x => x != Guid.Empty).Distinct())
            {
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    var store = scope.ServiceProvider.GetRequiredService<IRuntimeProjectionStore>();
                    var rebuild = scope.ServiceProvider.GetRequiredService<RebuildRuntimeProjection>();
                    var active = await store.ReadActiveAsync(ns, stoppingToken);
                    var old = active is null ? null : await store.ReadGenerationAsync(active.GenerationId, stoppingToken);
                    if (old?.Versions == rebuild.Versions && !await store.HasNewInputsAsync(ns, old.Id, stoppingToken)) continue;
                    var building = await store.FindBuildingAsync(ns, rebuild.Versions, stoppingToken);
                    var generation = building ?? (await store.FreezeAsync(ns, rebuild.Versions, stoppingToken)).Id;
                    var completed = await rebuild.ExecuteAsync(generation, stoppingToken);
                    // A competing publisher/rollback is never overwritten using a stale baseline.
                    if (completed.BaseGenerationId != active?.GenerationId) continue;
                    await store.ActivateAsync(generation, completed.BaseGenerationId, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
                catch (Exception ex) { logger.LogWarning(ex, "Projection rebuild for {Namespace} remains retryable", ns); }
            }
            try { await Task.Delay(1000, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
