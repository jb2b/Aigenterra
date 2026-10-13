using System.Text.Json;
using Aigenterra.Application.Evaluations;

namespace Aigenterra.Adapters.Codex;

public sealed class CodexCommandEvidenceDecoder : ICommandEvidenceDecoder
{
    public DecodedCommands Decode(EvaluationInput input)
    {
        var commands = new List<CommandEvidence>();
        var limited = false;
        foreach (var observation in input.Observations.Where(o => o.Kind is "toolinvocation" or "toolresult" or "testexecution"))
        {
            if (observation.Reference?.Runtime != "codex" || observation.Evidence?.MediaType != "application/json") { limited = true; continue; }
            try
            {
                using var document = JsonDocument.Parse(observation.Evidence.Content);
                var root = document.RootElement;
                var session = Text(root, "session_id");
                var turn = Text(root, "turn_id");
                string? call;
                string? command;
                int? exit = null;
                bool invalid = false;
                CommandEvidenceStage stage;
                if (Text(root, "source") == "codex-app-server-command/v1" && observation.Reference.ResourceKind == "command-result")
                {
                    var item = root.GetProperty("item");
                    call = Text(item, "id");
                    command = OriginalCommand(item);
                    stage = CommandEvidenceStage.Terminal;
                    var status = Text(item, "status");
                    if (item.TryGetProperty("exitCode", out var code) && code.ValueKind == JsonValueKind.Number && code.TryGetInt32(out var value)) exit = value;
                    invalid = Text(item, "type") != "commandExecution" || exit is null
                        || (status != "completed" && status != "failed")
                        || (status == "completed" && exit != 0) || (status == "failed" && exit == 0);
                }
                else
                {
                    var hook = Text(root, "hook_event_name");
                    if (hook is not ("PreToolUse" or "PostToolUse") || Text(root, "tool_name") != "Bash") { limited = true; continue; }
                    if (observation.Reference.ResourceKind != (hook == "PreToolUse" ? "tool-invocation" : "tool-result")) { limited = true; continue; }
                    call = Text(root, "tool_use_id");
                    command = root.TryGetProperty("tool_input", out var toolInput) ? Text(toolInput, "command") : null;
                    stage = hook == "PreToolUse" ? CommandEvidenceStage.Requested : CommandEvidenceStage.Returned;
                    // Hook stdout is not a trusted exit-code channel, regardless of its wording.
                }
                if (session is null || turn is null || call is null) { limited = true; continue; }
                commands.Add(new(observation.Id, observation.Sequence, observation.ObservedAt,
                    JsonSerializer.Serialize(new[] { session, turn, call }), command, stage, exit, invalid));
            }
            catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
            { limited = true; }
        }
        return new(commands, limited);
    }

    internal static string? Text(JsonElement root, string name) => root.ValueKind == JsonValueKind.Object
        && root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
        && !string.IsNullOrWhiteSpace(value.GetString()) ? value.GetString() : null;

    internal static string? OriginalCommand(JsonElement item)
    {
        // Verified 0.153.4 representation. Do not unwrap arbitrary shell strings or guess from output.
        if (!item.TryGetProperty("commandActions", out var actions) || actions.ValueKind != JsonValueKind.Array
            || actions.GetArrayLength() != 1 || Text(actions[0], "type") != "unknown") return null;
        return Text(actions[0], "command");
    }
}
