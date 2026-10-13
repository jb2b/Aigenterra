using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Aigenterra.Application.Common;
using Aigenterra.Application.Recording;
using Aigenterra.Application.Runtime;

namespace Aigenterra.Adapters.Codex.Diagnostics;

public sealed class CodexCapabilityDetector : IRuntimeHealthDecoder
{
    public const string DetectorVersion = "codex.health/1";
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly Dictionary<string, string[]> Schemas = new(StringComparer.Ordinal)
    {
        ["0.153.4"] = ["E8284C5CB8157554A3DD1E035AADBD4325AEA501AF56887E9C2E12EB1B9B9448", "B06F77062369D481A59CC70720C12B89CB9DD49C385863923262102D3AD6C978"],
        ["0.160.1"] = ["8683F6FD9FBB994FA2BFB704AB5B02C9890238FA507A4C0FF33FF45884CD0A2E", "7243BA241962AF92CA60581F1A81808EBDA4212A800F8B205F54703BCFD508C5"],
        ["0.162.1"] = ["71A94EAB0D0B27F2AAB30993097BC13D78314EB386801C105280B53F0963AF44", "E4E7F0C7D3FD77C48CD8619CAD2BF2B315D860AB006AD5A7EBE44F6E32E666C1"]
    };
    private static readonly Dictionary<string, string> Binaries = new(StringComparer.Ordinal)
    {
        ["0.160.1"] = "3B8F6E33CAA75F232558A3CF76FF9B87BB5EF6DBCF4996372F24E55C78B1B916",
        ["0.162.1"] = "DD13BDB162A184FA04039488B234A8258EF9B76F84E926C505D5742B19249A6A"
    };

    public UseCaseResult<RuntimeSourceHealth> Decode(RuntimeCaptureEnvelope capture)
    {
        if (capture.Source is not { Provider: "codex", SourceKind: "diagnostic", EvidenceStage: "RecorderDiagnostic", Provenance: "inventory" }
            || capture.MediaType != "application/json" || capture.Payload.Length > 256 * 1024) return Invalid("unsupported_health_capture");
        try
        {
            var inventory = JsonSerializer.Deserialize<CodexDiagnosticInventory>(capture.Payload, JsonOptions);
            if (inventory is null || inventory.Version != 1 || inventory.ObservedAt.Offset != TimeSpan.Zero || inventory.ObservedAt == default
                || inventory.Surface is not ("cli" or "desktop" or "unknown") || inventory.SurfaceEvidence is not ("declared" or "owner_verified")
                || inventory.ReconnectCount < 0 || inventory.Hooks?.Count > 200 || inventory.Features?.Count > 200
                || inventory.Capture is { Pending: < 0 } or { Retained: < 0 } or { Incomplete: < 0 }
                || inventory.LastFrameAt?.Offset != null && inventory.LastFrameAt.Value.Offset != TimeSpan.Zero
                || inventory.LastSuccessfulRequestAt?.Offset != null && inventory.LastSuccessfulRequestAt.Value.Offset != TimeSpan.Zero
                || !Text(inventory.ExecutableVersion, 128) || !Text(inventory.ReportedRuntimeVersion, 128)
                || !Text(inventory.ExecutablePath, 4096) || !Text(inventory.DesktopApplicationVersion, 128)
                || !Text(inventory.Model, 128) || !Text(inventory.ProbeFailure, 256)
                || !Hash(inventory.SchemaSha256) || !Hash(inventory.ExecutableSha256)
                || inventory.Hooks?.Any(h => h is null || string.IsNullOrWhiteSpace(h.Name) || h.Name.Length > 128
                    || !Hash(h.CurrentHash) || !Hash(h.ApprovedHash)) == true)
                return Invalid("invalid_health_inventory");
            return UseCaseResult<RuntimeSourceHealth>.Success(Detect(capture, inventory));
        }
        catch (JsonException) { return Invalid("invalid_health_inventory"); }
    }

