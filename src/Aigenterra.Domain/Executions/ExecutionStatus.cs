namespace Aigenterra.Domain.Executions;

/// <summary>Represents the current lifecycle state of an execution.</summary>
public enum ExecutionStatus
{
    Running,
    Completed,
    Failed,
    Interrupted
}
