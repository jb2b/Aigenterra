using Microsoft.EntityFrameworkCore;

namespace Aigenterra.Infrastructure.Executions.Postgres;

internal sealed class RuntimeWorkRow
{
    public Guid CaptureId { get; set; }
    public Guid? LeaseId { get; set; }
    public long AvailableAtTicks { get; set; }
    public int Attempts { get; set; }
    public bool Complete { get; set; }
    public string? Outcome { get; set; }
}
internal sealed class RuntimeRequestRow
{
    public Guid ResponseCaptureId { get; set; }
    public Guid ContextCaptureId { get; set; }
    public Guid RequestCaptureId { get; set; }
    public string Request { get; set; } = null!;
}
internal static class RuntimeProcessingRows
{
    public static void Configure(ModelBuilder builder)
    {
        var work = builder.Entity<RuntimeWorkRow>();
        work.ToTable("runtime_work");
        work.HasKey(x => x.CaptureId);
        work.Property(x => x.CaptureId).ValueGeneratedNever();
        work.HasIndex(x => new { x.Complete, x.AvailableAtTicks });
        work.HasOne<CaptureSourceRow>().WithMany().HasForeignKey(x => x.CaptureId).OnDelete(DeleteBehavior.Restrict);
        var context = builder.Entity<RuntimeRequestRow>();
        context.ToTable("runtime_requests");
        context.HasKey(x => x.ResponseCaptureId);
        context.Property(x => x.ResponseCaptureId).ValueGeneratedNever();
        context.Property(x => x.Request).HasColumnType("jsonb");
        context.HasOne<CaptureSourceRow>().WithMany().HasForeignKey(x => x.ResponseCaptureId).OnDelete(DeleteBehavior.Restrict);
        context.HasOne<CaptureSourceRow>().WithMany().HasForeignKey(x => x.ContextCaptureId).OnDelete(DeleteBehavior.Restrict);
        context.HasOne<CaptureSourceRow>().WithMany().HasForeignKey(x => x.RequestCaptureId).OnDelete(DeleteBehavior.Restrict);
    }
}
