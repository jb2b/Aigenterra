using Aigenterra.Application.Runtime;

namespace Aigenterra.Adapters.Codex.Diagnostics;

/// <summary>Offline inventory contract, not a Codex control RPC. Unknown values remain null.</summary>
public sealed record CodexDiagnosticInventory(
    int Version,
    DateTimeOffset ObservedAt,
    string Surface = "unknown",
    string SurfaceEvidence = "declared",
    string? ExecutablePath = null,
    string? ExecutableVersion = null,
    string? ReportedRuntimeVersion = null,
    string? ExecutableSha256 = null,
    string? SchemaSha256 = null,
    string? DesktopApplicationVersion = null,
    string? Model = null,
    bool? Connected = null,
    DateTimeOffset? LastFrameAt = null,
    DateTimeOffset? LastSuccessfulRequestAt = null,
    int? ReconnectCount = null,
    RuntimeCaptureHealth? Capture = null,
    IReadOnlyList<CodexHookInventory>? Hooks = null,
    IReadOnlyDictionary<string, bool>? Features = null,
    string? ProbeFailure = null);

public sealed record CodexHookInventory(string Name, bool? Configured = null, bool? Enabled = null,
    string? TrustStatus = null, string? CurrentHash = null, string? ApprovedHash = null);
