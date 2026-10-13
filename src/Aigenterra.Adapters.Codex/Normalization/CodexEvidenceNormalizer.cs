using System.Text.Json;
using Aigenterra.Application.Recording;
using Aigenterra.Application.Runtime;

namespace Aigenterra.Adapters.Codex.Normalization;

public sealed class CodexEvidenceNormalizer : IRuntimeEvidenceNormalizer
{
    public const string NormalizerVersion = "codex.evidence/1";
    public string Version => NormalizerVersion;
    public RuntimeEvidenceBatch Normalize(RuntimeCaptureEnvelope capture, VerifiedRuntimeRequest? request)
    {
        var facts = new List<RuntimeEvidenceFact>();
        if (capture.Source.Provider != "codex" || capture.MediaType != "application/json"
            || capture.Source.RuntimeVersion is not ("0.153.4" or "0.160.1" or "0.162.1")) return Result("unsupported", "unverified_provider_version_or_media");
        try
        {
            using var document = JsonDocument.Parse(capture.Payload);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw new JsonException();
            if (capture.Source is { SourceKind: "hook", EvidenceStage: "HookVisible", Provenance: "hook_stdin" }) Hook(root);
            else if (capture.Source is { SourceKind: "app_server", EvidenceStage: "AppServerVisible", Provenance: "history_response" or "reader_frame" }
                && request is not null && request.ResponseCaptureId == capture.CaptureId && request.RuntimeNamespaceId == capture.Source.RuntimeNamespaceId
                && request.SourceId == capture.Source.SourceId && request.ConnectionEpochId == capture.Source.ConnectionEpochId)
            {
                if (root.TryGetProperty("error", out _)) return Result("rejected", "history_rpc_error");
                var result = root.GetProperty("result");
                if (request.Method == "thread/read")
                {
                    var thread = result.GetProperty("thread");
                    if (Text(thread, "id") != request.ThreadId) throw new JsonException();
                    Turns(Property(thread, "turns"), "/result/thread/turns");
                }
                else if (request.Method == "thread/turns/list") Turns(Property(result, "data"), "/result/data");
                else if (request.Method == "thread/items/list" && request.TurnId is not null)
                    Items(Property(result, "data"), request.ThreadId, request.TurnId, "/result/data");
            }
            return Result(facts.Count == 0 ? "unsupported" : "normalized", facts.Count == 0 ? "no_supported_activity" : null);

            void Turns(JsonElement turns, string pointer)
            {
                if (turns.ValueKind != JsonValueKind.Array) return;
                foreach (var (turn, index) in turns.EnumerateArray().Select((value, index) => (value, index)))
                    Items(Property(turn, "items"), request!.ThreadId, Id(Text(turn, "id")), pointer + "/" + index + "/items");
            }
            void Items(JsonElement items, string thread, string turn, string pointer)
            {
                if (items.ValueKind != JsonValueKind.Array) return;
                foreach (var (item, index) in items.EnumerateArray().Select((value, index) => (value, index)))
                {
                    var kind = Text(item, "type");
                    if (kind is not ("mcpToolCall" or "commandExecution" or "fileChange")) continue;
                    var status = Text(item, "status") switch
                    { "completed" => "Completed", "failed" or "declined" => "Failed", "inProgress" => "Running", "interrupted" => "Interrupted", _ => "TerminalUnknown" };
                    Add(thread, turn, OptionalId(Text(item, "id")), kind, Text(item, "server"), Text(item, "tool"), status,
                        "Reconciled", "Runtime", pointer + "/" + index, pointer + "/" + index + "/status");
                }
            }
            void Hook(JsonElement hook)
            {
                var name = Text(hook, "hook_event_name");
                if (name is not ("PreToolUse" or "PostToolUse")) return;
                var thread = Id(Text(hook, "agent_id") ?? Text(hook, "session_id"));
                var tool = Text(hook, "tool_name");
                var parts = tool?.Split("__", StringSplitOptions.None);
                var isMcp = parts is { Length: 3 } && parts[0] == "mcp";
                var status = name == "PreToolUse" ? "Requested" : "ResultObserved";
                var response = Property(hook, "tool_response");
                if (name == "PostToolUse" && Property(response, "isError").ValueKind is JsonValueKind.True or JsonValueKind.False)
                    status = response.GetProperty("isError").GetBoolean() ? "Failed" : "Completed";
                Add(thread, OptionalId(Text(hook, "turn_id")), OptionalId(Text(hook, "tool_use_id")), isMcp ? "mcpToolCall" : tool is "Bash" ? "commandExecution" : "Unknown",
                    isMcp ? parts![1] : null, isMcp ? parts![2] : tool, status, "Observed", "Hook", "", status is "Completed" or "Failed" ? "/tool_response/isError" : "/hook_event_name");
            }
            void Add(string thread, string? turn, string? item, string kind, string? server, string? tool, string status,
                string derivation, string authority, string pointer, string statusPointer)
            {
                var fields = new List<RuntimeEvidenceField>
                {
                    new("itemId", capture.CaptureId, authority == "Hook" ? "/tool_use_id" : pointer + "/id", item is null ? "Unknown" : derivation),
                    new("status", capture.CaptureId, statusPointer, derivation),
                    new("input", capture.CaptureId, authority == "Hook" ? "/tool_input" : pointer, derivation),
                    new("output", capture.CaptureId, authority == "Hook" ? "/tool_response" : pointer, derivation)
                };
                fields.Add(new("threadId", authority == "Hook" ? capture.CaptureId : request!.RequestCaptureId,
                    authority == "Hook" ? Text(root, "agent_id") is null ? "/session_id" : "/agent_id" : "/params/threadId", derivation));
                if (request is not null) fields.Add(new("requestContext", request.ContextCaptureId, "", "Reconciled"));
                facts.Add(new(capture.CaptureId, facts.Count, capture.Source.RuntimeNamespaceId, "activity", thread, turn, item, status, derivation, authority,
                    new SortedDictionary<string, string?>(StringComparer.Ordinal) { ["activityKind"] = kind, ["server"] = server, ["tool"] = tool, ["evidenceStage"] = capture.Source.EvidenceStage }, fields));
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException)
        { return new(capture.CaptureId, Version, "rejected", "invalid_activity_payload", []); }
        RuntimeEvidenceBatch Result(string outcome, string? reason) => new(capture.CaptureId, Version, outcome, reason, facts);
    }

    internal static JsonElement Property(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var p) ? p : default;
    internal static string? Text(JsonElement value, string name) => Property(value, name) is { ValueKind: JsonValueKind.String } p ? p.GetString() : null;
    internal static string Id(string? value) => OptionalId(value) ?? throw new JsonException("Missing explicit identity.");
    internal static string? OptionalId(string? value) => string.IsNullOrWhiteSpace(value) || value.Length > 512 ? null : value;
}
