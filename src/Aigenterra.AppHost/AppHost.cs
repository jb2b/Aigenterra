var builder = DistributedApplication.CreateBuilder(args);

var api = builder.AddProject<Projects.Aigenterra_Api>("api")
    .WithHttpHealthCheck("/health");

if (Aigenterra.AppHost.DatabaseMode.UseContainer(builder.Configuration["Database:Mode"]))
{
    var password = builder.AddParameter("postgres-password", secret: true);
    var database = builder.AddPostgres("postgres", password: password)
        .WithImageTag("17.11-bookworm")
        .WithDataVolume("aigenterra-postgres-data")
        .AddDatabase("aigenterra");
    api.WithReference(database).WaitFor(database);
}
else
{
    if (string.IsNullOrWhiteSpace(builder.Configuration["ConnectionStrings:aigenterra"]))
        throw new InvalidOperationException("External PostgreSQL requires ConnectionStrings:aigenterra in AppHost user secrets or environment. See docs/development/postgresql.md.");
    api.WithReference(builder.AddConnectionString("aigenterra"));
}

var collector = builder.AddProject<Projects.Aigenterra_Collector>("collector")
    .WithArgs("drain", "--watch")
    .WithEnvironment("AIGENTERRA_CAPTURE_URL", ReferenceExpression.Create($"{api.GetEndpoint("http")}/ingest/codex/captures/v1"));

if (builder.Configuration["AIGENTERRA_SPOOL_PATH"] is { } spoolPath)
    collector.WithEnvironment("AIGENTERRA_SPOOL_PATH", spoolPath);
if (builder.Configuration["AIGENTERRA_CAPTURE_MAX_BYTES"] is { } captureLimit)
    collector.WithEnvironment("AIGENTERRA_CAPTURE_MAX_BYTES", captureLimit);

builder.AddJavaScriptApp("web", "../Aigenterra.Web")
    .WithNpm(installCommand: "ci")
    .WithHttpEndpoint(env: "PORT")
    .WithReference(api)
    .WithEnvironment("API_HTTP", api.GetEndpoint("http"))
    .WaitFor(api);

builder.Build().Run();
