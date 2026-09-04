namespace Aigenterra.Domain.Executions;

/// <summary>
/// Identifies a conversation within Aigenterra.
/// </summary>
public readonly record struct ConversationId
{
    public ConversationId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A conversation ID cannot be empty.", nameof(value));
        }

        Value = value;
    }

    public Guid Value { get; }

    public static ConversationId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}
