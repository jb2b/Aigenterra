namespace Aigenterra.Domain.Executions;

public enum ExecutionFinishResult
{
    Applied,
    AlreadyApplied,
    InvalidStatus,
    EndedBeforeStarted,
    ConflictingTerminalStatus
}
