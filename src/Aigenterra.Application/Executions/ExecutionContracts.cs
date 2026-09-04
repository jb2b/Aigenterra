namespace Aigenterra.Application.Executions;

public sealed record RuntimeReferenceInput(
    string? Runtime,
    string? ResourceKind,
    string? ExternalId);

public sealed record RuntimeReferenceModel(
    string Runtime,
    string ResourceKind,
    string ExternalId);

public sealed record CreateConversationCommand(
    DateTimeOffset? StartedAt,
    RuntimeReferenceInput? RuntimeReference);

public sealed record ConversationCreated(
    Guid ConversationId,
    DateTimeOffset StartedAt,
    RuntimeReferenceModel? RuntimeReference);

public sealed record StartTaskExecutionCommand(
    Guid ConversationId,
    string? Objective,
    DateTimeOffset? StartedAt,
    RuntimeReferenceInput? RuntimeReference);

public sealed record TaskExecutionStarted(
    Guid TaskExecutionId,
    Guid ConversationId,
    string Status,
    DateTimeOffset StartedAt);

public sealed record StartAgentExecutionCommand(
    Guid TaskExecutionId,
    Guid? ParentAgentExecutionId,
    DateTimeOffset? StartedAt,
    RuntimeReferenceInput? RuntimeReference);

public sealed record AgentExecutionStarted(
    Guid AgentExecutionId,
    Guid TaskExecutionId,
    Guid? ParentAgentExecutionId,
    string Status,
    DateTimeOffset StartedAt);

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

public sealed record RecordObservationCommand(
    Guid TaskExecutionId,
    Guid? AgentExecutionId,
    DateTimeOffset ObservedAt,
    string? Kind,
    string? Summary,
    RuntimeReferenceInput? RuntimeReference,
    SourceEvidence? SourceEvidence);

public sealed record ObservationRecorded(
    Guid ObservationId,
    long Sequence,
    DateTimeOffset RecordedAt);

public sealed record FinishTaskExecutionCommand(
    Guid TaskExecutionId,
    string? Outcome,
    DateTimeOffset? EndedAt);

public sealed record FinishAgentExecutionCommand(
    Guid TaskExecutionId,
    Guid AgentExecutionId,
    string? Outcome,
    DateTimeOffset? EndedAt);

public sealed record ExecutionFinished(
    Guid ExecutionId,
    string Status,
    DateTimeOffset EndedAt);

public sealed record GetTaskExecutionTimelineQuery(Guid TaskExecutionId);
