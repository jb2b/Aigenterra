using System.Security.Cryptography;
using System.Text.Json;
using Aigenterra.Adapters.Codex.Diagnostics;
using Aigenterra.Application.Recording;
using Aigenterra.Application.Runtime;

namespace Aigenterra.Collector;

public static class CodexDoctorCommand
{
    public static async Task<int> ExecuteAsync(string[] arguments, TextWriter output, TextWriter error, CaptureSpool spool,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var options = new Dictionary<string, string>(StringComparer.Ordinal);
            var record = false;
            for (var index = 0; index < arguments.Length; index++)
            {
                var option = arguments[index];
                if (option == "--record") { record = true; continue; }
                if (option is not ("--executable" or "--inventory" or "--schema" or "--surface" or "--namespace" or "--source" or "--instance")
                    || ++index >= arguments.Length || !options.TryAdd(option, arguments[index])) throw new ArgumentException("Invalid doctor options.");
            }
            if (!options.TryGetValue("--executable", out var executable)) throw new ArgumentException("--executable is required.");
            // Ephemeral IDs label stdout only; persistent enrollment is mandatory for --record.
            var runtimeNamespace = Enrollment("--namespace", record);
            var sourceId = Enrollment("--source", record);
            Guid? instance = options.ContainsKey("--instance") ? Enrollment("--instance", true) : null;
            var inventory = await CodexDoctorProbe.ReadAsync(executable, options.GetValueOrDefault("--inventory"),
                options.GetValueOrDefault("--schema"), options.GetValueOrDefault("--surface"), cancellationToken);
            if (record)
            {
                var status = JsonSerializer.SerializeToElement(spool.Status());
                inventory = inventory with
                {
                    Capture = new RuntimeCaptureHealth(status.GetProperty("pending").GetInt32(),
                    status.GetProperty("retained").GetInt32(), status.GetProperty("incomplete").GetInt32(), status.GetProperty("captureFailures").GetBoolean())
                };
            }
            var payload = JsonSerializer.SerializeToUtf8Bytes(inventory, CodexCapabilityDetector.JsonOptions);
            var now = DateTimeOffset.UtcNow;
            var source = new RuntimeCaptureSource(runtimeNamespace, sourceId, Guid.NewGuid(), "codex", "diagnostic", "RecorderDiagnostic", "inventory", instance, inventory.ExecutableVersion);
            var envelope = new RuntimeCaptureEnvelope(2, Guid.NewGuid(), now, now, source, "application/json", payload.Length,
                Convert.ToHexString(SHA256.HashData(payload)), payload);
            var detected = new CodexCapabilityDetector().Decode(envelope);
            if (!detected.IsSuccess) throw new InvalidDataException("Invalid inventory fields.");
            Guid? delivery = null;
            if (record) delivery = await spool.CaptureRuntimeAsync(new MemoryStream(payload), now, new NoRepositoryContext(), source, cancellationToken);
            await output.WriteLineAsync(JsonSerializer.Serialize(new
            {
                overall = "PARTIAL",
                recordedCaptureId = delivery,
                health = detected.Value! with { CaptureId = delivery ?? envelope.CaptureId }
            }, CodexCapabilityDetector.JsonOptions));
            return 0;

            Guid Enrollment(string key, bool required)
            {
                if (options.TryGetValue(key, out var value) && Guid.TryParse(value, out var id) && id != Guid.Empty) return id;
                if (required || options.ContainsKey(key)) throw new ArgumentException(key + " requires a nonempty UUID.");
                return Guid.NewGuid();
            }
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException or JsonException)
        {
            await error.WriteLineAsync("Doctor inventory failed; verify options, input files and local access. No runtime configuration was changed.");
            return 1;
        }
    }

    private sealed class NoRepositoryContext : IRepositoryContextReader
    {
        public Task<RepositoryContext> ReadAsync(string workingDirectory, CancellationToken cancellationToken = default) => throw new InvalidOperationException();
    }
}
