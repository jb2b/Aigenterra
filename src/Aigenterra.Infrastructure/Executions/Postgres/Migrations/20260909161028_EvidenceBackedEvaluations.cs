using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aigenterra.Infrastructure.Executions.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class EvidenceBackedEvaluations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_observations_task_execution_id_id",
                table: "observations",
                columns: new[] { "task_execution_id", "id" });

            migrationBuilder.CreateTable(
                name: "evaluations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_execution_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_ticks = table.Column<long>(type: "bigint", nullable: false),
                    evaluator_id = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    watermark = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_evaluations", x => x.id);
                    table.UniqueConstraint("AK_evaluations_task_execution_id_id", x => new { x.task_execution_id, x.id });
                    table.CheckConstraint("ck_evaluation_snapshot", "watermark >= 0 AND version > 0 AND length(btrim(evaluator_id)) > 0");
                    table.ForeignKey(
                        name: "FK_evaluations_task_executions_task_execution_id",
                        column: x => x.task_execution_id,
                        principalTable: "task_executions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "evaluation_findings",
                columns: table => new
                {
                    task_execution_id = table.Column<Guid>(type: "uuid", nullable: false),
                    evaluation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    check = table.Column<string>(type: "text", nullable: false),
                    detection = table.Column<string>(type: "text", nullable: false),
                    limitations = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_evaluation_findings", x => new { x.task_execution_id, x.evaluation_id, x.check });
                    table.CheckConstraint("ck_evaluation_finding", "\"check\" IN ('Build','Tests') AND detection IN ('Observed','NotObserved','Unknown')");
                    table.ForeignKey(
                        name: "FK_evaluation_findings_evaluations_task_execution_id_evaluatio~",
                        columns: x => new { x.task_execution_id, x.evaluation_id },
                        principalTable: "evaluations",
                        principalColumns: new[] { "task_execution_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "evaluation_attempts",
                columns: table => new
                {
                    task_execution_id = table.Column<Guid>(type: "uuid", nullable: false),
                    evaluation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    check = table.Column<string>(type: "text", nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false),
                    verdict = table.Column<string>(type: "text", nullable: false),
                    exit_code = table.Column<int>(type: "integer", nullable: true),
                    explanation = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_evaluation_attempts", x => new { x.task_execution_id, x.evaluation_id, x.check, x.number });
                    table.CheckConstraint("ck_evaluation_attempt", "number > 0 AND ((verdict = 'Succeeded' AND exit_code IS NOT NULL AND exit_code = 0) OR (verdict = 'Failed' AND exit_code IS NOT NULL AND exit_code <> 0) OR (verdict IN ('Unknown','RequestedOnly') AND exit_code IS NULL))");
                    table.ForeignKey(
                        name: "FK_evaluation_attempts_evaluation_findings_task_execution_id_e~",
                        columns: x => new { x.task_execution_id, x.evaluation_id, x.check },
                        principalTable: "evaluation_findings",
                        principalColumns: new[] { "task_execution_id", "evaluation_id", "check" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "evaluation_evidence",
                columns: table => new
                {
                    task_execution_id = table.Column<Guid>(type: "uuid", nullable: false),
                    evaluation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    check = table.Column<string>(type: "text", nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false),
                    observation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_evaluation_evidence", x => new { x.task_execution_id, x.evaluation_id, x.check, x.number, x.observation_id });
                    table.ForeignKey(
                        name: "FK_evaluation_evidence_evaluation_attempts_task_execution_id_e~",
                        columns: x => new { x.task_execution_id, x.evaluation_id, x.check, x.number },
                        principalTable: "evaluation_attempts",
                        principalColumns: new[] { "task_execution_id", "evaluation_id", "check", "number" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_evaluation_evidence_observations_task_execution_id_observat~",
                        columns: x => new { x.task_execution_id, x.observation_id },
                        principalTable: "observations",
                        principalColumns: new[] { "task_execution_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_evaluation_evidence_task_execution_id_observation_id",
                table: "evaluation_evidence",
                columns: new[] { "task_execution_id", "observation_id" });

            migrationBuilder.CreateIndex(
                name: "IX_evaluations_task_execution_id_evaluator_id_version_watermark",
                table: "evaluations",
                columns: new[] { "task_execution_id", "evaluator_id", "version", "watermark" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_evaluations_task_execution_id_id",
                table: "evaluations",
                columns: new[] { "task_execution_id", "id" },
                descending: new bool[0]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "evaluation_evidence");

            migrationBuilder.DropTable(
                name: "evaluation_attempts");

            migrationBuilder.DropTable(
                name: "evaluation_findings");

            migrationBuilder.DropTable(
                name: "evaluations");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_observations_task_execution_id_id",
                table: "observations");
        }
    }
}
