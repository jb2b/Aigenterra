namespace Aigenterra.Domain.Executions;

/// <summary>
/// Represents one agent's participation in a task execution.
/// </summary>
public sealed class AgentExecution
{
    public AgentExecution(
        AgentExecutionId id,
        TaskExecutionId taskExecutionId,
        AgentExecutionId? parentAgentExecutionId,
        DateTimeOffset startedAt,
        RuntimeReference? runtimeReference = null)
    {
        EnsureNotEmpty(id.Value, nameof(id));
        EnsureNotEmpty(taskExecutionId.Value, nameof(taskExecutionId));

        if (parentAgentExecutionId is { } parentId)
        {
            EnsureNotEmpty(parentId.Value, nameof(parentAgentExecutionId));
            if (parentId == id)
            {
                throw new ArgumentException("An agent execution cannot be its own parent.", nameof(parentAgentExecutionId));
            }
        }

        Id = id;
        TaskExecutionId = taskExecutionId;
        ParentAgentExecutionId = parentAgentExecutionId;
        StartedAt = startedAt.ToUniversalTime();
        RuntimeReference = runtimeReference;
        Status = ExecutionStatus.Running;
    }

    public AgentExecutionId Id { get; }

    public TaskExecutionId TaskExecutionId { get; }

    public AgentExecutionId? ParentAgentExecutionId { get; }

    public DateTimeOffset StartedAt { get; }

    public DateTimeOffset? EndedAt { get; private set; }

    public ExecutionStatus Status { get; private set; }

    public RuntimeReference? RuntimeReference { get; }

    public ExecutionFinishResult Finish(ExecutionStatus finalStatus, DateTimeOffset endedAt)
    {
        if (finalStatus == ExecutionStatus.Running)
        {
            return ExecutionFinishResult.InvalidStatus;
        }

        if (Status != ExecutionStatus.Running)
        {
            return Status == finalStatus
                ? ExecutionFinishResult.AlreadyApplied
                : ExecutionFinishResult.ConflictingTerminalStatus;
        }

        var normalizedEndedAt = endedAt.ToUniversalTime();
        if (normalizedEndedAt < StartedAt)
        {
            return ExecutionFinishResult.EndedBeforeStarted;
        }

        Status = finalStatus;
        EndedAt = normalizedEndedAt;
        return ExecutionFinishResult.Applied;
    }

    private static void EnsureNotEmpty(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("The ID cannot be empty.", parameterName);
        }
    }
}
