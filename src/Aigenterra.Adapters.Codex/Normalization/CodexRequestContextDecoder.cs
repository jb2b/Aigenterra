using System.Text.Json;
using Aigenterra.Application.Recording;
using Aigenterra.Application.Runtime;

namespace Aigenterra.Adapters.Codex.Normalization;

public sealed class CodexRequestContextDecoder : IRuntimeRequestContextDecoder
{
    public RuntimeRequestContext? Read(RuntimeCaptureEnvelope context)
    {
        try { return JsonSerializer.Deserialize<RuntimeRequestContext>(context.Payload, new JsonSerializerOptions(JsonSerializerDefaults.Web)); }
        catch (JsonException) { return null; }
    }

    public VerifiedRuntimeRequest? Decode(RuntimeCaptureEnvelope context, RuntimeCaptureEnvelope request, RuntimeCaptureEnvelope response)
    {
        if (context.Source.Provider != "codex" || request.Source.Provider != "codex" || response.Source.Provider != "codex"
            || context.Source is not { SourceKind: "diagnostic", EvidenceStage: "RecorderDiagnostic", Provenance: "request_context" }
            || request.Source is not { SourceKind: "app_server", EvidenceStage: "RecorderDiagnostic", Provenance: "history_request" }
            || response.Source is not { SourceKind: "app_server", EvidenceStage: "AppServerVisible", Provenance: "history_response" }
            || !SameScope(context.Source, request.Source) || !SameScope(context.Source, response.Source)) return null;
        try
        {
            var link = JsonSerializer.Deserialize<RuntimeRequestContext>(context.Payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (link is not { Version: 1 } || link.RequestCaptureId != request.CaptureId || link.ResponseCaptureId != response.CaptureId || link.CycleId == Guid.Empty) return null;
            using var sent = JsonDocument.Parse(request.Payload);
            using var received = JsonDocument.Parse(response.Payload);
            var a = sent.RootElement;
            var b = received.RootElement;
            if (!a.TryGetProperty("id", out var id) || id.ValueKind is not (JsonValueKind.Number or JsonValueKind.String)
                || !b.TryGetProperty("id", out var replyId) || id.GetRawText() != replyId.GetRawText()
                || b.TryGetProperty("method", out _) || (!b.TryGetProperty("result", out _) && !b.TryGetProperty("error", out _))) return null;
            var method = a.GetProperty("method").GetString();
            if (method is not ("thread/read" or "thread/turns/list" or "thread/items/list")) return null;
            var parameters = a.GetProperty("params");
            var thread = parameters.GetProperty("threadId").GetString();
            if (string.IsNullOrWhiteSpace(thread) || thread.Length > 512) return null;
            var turn = parameters.TryGetProperty("turnId", out var t) ? t.GetString() : null;
            var cursor = parameters.TryGetProperty("cursor", out var c) && c.ValueKind != JsonValueKind.Null ? c.GetRawText() : null;
            return new(context.CaptureId, request.CaptureId, response.CaptureId, context.Source.RuntimeNamespaceId,
                context.Source.SourceId, context.Source.ConnectionEpochId, link.CycleId, method, thread, turn, cursor);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException) { return null; }
    }

    private static bool SameScope(RuntimeCaptureSource a, RuntimeCaptureSource b) => a.RuntimeNamespaceId == b.RuntimeNamespaceId
        && a.SourceId == b.SourceId && a.ConnectionEpochId == b.ConnectionEpochId && a.RuntimeVersion == b.RuntimeVersion;
}
