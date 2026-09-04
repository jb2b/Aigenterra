using System.Text.Json;
using Aigenterra.Application.Common;
using Aigenterra.Application.Executions;

namespace Aigenterra.Api.Executions;

/// <summary>
/// Maps HTTP contracts to execution application use cases.
/// </summary>
internal static class ExecutionEndpoints
{
    private const int MaxRawEvidenceBytes = 1024 * 1024;

    public static IEndpointRouteBuilder MapExecutionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/conversations", CreateConversationAsync);
        endpoints.MapPost(
            "/conversations/{conversationId:guid}/task-executions",
            StartTaskExecutionAsync);
        endpoints.MapPost(
            "/task-executions/{taskExecutionId:guid}/agent-executions",
            StartAgentExecutionAsync);
        endpoints.MapPost(
            "/task-executions/{taskExecutionId:guid}/observations",
            RecordObservationAsync);
        endpoints.MapPost(
            "/task-executions/{taskExecutionId:guid}/agent-executions/{agentExecutionId:guid}/finish",
            FinishAgentExecutionAsync);
        endpoints.MapPost(
            "/task-executions/{taskExecutionId:guid}/finish",
            FinishTaskExecutionAsync);
        endpoints.MapGet(
            "/task-executions/{taskExecutionId:guid}/timeline",
            GetTimelineAsync);
        endpoints.MapGet(
            "/task-executions/by-runtime/{runtime}/{resourceKind}/{externalId}/timeline",
            GetTimelineByRuntimeReferenceAsync);

