namespace Aigenterra.Domain.Executions;

/// <summary>
/// Captures one immutable, normalized event observed during a task execution.
/// </summary>
public sealed class Observation
{
    public Observation(
        ObservationId id,
        TaskExecutionId taskExecutionId,
        AgentExecutionId? agentExecutionId,
        DateTimeOffset observedAt,
        ObservationKind kind,
        string summary,
        RuntimeReference? runtimeReference = null)
    {
        EnsureNotEmpty(id.Value, nameof(id));
        EnsureNotEmpty(taskExecutionId.Value, nameof(taskExecutionId));

        if (agentExecutionId is { } agentId)
        {
            EnsureNotEmpty(agentId.Value, nameof(agentExecutionId));
        }

        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(summary);

        Id = id;
        TaskExecutionId = taskExecutionId;
        AgentExecutionId = agentExecutionId;
        ObservedAt = observedAt.ToUniversalTime();
        Kind = kind;
        Summary = summary.Trim();
        RuntimeReference = runtimeReference;
    }

    public ObservationId Id { get; }

    public TaskExecutionId TaskExecutionId { get; }

    public AgentExecutionId? AgentExecutionId { get; }

    public DateTimeOffset ObservedAt { get; }

    public ObservationKind Kind { get; }

    public string Summary { get; }

    public RuntimeReference? RuntimeReference { get; }

    private static void EnsureNotEmpty(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("The ID cannot be empty.", parameterName);
        }
    }
}
