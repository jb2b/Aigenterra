using Aigenterra.Application.Common;
using Aigenterra.Application.Recording;

namespace Aigenterra.Application.Runtime;

/// <summary>Source time retains its original value/unit. Receipt time is never substituted.</summary>
public sealed record RuntimeSourceTime(long Value, string Unit, string Meaning);
public sealed record RuntimeFieldEvidence(string Field, string Pointer, string Derivation);
public sealed record RuntimeFact(
    Guid CaptureId, int Ordinal, Guid RuntimeNamespaceId, string Kind, string ThreadId, string? TurnId,
    string Status, string ThreadScope, string? ConversationThreadId, string Authority, string Derivation,
    RuntimeSourceTime? OccurredAt, long? SourceEmittedAtMilliseconds, IReadOnlyList<RuntimeFieldEvidence> Fields);

public sealed record RuntimeNormalization(string Status, string? Reason, IReadOnlyList<RuntimeFact> Facts);
public sealed record NormalizationAttempt(Guid AttemptId, Guid CaptureId, string NormalizerVersion,
    DateTimeOffset RecordedAt, string Status, string? Reason, IReadOnlyList<RuntimeFact> Facts);

public interface IRuntimeCaptureNormalizer
{
    string Version { get; }
    RuntimeNormalization Normalize(RuntimeCaptureEnvelope capture);
}

public interface IRuntimeFactStore
{
    Task AppendAsync(NormalizationAttempt attempt, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<NormalizationAttempt>> ReadAttemptsAsync(Guid captureId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RuntimeFact>> ReadTurnFactsAsync(Guid runtimeNamespaceId, string threadId, string turnId,
        string normalizerVersion, CancellationToken cancellationToken = default);
}

public sealed class NormalizeRuntimeCapture(IRawCaptureStore archive, IRuntimeCaptureNormalizer normalizer,
    IRuntimeFactStore facts, TimeProvider clock)
{
    public async Task<UseCaseResult<NormalizationAttempt>> ExecuteAsync(Guid captureId, CancellationToken cancellationToken = default)
    {
        var capture = await archive.ReadRuntimeAsync(captureId, cancellationToken);
        if (capture is null)
            return UseCaseResult<NormalizationAttempt>.Failure(ApplicationErrorType.NotFound, "runtime_capture_not_found", "No v2 capture was archived with this ID.");
        var result = normalizer.Normalize(capture.Envelope);
        var attempt = new NormalizationAttempt(Guid.NewGuid(), captureId, normalizer.Version, clock.GetUtcNow(), result.Status, result.Reason, result.Facts);
        await facts.AppendAsync(attempt, cancellationToken);
        return UseCaseResult<NormalizationAttempt>.Success(attempt);
    }
}

public sealed record RuntimeFactReference(Guid CaptureId, int Ordinal);
public sealed record RuntimeTurnProjection(Guid RuntimeNamespaceId, string ThreadId, string TurnId,
    string NormalizerVersion, string Status, string ThreadScope, string? ConversationThreadId,
    string Coverage, string Derivation, bool Conflict, IReadOnlyList<RuntimeFactReference> Evidence);

/// <summary>Small read model only. No writes to legacy execution aggregates, no inferred parent task assignment.</summary>
public static class RuntimeTurnProjector
{
    public static RuntimeTurnProjection? Project(Guid runtimeNamespaceId, string threadId, string turnId,
        string normalizerVersion, IReadOnlyList<RuntimeFact> facts)
    {
        var relevant = facts.Where(f => f.RuntimeNamespaceId == runtimeNamespaceId && f.ThreadId == threadId && f.TurnId == turnId && f.Kind == "turn").ToArray();
        if (relevant.Length == 0) return null;
        var terminals = relevant.Where(f => f.Status != "Running").ToArray();
        var candidates = terminals.Length == 0 ? relevant : terminals;
        var authority = candidates.Max(f => f.Authority == "Runtime" ? 2 : 1);
        candidates = candidates.Where(f => (f.Authority == "Runtime" ? 2 : 1) == authority).ToArray();
        var statuses = candidates.Select(f => f.Status).Distinct(StringComparer.Ordinal).ToArray();
        var contextFacts = facts.Where(f => f.RuntimeNamespaceId == runtimeNamespaceId && f.ThreadId == threadId
            && (f.Kind == "thread" || f.TurnId == turnId)).ToArray();
        var scopes = contextFacts.Select(f => f.ThreadScope).Where(s => s != "Unknown").Distinct(StringComparer.Ordinal).ToArray();
        var contexts = contextFacts.Select(f => f.ConversationThreadId).Where(s => s is not null).Distinct(StringComparer.Ordinal).ToArray();
        var conflict = statuses.Length > 1 || scopes.Length > 1 || contexts.Length > 1;
        var derivation = candidates.Any(f => f.Derivation == "Observed") ? "Observed"
            : candidates.Any(f => f.Derivation == "Reconciled") ? "Reconciled" : "Inferred";
        return new(runtimeNamespaceId, threadId, turnId, normalizerVersion,
            statuses.Length == 1 ? statuses[0] : "TerminalUnknown", scopes.Length == 1 ? scopes[0] : "Unknown",
            contexts.Length == 1 ? contexts[0] : null, "Partial", derivation, conflict,
            contextFacts.Select(f => new RuntimeFactReference(f.CaptureId, f.Ordinal)).Distinct()
                .OrderBy(f => f.CaptureId).ThenBy(f => f.Ordinal).ToArray());
    }
}

public sealed class GetRuntimeTurn(IRuntimeFactStore facts)
{
    public async Task<RuntimeTurnProjection?> ExecuteAsync(Guid runtimeNamespaceId, string threadId, string turnId,
        string normalizerVersion, CancellationToken cancellationToken = default) =>
        RuntimeTurnProjector.Project(runtimeNamespaceId, threadId, turnId, normalizerVersion,
            await facts.ReadTurnFactsAsync(runtimeNamespaceId, threadId, turnId, normalizerVersion, cancellationToken));
}
