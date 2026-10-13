using Aigenterra.Application.Runtime;

namespace Aigenterra.Api.Codex;

internal sealed class RuntimeProcessingWorker(IServiceScopeFactory scopes, IConfiguration configuration,
    ILogger<RuntimeProcessingWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var queue = scope.ServiceProvider.GetRequiredService<IRuntimeWorkStore>();
                var work = await queue.ClaimAsync(configuration.GetValue<bool>("Runtime:NormalizeCaptures"),
                    configuration.GetValue<bool>("Runtime:RecordSourceHealth"), stoppingToken);
                if (work is null) { await Task.Delay(500, stoppingToken); continue; }
                try
                {
                    var outcome = await scope.ServiceProvider.GetRequiredService<ProcessRuntimeCapture>().ExecuteAsync(work.CaptureId, stoppingToken);
                    await queue.FinishAsync(work, outcome, outcome == "pending_context", stoppingToken);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.LogWarning(ex, "Runtime processing failed for {CaptureId}; archived evidence remains retryable", work.CaptureId);
                    await queue.FinishAsync(work, "processing_failed", true, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Runtime processing queue unavailable");
                try { await Task.Delay(1000, stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            }
        }
    }
}
