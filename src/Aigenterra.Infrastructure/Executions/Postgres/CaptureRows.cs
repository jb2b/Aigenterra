using Microsoft.EntityFrameworkCore;

namespace Aigenterra.Infrastructure.Executions.Postgres;

internal sealed class RawCaptureRow
{
    public Guid CaptureId { get; set; }
    public int Version { get; set; }
    public long CapturedAtTicks { get; set; }
    public long RecordedAtTicks { get; set; }
    public byte[] Payload { get; set; } = null!;
    public string Fingerprint { get; set; } = null!;
    public string? ContextPhase { get; set; }
    public string? RepositoryContext { get; set; }
}

internal sealed class CaptureOutcomeRow
{
    public Guid CaptureId { get; set; }
    public string Disposition { get; set; } = null!;
    public string? Reason { get; set; }
    public Guid? ConversationId { get; set; }
    public Guid? TaskExecutionId { get; set; }
    public Guid? AgentExecutionId { get; set; }
    public Guid? ObservationId { get; set; }
}

internal static class CaptureRows
{
    public static void Configure(ModelBuilder modelBuilder)
    {
        var raw = modelBuilder.Entity<RawCaptureRow>();
        raw.ToTable("raw_captures");
        raw.HasKey(x => x.CaptureId);
        raw.Property(x => x.CaptureId).ValueGeneratedNever();
        raw.Property(x => x.Payload).HasColumnType("bytea");
        raw.Property(x => x.RepositoryContext).HasColumnType("jsonb");
        var outcome = modelBuilder.Entity<CaptureOutcomeRow>();
        outcome.ToTable("capture_outcomes");
        outcome.HasKey(x => x.CaptureId);
        outcome.Property(x => x.CaptureId).ValueGeneratedNever();
        outcome.HasOne<RawCaptureRow>().WithOne().HasForeignKey<CaptureOutcomeRow>(x => x.CaptureId).OnDelete(DeleteBehavior.Restrict);
        outcome.HasOne<TaskExecutionRow>().WithMany().HasForeignKey(x => x.TaskExecutionId).OnDelete(DeleteBehavior.Restrict);
        outcome.HasOne<ConversationRow>().WithMany().HasForeignKey(x => x.ConversationId).OnDelete(DeleteBehavior.Restrict);
        outcome.HasOne<AgentExecutionRow>().WithMany().HasForeignKey(x => x.AgentExecutionId).OnDelete(DeleteBehavior.Restrict);
        outcome.HasOne<ObservationRow>().WithMany().HasForeignKey(x => x.ObservationId).OnDelete(DeleteBehavior.Restrict);
    }
}
