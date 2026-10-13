using System.Net.Http.Json;
using System.Text.Json;
using Aigenterra.Adapters.Codex;
using Aigenterra.Application.Recording;

namespace Aigenterra.Collector;

/// <summary>One immutable directory per delivery; only a database acknowledgement permits deletion.</summary>
public sealed class CaptureSpool(string root, int maxPayloadBytes = 16 * 1024 * 1024)
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private string DirectoryFor(string kind) => Path.Combine(Path.GetFullPath(root), kind);

    public async Task<Guid> CaptureAsync(Stream input, DateTimeOffset capturedAt,
        IRepositoryContextReader contextReader, CancellationToken cancellationToken = default)
    {
        var id = Guid.NewGuid();
        var name = $"{capturedAt.UtcTicks:D19}-{id:N}";
        var staging = Path.Combine(DirectoryFor("staging"), name);
        Directory.CreateDirectory(staging);
        var payloadPath = Path.Combine(staging, "payload.bin");
        // Stream even oversized evidence to disk; never silently discard or truncate it.
        await using (var file = new FileStream(payloadPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            81920, FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            await input.CopyToAsync(file, cancellationToken);
            file.Flush(flushToDisk: true);
        }
        RepositoryContext? context = null;
        string? phase = null;
        if (new FileInfo(payloadPath).Length <= maxPayloadBytes)
        {
            var request = CodexCaptureContext.Identify(await File.ReadAllBytesAsync(payloadPath, cancellationToken));
            phase = request.Phase;
            if (phase is not null && request.WorkingDirectory is { } cwd)
                context = await contextReader.ReadAsync(cwd, cancellationToken);
        }
        var envelope = new CaptureEnvelope(1, id, capturedAt.ToUniversalTime(), [], phase, context);
        await WriteDurableJsonAsync(Path.Combine(staging, "record.json"), envelope, cancellationToken);
        var oversized = new FileInfo(payloadPath).Length > maxPayloadBytes;
        if (oversized) await WriteDurableJsonAsync(Path.Combine(staging, "failure.json"), new { reason = "payload_too_large" }, cancellationToken);
        var destination = DirectoryFor(oversized ? "retained" : "pending");
        Directory.CreateDirectory(destination);
        Directory.Move(staging, Path.Combine(destination, name));
        return id;
    }

    public async Task<DrainResult> DrainAsync(HttpClient client, Uri endpoint, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(root);
        FileStream lease;
        try { lease = new FileStream(Path.Combine(root, "drain.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) { return new(0, "worker_already_running"); }
        using (lease)
        {
            var delivered = 0;
            var pending = DirectoryFor("pending");
            if (!Directory.Exists(pending)) return new(0, null);
            foreach (var directory in Directory.EnumerateDirectories(pending).Order(StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var record = JsonSerializer.Deserialize<CaptureEnvelope>(
                        await File.ReadAllBytesAsync(Path.Combine(directory, "record.json"), cancellationToken), JsonOptions)
                        ?? throw new JsonException();
                    var payloadPath = Path.Combine(directory, "payload.bin");
                    if (new FileInfo(payloadPath).Length > maxPayloadBytes)
                    {
                        await RetainAsync(directory, "payload_too_large", cancellationToken);
                        continue;
                    }
                    record = record with { Payload = await File.ReadAllBytesAsync(payloadPath, cancellationToken) };
                    using var response = await client.PostAsJsonAsync(endpoint, record, JsonOptions, cancellationToken);
                    if (response.IsSuccessStatusCode)
                    {
                        var receipt = await response.Content.ReadFromJsonAsync<ArchiveAcknowledgement>(JsonOptions, cancellationToken);
                        if (receipt is not { Archived: true } || receipt.CaptureId != record.CaptureId)
                            throw new JsonException("Missing archive acknowledgement.");
                        // Remove acknowledged records atomically from pending before cleaning their files.
                        var acknowledged = DirectoryFor("acknowledged");
                        Directory.CreateDirectory(acknowledged);
                        var completed = Path.Combine(acknowledged, Path.GetFileName(directory));
                        Directory.Move(directory, completed);
                        CleanupAcknowledged(completed);
                        delivered++;
                    }
                    else if ((int)response.StatusCode is 400 or 409 or 413 or 422)
                    {
                        await RetainAsync(directory, $"http_{(int)response.StatusCode}", cancellationToken);
                    }
                    else
                    {
                        await MarkFailureAsync(directory, $"http_{(int)response.StatusCode}", cancellationToken);
                        return new(delivered, "delivery_failed");
                    }
                }
                catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException or IOException)
                {
                    if (cancellationToken.IsCancellationRequested) throw;
                    if (Directory.Exists(directory))
                        await MarkFailureAsync(directory, exception is JsonException ? "invalid_record_or_acknowledgement" : "delivery_failed", cancellationToken);
                    return new(delivered, "delivery_failed");
                }
            }
            return new(delivered, null);
        }
    }

    public object Status() => new
    {
        pending = Count("pending"),
        retained = Count("retained"),
        incomplete = Count("staging"),
        acknowledged = Count("acknowledged"),
        failures = new[] { "pending", "retained" }.SelectMany(kind =>
            Directory.Exists(DirectoryFor(kind)) ? Directory.EnumerateFiles(DirectoryFor(kind), "failure.json", SearchOption.AllDirectories) : [])
            .Select(path =>
            {
                try { using var doc = JsonDocument.Parse(File.ReadAllText(path)); return Text(doc.RootElement, "reason") ?? "unknown"; }
                catch (Exception ex) when (ex is IOException or JsonException) { return "unreadable_failure"; }
            }).GroupBy(x => x).ToDictionary(group => group.Key, group => group.Count()),
        captureFailures = File.Exists(Path.Combine(root, "capture-failure.json"))
    };

    public async Task RecordCaptureFailureAsync()
    {
        try
        {
            Directory.CreateDirectory(root);
            await WriteDurableJsonAsync(Path.Combine(root, "capture-failure.json"),
                new { reason = "local_capture_failed", at = DateTimeOffset.UtcNow }, CancellationToken.None);
        }
        catch (Exception) { /* A full or inaccessible disk may prevent diagnostics too. */ }
    }

    private static void CleanupAcknowledged(string directory)
    {
        File.Delete(Path.Combine(directory, "payload.bin"));
        File.Delete(Path.Combine(directory, "failure.json"));
        File.Delete(Path.Combine(directory, "record.json"));
        if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
    }
    private int Count(string kind) => Directory.Exists(DirectoryFor(kind)) ? Directory.EnumerateDirectories(DirectoryFor(kind)).Count() : 0;
    private async Task RetainAsync(string directory, string reason, CancellationToken token)
    {
        await MarkFailureAsync(directory, reason, token);
        Directory.CreateDirectory(DirectoryFor("retained"));
        Directory.Move(directory, Path.Combine(DirectoryFor("retained"), Path.GetFileName(directory)));
    }
    private static Task MarkFailureAsync(string directory, string reason, CancellationToken token) =>
        WriteDurableJsonAsync(Path.Combine(directory, "failure.json"), new { reason, at = DateTimeOffset.UtcNow }, token);

    private static async Task WriteDurableJsonAsync<T>(string path, T value, CancellationToken token)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        await using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        {
            await JsonSerializer.SerializeAsync(file, value, JsonOptions, token);
            file.Flush(flushToDisk: true);
        }
        File.Move(temporary, path, overwrite: true);
    }
    private static string? Text(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object
        && value.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String ? property.GetString() : null;
    public sealed record DrainResult(int Delivered, string? Error);
    public sealed record ArchiveAcknowledgement(Guid CaptureId, bool Archived);
}
