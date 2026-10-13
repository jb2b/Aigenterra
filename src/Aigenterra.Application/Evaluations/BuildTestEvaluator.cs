using System.Text;
using Aigenterra.Domain.Evaluations;

namespace Aigenterra.Application.Evaluations;

/// <summary>Pure rules over source-neutral command evidence. Never executes commands or reads their files.</summary>
public sealed class BuildTestEvaluator
{
    public const string Id = "dotnet-command-results";
    public const int Version = 2;
    private const string Coverage = "Only supported recorded commands are evaluated. Missing telemetry is possible; command success does not prove tests ran or passed.";

    public Evaluation Evaluate(EvaluationInput input, DecodedCommands decoded, DateTimeOffset now)
    {
        var groups = decoded.Commands.GroupBy(c => c.Correlation)
            .OrderBy(g => g.Min(c => c.ObservedAt)).ThenBy(g => g.Min(c => c.Sequence)).ToArray();
        var findings = new List<EvaluationFinding>();
        foreach (var check in Enum.GetValues<EvaluationCheck>())
        {
            var attempts = new List<EvaluationAttempt>();
            var limited = decoded.HasUninterpretableEvidence;
            foreach (var group in groups)
            {
                var entries = group.ToArray();
                var recognized = entries.Select(c => Classify(c.Command)).ToArray();
                limited |= recognized.Any(r => r.Unsupported);
                if (!recognized.Any(r => r.Check == check)) continue;
                var terminal = entries.Where(c => c.Stage == CommandEvidenceStage.Terminal).ToArray();
                var conflict = entries.Any(c => c.Invalid || c.Command is null)
                    || entries.Select(c => c.Command).Distinct(StringComparer.Ordinal).Count() != 1
                    || terminal.Length > 1;
                int? exit = null;
                var verdict = CommandVerdict.Unknown;
                var explanation = "Command activity observed; no reliable terminal exit code was captured.";
                if (conflict) explanation = "Conflicting or incomplete evidence prevents a reliable command result.";
                else if (terminal.Length == 1 && terminal[0].ExitCode is { } code)
                {
                    exit = code;
                    verdict = code == 0 ? CommandVerdict.Succeeded : CommandVerdict.Failed;
                    explanation = code == 0 ? "Command completed with exit code 0." : "Command completed with a non-zero exit code.";
                }
                else if (entries.All(c => c.Stage == CommandEvidenceStage.Requested))
                {
                    verdict = CommandVerdict.RequestedOnly;
                    explanation = "Command requested; execution and terminal outcome are not established.";
                }
                attempts.Add(new(attempts.Count + 1, verdict, exit, explanation,
                    entries.OrderBy(c => c.Sequence).Select(c => c.ObservationId).Distinct()));
            }
            findings.Add(new(check, attempts.Count > 0 ? Detection.Observed : limited || groups.Length == 0 ? Detection.Unknown : Detection.NotObserved,
                Coverage + (groups.Length == 0 ? " No interpretable command evidence is available." : "")
                    + (limited ? " Some tool evidence could not be interpreted; additional attempts may be missing." : ""), attempts));
        }
        return new(Guid.CreateVersion7(), input.TaskId, now, Id, Version, input.Watermark, findings);
    }

    // Intentionally a tiny accepted language, not a cross-platform shell parser.
    public static (EvaluationCheck? Check, bool Unsupported) Classify(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return (null, true);
        if (command.Contains("''", StringComparison.Ordinal) || command.Contains("\"\"", StringComparison.Ordinal)
            || command.Any(c => char.IsControl(c) && c != '\t')) return (null, true);
        if (command.IndexOfAny(['\r', '\n', ';', '|', '&', '<', '>', '$', '`', '%', '*', '?', '(', ')', '{', '}', '#']) >= 0)
            return (null, true);
        var words = new List<string>();
        var word = new StringBuilder();
        char quote = '\0';
        foreach (var ch in command)
        {
            if (quote != '\0')
            {
                if (ch == quote) quote = '\0';
                else word.Append(ch);
            }
            else if (ch is '\'' or '"') quote = ch;
            else if (char.IsWhiteSpace(ch))
            {
                if (ch is not (' ' or '\t')) return (null, true);
                if (word.Length > 0) { words.Add(word.ToString()); word.Clear(); }
            }
            else word.Append(ch);
        }
        if (quote != '\0' || command.Contains("\\\"", StringComparison.Ordinal)) return (null, true);
        if (word.Length > 0) words.Add(word.ToString());
        if (words.Count < 2 || words[0] != "dotnet") return (null, true);
        EvaluationCheck? check = words[1] switch { "build" => EvaluationCheck.Build, "test" => EvaluationCheck.Tests, _ => null };
        if (check is null) return (null, false);
        // Response files, custom targets and discovery could turn this into something other than a build/test.
        if (words.Skip(2).Any(w => w.StartsWith('@') || w.StartsWith("-t:", StringComparison.OrdinalIgnoreCase)
            || w.StartsWith("/t:", StringComparison.OrdinalIgnoreCase) || w.StartsWith("-target:", StringComparison.OrdinalIgnoreCase)
            || w.StartsWith("/target:", StringComparison.OrdinalIgnoreCase) || w.StartsWith("--target", StringComparison.OrdinalIgnoreCase)
            || w.StartsWith("-get", StringComparison.OrdinalIgnoreCase) || w.StartsWith("/get", StringComparison.OrdinalIgnoreCase))) return (null, true);
        if (words.Skip(2).Any(w => w is "-h" or "--help" or "-?" or "/?" or "-t" or "--list-tests"
            || w.StartsWith("--list-tests=", StringComparison.Ordinal))) return (null, false);
        return (check, false);
    }
}
