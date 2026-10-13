using Aigenterra.Collector;

namespace Aigenterra.Codex.Tests;

public sealed class ReconciliationCheckpointTests
{
    [Fact]
    public async Task Checkpoint_reopens_with_cursor_inflight_marker_and_enrollment_binding_without_path_escape()
    {
        var root = Path.Combine(Path.GetTempPath(), "aigenterra-checkpoint-" + Guid.NewGuid().ToString("N"));
        try
        {
            var checkpoint = new ReconciliationCheckpoint("../../untrusted/id", new string('A', 64), "evidence", "cursor", DateTimeOffset.UtcNow, true, "reading_history");
            await new ReconciliationCheckpointStore(root).SaveAsync(checkpoint);
            var reopened = new ReconciliationCheckpointStore(root);
            Assert.Equal(checkpoint, reopened.Read(checkpoint.ThreadId));
            Assert.Single(Directory.GetFiles(root));
            await reopened.SaveAsync(checkpoint with { InProgress = false, Outcome = "budget_limited" });
            Assert.Equal("cursor", reopened.Read(checkpoint.ThreadId)!.Cursor);
            Assert.False(reopened.Read(checkpoint.ThreadId)!.InProgress);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
