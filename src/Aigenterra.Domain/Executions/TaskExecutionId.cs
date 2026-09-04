namespace Aigenterra.Domain.Executions;

/// <summary>
/// Identifies a task execution within Aigenterra.
/// </summary>
public readonly record struct TaskExecutionId
{
    public TaskExecutionId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A task execution ID cannot be empty.", nameof(value));
        }

        Value = value;
    }

    public Guid Value { get; }

    public static TaskExecutionId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}
