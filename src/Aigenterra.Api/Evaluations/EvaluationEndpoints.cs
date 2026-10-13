using Aigenterra.Api.Executions;
using Aigenterra.Application.Evaluations;

namespace Aigenterra.Api.Evaluations;

internal static class EvaluationEndpoints
{
    public static IEndpointRouteBuilder MapEvaluationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/task-executions/{taskId:guid}/evaluations", async (Guid taskId, EvaluateTaskExecution useCase, HttpContext context, CancellationToken ct) =>
            ExecutionEndpoints.ToHttpResult(await useCase.ExecuteAsync(taskId, ct), saved =>
            {
                // Relative Location remains valid both directly and behind the /api reverse proxy.
                if (saved.Created) context.Response.Headers.Location = $"evaluations/{saved.Evaluation.Id}";
                return Results.Json(ToResponse(saved), statusCode: saved.Created ? 201 : 200);
            }));
        endpoints.MapGet("/task-executions/{taskId:guid}/evaluations", async (Guid taskId, string? cursor, GetTaskExecutionEvaluations useCase, CancellationToken ct) =>
            ExecutionEndpoints.ToHttpResult(await useCase.ExecuteAsync(taskId, cursor, ct), page => Results.Ok(new
            {
                items = page.Items.Select(item => new { evaluationId = item.Id, item.CreatedAt, item.EvaluatorId, item.Version, item.Watermark, item.HasNewerEvidence, category = "Deterministic" }),
                page.NextCursor
            })));
        endpoints.MapGet("/task-executions/{taskId:guid}/evaluations/{evaluationId:guid}", async (Guid taskId, Guid evaluationId, GetTaskExecutionEvaluation useCase, CancellationToken ct) =>
            ExecutionEndpoints.ToHttpResult(await useCase.ExecuteAsync(taskId, evaluationId, ct), saved => Results.Ok(ToResponse(saved))));
        return endpoints;
    }

    private static object ToResponse(StoredEvaluation saved) => new
    {
        evaluationId = saved.Evaluation.Id,
        taskExecutionId = saved.Evaluation.TaskExecutionId.Value,
        saved.Evaluation.CreatedAt,
        saved.Evaluation.EvaluatorId,
        saved.Evaluation.Version,
        saved.Evaluation.Category,
        saved.Evaluation.Watermark,
        saved.HasNewerEvidence,
        findings = saved.Evaluation.Findings.Select(f => new
        {
            check = f.Check.ToString(),
            detection = f.Detection.ToString(),
            f.Limitations,
            attempts = f.Attempts.Select(a => new
            {
                a.Number,
                verdict = a.Verdict.ToString(),
                a.ExitCode,
                a.Explanation,
                observationIds = a.Evidence.Select(id => id.Value)
            })
        })
    };
}
