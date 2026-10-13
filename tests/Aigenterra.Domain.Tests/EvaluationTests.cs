using Aigenterra.Domain.Evaluations;
using Aigenterra.Domain.Executions;

namespace Aigenterra.Domain.Tests;

public sealed class EvaluationTests
{
    [Theory]
    [InlineData(CommandVerdict.Succeeded, null)]
    [InlineData(CommandVerdict.Succeeded, 1)]
    [InlineData(CommandVerdict.Failed, null)]
    [InlineData(CommandVerdict.Failed, 0)]
    [InlineData(CommandVerdict.Unknown, 0)]
    [InlineData(CommandVerdict.RequestedOnly, 1)]
    public void Verdict_requires_matching_exit_code(CommandVerdict verdict, int? code) =>
        Assert.Throws<ArgumentException>(() => new EvaluationAttempt(1, verdict, code, "Reason", [ObservationId.New()]));

    [Fact]
    public void Attempts_require_evidence_and_copy_mutable_input()
    {
        Assert.Throws<ArgumentException>(() => new EvaluationAttempt(1, CommandVerdict.Unknown, null, "Reason", []));
        var evidence = new[] { ObservationId.New() };
        var attempt = new EvaluationAttempt(1, CommandVerdict.Succeeded, 0, "Command completed", evidence);
        var original = evidence[0];
        evidence[0] = ObservationId.New();
        Assert.Equal(original, attempt.Evidence[0]);
        Assert.Throws<ArgumentException>(() => new EvaluationAttempt(1, CommandVerdict.Failed, 1, "Reason", [original, original]));
    }

    [Fact]
    public void Findings_and_evaluation_enforce_shape()
    {
        Assert.Throws<ArgumentException>(() => new EvaluationFinding(EvaluationCheck.Build, Detection.Observed, "Limits", []));
        var finding = new EvaluationFinding(EvaluationCheck.Build, Detection.NotObserved, "Limits", []);
        Assert.Throws<ArgumentException>(() => new Evaluation(Guid.NewGuid(), TaskExecutionId.New(), DateTimeOffset.UtcNow, "rules", 1, 0, [finding, finding]));
    }
}
