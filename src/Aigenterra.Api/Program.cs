using Aigenterra.Adapters.Codex;
using Aigenterra.Api.Codex;
using Aigenterra.Api.Executions;
using Aigenterra.Application.Executions;
using Aigenterra.Infrastructure.Executions.Postgres;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddProblemDetails();
builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);

builder.Services.AddPostgresExecutions(builder.Configuration.GetConnectionString("aigenterra")
    ?? throw new InvalidOperationException("ConnectionStrings:aigenterra is required. Start the API through AppHost or supply PostgreSQL configuration."));

builder.Services.AddTransient<CreateConversation>();
builder.Services.AddTransient<StartTaskExecution>();
builder.Services.AddTransient<StartAgentExecution>();
builder.Services.AddTransient<RecordObservation>();
builder.Services.AddTransient<FinishAgentExecution>();
builder.Services.AddTransient<FinishTaskExecution>();
builder.Services.AddTransient<GetTaskExecutionTimeline>();
builder.Services.AddTransient<ResolveExecutionRuntimeReference>();
builder.Services.AddTransient<GetTaskExecutionTimelineByRuntimeReference>();
builder.Services.AddTransient<ICodexHookIngestor, CodexHookIngestor>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    await app.Services.MigrateExecutionDevelopmentDatabaseAsync();
}

app.MapDefaultEndpoints();

app.MapGet("/status", () => Results.Ok(new { status = "ok" }));
app.MapExecutionEndpoints();
app.MapCodexIngestEndpoints();

app.Run();
