namespace Aigenterra.Application.Executions.Ports;

/// <summary>Loads original source bytes only when an observation is inspected.</summary>
public interface IObservationEvidenceReader
{
    ValueTask<SourceEvidence?> GetEvidenceAsync(Guid observationId, CancellationToken cancellationToken = default);
}