        return endpoints;
    }

    private static async Task<IResult> CreateConversationAsync(
        CreateConversationRequest request,
        CreateConversation useCase,
        CancellationToken cancellationToken)
    {
        var result = await useCase.ExecuteAsync(
            new CreateConversationCommand(
                request.StartedAt,
                ToInput(request.RuntimeReference)),
            cancellationToken);

        return ToHttpResult(result, created => Results.Json(
            new ConversationCreatedResponse(
                created.ConversationId,
                created.StartedAt,
                ToResponse(created.RuntimeReference)),
            statusCode: StatusCodes.Status201Created));
    }

    private static async Task<IResult> StartTaskExecutionAsync(
        Guid conversationId,
        StartTaskExecutionRequest request,
        StartTaskExecution useCase,
        CancellationToken cancellationToken)
    {
        var result = await useCase.ExecuteAsync(
            new StartTaskExecutionCommand(
                conversationId,
                request.Objective,
                request.StartedAt,
                ToInput(request.RuntimeReference)),
            cancellationToken);

        return ToHttpResult(result, started => Results.Json(
            new TaskExecutionStartedResponse(
                started.TaskExecutionId,
                started.ConversationId,
                started.Status,
                started.StartedAt),
            statusCode: StatusCodes.Status201Created));
    }

    private static async Task<IResult> StartAgentExecutionAsync(
        Guid taskExecutionId,
        StartAgentExecutionRequest request,
        StartAgentExecution useCase,
        CancellationToken cancellationToken)
    {
        var result = await useCase.ExecuteAsync(
            new StartAgentExecutionCommand(
                taskExecutionId,
                request.ParentAgentExecutionId,
                request.StartedAt,
                ToInput(request.RuntimeReference)),
            cancellationToken);

        return ToHttpResult(result, started => Results.Json(
            new AgentExecutionStartedResponse(
                started.AgentExecutionId,
                started.TaskExecutionId,
                started.ParentAgentExecutionId,
                started.Status,
                started.StartedAt),
            statusCode: StatusCodes.Status201Created));
    }

    private static async Task<IResult> RecordObservationAsync(
        Guid taskExecutionId,
        RecordObservationRequest request,
        RecordObservation useCase,
        CancellationToken cancellationToken)
    {
        if (request.ObservedAt is null)
        {
            return Problem(
                ApplicationErrorType.Validation,
                "observed_at_required",
                "ObservedAt is required.");
        }

        SourceEvidence? evidence = null;
        if (request.RawSourcePayload is { } rawSourcePayload)
        {
            var content = JsonSerializer.SerializeToUtf8Bytes(rawSourcePayload);
            if (content.Length > MaxRawEvidenceBytes)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status413PayloadTooLarge,
                    title: "Raw evidence is too large",
                    detail: $"RawSourcePayload cannot exceed {MaxRawEvidenceBytes} bytes.",
                    extensions: new Dictionary<string, object?>
                    {
                        ["code"] = "raw_evidence_too_large"
                    });
            }

            evidence = new SourceEvidence("application/json", content);
        }

        var result = await useCase.ExecuteAsync(
            new RecordObservationCommand(
                taskExecutionId,
                request.AgentExecutionId,
                request.ObservedAt.Value,
                request.Kind,
                request.Summary,
                ToInput(request.RuntimeReference),
                evidence),
            cancellationToken);

        return ToHttpResult(result, recorded => Results.Json(
            new ObservationRecordedResponse(
                recorded.ObservationId,
                recorded.Sequence,
                recorded.RecordedAt),
            statusCode: StatusCodes.Status201Created));
    }

    private static async Task<IResult> FinishAgentExecutionAsync(
        Guid taskExecutionId,
        Guid agentExecutionId,
        FinishExecutionRequest request,
        FinishAgentExecution useCase,
        CancellationToken cancellationToken)
    {
        var result = await useCase.ExecuteAsync(
            new FinishAgentExecutionCommand(
                taskExecutionId,
                agentExecutionId,
                request.Outcome,
                request.EndedAt),
            cancellationToken);

        return ToHttpResult(result, finished => Results.Ok(new ExecutionFinishedResponse(
            finished.ExecutionId,
            finished.Status,
            finished.EndedAt)));
    }

    private static async Task<IResult> FinishTaskExecutionAsync(
        Guid taskExecutionId,
        FinishExecutionRequest request,
        FinishTaskExecution useCase,
        CancellationToken cancellationToken)
    {
        var result = await useCase.ExecuteAsync(
            new FinishTaskExecutionCommand(
                taskExecutionId,
                request.Outcome,
                request.EndedAt),
            cancellationToken);

        return ToHttpResult(result, finished => Results.Ok(new ExecutionFinishedResponse(
            finished.ExecutionId,
            finished.Status,
            finished.EndedAt)));
    }

    private static async Task<IResult> GetTimelineAsync(
        Guid taskExecutionId,
        GetTaskExecutionTimeline useCase,
        CancellationToken cancellationToken)
    {
        var result = await useCase.ExecuteAsync(
            new GetTaskExecutionTimelineQuery(taskExecutionId),
            cancellationToken);

        return ToHttpResult(result, timeline => Results.Ok(ToResponse(timeline)));
    }

    private static async Task<IResult> GetTimelineByRuntimeReferenceAsync(
        string runtime,
        string resourceKind,
        string externalId,
        GetTaskExecutionTimelineByRuntimeReference useCase,
        CancellationToken cancellationToken)
    {
        var result = await useCase.ExecuteAsync(
            new GetTaskExecutionTimelineByRuntimeReferenceQuery(
                new RuntimeReferenceInput(runtime, resourceKind, externalId)),
            cancellationToken);

        return ToHttpResult(result, timeline => Results.Ok(ToResponse(timeline)));
    }

    internal static IResult ToHttpResult<T>(
        UseCaseResult<T> result,
        Func<T, IResult> onSuccess) =>
        result.IsSuccess
            ? onSuccess(result.Value!)
            : Problem(result.Error!.Type, result.Error.Code, result.Error.Message);

    internal static IResult Problem(
        ApplicationErrorType errorType,
        string code,
        string message) =>
        Results.Problem(
            statusCode: errorType switch
            {
                ApplicationErrorType.Validation => StatusCodes.Status400BadRequest,
                ApplicationErrorType.NotFound => StatusCodes.Status404NotFound,
                ApplicationErrorType.Conflict => StatusCodes.Status409Conflict,
                _ => StatusCodes.Status500InternalServerError
            },
            title: errorType switch
            {
                ApplicationErrorType.Validation => "Request validation failed",
                ApplicationErrorType.NotFound => "Execution resource not found",
                ApplicationErrorType.Conflict => "Execution state conflict",
                _ => "Unexpected application error"
            },
            detail: message,
            extensions: new Dictionary<string, object?>
            {
                ["code"] = code
            });

    private static RuntimeReferenceInput? ToInput(RuntimeReferenceRequest? request) =>
        request is null
            ? null
            : new RuntimeReferenceInput(
                request.Runtime,
                request.ResourceKind,
                request.ExternalId);

    private static RuntimeReferenceResponse? ToResponse(RuntimeReferenceModel? model) =>
        model is null
            ? null
            : new RuntimeReferenceResponse(
                model.Runtime,
                model.ResourceKind,
                model.ExternalId);

    private static TaskExecutionTimelineResponse ToResponse(TaskExecutionTimeline timeline) =>
        new(
            timeline.TaskExecutionId,
            timeline.ConversationId,
            timeline.Objective,
            timeline.Status,
            timeline.StartedAt,
            timeline.EndedAt,
            ToResponse(timeline.RuntimeReference),
            timeline.Agents.Select(agent => new AgentExecutionTimelineResponse(
                agent.AgentExecutionId,
                agent.ParentAgentExecutionId,
                agent.Status,
                agent.StartedAt,
                agent.EndedAt,
                ToResponse(agent.RuntimeReference))).ToArray(),
            timeline.Observations.Select(observation => new ObservationTimelineResponse(
                observation.ObservationId,
                observation.AgentExecutionId,
                observation.ObservedAt,
                observation.RecordedAt,
                observation.Sequence,
                observation.Kind,
                observation.Summary,
                ToResponse(observation.RuntimeReference),
                observation.HasRawEvidence)).ToArray());
}

