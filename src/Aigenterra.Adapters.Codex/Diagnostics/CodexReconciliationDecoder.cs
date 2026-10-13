using System.Text.Json;
using Aigenterra.Application.Recording;
using Aigenterra.Application.Runtime;

namespace Aigenterra.Adapters.Codex.Diagnostics;

public sealed class CodexReconciliationDecoder : IRuntimeReconciliationDecoder
{
    public RuntimeReconciliationInput? Decode(RuntimeCaptureEnvelope capture)
    {
        if (capture.Source is not { Provider: "codex", SourceKind: "diagnostic", EvidenceStage: "RecorderDiagnostic", Provenance: "reconciliation" }
            || capture.MediaType != "application/json") return null;
        try
        {
            var input = JsonSerializer.Deserialize<RuntimeReconciliationInput>(capture.Payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (input is not { Version: 1 } || input.ObservedAt.Offset != TimeSpan.Zero || input.Kind is not ("enrollment" or "cycle" or "gap")
                || string.IsNullOrWhiteSpace(input.Outcome) || input.Outcome.Length > 128 || input.ThreadId?.Length > 512 || input.NextCursor?.Length > 65536) return null;
            if (input.Kind == "enrollment" && (input.Enrollment is not { Fingerprint.Length: 64 } enrollment
                || enrollment.HookSourceIds is null || enrollment.ThreadIds is null || enrollment.HookSourceIds.Count > 32 || enrollment.ThreadIds.Count > 1000
                || enrollment.HookSourceIds.Any(id => id == Guid.Empty || id == capture.Source.SourceId)
                || enrollment.ThreadIds.Any(id => string.IsNullOrWhiteSpace(id) || id.Length > 512))) return null;
            return input;
        }
        catch (JsonException) { return null; }
    }
}
