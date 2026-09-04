namespace Aigenterra.Application.Executions;

/// <summary>Provides the task metadata, agents and ordered observations needed for inspection.</summary>
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

/// <summary>Represents one agent in a task-execution timeline.</summary>
public sealed record AgentExecutionTimelineItem(
    Guid AgentExecutionId,
    Guid? ParentAgentExecutionId,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    RuntimeReferenceModel? RuntimeReference);

/// <summary>Represents one ordered observation in a task-execution timeline.</summary>
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
