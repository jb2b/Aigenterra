using System.Text.Json;

namespace Aigenterra.Adapters.Codex;

/// <summary>Represents the validated fields needed from a supported Codex hook payload.</summary>
internal sealed record CodexHookEvent(
    string HookEventName,
    string? SessionId,
    string? TurnId,
    string? Prompt,
    string? ToolName,
    string? ToolUseId,
    string? AgentId,
    string? AgentType,
    bool? StopHookActive,
    string? LastAssistantMessage)
{
    public static CodexHookEvent Parse(ReadOnlyMemory<byte> payload)
    {
        using var document = JsonDocument.Parse(payload);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("The Codex hook payload must be a JSON object.");
        }

        var root = document.RootElement;
        var eventName = RequiredString(root, "hook_event_name");
        if (!IsSupported(eventName))
        {
            return new CodexHookEvent(
                eventName,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null);
        }

        var sessionId = RequiredString(root, "session_id");
        return eventName switch
        {
            "SessionStart" => Create(eventName, sessionId),
            "UserPromptSubmit" => Create(
                eventName,
                sessionId,
                turnId: RequiredString(root, "turn_id"),
                prompt: RequiredString(root, "prompt")),
            "PreToolUse" or "PostToolUse" => Create(
                eventName,
                sessionId,
                turnId: RequiredString(root, "turn_id"),
                toolName: RequiredString(root, "tool_name"),
                toolUseId: RequiredString(root, "tool_use_id")),
            "SubagentStart" => Create(
                eventName,
                sessionId,
                turnId: RequiredString(root, "turn_id"),
                agentId: RequiredString(root, "agent_id"),
                agentType: RequiredString(root, "agent_type")),
            "SubagentStop" => Create(
                eventName,
                sessionId,
                turnId: RequiredString(root, "turn_id"),
                agentId: RequiredString(root, "agent_id"),
                agentType: RequiredString(root, "agent_type"),
                stopHookActive: RequiredBoolean(root, "stop_hook_active"),
                lastAssistantMessage: OptionalString(root, "last_assistant_message")),
            "Stop" => Create(
                eventName,
                sessionId,
                turnId: RequiredString(root, "turn_id"),
                stopHookActive: RequiredBoolean(root, "stop_hook_active"),
                lastAssistantMessage: OptionalString(root, "last_assistant_message")),
            "Interrupt" => Create(
                eventName,
                sessionId,
                turnId: RequiredString(root, "turn_id")),
            _ => throw new InvalidOperationException("Unsupported Codex hook event reached parsing.")
        };
    }

    public static bool IsSupported(string eventName) =>
        eventName is "SessionStart"
            or "UserPromptSubmit"
            or "PreToolUse"
            or "PostToolUse"
            or "SubagentStart"
            or "SubagentStop"
            or "Stop"
            or "Interrupt";

    private static CodexHookEvent Create(
        string eventName,
        string sessionId,
        string? turnId = null,
        string? prompt = null,
        string? toolName = null,
        string? toolUseId = null,
        string? agentId = null,
        string? agentType = null,
        bool? stopHookActive = null,
        string? lastAssistantMessage = null) =>
        new(
            eventName,
            sessionId,
            turnId,
            prompt,
            toolName,
            toolUseId,
            agentId,
            agentType,
            stopHookActive,
            lastAssistantMessage);

    private static string RequiredString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value)
            || value.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw new JsonException($"Codex hook field '{propertyName}' must be a non-empty string.");
        }

        return value.GetString()!;
    }

    private static string? OptionalString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value)
            || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            throw new JsonException($"Codex hook field '{propertyName}' must be a string or null.");
        }

        return value.GetString();
    }

    private static bool RequiredBoolean(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value)
            || value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new JsonException($"Codex hook field '{propertyName}' must be a boolean.");
        }

        return value.GetBoolean();
    }
}
