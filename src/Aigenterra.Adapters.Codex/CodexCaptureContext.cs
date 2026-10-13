using System.Text.Json;

namespace Aigenterra.Adapters.Codex;

/// <summary>Provider-specific selection of local context; no inferred process cwd or subagent cwd.</summary>
public static class CodexCaptureContext
{
    public static (string? Phase, string? WorkingDirectory) Identify(ReadOnlyMemory<byte> payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            string? Text(string name) => root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var value)
                && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
            var phase = Text("hook_event_name") switch { "UserPromptSubmit" => "start", "Stop" or "Interrupt" => "end", _ => null };
            var cwd = Text("cwd");
            return (phase, cwd is not null && Path.IsPathFullyQualified(cwd) ? cwd : null);
        }
        catch (JsonException) { return (null, null); }
    }
}
