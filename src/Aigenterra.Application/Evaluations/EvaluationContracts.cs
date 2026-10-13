using Aigenterra.Application.Executions;
using Aigenterra.Domain.Evaluations;
using Aigenterra.Domain.Executions;

namespace Aigenterra.Application.Evaluations;

public sealed record EvaluationObservation(ObservationId Id, long Sequence, DateTimeOffset ObservedAt,
    string Kind, RuntimeReferenceModel? Reference, SourceEvidence? Evidence);
public sealed record EvaluationInput(TaskExecutionId TaskId, long Watermark, IReadOnlyList<EvaluationObservation> Observations);
public enum CommandEvidenceStage { Requested, Returned, Terminal }
public sealed record CommandEvidence(ObservationId ObservationId, long Sequence, DateTimeOffset ObservedAt,
    string Correlation, string? Command, CommandEvidenceStage Stage, int? ExitCode, bool Invalid = false);
public sealed record DecodedCommands(IReadOnlyList<CommandEvidence> Commands, bool HasUninterpretableEvidence);
public sealed record StoredEvaluation(Evaluation Evaluation, bool Created, bool HasNewerEvidence);
public sealed record EvaluationSummary(Guid Id, DateTimeOffset CreatedAt, string EvaluatorId, int Version,
    long Watermark, bool HasNewerEvidence);
public sealed record EvaluationPage(IReadOnlyList<EvaluationSummary> Items, string? NextCursor);

public interface IExecutionEvaluationInputReader
{
    ValueTask<EvaluationInput?> ReadAsync(TaskExecutionId taskId, CancellationToken cancellationToken = default);
}
public interface ICommandEvidenceDecoder
{
    DecodedCommands Decode(EvaluationInput input);
}
public interface IEvaluationStore
{
    ValueTask<StoredEvaluation> SaveAsync(Evaluation evaluation, CancellationToken cancellationToken = default);
    ValueTask<StoredEvaluation?> GetAsync(TaskExecutionId taskId, Guid evaluationId, CancellationToken cancellationToken = default);
    ValueTask<EvaluationPage?> ListAsync(TaskExecutionId taskId, Guid? beforeId, CancellationToken cancellationToken = default);
}
