var builder = DistributedApplication.CreateBuilder(args);

var password = builder.AddParameter("postgres-password", secret: true);
var database = builder.AddPostgres("postgres", password: password)
    .WithImageTag("17.11-bookworm")
    .WithDataVolume("aigenterra-postgres-data")
    .AddDatabase("aigenterra");

builder.AddProject<Projects.Aigenterra_Api>("api")
    .WithReference(database)
    .WaitFor(database)
    .WithHttpHealthCheck("/health");

builder.Build().Run();
