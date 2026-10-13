using Microsoft.EntityFrameworkCore;

namespace Aigenterra.Infrastructure.Executions.Postgres;

internal sealed class NormalizationAttemptRow
{
    public Guid AttemptId { get; set; }
    public Guid CaptureId { get; set; }
    public string NormalizerVersion { get; set; } = null!;
    public long RecordedAtTicks { get; set; }
    public string Status { get; set; } = null!;
    public string? Reason { get; set; }
    public string Fingerprint { get; set; } = null!;
}

internal sealed class RuntimeFactRow
{
    public Guid AttemptId { get; set; }
    public int Ordinal { get; set; }
    public Guid RuntimeNamespaceId { get; set; }
    public string ThreadId { get; set; } = null!;
    public string? TurnId { get; set; }
    public string Fact { get; set; } = null!;
}

internal static class RuntimeFactRows
{
    public static void Configure(ModelBuilder builder)
    {
        var attempt = builder.Entity<NormalizationAttemptRow>();
        attempt.ToTable("normalization_attempts");
        attempt.HasKey(x => x.AttemptId);
        attempt.Property(x => x.AttemptId).ValueGeneratedNever();
        attempt.HasIndex(x => new { x.CaptureId, x.NormalizerVersion });
        attempt.HasOne<CaptureSourceRow>().WithMany().HasForeignKey(x => x.CaptureId).OnDelete(DeleteBehavior.Restrict);
        var fact = builder.Entity<RuntimeFactRow>();
        fact.ToTable("runtime_facts");
        fact.HasKey(x => new { x.AttemptId, x.Ordinal });
        fact.Property(x => x.Fact).HasColumnType("jsonb");
        fact.Property(x => x.ThreadId).UseCollation("C");
        fact.Property(x => x.TurnId).UseCollation("C");
        fact.HasIndex(x => new { x.RuntimeNamespaceId, x.ThreadId, x.TurnId });
        fact.HasOne<NormalizationAttemptRow>().WithMany().HasForeignKey(x => x.AttemptId).OnDelete(DeleteBehavior.Restrict);
    }
}
