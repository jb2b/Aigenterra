using System.Text.Json;
using Aigenterra.Application.Recording;
using Aigenterra.Application.Runtime;

namespace Aigenterra.Adapters.Codex.Normalization;

public sealed class CodexLifecycleNormalizer : IRuntimeCaptureNormalizer
{
    public const string NormalizerVersion = "codex.lifecycle/1";
    public string Version => NormalizerVersion;
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = false };

    public RuntimeNormalization Normalize(RuntimeCaptureEnvelope capture)
    {
        if (capture.Source.Provider != "codex") return Unsupported("unsupported_provider");
        if (capture.Source.RuntimeVersion is not ("0.153.4" or "0.160.1" or "0.162.1")) return Unsupported("unverified_runtime_version");
        if (capture.MediaType != "application/json") return Unsupported("unsupported_media_type");
        try
        {
            using var doc = JsonDocument.Parse(capture.Payload);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException();
            var root = doc.RootElement;
            var facts = new List<RuntimeFact>();
            if (capture.Source is { SourceKind: "hook", EvidenceStage: "HookVisible", Provenance: "hook_stdin" })
                Hook(capture, root, facts);
            else if (capture.Source is { SourceKind: "app_server", EvidenceStage: "AppServerVisible", Provenance: "live_notification" })
                Notification(capture, root, facts);
            else if (capture.Source is { SourceKind: "app_server", EvidenceStage: "AppServerVisible", Provenance: "history_response" })
            {
                // Only self-identifying snapshots. A response ID alone never supplies a thread identity.
                if (Property(Property(root, "result"), "thread") is { ValueKind: JsonValueKind.Object } thread)
                    Thread(capture, thread, facts, "/result/thread", "Reconciled", null);
            }
            else return Unsupported("unsupported_source_stage_or_provenance");
            return facts.Count == 0 ? Unsupported("unsupported_event_or_missing_context") : new("normalized", null, facts);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        { return new("rejected", "invalid_lifecycle_payload", []); }
    }

    private static void Notification(RuntimeCaptureEnvelope capture, JsonElement root, List<RuntimeFact> facts)
    {
        // Server requests, even with a familiar method, are not notifications.
        if (root.TryGetProperty("id", out _)) return;
        var parameters = Property(root, "params");
        var emitted = Number(root, "emittedAtMs");
        switch (Text(root, "method"))
        {
            case "turn/started":
            case "turn/completed":
                var data = parameters.Deserialize<CodexTurnEvent>(Options) ?? throw new JsonException();
                Turn(capture, Id(data.ThreadId), data.Turn ?? throw new JsonException(), facts, "/params/turn", "Observed", emitted, "Unknown", null);
                break;
            case "thread/started":
                Thread(capture, Property(parameters, "thread"), facts, "/params/thread", "Observed", emitted);
                break;
            case "thread/status/changed":
                var status = Text(Property(parameters, "status"), "type");
                Add(capture, facts, "thread", Id(Text(parameters, "threadId")), null,
                    status == "idle" ? "Idle" : status == "active" ? "Running" : "Unknown", "Unknown", null,
                    "Runtime", "Observed", null, emitted, "/params", "/params/status/type");
                break;
        }
    }

    private static void Thread(RuntimeCaptureEnvelope capture, JsonElement element, List<RuntimeFact> facts,
        string pointer, string derivation, long? emitted)
    {
        var thread = element.Deserialize<CodexThread>(Options) ?? throw new JsonException();
        var id = Id(thread.Id);
        // Parent presence proves a child, explicit null proves root only for the tested direct-client source strings.
        var direct = thread.Source.ValueKind == JsonValueKind.String && thread.Source.GetString() is "cli" or "vscode";
        var scope = thread.ParentThreadId is not null || Property(thread.Source, "subAgent").ValueKind == JsonValueKind.Object ? "Child"
            : direct && element.TryGetProperty("parentThreadId", out var parent) && parent.ValueKind == JsonValueKind.Null ? "Root" : "Unknown";
        var context = scope == "Root" ? id : null;
        Add(capture, facts, "thread", id, null, Text(thread.Status, "type") == "idle" ? "Idle" : "Unknown",
            scope, context, "Runtime", derivation, null, emitted, pointer, pointer + "/status/type");
        foreach (var (turn, index) in (thread.Turns ?? []).Select((value, index) => (value, index)))
            Turn(capture, id, turn ?? throw new JsonException(), facts, pointer + "/turns/" + index, derivation, emitted, scope, context);
    }

    private static void Turn(RuntimeCaptureEnvelope capture, string threadId, CodexTurn turn, List<RuntimeFact> facts,
        string pointer, string derivation, long? emitted, string scope, string? context)
    {
        var status = turn.Status switch
        {
            "inProgress" => "Running",
            "completed" => "Completed",
            "failed" => "Failed",
            "interrupted" => "Interrupted",
            _ => "TerminalUnknown"
        };
        var time = status == "Running" ? turn.StartedAt : turn.CompletedAt;
        Add(capture, facts, "turn", threadId, Id(turn.Id), status, scope, context, "Runtime", derivation,
            time is null ? null : new(time.Value, "unix_seconds", status == "Running" ? "started" : "completed"),
            emitted, pointer, pointer + "/status");
    }

    private static void Hook(RuntimeCaptureEnvelope capture, JsonElement root, List<RuntimeFact> facts)
    {
        var name = Text(root, "hook_event_name");
        if (name is not ("SessionStart" or "UserPromptSubmit" or "Stop" or "Interrupt" or "SubagentStart" or "SubagentStop")) return;
        var conversation = Id(Text(root, "session_id"));
        var agent = Text(root, "agent_id");
        if (name is "SubagentStart" or "SubagentStop") agent = Id(agent);
        var owner = agent is null ? conversation : Id(agent);
        var scope = agent is null ? "Root" : "Child";
        if (name is "SessionStart" or "UserPromptSubmit" or "SubagentStart")
            Add(capture, facts, "thread", owner, null, "Unknown", scope, conversation, "Hook", "Observed", null, null, "", "/hook_event_name");
        if (name == "SessionStart") return;
        var turn = Id(Text(root, "turn_id"));
        if (name is "Stop" or "SubagentStop")
        {
            var active = Property(root, "stop_hook_active");
            if (active.ValueKind == JsonValueKind.True) return;
            if (active.ValueKind != JsonValueKind.False) throw new JsonException();
        }
        var status = name is "Stop" or "SubagentStop" ? "Completed" : name == "Interrupt" ? "Interrupted" : "Running";
        Add(capture, facts, "turn", owner, turn, status, scope, conversation, "Hook",
            name is "Stop" or "SubagentStop" ? "Inferred" : "Observed", null, null, "", "/hook_event_name");
    }

    private static void Add(RuntimeCaptureEnvelope capture, List<RuntimeFact> facts, string kind, string thread, string? turn,
        string status, string scope, string? conversation, string authority, string derivation, RuntimeSourceTime? time,
        long? emitted, string pointer, string statusPointer)
    {
        var threadPointer = authority == "Hook" ? (scope == "Child" ? "/agent_id" : "/session_id")
            : kind == "thread" && pointer != "/params" ? pointer + "/id"
            : pointer.StartsWith("/result/thread", StringComparison.Ordinal) ? "/result/thread/id" : "/params/threadId";
        facts.Add(new(capture.CaptureId, facts.Count, capture.Source.RuntimeNamespaceId, kind, thread, turn, status, scope,
            conversation, authority, derivation, time, emitted,
            [new("kind", authority == "Hook" ? "/hook_event_name" : pointer, derivation),
             new("threadId", threadPointer, "Observed"),
             new("turnId", authority == "Hook" ? "/turn_id" : pointer + "/id", turn is null ? "Unknown" : "Observed"),
             new("status", statusPointer, derivation),
             new("threadScope", authority == "Hook" ? threadPointer : pointer, scope == "Unknown" ? "Unknown" : derivation),
             new("conversationThreadId", authority == "Hook" ? "/session_id" : threadPointer, conversation is null ? "Unknown" : derivation),
             new("occurredAt", pointer + (status == "Running" ? "/startedAt" : "/completedAt"), time is null ? "Unknown" : derivation),
             new("sourceEmittedAtMilliseconds", "/emittedAtMs", emitted is null ? "Unknown" : "Observed")]));
    }

    private static RuntimeNormalization Unsupported(string reason) => new("unsupported", reason, []);
    private static string Id(string? id) => !string.IsNullOrWhiteSpace(id) && id.Length <= 512 ? id : throw new JsonException();
    private static JsonElement Property(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var property) ? property : default;
    private static string? Text(JsonElement value, string name) => Property(value, name) is { ValueKind: JsonValueKind.String } property ? property.GetString() : null;
    private static long? Number(JsonElement value, string name) => Property(value, name) is { ValueKind: JsonValueKind.Number } property && property.TryGetInt64(out var number) ? number : null;
}
