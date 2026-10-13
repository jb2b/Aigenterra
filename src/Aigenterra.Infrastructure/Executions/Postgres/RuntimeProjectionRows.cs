using Microsoft.EntityFrameworkCore;

namespace Aigenterra.Infrastructure.Executions.Postgres;

internal sealed class RuntimeProjectionGenerationRow
{
    public Guid Id { get; set; }
    public Guid RuntimeNamespaceId { get; set; }
    public Guid? BaseGenerationId { get; set; }
    public string Versions { get; set; } = null!;
    public string State { get; set; } = null!;
    public int InputCount { get; set; }
    public string ManifestSha256 { get; set; } = null!;
    public string? ProjectionSha256 { get; set; }
    public string? Document { get; set; }
    public long CreatedAtTicks { get; set; }
}
internal sealed class RuntimeProjectionInputRow
{
    public Guid GenerationId { get; set; }
    public Guid CaptureId { get; set; }
    public string EnvelopeFingerprint { get; set; } = null!;
    public bool Complete { get; set; }
    public string? Result { get; set; }
    public string? ResultSha256 { get; set; }
}
internal sealed class RuntimeProjectionActiveRow
{
    public Guid RuntimeNamespaceId { get; set; }
    public long Revision { get; set; }
    public Guid GenerationId { get; set; }
}
internal sealed class RuntimeProjectionChangeRow
{
    public Guid RuntimeNamespaceId { get; set; }
    public long Revision { get; set; }
    public Guid GenerationId { get; set; }
    public Guid? PreviousGenerationId { get; set; }
    public string ProjectionSha256 { get; set; } = null!;
}
internal static class RuntimeProjectionRows
{
    public static void Configure(ModelBuilder builder)
    {
        var generation = builder.Entity<RuntimeProjectionGenerationRow>();
        generation.ToTable("runtime_projection_generations"); generation.HasKey(x => x.Id); generation.Property(x => x.Id).ValueGeneratedNever();
        generation.Property(x => x.Versions).HasColumnType("jsonb"); generation.Property(x => x.Document).HasColumnType("jsonb");
        generation.HasIndex(x => new { x.RuntimeNamespaceId, x.State });
        var input = builder.Entity<RuntimeProjectionInputRow>();
        input.ToTable("runtime_projection_inputs"); input.HasKey(x => new { x.GenerationId, x.CaptureId }); input.Property(x => x.Result).HasColumnType("jsonb");
        input.HasOne<RuntimeProjectionGenerationRow>().WithMany().HasForeignKey(x => x.GenerationId).OnDelete(DeleteBehavior.Restrict);
        input.HasOne<CaptureSourceRow>().WithMany().HasForeignKey(x => x.CaptureId).OnDelete(DeleteBehavior.Restrict);
        var active = builder.Entity<RuntimeProjectionActiveRow>();
        active.ToTable("runtime_projection_active"); active.HasKey(x => x.RuntimeNamespaceId); active.Property(x => x.RuntimeNamespaceId).ValueGeneratedNever();
        active.HasOne<RuntimeProjectionGenerationRow>().WithMany().HasForeignKey(x => x.GenerationId).OnDelete(DeleteBehavior.Restrict);
        var change = builder.Entity<RuntimeProjectionChangeRow>();
        change.ToTable("runtime_projection_changes"); change.HasKey(x => new { x.RuntimeNamespaceId, x.Revision });
        change.HasOne<RuntimeProjectionGenerationRow>().WithMany().HasForeignKey(x => x.GenerationId).OnDelete(DeleteBehavior.Restrict);
    }
}
