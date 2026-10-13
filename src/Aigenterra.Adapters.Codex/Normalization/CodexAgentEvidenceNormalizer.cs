using System.Text.Json;
using Aigenterra.Application.Recording;
using Aigenterra.Application.Runtime;
using static Aigenterra.Adapters.Codex.Normalization.CodexEvidenceNormalizer;

namespace Aigenterra.Adapters.Codex.Normalization;

/// <summary>Version 2 adds identity/participation; version 1 activity interpretation remains available.</summary>
public sealed class CodexAgentEvidenceNormalizer : IRuntimeEvidenceNormalizer
{
    public const string NormalizerVersion = "codex.evidence/2";
    public string Version => NormalizerVersion;
    public RuntimeEvidenceBatch Normalize(RuntimeCaptureEnvelope capture, VerifiedRuntimeRequest? request)
    {
        var activity = new CodexEvidenceNormalizer().Normalize(capture, request);
        var lifecycle = new CodexHistoryNormalizer().Normalize(capture, request);
        var facts = activity.Facts.ToList();
        if (activity.Outcome == "rejected" || lifecycle.Status == "rejected")
            return new(capture.CaptureId, Version, "rejected", activity.Reason ?? lifecycle.Reason, []);
        foreach (var fact in lifecycle.Facts)
        {
            var values = new SortedDictionary<string, string?>(StringComparer.Ordinal)
            { ["conversationThreadId"] = fact.ConversationThreadId, ["scope"] = fact.ThreadScope };
            var fields = fact.Fields.Select(field => Locator(field)).ToArray();
            Add("agent", fact.ThreadId, null, null, "Unknown", fact.Authority, fact.Derivation, values, fields);
            if (fact.Kind == "turn") Add("segment", fact.ThreadId, fact.TurnId, null, fact.Status, fact.Authority, fact.Derivation, values, fields);
        }
        // Activity-only hooks still provide an explicit owner identity.
        foreach (var fact in activity.Facts)
            Add("agent", fact.ThreadId, null, null, "Unknown", fact.Authority, fact.Derivation,
                new SortedDictionary<string, string?>(StringComparer.Ordinal), fact.Fields.Where(f => f.Field == "threadId").ToArray());

        if (request is not null && capture.Source.Provider == "codex" && capture.Source.RuntimeVersion is "0.153.4" or "0.160.1" or "0.162.1"
            && capture.Source is { SourceKind: "app_server", EvidenceStage: "AppServerVisible", Provenance: "reader_frame" or "history_response" }
            && request.ResponseCaptureId == capture.CaptureId && request.RuntimeNamespaceId == capture.Source.RuntimeNamespaceId
            && request.SourceId == capture.Source.SourceId && request.ConnectionEpochId == capture.Source.ConnectionEpochId)
        {
            try
            {
                using var document = JsonDocument.Parse(capture.Payload);
                var result = document.RootElement.GetProperty("result");
                if (request.Method == "thread/read")
                {
                    var thread = result.GetProperty("thread");
                    if (Text(thread, "id") != request.ThreadId) throw new JsonException();
                    Add("agent", request.ThreadId, null, null, "Unknown", "Runtime", "Reconciled",
                        new SortedDictionary<string, string?>(StringComparer.Ordinal)
                        { ["nickname"] = Text(thread, "agentNickname"), ["role"] = Text(thread, "agentRole"), ["model"] = Text(thread, "model") },
                        [new("identity", capture.CaptureId, "/result/thread/id", "Reconciled"), new("metadata", capture.CaptureId, "/result/thread", "Reconciled")]);
                    if (OptionalId(Text(thread, "parentThreadId")) is { } parent)
                        Relation(parent, null, request.ThreadId, null, "parent_child", "/result/thread/parentThreadId");
                    Turns(Property(thread, "turns"), "/result/thread/turns");
                }
                else if (request.Method == "thread/turns/list") Turns(Property(result, "data"), "/result/data");
                else if (request.Method == "thread/items/list" && request.TurnId is not null) Items(Property(result, "data"), request.TurnId, "/result/data");
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException)
            { return new(capture.CaptureId, Version, "rejected", "invalid_agent_identity_payload", []); }
        }
        return new(capture.CaptureId, Version, facts.Count > 0 ? "normalized" : activity.Outcome, facts.Count > 0 ? null : activity.Reason, facts);

        void Turns(JsonElement turns, string pointer)
        {
            if (turns.ValueKind != JsonValueKind.Array) return;
            foreach (var (turn, index) in turns.EnumerateArray().Select((value, index) => (value, index)))
                Items(Property(turn, "items"), Id(Text(turn, "id")), pointer + "/" + index + "/items");
        }
        void Items(JsonElement items, string turn, string pointer)
        {
            if (items.ValueKind != JsonValueKind.Array) return;
            foreach (var (item, index) in items.EnumerateArray().Select((value, index) => (value, index)))
            {
                if (Text(item, "type") == "subAgentActivity" && OptionalId(Text(item, "agentThreadId")) is { } child)
                {
                    Relation(request!.ThreadId, null, child, null, "parent_child", pointer + "/" + index + "/agentThreadId");
                    Relation(request.ThreadId, turn, child, OptionalId(Text(item, "id")), "delegation", pointer + "/" + index);
                }
                // Empty collab receivers and rootTurnId do not prove child ownership or task assignment.
            }
        }
        void Relation(string parent, string? turn, string child, string? call, string kind, string pointer)
        {
            if (parent == child) return;
            Add("relation", parent, turn, call, "Unknown", "Runtime", "Reconciled",
                new SortedDictionary<string, string?>(StringComparer.Ordinal) { ["childThreadId"] = child, ["relationKind"] = kind },
                [new("relation", capture.CaptureId, pointer, "Reconciled"), new("requestContext", request!.ContextCaptureId, "", "Reconciled")]);
            Add("agent", child, null, null, "Unknown", "Runtime", "Reconciled",
                new SortedDictionary<string, string?>(StringComparer.Ordinal), [new("identity", capture.CaptureId, pointer, "Reconciled")]);
        }
        void Add(string kind, string thread, string? turn, string? external, string status, string authority, string derivation,
            IReadOnlyDictionary<string, string?> values, IReadOnlyList<RuntimeEvidenceField> fields) =>
            facts.Add(new(capture.CaptureId, facts.Count, capture.Source.RuntimeNamespaceId, kind, thread, turn, external, status, derivation, authority, values, fields));
        RuntimeEvidenceField Locator(RuntimeFieldEvidence field)
        {
            if (field.Pointer.StartsWith("capture:", StringComparison.Ordinal) && field.Pointer.IndexOf('#') is var marker && marker > 8
                && Guid.TryParse(field.Pointer.AsSpan(8, marker - 8), out var id))
                return new(field.Field, id, field.Pointer[(marker + 1)..], field.Derivation);
            return new(field.Field, capture.CaptureId, field.Pointer, field.Derivation);
        }
    }
}
