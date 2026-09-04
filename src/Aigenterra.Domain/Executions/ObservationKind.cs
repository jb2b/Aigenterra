namespace Aigenterra.Domain.Executions;

/// <summary>Classifies normalized observations independently of their runtime source.</summary>
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
