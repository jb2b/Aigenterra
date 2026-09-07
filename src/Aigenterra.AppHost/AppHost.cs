var builder = DistributedApplication.CreateBuilder(args);

var password = builder.AddParameter("postgres-password", secret: true);
var database = builder.AddPostgres("postgres", password: password)
    .WithImageTag("17.11-bookworm")
    .WithDataVolume("aigenterra-postgres-data")
    .AddDatabase("aigenterra");

var api = builder.AddProject<Projects.Aigenterra_Api>("api")
    .WithReference(database)
    .WaitFor(database)
    .WithHttpHealthCheck("/health");

builder.AddJavaScriptApp("web", "../Aigenterra.Web")
    .WithNpm(installCommand: "ci")
    .WithHttpEndpoint(env: "PORT")
    .WithReference(api)
    .WithEnvironment("API_HTTP", api.GetEndpoint("http"))
    .WaitFor(api);

builder.Build().Run();
