using System.Text.Json;
using System.Text.Json.Serialization;

namespace Aigenterra.Adapters.Codex.Normalization;

// Only the interpreted protocol subset is typed. Unknown fields remain in the original archived bytes.
internal sealed record CodexTurn(string Id, string Status, long? StartedAt, long? CompletedAt)
{
    [JsonExtensionData] public Dictionary<string, JsonElement>? Other { get; init; }
}

internal sealed record CodexTurnEvent(string ThreadId, CodexTurn Turn);

internal sealed record CodexThread(string Id, string? ParentThreadId, JsonElement Source, JsonElement Status, CodexTurn[]? Turns)
{
    [JsonExtensionData] public Dictionary<string, JsonElement>? Other { get; init; }
}
