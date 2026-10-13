using Microsoft.EntityFrameworkCore;

namespace Aigenterra.Infrastructure.Executions.Postgres;

internal sealed class EvaluationRow
{
    public Guid Id { get; set; }
    public Guid TaskExecutionId { get; set; }
    public long CreatedAtTicks { get; set; }
    public string EvaluatorId { get; set; } = null!;
    public int Version { get; set; }
    public long Watermark { get; set; }
}
internal sealed class EvaluationFindingRow
{
    public Guid TaskExecutionId { get; set; }
    public Guid EvaluationId { get; set; }
    public string Check { get; set; } = null!;
    public string Detection { get; set; } = null!;
    public string Limitations { get; set; } = null!;
}
internal sealed class EvaluationAttemptRow
{
    public Guid TaskExecutionId { get; set; }
    public Guid EvaluationId { get; set; }
    public string Check { get; set; } = null!;
    public int Number { get; set; }
    public string Verdict { get; set; } = null!;
    public int? ExitCode { get; set; }
    public string Explanation { get; set; } = null!;
}
internal sealed class EvaluationEvidenceRow
{
    public Guid TaskExecutionId { get; set; }
    public Guid EvaluationId { get; set; }
    public string Check { get; set; } = null!;
    public int Number { get; set; }
    public Guid ObservationId { get; set; }
    public int Position { get; set; }
}
internal static class EvaluationRows
{
    public static void Configure(ModelBuilder builder)
    {
        var evaluations = builder.Entity<EvaluationRow>();
        evaluations.ToTable("evaluations", t => t.HasCheckConstraint("ck_evaluation_snapshot", "watermark >= 0 AND version > 0 AND length(btrim(evaluator_id)) > 0"));
        evaluations.HasKey(e => e.Id);
        evaluations.Property(e => e.Id).ValueGeneratedNever();
        evaluations.HasAlternateKey(e => new { e.TaskExecutionId, e.Id });
        evaluations.HasOne<TaskExecutionRow>().WithMany().HasForeignKey(e => e.TaskExecutionId).OnDelete(DeleteBehavior.Restrict);
        evaluations.HasIndex(e => new { e.TaskExecutionId, e.EvaluatorId, e.Version, e.Watermark }).IsUnique();
        evaluations.HasIndex(e => new { e.TaskExecutionId, e.Id }).IsDescending();

        var findings = builder.Entity<EvaluationFindingRow>();
        findings.ToTable("evaluation_findings", t => t.HasCheckConstraint("ck_evaluation_finding", "\"check\" IN ('Build','Tests') AND detection IN ('Observed','NotObserved','Unknown')"));
        findings.HasKey(e => new { e.TaskExecutionId, e.EvaluationId, e.Check });
        findings.HasOne<EvaluationRow>().WithMany().HasForeignKey(e => new { e.TaskExecutionId, e.EvaluationId })
            .HasPrincipalKey(e => new { e.TaskExecutionId, e.Id }).OnDelete(DeleteBehavior.Restrict);

        var attempts = builder.Entity<EvaluationAttemptRow>();
        attempts.ToTable("evaluation_attempts", t => t.HasCheckConstraint("ck_evaluation_attempt",
            "number > 0 AND ((verdict = 'Succeeded' AND exit_code IS NOT NULL AND exit_code = 0) OR " +
            "(verdict = 'Failed' AND exit_code IS NOT NULL AND exit_code <> 0) OR " +
            "(verdict IN ('Unknown','RequestedOnly') AND exit_code IS NULL))"));
        attempts.HasKey(e => new { e.TaskExecutionId, e.EvaluationId, e.Check, e.Number });
        attempts.HasOne<EvaluationFindingRow>().WithMany().HasForeignKey(e => new { e.TaskExecutionId, e.EvaluationId, e.Check }).OnDelete(DeleteBehavior.Restrict);

        var evidence = builder.Entity<EvaluationEvidenceRow>();
        evidence.ToTable("evaluation_evidence");
        evidence.HasKey(e => new { e.TaskExecutionId, e.EvaluationId, e.Check, e.Number, e.ObservationId });
        evidence.HasOne<EvaluationAttemptRow>().WithMany().HasForeignKey(e => new { e.TaskExecutionId, e.EvaluationId, e.Check, e.Number }).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<ObservationRow>().HasAlternateKey(e => new { e.TaskExecutionId, e.Id });
        evidence.HasOne<ObservationRow>().WithMany().HasForeignKey(e => new { e.TaskExecutionId, e.ObservationId })
            .HasPrincipalKey(e => new { e.TaskExecutionId, e.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
