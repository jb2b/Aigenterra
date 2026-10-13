using Microsoft.EntityFrameworkCore;

namespace Aigenterra.Infrastructure.Executions.Postgres;

internal sealed class SourceHealthEventRow
{
    public Guid CaptureId { get; set; }
    public string DetectorVersion { get; set; } = null!;
    public long IngestPosition { get; set; }
    public string Health { get; set; } = null!;
    public string Fingerprint { get; set; } = null!;
}

internal sealed class ObservationSourceRow
{
    public Guid RuntimeNamespaceId { get; set; }
    public Guid SourceId { get; set; }
    public Guid? RuntimeInstanceId { get; set; }
    public long LatestPosition { get; set; }
    public Guid LatestCaptureId { get; set; }
    public string DetectorVersion { get; set; } = null!;
}

internal sealed class RuntimeInstanceRow
{
    public Guid RuntimeNamespaceId { get; set; }
    public Guid RuntimeInstanceId { get; set; }
    public long LatestPosition { get; set; }
    public Guid LatestCaptureId { get; set; }
    public string DetectorVersion { get; set; } = null!;
}

internal static class RuntimeHealthRows
{
    public static void Configure(ModelBuilder builder)
    {
        var health = builder.Entity<SourceHealthEventRow>();
        health.ToTable("source_health_events");
        health.HasKey(x => new { x.CaptureId, x.DetectorVersion });
        health.Property(x => x.Health).HasColumnType("jsonb");
        health.HasOne<CaptureSourceRow>().WithMany().HasForeignKey(x => x.CaptureId).OnDelete(DeleteBehavior.Restrict);
        var source = builder.Entity<ObservationSourceRow>();
        source.ToTable("observation_sources");
        source.HasKey(x => new { x.RuntimeNamespaceId, x.SourceId });
        source.HasOne<SourceHealthEventRow>().WithMany().HasForeignKey(x => new { x.LatestCaptureId, x.DetectorVersion }).OnDelete(DeleteBehavior.Restrict);
        var instance = builder.Entity<RuntimeInstanceRow>();
        instance.ToTable("runtime_instances");
        instance.HasKey(x => new { x.RuntimeNamespaceId, x.RuntimeInstanceId });
        instance.HasIndex(x => new { x.RuntimeNamespaceId, x.LatestPosition });
        instance.HasOne<SourceHealthEventRow>().WithMany().HasForeignKey(x => new { x.LatestCaptureId, x.DetectorVersion }).OnDelete(DeleteBehavior.Restrict);
    }
}
