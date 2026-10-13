namespace Aigenterra.AppHost;

public static class DatabaseMode
{
    public static bool UseContainer(string? mode) => (mode ?? "External").ToLowerInvariant() switch
    {
        "external" => false,
        "container" => true,
        _ => throw new InvalidOperationException("Database:Mode must be External or Container.")
    };
}
