using Aigenterra.Adapters.Codex;
using Aigenterra.Api.Codex;
using Aigenterra.Api.Executions;
using Aigenterra.Api.Health;
using Aigenterra.Api.Evaluations;
using Aigenterra.Application.Evaluations;
using Aigenterra.Application.Executions;
using Aigenterra.Application.Recording;
using Aigenterra.Application.Runtime;
using Aigenterra.Adapters.Codex.Normalization;
using Aigenterra.Adapters.Codex.Diagnostics;
using Aigenterra.Infrastructure.Executions.Postgres;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddProblemDetails();
builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);

builder.Services.AddPostgresExecutions(builder.Configuration.GetConnectionString("aigenterra")
    ?? throw new InvalidOperationException("ConnectionStrings:aigenterra is required. Start the API through AppHost or supply PostgreSQL configuration."));
builder.Services.AddHealthChecks()
    .AddCheck<ExecutionDatabaseHealthCheck>("postgres", timeout: TimeSpan.FromSeconds(3));

builder.Services.AddTransient<CreateConversation>();
builder.Services.AddTransient<StartTaskExecution>();
builder.Services.AddTransient<StartAgentExecution>();
builder.Services.AddTransient<RecordObservation>();
builder.Services.AddTransient<FinishAgentExecution>();
builder.Services.AddTransient<FinishTaskExecution>();
builder.Services.AddTransient<GetTaskExecutionTimeline>();
builder.Services.AddTransient<GetRecentTaskExecutions>();
builder.Services.AddTransient<ResolveExecutionRuntimeReference>();
builder.Services.AddTransient<GetTaskExecutionTimelineByRuntimeReference>();
builder.Services.AddTransient<ICodexHookIngestor, CodexHookIngestor>();
builder.Services.AddTransient<CodexCaptureIngestor>();
builder.Services.AddTransient<ArchiveRuntimeCapture>();
builder.Services.AddTransient<IRuntimeCaptureNormalizer, CodexHistoryNormalizer>();
builder.Services.AddTransient<NormalizeRuntimeCapture>();
builder.Services.AddTransient<IRuntimeRequestContextDecoder, CodexRequestContextDecoder>();
builder.Services.AddTransient<IRuntimeEvidenceNormalizer, CodexAgentEvidenceNormalizer>();
builder.Services.AddTransient<IRuntimeReconciliationDecoder, CodexReconciliationDecoder>();
builder.Services.AddTransient<GetRuntimeReconciliationTargets>();
builder.Services.AddTransient<ProcessRuntimeCapture>();
builder.Services.AddHostedService<RuntimeProcessingWorker>();
builder.Services.AddTransient<GetRuntimeTurn>();
builder.Services.AddTransient<IRuntimeHealthDecoder, CodexCapabilityDetector>();
builder.Services.AddTransient<RecordRuntimeSourceHealth>();
builder.Services.AddTransient<GetRuntimeSourceHealth>();
builder.Services.AddTransient<GetRuntimeInstances>();
builder.Services.AddTransient<CodexCommandResultIngestor>();
builder.Services.AddTransient<ICommandEvidenceDecoder, CodexCommandEvidenceDecoder>();
builder.Services.AddTransient<EvaluateTaskExecution>();
builder.Services.AddTransient<GetTaskExecutionEvaluations>();
builder.Services.AddTransient<GetTaskExecutionEvaluation>();

var app = builder.Build();
app.UseExceptionHandler();

if (builder.Configuration.GetValue<bool?>("Database:ApplyMigrationsOnStartup")
    ?? app.Environment.IsDevelopment())
{
    await app.Services.MigrateExecutionDatabaseAsync();
}

app.MapDefaultEndpoints();

app.MapGet("/status", () => Results.Ok(new { status = "ok" }));
app.MapExecutionEndpoints();
app.MapCodexIngestEndpoints();
app.MapCaptureEndpoints();
app.MapRuntimeCaptureEndpoints();
app.MapRuntimeHealthEndpoints();
app.MapRuntimeReconciliationEndpoints();
app.MapEvaluationEndpoints();

app.Run();
