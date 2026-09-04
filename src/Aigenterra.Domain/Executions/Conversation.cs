namespace Aigenterra.Domain.Executions;

public sealed class Conversation
{
    public Conversation(
        ConversationId id,
        DateTimeOffset startedAt,
        RuntimeReference? runtimeReference = null)
    {
        EnsureNotEmpty(id.Value, nameof(id));

        Id = id;
        StartedAt = startedAt.ToUniversalTime();
        RuntimeReference = runtimeReference;
    }

    public ConversationId Id { get; }

    public DateTimeOffset StartedAt { get; }

    public RuntimeReference? RuntimeReference { get; }

    private static void EnsureNotEmpty(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("The ID cannot be empty.", parameterName);
        }
    }
}
