using System.Text.Json;
using Aigenterra.Adapters.Codex.Normalization;
using Aigenterra.Application.Runtime;

namespace Aigenterra.Codex.Tests;

public sealed class RuntimeAgentTests
{
    [Fact]
    public void Research_child_reuse_has_one_identity_four_segments_and_no_invented_task_assignment()
    {
        using var fixture = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "runtime-history-agents.json")));
        var root = fixture.RootElement;
        var parentId = root.GetProperty("rootThreadId").GetString()!;
        var childId = root.GetProperty("childThreadId").GetString()!;
        var parent = RuntimeActivityTests.Capture(root.GetProperty("rootResponse").GetRawText(), "reader_frame");
        var child = RuntimeActivityTests.Capture(root.GetProperty("childResponse").GetRawText(), "reader_frame", parent.Source);
        var normalizer = new CodexAgentEvidenceNormalizer();
        RuntimeEvidenceBatch[] inputs = [normalizer.Normalize(parent, RuntimeActivityTests.Context(parent, parentId)),
            normalizer.Normalize(child, RuntimeActivityTests.Context(child, childId))];
        var topology = RuntimeAgentProjector.Project(inputs);
        Assert.Single(topology.Agents, a => a.ThreadId == childId);
        Assert.Equal(4, topology.Segments.Count(s => s.ThreadId == childId));
        Assert.All(topology.Segments, segment => { Assert.Null(segment.ParentTaskTurnId); Assert.Equal("Unknown", segment.Assignment); });
        Assert.All(topology.Agents, agent => Assert.Equal("Unknown", agent.LifetimeState));
        Assert.Single(topology.Relations, relation => relation.Kind == "parent_child" && relation.ParentThreadId == parentId && relation.ChildThreadId == childId);
        Assert.All(topology.Relations.Where(r => r.Kind == "delegation"), relation => Assert.Null(relation.ChildTurnId));
        Assert.Equal(JsonSerializer.Serialize(topology), JsonSerializer.Serialize(RuntimeAgentProjector.Project(inputs.Reverse().Concat(inputs))));
        Assert.Contains(topology.Segments, s => s.ThreadId == parentId && s.Status == "Interrupted");
        Assert.Contains(RuntimeActivityProjector.Project(inputs), a => a.ThreadId == childId && a.Status == "Completed");
    }

    [Fact]
    public void Three_explicit_children_keep_distinct_identity_and_empty_receivers_create_no_fourth_relation()
    {
        var parent = RuntimeActivityTests.Capture("""{"id":2,"result":{"data":[{"id":"parentTurn","status":"completed","items":[{"type":"subAgentActivity","id":"one","agentThreadId":"childA"},{"type":"subAgentActivity","id":"two","agentThreadId":"childB"},{"type":"subAgentActivity","id":"three","agentThreadId":"childC"},{"type":"collabAgentToolCall","id":"empty","receiverThreadIds":[]}]}]}}""", "reader_frame");
        var normalizer = new CodexAgentEvidenceNormalizer();
        var inputs = new List<RuntimeEvidenceBatch> { normalizer.Normalize(parent, RuntimeActivityTests.Context(parent, "parent")) };
        foreach (var (thread, turn) in new[] { ("childA", "a1"), ("childA", "a2"), ("childB", "b1"), ("childC", "c1") })
        {
            var capture = RuntimeActivityTests.Capture(JsonSerializer.Serialize(new { id = 2, result = new { data = new[] { new { id = turn, rootTurnId = "parentTurn", status = "completed", items = Array.Empty<object>() } } } }), "reader_frame", parent.Source);
            inputs.Add(normalizer.Normalize(capture, RuntimeActivityTests.Context(capture, thread)));
        }
        var topology = RuntimeAgentProjector.Project(inputs);
        Assert.Equal(4, topology.Agents.Count);
        Assert.Equal(4, topology.Segments.Count(s => s.ThreadId != "parent"));
        Assert.Equal(3, topology.Relations.Count(r => r.Kind == "parent_child"));
        Assert.All(topology.Segments, s => Assert.Null(s.ParentTaskTurnId));
        var unknown = parent with { Source = parent.Source with { RuntimeVersion = "future" } };
        Assert.Empty(normalizer.Normalize(unknown, RuntimeActivityTests.Context(unknown, "parent")).Facts);
    }

    [Fact]
    public void Hook_context_does_not_fabricate_direct_parent_and_does_not_finish_agent_lifetime()
    {
        var capture = RuntimeActivityTests.Capture("""{"hook_event_name":"SubagentStop","session_id":"root","agent_id":"child","turn_id":"childTurn","stop_hook_active":false}""", "hook_stdin");
        var batch = new CodexAgentEvidenceNormalizer().Normalize(capture, null);
        var topology = RuntimeAgentProjector.Project([batch]);
        Assert.Equal("Unknown", Assert.Single(topology.Agents).LifetimeState);
        Assert.Equal("Completed", Assert.Single(topology.Segments).Status);
        Assert.Empty(topology.Relations);
    }
}
