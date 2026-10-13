using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aigenterra.Infrastructure.Executions.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class RuntimeActivityEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "runtime_evidence",
                columns: table => new
                {
                    capture_id = table.Column<Guid>(type: "uuid", nullable: false),
                    normalizer_version = table.Column<string>(type: "text", nullable: false),
                    batch = table.Column<string>(type: "jsonb", nullable: false),
                    fingerprint = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_runtime_evidence", x => new { x.capture_id, x.normalizer_version });
                    table.ForeignKey(
                        name: "FK_runtime_evidence_capture_sources_capture_id",
                        column: x => x.capture_id,
                        principalTable: "capture_sources",
                        principalColumn: "capture_id",
                        onDelete: ReferentialAction.Restrict);
                });
            migrationBuilder.Sql("""
                CREATE TRIGGER runtime_evidence_append_only BEFORE UPDATE OR DELETE ON runtime_evidence
                    FOR EACH ROW EXECUTE FUNCTION reject_raw_capture_mutation();
                UPDATE runtime_work SET complete = false, lease_id = NULL, available_at_ticks = 0;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "runtime_evidence");
        }
    }
}
