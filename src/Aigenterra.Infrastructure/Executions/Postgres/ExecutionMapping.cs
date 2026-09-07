using Aigenterra.Domain.Executions;

namespace Aigenterra.Infrastructure.Executions.Postgres;

internal static class ExecutionMapping
{
    internal static DateTimeOffset Time(long ticks) => new(ticks, TimeSpan.Zero);

    internal static string Contract<T>(T value) where T : struct, Enum => value.ToString().ToLowerInvariant();

    internal static RuntimeReference? Reference(string? runtime, string? kind, string? externalId) =>
        runtime is null && kind is null && externalId is null ? null : new(runtime!, kind!, externalId!);

    internal static Conversation ToDomain(ConversationRow row) => new(
        new ConversationId(row.Id), Time(row.StartedAtTicks), Reference(row.Runtime, row.ResourceKind, row.ExternalId));

    internal static TaskExecution ToDomain(TaskExecutionRow row)
    {
        var execution = new TaskExecution(new TaskExecutionId(row.Id), new ConversationId(row.ConversationId),
            row.Objective, Time(row.StartedAtTicks), Reference(row.Runtime, row.ResourceKind, row.ExternalId));
        RestoreLifecycle(row.Status, row.EndedAtTicks, execution.Finish);
        return execution;
    }

    internal static AgentExecution ToDomain(AgentExecutionRow row)
    {
        var execution = new AgentExecution(new AgentExecutionId(row.Id), new TaskExecutionId(row.TaskExecutionId),
            row.ParentAgentExecutionId is { } parent ? new AgentExecutionId(parent) : null,
            Time(row.StartedAtTicks), Reference(row.Runtime, row.ResourceKind, row.ExternalId));
        RestoreLifecycle(row.Status, row.EndedAtTicks, execution.Finish);
        return execution;
    }

    private static void RestoreLifecycle(string status, long? endedAt,
        Func<ExecutionStatus, DateTimeOffset, ExecutionFinishResult> finish)
    {
        if (status == "running" && endedAt is null)
        {
            return;
        }

        if (endedAt is null || !Enum.TryParse<ExecutionStatus>(status, true, out var parsed)
            || !Enum.IsDefined(parsed) || parsed == ExecutionStatus.Running
            || finish(parsed, Time(endedAt.Value)) != ExecutionFinishResult.Applied)
        {
            throw new InvalidOperationException("Stored execution has an invalid lifecycle state.");
        }
    }

    internal static ConversationRow ToRow(Conversation value) => new()
    {
        Id = value.Id.Value,
        StartedAtTicks = value.StartedAt.UtcTicks,
        Runtime = value.RuntimeReference?.Runtime,
        ResourceKind = value.RuntimeReference?.ResourceKind,
        ExternalId = value.RuntimeReference?.ExternalId
    };

    internal static TaskExecutionRow ToRow(TaskExecution value) => new()
    {
        Id = value.Id.Value,
        ConversationId = value.ConversationId.Value,
        Objective = value.Objective,
        StartedAtTicks = value.StartedAt.UtcTicks,
        EndedAtTicks = value.EndedAt?.UtcTicks,
        Status = Contract(value.Status),
        Runtime = value.RuntimeReference?.Runtime,
        ResourceKind = value.RuntimeReference?.ResourceKind,
        ExternalId = value.RuntimeReference?.ExternalId
    };

    internal static AgentExecutionRow ToRow(AgentExecution value) => new()
    {
        Id = value.Id.Value,
        TaskExecutionId = value.TaskExecutionId.Value,
        ParentAgentExecutionId = value.ParentAgentExecutionId?.Value,
        StartedAtTicks = value.StartedAt.UtcTicks,
        EndedAtTicks = value.EndedAt?.UtcTicks,
        Status = Contract(value.Status),
        Runtime = value.RuntimeReference?.Runtime,
        ResourceKind = value.RuntimeReference?.ResourceKind,
        ExternalId = value.RuntimeReference?.ExternalId
    };
}
