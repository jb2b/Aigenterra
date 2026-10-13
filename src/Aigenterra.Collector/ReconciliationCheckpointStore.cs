using System.Text.Json;
using Aigenterra.Application.Runtime;

namespace Aigenterra.Collector;

public sealed record ReconciliationCheckpoint(string ThreadId, string EnrollmentFingerprint, string EvidenceKey,
    string? Cursor, DateTimeOffset DueAt, bool InProgress, string Outcome);

public sealed class ReconciliationCheckpointStore(string directory)
{
    public ReconciliationCheckpoint? Read(string thread)
    {
        var path = PathFor(thread);
        if (!File.Exists(path)) return null;
        if (new FileInfo(path).Length > 256 * 1024) throw new InvalidDataException("Oversized checkpoint.");
        var checkpoint = JsonSerializer.Deserialize<ReconciliationCheckpoint>(File.ReadAllBytes(path), CaptureSpool.JsonOptions);
        if (checkpoint?.ThreadId != thread) throw new InvalidDataException("Checkpoint thread mismatch.");
        return checkpoint;
    }
    public async Task SaveAsync(ReconciliationCheckpoint checkpoint, CancellationToken token = default)
    {
        Directory.CreateDirectory(directory);
        var path = PathFor(checkpoint.ThreadId);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        await using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        {
            await JsonSerializer.SerializeAsync(file, checkpoint, CaptureSpool.JsonOptions, token);
            file.Flush(flushToDisk: true);
        }
        File.Move(temporary, path, overwrite: true);
    }
    private string PathFor(string thread) => Path.Combine(directory, RuntimeIdentity.Key(thread) + ".json");
}
