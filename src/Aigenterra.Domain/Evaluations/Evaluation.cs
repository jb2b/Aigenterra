using Aigenterra.Domain.Executions;

namespace Aigenterra.Domain.Evaluations;

public enum EvaluationCheck { Build, Tests }
public enum CommandVerdict { RequestedOnly, Succeeded, Failed, Unknown }
public enum Detection { Observed, NotObserved, Unknown }

public sealed class EvaluationAttempt
{
    public EvaluationAttempt(int number, CommandVerdict verdict, int? exitCode, string explanation,
        IEnumerable<ObservationId> evidence)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(explanation);
        if (number < 1 || !Enum.IsDefined(verdict)) throw new ArgumentOutOfRangeException(nameof(number));
        if ((verdict == CommandVerdict.Succeeded && exitCode != 0)
            || (verdict == CommandVerdict.Failed && (exitCode is null or 0))
            || (verdict is CommandVerdict.Unknown or CommandVerdict.RequestedOnly && exitCode is not null))
            throw new ArgumentException("Verdict and exit code disagree.");
        var ids = evidence.ToArray();
        if (ids.Length == 0 || ids.Any(id => id.Value == Guid.Empty) || ids.Distinct().Count() != ids.Length)
            throw new ArgumentException("An attempt requires distinct evidence references.");
        Number = number;
        Verdict = verdict;
        ExitCode = exitCode;
        Explanation = explanation;
        Evidence = Array.AsReadOnly(ids);
    }
    public int Number { get; }
    public CommandVerdict Verdict { get; }
    public int? ExitCode { get; }
    public string Explanation { get; }
    public IReadOnlyList<ObservationId> Evidence { get; }
}

public sealed class EvaluationFinding
{
    public EvaluationFinding(EvaluationCheck check, Detection detection, string limitations, IEnumerable<EvaluationAttempt> attempts)
    {
        var items = attempts.ToArray();
        if (!Enum.IsDefined(check) || !Enum.IsDefined(detection)) throw new ArgumentOutOfRangeException(nameof(check));
        if ((items.Length > 0) != (detection == Detection.Observed)
            || !items.Select(a => a.Number).SequenceEqual(Enumerable.Range(1, items.Length)))
            throw new ArgumentException("Finding detection or attempt numbering is inconsistent.");
        ArgumentException.ThrowIfNullOrWhiteSpace(limitations);
        Check = check;
        Detection = detection;
        Limitations = limitations;
        Attempts = Array.AsReadOnly(items);
    }
    public EvaluationCheck Check { get; }
    public Detection Detection { get; }
    public string Limitations { get; }
    public IReadOnlyList<EvaluationAttempt> Attempts { get; }
}

/// <summary>An immutable derived conclusion, independent of execution lifecycle and experiments.</summary>
public sealed class Evaluation
{
    public Evaluation(Guid id, TaskExecutionId taskExecutionId, DateTimeOffset createdAt, string evaluatorId,
        int version, long watermark, IEnumerable<EvaluationFinding> findings)
    {
        if (id == Guid.Empty || taskExecutionId.Value == Guid.Empty) throw new ArgumentException("Identity is required.");
        ArgumentException.ThrowIfNullOrWhiteSpace(evaluatorId);
        if (version < 1 || watermark < 0) throw new ArgumentOutOfRangeException(nameof(version));
        var items = findings.OrderBy(f => f.Check).ToArray();
        if (!items.Select(f => f.Check).SequenceEqual(new[] { EvaluationCheck.Build, EvaluationCheck.Tests }))
            throw new ArgumentException("Exactly one build and test finding is required.");
        Id = id;
        TaskExecutionId = taskExecutionId;
        CreatedAt = createdAt.ToUniversalTime();
        EvaluatorId = evaluatorId;
        Version = version;
        Watermark = watermark;
        Findings = Array.AsReadOnly(items);
    }
    public Guid Id { get; }
    public TaskExecutionId TaskExecutionId { get; }
    public DateTimeOffset CreatedAt { get; }
    public string EvaluatorId { get; }
    public int Version { get; }
    public long Watermark { get; }
    public string Category => "Deterministic";
    public IReadOnlyList<EvaluationFinding> Findings { get; }
}
