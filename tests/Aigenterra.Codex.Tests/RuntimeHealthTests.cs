using System.Security.Cryptography;
using System.Text.Json;
using Aigenterra.Adapters.Codex.Diagnostics;
using Aigenterra.Application.Recording;
using Aigenterra.Collector;

namespace Aigenterra.Codex.Tests;

public sealed class RuntimeHealthTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "aigenterra-doctor-" + Guid.NewGuid().ToString("N"));
    private readonly CodexCapabilityDetector detector = new();

    [Fact]
    public void Enabled_untrusted_modified_disconnected_and_version_mismatch_are_independent_visible_checks()
    {
        var inventory = new CodexDiagnosticInventory(1, DateTimeOffset.UtcNow, ExecutableVersion: "0.162.1", ReportedRuntimeVersion: "0.160.1", Connected: false,
            Hooks: [new("pre", true, true, "untrusted"), new("post", true, true, "trusted", new string('A', 64), new string('B', 64)), new("stop", true, false, "trusted")]);
        var health = detector.Decode(Capture(inventory)).Value!;
        Assert.Equal("Disconnected", health.ConnectionState);
        Assert.Contains(health.Checks, check => check.Capability == "reported_version" && check.Status == "WARN");
        Assert.True(health.Hooks[0].Enabled);
        Assert.Equal("Untrusted", health.Hooks[0].Trust);
        Assert.Equal("Modified", health.Hooks[1].Trust);
        Assert.False(health.Hooks[2].Enabled);
        Assert.Equal("Unknown", health.ProjectionState);
        Assert.Equal(64, health.Profile.ProfileSha256.Length);
    }

    [Theory]
    [InlineData("0.162.1", "cli", "UNKNOWN", "71A94EAB0D0B27F2AAB30993097BC13D78314EB386801C105280B53F0963AF44", "DD13BDB162A184FA04039488B234A8258EF9B76F84E926C505D5742B19249A6A")]
    [InlineData("0.160.1", "desktop", "PASSIVE_HISTORY_ONLY", "8683F6FD9FBB994FA2BFB704AB5B02C9890238FA507A4C0FF33FF45884CD0A2E", "3B8F6E33CAA75F232558A3CF76FF9B87BB5EF6DBCF4996372F24E55C78B1B916")]
    public void Exact_research_profiles_preserve_surface_limits_and_binary_changes_invalidate_verification(string version, string surface,
        string classification, string schema, string binary)
    {
        var inventory = new CodexDiagnosticInventory(1, DateTimeOffset.UtcNow, Surface: surface, SurfaceEvidence: "owner_verified", ExecutableVersion: version,
            ExecutableSha256: binary, SchemaSha256: schema, DesktopApplicationVersion: surface == "desktop" ? "26.930.61225" : null);
        var health = detector.Decode(Capture(inventory)).Value!;
        Assert.Equal(classification, health.Profile.PassiveLive);
        Assert.Equal("RecordedProfileMatch", health.Profile.Compatibility);
        Assert.Equal(schema, health.Profile.SchemaSha256);
        Assert.Equal("NOT_TESTED", Assert.Single(health.Checks, x => x.Capability == "passive_live").Status);
        var changed = detector.Decode(Capture(inventory with { ExecutableSha256 = new string('A', 64) })).Value!;
        Assert.Equal("Unverified", changed.Profile.Compatibility);
        Assert.Equal("UNKNOWN", changed.Profile.PassiveLive);
        Assert.NotEqual(health.Profile.ProfileSha256, changed.Profile.ProfileSha256);
        Assert.Equal("Unverified", detector.Decode(Capture(inventory with { SchemaSha256 = new string('B', 64) })).Value!.Profile.Compatibility);
        Assert.Equal("Unverified", detector.Decode(Capture(inventory with { SurfaceEvidence = "declared" })).Value!.Profile.Compatibility);
        if (surface == "desktop")
            Assert.Equal("Unverified", detector.Decode(Capture(inventory with { DesktopApplicationVersion = "future" })).Value!.Profile.Compatibility);
    }

    [Fact]
    public void Old_traffic_is_not_failure_and_absent_inventory_is_not_trusted_or_connected()
    {
        var idle = detector.Decode(Capture(new(1, DateTimeOffset.UtcNow, Connected: true, LastFrameAt: DateTimeOffset.UtcNow.AddDays(-10)))).Value!;
        Assert.Equal("Connected", idle.ConnectionState);
        Assert.DoesNotContain(idle.Checks, check => check.Capability == "connection" && check.Status is "WARN" or "FAIL");
        var unknown = detector.Decode(Capture(new(1, DateTimeOffset.UtcNow))).Value!;
        Assert.Equal("Unknown", unknown.ConnectionState);
        Assert.Empty(unknown.Hooks);
        Assert.Equal("Unverified", unknown.Profile.Compatibility);
        Assert.Null(unknown.Capture.KnownCaptureFailure);
    }

    [Fact]
    public async Task Doctor_inventory_is_opt_in_reads_only_and_rejects_control_options_before_execution()
    {
        var spool = new CaptureSpool(root);
        using var output = new StringWriter();
        using var errors = new StringWriter();
        var dotnet = Path.GetFullPath(Path.Combine(System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", "..",
            OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet"));
        Assert.Equal(0, await CodexDoctorCommand.ExecuteAsync(["--executable", dotnet], output, errors, spool));
        Assert.False(Directory.Exists(root));
        using var report = JsonDocument.Parse(output.ToString());
        var binary = report.RootElement.GetProperty("health").GetProperty("binary");
        Assert.Equal(64, binary.GetProperty("executableSha256").GetString()!.Length);
        Assert.Equal(JsonValueKind.Null, binary.GetProperty("executableVersion").ValueKind); // dotnet output is not a Codex version.
        Assert.Equal(1, await CodexDoctorCommand.ExecuteAsync(["--executable", dotnet, "--resume", "anything"], output, errors, spool));
        Assert.False(Directory.Exists(root));
        Assert.Equal(1, await CodexDoctorCommand.ExecuteAsync(["--executable", dotnet, "--record"], output, errors, spool));
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public async Task Explicit_record_spools_diagnostics_with_enrollment_without_contacting_api_or_runtime()
    {
        var ns = Guid.NewGuid();
        var source = Guid.NewGuid();
        using var output = new StringWriter();
        Assert.Equal(0, await CodexDoctorCommand.ExecuteAsync(["--executable", Path.Combine(root, "missing-codex.exe"), "--record",
            "--namespace", ns.ToString(), "--source", source.ToString()], output, TextWriter.Null, new CaptureSpool(root)));
        var pending = Assert.Single(Directory.GetDirectories(Path.Combine(root, "pending")));
        var metadata = JsonSerializer.Deserialize<RuntimeCaptureEnvelope>(await File.ReadAllBytesAsync(Path.Combine(pending, "record.json")), CaptureSpool.JsonOptions)!;
        var payload = await File.ReadAllBytesAsync(Path.Combine(pending, "payload.bin"));
        Assert.Equal(ns, metadata.Source.RuntimeNamespaceId);
        Assert.Equal(source, metadata.Source.SourceId);
        Assert.Equal("RecorderDiagnostic", metadata.Source.EvidenceStage);
        var health = detector.Decode(metadata with { Payload = payload }).Value!;
        Assert.Contains(health.Checks, check => check.Detail == "executable_unavailable");
        Assert.Equal("Unknown", health.ConnectionState);
    }

    [Theory]
    [InlineData("{\"version\":1,\"observedAt\":\"2026-10-13T00:00:00Z\",\"executableSha256\":\"invalid\"}")]
    [InlineData("{\"version\":1}")]
    [InlineData("{\"version\":1,\"observedAt\":\"2026-10-13T00:00:00Z\",\"hooks\":[null]}")]
    public void Malformed_diagnostics_are_rejected(string json)
    {
        var capture = Capture(new(1, DateTimeOffset.UtcNow)) with { Payload = System.Text.Encoding.UTF8.GetBytes(json) };
        Assert.False(detector.Decode(capture).IsSuccess);
    }

    internal static RuntimeCaptureEnvelope Capture(CodexDiagnosticInventory inventory)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(inventory, CodexCapabilityDetector.JsonOptions);
        return new(2, Guid.NewGuid(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
            new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "codex", "diagnostic", "RecorderDiagnostic", "inventory", Guid.NewGuid()),
            "application/json", payload.Length, Convert.ToHexString(SHA256.HashData(payload)), payload);
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
}
