namespace Aigenterra.Infrastructure.Executions.Postgres;

internal sealed class ConversationRow
{
    public Guid Id { get; set; }
    public long StartedAtTicks { get; set; }
    public string? Runtime { get; set; }
    public string? ResourceKind { get; set; }
    public string? ExternalId { get; set; }
}

internal sealed class TaskExecutionRow
{
    public Guid Id { get; set; }
    public Guid ConversationId { get; set; }
    public string Objective { get; set; } = null!;
    public long StartedAtTicks { get; set; }
    public long? EndedAtTicks { get; set; }
    public string Status { get; set; } = null!;
    public string? Runtime { get; set; }
    public string? ResourceKind { get; set; }
    public string? ExternalId { get; set; }

    // Serializes concurrent appends; deliberately absent from the Domain model.
    public long LastSequence { get; set; }
}

internal sealed class AgentExecutionRow
{
    public Guid Id { get; set; }
    public Guid TaskExecutionId { get; set; }
    public Guid? ParentAgentExecutionId { get; set; }
    public long StartedAtTicks { get; set; }
    public long? EndedAtTicks { get; set; }
    public string Status { get; set; } = null!;
    public string? Runtime { get; set; }
    public string? ResourceKind { get; set; }
    public string? ExternalId { get; set; }
}

internal sealed class ObservationRow
{
    public Guid Id { get; set; }
    public Guid TaskExecutionId { get; set; }
    public Guid? AgentExecutionId { get; set; }
    public long ObservedAtTicks { get; set; }
    public long RecordedAtTicks { get; set; }
    public long Sequence { get; set; }
    public string Kind { get; set; } = null!;
    public string Summary { get; set; } = null!;
    public string? Runtime { get; set; }
    public string? ResourceKind { get; set; }
    public string? ExternalId { get; set; }
    public string? MediaType { get; set; }
    public byte[]? Evidence { get; set; }
}
