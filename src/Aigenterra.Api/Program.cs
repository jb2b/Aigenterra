using Aigenterra.Adapters.Codex;
using Aigenterra.Api.Codex;
using Aigenterra.Api.Executions;
using Aigenterra.Application.Executions;
using Aigenterra.Application.Executions.Ports;
using Aigenterra.Infrastructure.Executions;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddProblemDetails();
builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);

builder.Services.AddSingleton<InMemoryExecutionStore>();
builder.Services.AddSingleton<IExecutionWriteStore>(services =>
    services.GetRequiredService<InMemoryExecutionStore>());
builder.Services.AddSingleton<ITaskExecutionTimelineReader>(services =>
    services.GetRequiredService<InMemoryExecutionStore>());
builder.Services.AddSingleton<IExecutionRuntimeReferenceReader>(services =>
    services.GetRequiredService<InMemoryExecutionStore>());

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

app.MapDefaultEndpoints();

app.MapGet("/status", () => Results.Ok(new { status = "ok" }));
app.MapExecutionEndpoints();
app.MapCodexIngestEndpoints();

app.Run();
