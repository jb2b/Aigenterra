namespace Aigenterra.Domain.Executions;

/// <summary>
/// Correlates an Aigenterra record with a provider-neutral runtime identity.
/// </summary>
public sealed record RuntimeReference
{
    public RuntimeReference(string runtime, string resourceKind, string externalId)
    {
        Runtime = RequireValue(runtime, nameof(runtime));
        ResourceKind = RequireValue(resourceKind, nameof(resourceKind));
        ExternalId = RequireValue(externalId, nameof(externalId));
    }

    public string Runtime { get; }

    public string ResourceKind { get; }

    public string ExternalId { get; }

    private static string RequireValue(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value.Trim();
    }
}
