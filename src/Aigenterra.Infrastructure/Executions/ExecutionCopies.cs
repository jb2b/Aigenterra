using Aigenterra.Domain.Executions;

namespace Aigenterra.Infrastructure.Executions;

internal static class ExecutionCopies
{
    internal static TaskExecution Copy(TaskExecution source)
    {
        var copy = new TaskExecution(source.Id, source.ConversationId, source.Objective,
            source.StartedAt, source.RuntimeReference);
        if (source.EndedAt is { } endedAt)
        {
            copy.Finish(source.Status, endedAt);
        }

        return copy;
    }

    internal static AgentExecution Copy(AgentExecution source)
    {
        var copy = new AgentExecution(source.Id, source.TaskExecutionId, source.ParentAgentExecutionId,
            source.StartedAt, source.RuntimeReference);
        if (source.EndedAt is { } endedAt)
        {
            copy.Finish(source.Status, endedAt);
        }

        return copy;
    }
}