/// <summary>
/// Accepts runtime-correlation values from an HTTP request.
/// </summary>
internal sealed record RuntimeReferenceRequest(
    string? Runtime,
    string? ResourceKind,
    string? ExternalId);

/// <summary>
/// Returns runtime-correlation values through HTTP.
/// </summary>
internal sealed record RuntimeReferenceResponse(
    string Runtime,
    string ResourceKind,
    string ExternalId);

/// <summary>
/// Defines the HTTP request for creating a conversation.
/// </summary>
internal sealed record CreateConversationRequest(
    DateTimeOffset? StartedAt,
    RuntimeReferenceRequest? RuntimeReference);

/// <summary>
/// Defines the HTTP response for a newly created conversation.
/// </summary>
internal sealed record ConversationCreatedResponse(
    Guid ConversationId,
    DateTimeOffset StartedAt,
    RuntimeReferenceResponse? RuntimeReference);

/// <summary>
/// Defines the HTTP request for starting a task execution.
/// </summary>
internal sealed record StartTaskExecutionRequest(
    string? Objective,
    DateTimeOffset? StartedAt,
    RuntimeReferenceRequest? RuntimeReference);

/// <summary>
/// Defines the HTTP response for a started task execution.
/// </summary>
internal sealed record TaskExecutionStartedResponse(
    Guid TaskExecutionId,
    Guid ConversationId,
    string Status,
    DateTimeOffset StartedAt);

/// <summary>
/// Defines the HTTP request for starting an agent execution.
/// </summary>
internal sealed record StartAgentExecutionRequest(
    Guid? ParentAgentExecutionId,
    DateTimeOffset? StartedAt,
    RuntimeReferenceRequest? RuntimeReference);

/// <summary>
/// Defines the HTTP response for a started agent execution.
/// </summary>
internal sealed record AgentExecutionStartedResponse(
    Guid AgentExecutionId,
    Guid TaskExecutionId,
    Guid? ParentAgentExecutionId,
    string Status,
    DateTimeOffset StartedAt);

/// <summary>
/// Defines the HTTP request for recording a normalized observation.
/// </summary>
internal sealed record RecordObservationRequest(
    Guid? AgentExecutionId,
    DateTimeOffset? ObservedAt,
    string? Kind,
    string? Summary,
    RuntimeReferenceRequest? RuntimeReference,
    JsonElement? RawSourcePayload);

/// <summary>
/// Returns the identity and ingest metadata of a recorded observation.
/// </summary>
internal sealed record ObservationRecordedResponse(
    Guid ObservationId,
    long Sequence,
    DateTimeOffset RecordedAt);

/// <summary>
/// Defines the HTTP request for finishing an execution.
/// </summary>
internal sealed record FinishExecutionRequest(
    string? Outcome,
    DateTimeOffset? EndedAt);

/// <summary>
/// Returns the effective terminal state of an execution.
/// </summary>
internal sealed record ExecutionFinishedResponse(
    Guid ExecutionId,
    string Status,
    DateTimeOffset EndedAt);

/// <summary>
/// Returns a complete inspectable task-execution timeline.
/// </summary>
internal sealed record TaskExecutionTimelineResponse(
    Guid TaskExecutionId,
    Guid ConversationId,
    string Objective,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    RuntimeReferenceResponse? RuntimeReference,
    IReadOnlyList<AgentExecutionTimelineResponse> Agents,
    IReadOnlyList<ObservationTimelineResponse> Observations);

/// <summary>
/// Represents an agent within an HTTP timeline response.
/// </summary>
internal sealed record AgentExecutionTimelineResponse(
    Guid AgentExecutionId,
    Guid? ParentAgentExecutionId,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    RuntimeReferenceResponse? RuntimeReference);

/// <summary>
/// Represents an observation within an HTTP timeline response.
/// </summary>
internal sealed record ObservationTimelineResponse(
    Guid ObservationId,
    Guid? AgentExecutionId,
    DateTimeOffset ObservedAt,
    DateTimeOffset RecordedAt,
    long Sequence,
    string Kind,
    string Summary,
    RuntimeReferenceResponse? RuntimeReference,
    bool HasRawEvidence);
