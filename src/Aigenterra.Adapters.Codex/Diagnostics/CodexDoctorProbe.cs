using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Aigenterra.Adapters.Codex.Diagnostics;

/// <summary>Opt-in local inventory only. The sole executable argument is --version; no daemon, RPC or config writes.</summary>
public static class CodexDoctorProbe
{
    public static async Task<CodexDiagnosticInventory> ReadAsync(string executable, string? inventoryPath = null,
        string? schemaPath = null, string? surface = null, CancellationToken cancellationToken = default)
    {
        CodexDiagnosticInventory inventory;
        if (inventoryPath is null) inventory = new(1, DateTimeOffset.UtcNow);
        else
        {
            await using var input = File.OpenRead(inventoryPath);
            if (input.Length > 256 * 1024) throw new InvalidDataException("Inventory exceeds 256 KiB.");
            inventory = await JsonSerializer.DeserializeAsync<CodexDiagnosticInventory>(input, CodexCapabilityDetector.JsonOptions, cancellationToken)
                ?? throw new InvalidDataException("Invalid inventory.");
        }
        var path = Path.GetFullPath(executable);
        inventory = inventory with
        {
            ExecutablePath = path,
            Surface = surface ?? inventory.Surface,
            SurfaceEvidence = surface is null || surface == inventory.Surface ? inventory.SurfaceEvidence : "declared"
        };
        if (schemaPath is not null)
        {
            await using var schema = File.OpenRead(schemaPath);
            inventory = inventory with { SchemaSha256 = Convert.ToHexString(await SHA256.HashDataAsync(schema, cancellationToken)) };
        }
        try
        {
            await using var binary = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var binaryHash = Convert.ToHexString(await SHA256.HashDataAsync(binary, cancellationToken));
            var start = new ProcessStartInfo(path)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                WorkingDirectory = Path.GetDirectoryName(path)!
            };
            start.ArgumentList.Add("--version");
            using var process = Process.Start(start) ?? throw new IOException("Version probe did not start.");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            var output = ReadBoundedAsync(process.StandardOutput, timeout.Token);
            var error = ReadBoundedAsync(process.StandardError, timeout.Token);
            process.StandardInput.Close();
            try
            {
                await process.WaitForExitAsync(timeout.Token);
                var text = await output;
                await error; // Do not copy potentially sensitive diagnostics into health logs.
                var match = Regex.Match(text.Trim(), "^codex-cli ([0-9]+\\.[0-9]+\\.[0-9]+)$", RegexOptions.CultureInvariant);
                return inventory with
                {
                    ExecutableSha256 = binaryHash,
                    ExecutableVersion = match.Success ? match.Groups[1].Value : null,
                    ProbeFailure = process.ExitCode != 0 ? "version_probe_failed" : match.Success ? null : "unrecognized_version_output"
                };
            }
            finally
            {
                if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(CancellationToken.None); }
                try { await Task.WhenAll(output, error); }
                catch (Exception ex) when (ex is OperationCanceledException or IOException) { }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested) throw;
            return inventory with
            {
                ExecutableVersion = null,
                ExecutableSha256 = null,
                ProbeFailure = exception is OperationCanceledException ? "version_probe_timeout" : "executable_unavailable"
            };
        }
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader, CancellationToken token)
    {
        var text = new StringBuilder();
        var buffer = new char[1024];
        int count;
        while ((count = await reader.ReadAsync(buffer, token)) > 0)
        {
            if (text.Length + count > 16 * 1024) throw new IOException("Version output limit exceeded.");
            text.Append(buffer, 0, count);
        }
        return text.ToString();
    }
}
