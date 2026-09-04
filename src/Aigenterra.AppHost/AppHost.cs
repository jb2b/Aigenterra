var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.Aigenterra_Api>("api")
    .WithHttpHealthCheck("/health");

builder.Build().Run();
