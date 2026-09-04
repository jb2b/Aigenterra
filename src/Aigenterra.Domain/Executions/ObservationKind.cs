namespace Aigenterra.Domain.Executions;

public enum ObservationKind
{
    Message,
    ToolInvocation,
    ToolResult,
    StatusTransition,
    Error,
    Approval,
    TestExecution,
    Measurement
}
