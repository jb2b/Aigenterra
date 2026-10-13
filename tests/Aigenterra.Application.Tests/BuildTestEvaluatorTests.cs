using Aigenterra.Application.Evaluations;
using Aigenterra.Domain.Evaluations;
using Aigenterra.Domain.Executions;

namespace Aigenterra.Application.Tests;

public sealed class BuildTestEvaluatorTests
{
    [Theory]
    [InlineData("dotnet build", EvaluationCheck.Build)]
    [InlineData("dotnet test tests.csproj --no-build", EvaluationCheck.Tests)]
    [InlineData("dotnet build \"project with spaces.csproj\" --no-restore", EvaluationCheck.Build)]
    public void Direct_commands_are_recognized(string command, EvaluationCheck check) => Assert.Equal(check, BuildTestEvaluator.Classify(command).Check);

    [Theory]
    [InlineData("echo 'dotnet test'")]
    [InlineData("dotnet test; echo ok")]
    [InlineData("dotnet build && dotnet test")]
    [InlineData("dotnet test | tee output")]
    [InlineData("pwsh -Command 'dotnet test'")]
    [InlineData("dotnet test $PROJECT")]
    [InlineData("dotnet build @args.rsp")]
    [InlineData("dotnet build -t:Clean")]
    [InlineData("dotnet build /target:Clean")]
    [InlineData("dotnet build -getProperty:TargetFramework")]
    [InlineData("dotnet '' build")]
    [InlineData("dotnet\u00a0build")]
    [InlineData("dotnet test --help")]
    [InlineData("dotnet test --list-tests")]
    [InlineData("dotnet --version")]
    [InlineData("dotnet test \"unclosed")]
    [InlineData("dotnet test\nexit 0")]
    public void Other_or_ambiguous_commands_do_not_produce_attempts(string command) => Assert.Null(BuildTestEvaluator.Classify(command).Check);

    [Fact]
    public void Attempts_preserve_failure_success_requested_and_unknown_without_a_global_pass()
    {
        var commands = new[] { Command("a", CommandEvidenceStage.Terminal, 1), Command("b", CommandEvidenceStage.Terminal, 0),
            Command("c", CommandEvidenceStage.Requested), Command("d", CommandEvidenceStage.Returned) };
        var result = Evaluate(commands);
        Assert.Equal([CommandVerdict.Failed, CommandVerdict.Succeeded, CommandVerdict.RequestedOnly, CommandVerdict.Unknown], result.Findings[1].Attempts.Select(a => a.Verdict));
        Assert.Equal(Detection.NotObserved, result.Findings[0].Detection);
    }
    [Fact]
    public void Out_of_order_pairs_use_correlation_not_arrival_order()
    {
        var requested = Command("a", CommandEvidenceStage.Requested);
        var completed = Command("a", CommandEvidenceStage.Terminal, 0) with { ObservedAt = requested.ObservedAt.AddSeconds(-1) };
        var attempt = Assert.Single(Evaluate([completed, requested]).Findings[1].Attempts);
        Assert.Equal(CommandVerdict.Succeeded, attempt.Verdict);
        Assert.Equal(2, attempt.Evidence.Count);
    }
    [Fact]
    public void Conflicting_or_missing_command_identity_prevents_certainty()
    {
        var first = Command("a", CommandEvidenceStage.Requested);
        var second = Command("a", CommandEvidenceStage.Terminal, 0) with { Command = "dotnet build" };
        Assert.All(Evaluate([first, second]).Findings, f => Assert.Equal(CommandVerdict.Unknown, Assert.Single(f.Attempts).Verdict));
        Assert.Equal(CommandVerdict.Unknown, Assert.Single(Evaluate([first, second with { Command = null }]).Findings[1].Attempts).Verdict);
        Assert.Equal(CommandVerdict.Unknown, Assert.Single(Evaluate([second, second with { ObservationId = ObservationId.New() }]).Findings[0].Attempts).Verdict);
    }
    [Fact]
    public void Empty_and_uninterpretable_evidence_cannot_establish_absence()
    {
        Assert.All(Evaluate([]).Findings, f => Assert.Equal(Detection.Unknown, f.Detection));
        Assert.All(Evaluate([], true).Findings, f => Assert.Equal(Detection.Unknown, f.Detection));
    }
    [Fact]
    public void Interpretable_non_matching_commands_establish_only_recorded_absence()
    {
        var result = Evaluate([Command("version", CommandEvidenceStage.Returned) with { Command = "dotnet --version" }]);
        Assert.Equal(2, result.Version);
        Assert.All(result.Findings, f => Assert.Equal(Detection.NotObserved, f.Detection));
    }
    [Theory]
    [InlineData(CommandEvidenceStage.Requested, CommandVerdict.RequestedOnly)]
    [InlineData(CommandEvidenceStage.Returned, CommandVerdict.Unknown)]
    public void Observed_build_is_independent_of_unknown_test_coverage(CommandEvidenceStage stage, CommandVerdict verdict)
    {
        var build = Command("build", stage) with { Command = "dotnet build" };
        var result = Evaluate([build]);
        Assert.Equal(Detection.Observed, result.Findings[0].Detection);
        Assert.Equal(verdict, Assert.Single(result.Findings[0].Attempts).Verdict);
        Assert.Equal(Detection.NotObserved, result.Findings[1].Detection);
        var ambiguous = Command("script", stage) with { Command = "pwsh -File task.ps1" };
        result = Evaluate([build, ambiguous]);
        Assert.Equal(Detection.Observed, result.Findings[0].Detection);
        Assert.Equal(Detection.Unknown, result.Findings[1].Detection);
        Assert.Contains("additional attempts may be missing", result.Findings[0].Limitations);
    }
    private static long sequence;
    private static CommandEvidence Command(string correlation, CommandEvidenceStage stage, int? exit = null) =>
        new(ObservationId.New(), Interlocked.Increment(ref sequence), DateTimeOffset.UnixEpoch, correlation, "dotnet test", stage, exit);
    private static Evaluation Evaluate(IReadOnlyList<CommandEvidence> commands, bool limited = false) =>
        new BuildTestEvaluator().Evaluate(new(TaskExecutionId.New(), sequence, []), new(commands, limited), DateTimeOffset.UtcNow);
}
