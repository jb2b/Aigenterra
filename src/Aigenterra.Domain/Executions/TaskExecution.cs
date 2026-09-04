namespace Aigenterra.Domain.Executions;

/// <summary>
/// Represents one execution of a concrete user objective.
/// </summary>
public sealed class TaskExecution
{
    public TaskExecution(
        TaskExecutionId id,
        ConversationId conversationId,
        string objective,
        DateTimeOffset startedAt,
        RuntimeReference? runtimeReference = null)
    {
        EnsureNotEmpty(id.Value, nameof(id));
        EnsureNotEmpty(conversationId.Value, nameof(conversationId));
        ArgumentException.ThrowIfNullOrWhiteSpace(objective);

        Id = id;
        ConversationId = conversationId;
        Objective = objective.Trim();
        StartedAt = startedAt.ToUniversalTime();
        RuntimeReference = runtimeReference;
        Status = ExecutionStatus.Running;
    }

    public TaskExecutionId Id { get; }

    public ConversationId ConversationId { get; }

    public string Objective { get; }

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
