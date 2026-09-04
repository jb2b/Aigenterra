namespace Aigenterra.Domain.Executions;

public readonly record struct AgentExecutionId
{
    public AgentExecutionId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("An agent execution ID cannot be empty.", nameof(value));
        }

        Value = value;
    }

    public Guid Value { get; }

    public static AgentExecutionId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}
