namespace Aigenterra.Application.Executions;

/// <summary>
/// Supplies primitive runtime-correlation values to application use cases.
/// </summary>
public sealed record RuntimeReferenceInput(
    string? Runtime,
    string? ResourceKind,
    string? ExternalId);

/// <summary>
/// Exposes a validated runtime reference in application read models.
/// </summary>
public sealed record RuntimeReferenceModel(
    string Runtime,
    string ResourceKind,
    string ExternalId);

/// <summary>
/// Requests creation of a conversation boundary.
/// </summary>
public sealed record CreateConversationCommand(
    DateTimeOffset? StartedAt,
    RuntimeReferenceInput? RuntimeReference);

/// <summary>
/// Reports the conversation created by the application.
/// </summary>
public sealed record ConversationCreated(
    Guid ConversationId,
    DateTimeOffset StartedAt,
    RuntimeReferenceModel? RuntimeReference);

/// <summary>
/// Requests the start of a task execution for a concrete objective.
/// </summary>
public sealed record StartTaskExecutionCommand(
    Guid ConversationId,
    string? Objective,
    DateTimeOffset? StartedAt,
    RuntimeReferenceInput? RuntimeReference);

/// <summary>
/// Reports the task execution started by the application.
/// </summary>
public sealed record TaskExecutionStarted(
    Guid TaskExecutionId,
    Guid ConversationId,
    string Status,
    DateTimeOffset StartedAt);

/// <summary>
/// Requests the start of an agent's participation in a task execution.
/// </summary>
public sealed record StartAgentExecutionCommand(
    Guid TaskExecutionId,
    Guid? ParentAgentExecutionId,
    DateTimeOffset? StartedAt,
    RuntimeReferenceInput? RuntimeReference);

/// <summary>
/// Reports the agent execution started by the application.
/// </summary>
public sealed record AgentExecutionStarted(
    Guid AgentExecutionId,
    Guid TaskExecutionId,
    Guid? ParentAgentExecutionId,
    string Status,
    DateTimeOffset StartedAt);

/// <summary>
/// Carries opaque source bytes alongside a normalized observation.
/// </summary>
public sealed record SourceEvidence
{
    public SourceEvidence(string mediaType, ReadOnlyMemory<byte> content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);
        if (content.IsEmpty)
        {
            throw new ArgumentException("Source evidence cannot be empty.", nameof(content));
        }

        MediaType = mediaType.Trim();
        Content = content;
    }

    public string MediaType { get; }

    public ReadOnlyMemory<byte> Content { get; }
}

/// <summary>
/// Requests registration of one normalized execution observation.
/// </summary>
public sealed record RecordObservationCommand(
    Guid TaskExecutionId,
    Guid? AgentExecutionId,
    DateTimeOffset ObservedAt,
    string? Kind,
    string? Summary,
    RuntimeReferenceInput? RuntimeReference,
    SourceEvidence? SourceEvidence);

/// <summary>
/// Reports the identity and ingest metadata assigned to an observation.
/// </summary>
public sealed record ObservationRecorded(
    Guid ObservationId,
    long Sequence,
    DateTimeOffset RecordedAt);

/// <summary>
/// Requests a terminal transition for a task execution.
/// </summary>
public sealed record FinishTaskExecutionCommand(
    Guid TaskExecutionId,
    string? Outcome,
    DateTimeOffset? EndedAt);

/// <summary>
/// Requests a terminal transition for an agent execution.
/// </summary>
public sealed record FinishAgentExecutionCommand(
    Guid TaskExecutionId,
    Guid AgentExecutionId,
    string? Outcome,
    DateTimeOffset? EndedAt);

/// <summary>
/// Reports the effective terminal state of a finished execution.
/// </summary>
public sealed record ExecutionFinished(
    Guid ExecutionId,
    string Status,
    DateTimeOffset EndedAt);

/// <summary>
/// Requests the assembled timeline for a task execution.
/// </summary>
public sealed record GetTaskExecutionTimelineQuery(Guid TaskExecutionId);
