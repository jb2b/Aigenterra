using Aigenterra.Application.Common;
using Aigenterra.Domain.Executions;

namespace Aigenterra.Application.Evaluations;

public sealed class EvaluateTaskExecution(IExecutionEvaluationInputReader reader, ICommandEvidenceDecoder decoder,
    IEvaluationStore store, TimeProvider clock)
{
    public async ValueTask<UseCaseResult<StoredEvaluation>> ExecuteAsync(Guid taskId, CancellationToken cancellationToken = default)
    {
        if (taskId == Guid.Empty) return EvaluationErrors.Invalid<StoredEvaluation>();
        var input = await reader.ReadAsync(new(taskId), cancellationToken);
        if (input is null) return EvaluationErrors.NotFound<StoredEvaluation>();
        var evaluation = new BuildTestEvaluator().Evaluate(input, decoder.Decode(input), clock.GetUtcNow());
        return UseCaseResult<StoredEvaluation>.Success(await store.SaveAsync(evaluation, cancellationToken));
    }
}
public sealed class GetTaskExecutionEvaluations(IEvaluationStore store)
{
    public async ValueTask<UseCaseResult<EvaluationPage>> ExecuteAsync(Guid taskId, string? cursor, CancellationToken cancellationToken = default)
    {
        if (taskId == Guid.Empty) return EvaluationErrors.Invalid<EvaluationPage>();
        Guid? before = null;
        if (cursor is not null)
        {
            try
            {
                if (cursor.Length > 64) return EvaluationErrors.Invalid<EvaluationPage>();
                var bytes = Convert.FromBase64String(cursor);
                if (bytes.Length != 16 || new Guid(bytes) == Guid.Empty) return EvaluationErrors.Invalid<EvaluationPage>();
                before = new Guid(bytes);
            }
            catch (FormatException) { return EvaluationErrors.Invalid<EvaluationPage>(); }
        }
        var page = await store.ListAsync(new(taskId), before, cancellationToken);
        return page is null ? EvaluationErrors.NotFound<EvaluationPage>() : UseCaseResult<EvaluationPage>.Success(page);
    }
}
public sealed class GetTaskExecutionEvaluation(IEvaluationStore store)
{
    public async ValueTask<UseCaseResult<StoredEvaluation>> ExecuteAsync(Guid taskId, Guid evaluationId, CancellationToken cancellationToken = default)
    {
        if (taskId == Guid.Empty || evaluationId == Guid.Empty) return EvaluationErrors.Invalid<StoredEvaluation>();
        var evaluation = await store.GetAsync(new TaskExecutionId(taskId), evaluationId, cancellationToken);
        return evaluation is null ? EvaluationErrors.NotFound<StoredEvaluation>() : UseCaseResult<StoredEvaluation>.Success(evaluation);
    }
}
internal static class EvaluationErrors
{
    public static UseCaseResult<T> Invalid<T>() => UseCaseResult<T>.Failure(ApplicationErrorType.Validation, "invalid_evaluation_request", "Invalid execution, evaluation ID or cursor.");
    public static UseCaseResult<T> NotFound<T>() => UseCaseResult<T>.Failure(ApplicationErrorType.NotFound, "evaluation_resource_not_found", "The execution or evaluation was not found.");
}
