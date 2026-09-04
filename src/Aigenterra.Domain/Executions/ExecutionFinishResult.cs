namespace Aigenterra.Domain.Executions;

/// <summary>
/// Describes the outcome of applying a terminal execution transition.
/// </summary>
public enum ExecutionFinishResult
{
    Applied,
    AlreadyApplied,
    InvalidStatus,
    EndedBeforeStarted,
    ConflictingTerminalStatus
}
