namespace Aigenterra.Application.Executions;

public sealed record TaskExecutionTimeline(
    Guid TaskExecutionId,
    Guid ConversationId,
    string Objective,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    RuntimeReferenceModel? RuntimeReference,
    IReadOnlyList<AgentExecutionTimelineItem> Agents,
    IReadOnlyList<ObservationTimelineItem> Observations);

public sealed record AgentExecutionTimelineItem(
    Guid AgentExecutionId,
    Guid? ParentAgentExecutionId,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    RuntimeReferenceModel? RuntimeReference);

public sealed record ObservationTimelineItem(
    Guid ObservationId,
    Guid? AgentExecutionId,
    DateTimeOffset ObservedAt,
    DateTimeOffset RecordedAt,
    long Sequence,
    string Kind,
    string Summary,
    RuntimeReferenceModel? RuntimeReference,
    bool HasRawEvidence);
