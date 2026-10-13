using System.Text.Json;
using Aigenterra.Application.Runtime;

namespace Aigenterra.Api.Codex;

internal static class RuntimeProjectionCommand
{
    // Local maintenance command, no HTTP route. Requires the explicitly configured database.
    public static async Task ExecuteAsync(IServiceProvider services, string[] args)
    {
        await using var scope = services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IRuntimeProjectionStore>();
        var rebuild = scope.ServiceProvider.GetRequiredService<RebuildRuntimeProjection>();
        object result;
        if (args.Length == 2 && args[0] == "freeze") result = await store.FreezeAsync(Guid.Parse(args[1]), rebuild.Versions);
        else if (args.Length == 2 && args[0] == "rebuild") result = await rebuild.ExecuteAsync(Guid.Parse(args[1]));
        else if (args.Length == 2 && args[0] == "status") result = (object?)await store.ReadGenerationAsync(Guid.Parse(args[1])) ?? new { error = "not_found" };
        else if (args.Length == 3 && args[0] == "activate") result = await store.ActivateAsync(Guid.Parse(args[1]), args[2] == "none" ? null : Guid.Parse(args[2]));
        else throw new ArgumentException("runtime-projection freeze <namespace> | rebuild <generation> | status <generation> | activate <generation> <expected-active|none>");
        Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }
}
