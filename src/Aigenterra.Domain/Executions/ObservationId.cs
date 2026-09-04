namespace Aigenterra.Domain.Executions;

public readonly record struct ObservationId
{
    public ObservationId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("An observation ID cannot be empty.", nameof(value));
        }

        Value = value;
    }

    public Guid Value { get; }

    public static ObservationId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}
