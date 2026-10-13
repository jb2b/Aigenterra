using System.Text.Json;
using Aigenterra.Application.Recording;
using Aigenterra.Application.Runtime;

namespace Aigenterra.Adapters.Codex.Normalization;

public sealed class CodexHistoryNormalizer : IContextualRuntimeNormalizer
{
    public const string NormalizerVersion = "codex.history/1";
    public string Version => NormalizerVersion;
    public bool RequiresRequestContext(RuntimeCaptureEnvelope capture)
    {
        if (capture.Source.Provenance != "reader_frame") return false;
        try
        {
            using var document = JsonDocument.Parse(capture.Payload);
            var root = document.RootElement;
            return root.ValueKind == JsonValueKind.Object && !root.TryGetProperty("method", out _)
                && root.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number && id.TryGetInt32(out var number) && number > 1;
        }
        catch (JsonException) { return false; }
    }

    public RuntimeNormalization Normalize(RuntimeCaptureEnvelope capture) => Normalize(capture, null);
    public RuntimeNormalization Normalize(RuntimeCaptureEnvelope capture, VerifiedRuntimeRequest? request)
    {
        if (request is null) return new CodexLifecycleNormalizer().Normalize(capture);
        if (capture.Source.Provider != "codex" || capture.Source.RuntimeVersion is not ("0.153.4" or "0.160.1" or "0.162.1")
            || request.ResponseCaptureId != capture.CaptureId || request.RuntimeNamespaceId != capture.Source.RuntimeNamespaceId
            || request.SourceId != capture.Source.SourceId || request.ConnectionEpochId != capture.Source.ConnectionEpochId)
            return new("unsupported", "unverified_history_context_or_version", []);
        try
        {
            using var document = JsonDocument.Parse(capture.Payload);
            if (document.RootElement.TryGetProperty("error", out _)) return new("rejected", "history_rpc_error", []);
            var result = document.RootElement.GetProperty("result");
            if (request.Method == "thread/read")
            {
                if (result.GetProperty("thread").GetProperty("id").GetString() != request.ThreadId) throw new JsonException("Thread identity conflict.");
                return new CodexLifecycleNormalizer().Normalize(capture with { Source = capture.Source with { Provenance = "history_response" } });
            }
            if (request.Method != "thread/turns/list") return new("unsupported", "history_item_decoder_pending", []);
            var data = result.GetProperty("data");
            if (data.ValueKind != JsonValueKind.Array) throw new JsonException();
            var facts = new List<RuntimeFact>();
            foreach (var turn in data.EnumerateArray())
            {
                var id = turn.GetProperty("id").GetString();
                if (string.IsNullOrWhiteSpace(id) || id.Length > 512) throw new JsonException();
                var status = turn.GetProperty("status").GetString() switch
                {
                    "inProgress" => "Running",
                    "completed" => "Completed",
                    "failed" => "Failed",
                    "interrupted" => "Interrupted",
                    _ => "TerminalUnknown"
                };
                var pointer = "/result/data/" + facts.Count;
                facts.Add(new(capture.CaptureId, facts.Count, capture.Source.RuntimeNamespaceId, "turn", request.ThreadId, id,
                    status, "Unknown", null, "Runtime", "Reconciled", null, null,
                    [new("threadId", "capture:" + request.RequestCaptureId + "#/params/threadId", "Reconciled"),
                     new("requestContext", "capture:" + request.ContextCaptureId + "#", "Reconciled"),
                     new("turnId", pointer + "/id", "Reconciled"), new("status", pointer + "/status", "Reconciled")]));
            }
            return new("normalized", null, facts);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException)
        { return new("rejected", "invalid_history_payload", []); }
    }
}