    private static RuntimeSourceHealth Detect(RuntimeCaptureEnvelope capture, CodexDiagnosticInventory inventory)
    {
        var checks = new List<RuntimeHealthCheck>();
        var version = inventory.ExecutableVersion;
        var versionKnown = version is not null && Schemas.ContainsKey(version);
        var schemaMatches = versionKnown && Schemas[version!].Contains(inventory.SchemaSha256, StringComparer.Ordinal);
        var binaryMatches = version is not null && Binaries.TryGetValue(version, out var expectedBinary) && expectedBinary == inventory.ExecutableSha256;
        var versionMismatch = inventory.ReportedRuntimeVersion is { } reported && version is not null && reported != version;
        var supportedSurface = (inventory.Surface, version) is ("cli", "0.162.1") or ("desktop", "0.160.1");
        var match = schemaMatches && binaryMatches && supportedSurface && !versionMismatch
            && inventory.SurfaceEvidence == "owner_verified"
            && (inventory.Surface != "desktop" || inventory.DesktopApplicationVersion == "26.930.61225");
        checks.Add(new("runtime_version", versionKnown ? "PASS" : "WARN", versionKnown ? "Version has lifecycle fixtures; delivery depends on surface/profile." : "Runtime version is unknown or untested."));
        checks.Add(new("schema", schemaMatches ? "PASS" : inventory.SchemaSha256 is null ? "NOT_TESTED" : "WARN", "Exact schema bundle hash compared with recorded research profiles."));
        checks.Add(new("binary", binaryMatches ? "PASS" : inventory.ExecutableSha256 is null ? "NOT_TESTED" : "WARN", "Exact binary hash compared with the recorded build; a version string alone is insufficient."));
        checks.Add(new("reported_version", versionMismatch ? "WARN" : inventory.ReportedRuntimeVersion is null ? "NOT_TESTED" : "PASS", versionMismatch ? "Executable and reported runtime versions differ." : "Reported service version is separate from executable and Desktop application version."));
        checks.Add(new("surface", inventory.SurfaceEvidence == "owner_verified" && inventory.Surface != "unknown" ? "PASS" : "NOT_TESTED", "Frontend ownership is supplied evidence; userAgent/originator never establishes the surface."));
        checks.Add(new("connection", inventory.Connected == false ? "WARN" : inventory.Connected == true ? "PASS" : "NOT_TESTED", inventory.Connected == false ? "Source explicitly reported disconnected." : "Snapshot only; no connection or liveness probe was made by doctor."));
        checks.Add(new("passive_live", "NOT_TESTED", "No production passive transport is enabled. CLI root turn/item coverage and Desktop live attachment remain unproven."));
        checks.Add(new("approval_routing", "NOT_TESTED", "Doctor does not attach, approve, resume or make runtime decisions."));
        checks.Add(new("existing_root_turn_item_delivery", "NOT_TESTED", "Normal CLI did not deliver these events in revalidation; Desktop had no same-runtime route."));
        checks.Add(new("child_reconnect_coverage", "NOT_TESTED", "CLI delivered new-child events while connected, but lost existing-child turn/item coverage after reconnect."));
        checks.Add(new("features", inventory.Features is null ? "NOT_TESTED" : "PASS", "Supplied feature inventory; enabled flags do not establish event delivery."));
        if (inventory.ProbeFailure is not null) checks.Add(new("inventory_probe", "WARN", inventory.ProbeFailure));
        if (inventory.Capture is { KnownCaptureFailure: true } or { Retained: > 0 } or { Incomplete: > 0 })
            checks.Add(new("capture", "WARN", "Capture failures or retained/incomplete local evidence require inspection."));
        else checks.Add(new("capture", "NOT_TESTED", "Spool counters do not prove complete hook coverage; idle traffic is not a failure."));
        var hooks = (inventory.Hooks ?? []).Select(h =>
        {
            var modified = h.TrustStatus == "modified" || h.CurrentHash is not null && h.ApprovedHash is not null && h.CurrentHash != h.ApprovedHash;
            var trust = modified ? "Modified" : h.TrustStatus switch { "trusted" => "Trusted", "untrusted" => "Untrusted", _ => "Unknown" };
            checks.Add(new("hook:" + h.Name, h.Enabled == false || trust is "Untrusted" or "Modified" ? "WARN" : "NOT_TESTED",
                "Effective inventory snapshot; enabled/trusted does not prove invocation or result completeness."));
            return new RuntimeHookHealth(h.Name, h.Configured, h.Enabled, trust, h.CurrentHash, h.ApprovedHash);
        }).ToArray();
        if (hooks.Length == 0) checks.Add(new("hooks", "NOT_TESTED", "No effective hooks inventory supplied; on-disk configuration is not effective trust."));
        var profileId = $"codex-research-20261013/{inventory.Surface}/{version ?? "unknown"}";
        var classification = match && inventory.Surface == "desktop" ? "PASSIVE_HISTORY_ONLY" : "UNKNOWN";
        var profileMaterial = $"{DetectorVersion}|{profileId}|{inventory.SchemaSha256}|{inventory.ExecutableSha256}|{inventory.DesktopApplicationVersion}|{inventory.SurfaceEvidence}|{match}|{classification}|root-live-unproven|child-reconnect-gap|approval-unproven";
        var profileHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(profileMaterial)));
        return new(capture.CaptureId, capture.Source.RuntimeNamespaceId, capture.Source.SourceId, capture.Source.RuntimeInstanceId,
            inventory.ObservedAt, DetectorVersion,
            new(inventory.ExecutablePath, version, inventory.ReportedRuntimeVersion, inventory.ExecutableSha256,
                inventory.DesktopApplicationVersion, inventory.Surface, inventory.SurfaceEvidence, inventory.Model),
            new(profileId, profileHash, inventory.SchemaSha256, match ? "RecordedProfileMatch" : "Unverified",
                classification, match ? "ObservedInResearch" : "Unknown", "Partial", inventory.Features),
            inventory.Connected switch { true => "Connected", false => "Disconnected", null => "Unknown" },
            inventory.LastFrameAt, inventory.LastSuccessfulRequestAt, inventory.ReconnectCount,
            inventory.Capture ?? new(null, null, null, null), "Unknown", hooks, checks);
    }

    private static bool Hash(string? value) => value is null || value.Length == 64 && value.All(c => c is >= '0' and <= '9' or >= 'A' and <= 'F');
    private static bool Text(string? value, int maximum) => value is null || value.Length <= maximum;
    private static UseCaseResult<RuntimeSourceHealth> Invalid(string code) =>
        UseCaseResult<RuntimeSourceHealth>.Failure(ApplicationErrorType.Validation, code, "Health inventory is unsupported or invalid; original capture remains archived.");
}
