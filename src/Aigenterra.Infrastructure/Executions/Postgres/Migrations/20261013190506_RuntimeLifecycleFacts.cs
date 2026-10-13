using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aigenterra.Infrastructure.Executions.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class RuntimeLifecycleFacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "normalization_attempts",
                columns: table => new
                {
                    attempt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    capture_id = table.Column<Guid>(type: "uuid", nullable: false),
                    normalizer_version = table.Column<string>(type: "text", nullable: false),
                    recorded_at_ticks = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    reason = table.Column<string>(type: "text", nullable: true),
                    fingerprint = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_normalization_attempts", x => x.attempt_id);
                    table.ForeignKey(
                        name: "FK_normalization_attempts_capture_sources_capture_id",
                        column: x => x.capture_id,
                        principalTable: "capture_sources",
                        principalColumn: "capture_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "runtime_facts",
                columns: table => new
                {
                    attempt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ordinal = table.Column<int>(type: "integer", nullable: false),
                    runtime_namespace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    thread_id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    turn_id = table.Column<string>(type: "text", nullable: true, collation: "C"),
                    fact = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_runtime_facts", x => new { x.attempt_id, x.ordinal });
                    table.ForeignKey(
                        name: "FK_runtime_facts_normalization_attempts_attempt_id",
                        column: x => x.attempt_id,
                        principalTable: "normalization_attempts",
                        principalColumn: "attempt_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_normalization_attempts_capture_id_normalizer_version",
                table: "normalization_attempts",
                columns: new[] { "capture_id", "normalizer_version" });

            migrationBuilder.CreateIndex(
                name: "IX_runtime_facts_runtime_namespace_id_thread_id_turn_id",
                table: "runtime_facts",
                columns: new[] { "runtime_namespace_id", "thread_id", "turn_id" });
            migrationBuilder.Sql("""
                CREATE TRIGGER normalization_attempts_append_only BEFORE UPDATE OR DELETE ON normalization_attempts
                FOR EACH ROW EXECUTE FUNCTION reject_raw_capture_mutation();
                CREATE TRIGGER runtime_facts_append_only BEFORE UPDATE OR DELETE ON runtime_facts
                FOR EACH ROW EXECUTE FUNCTION reject_raw_capture_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "runtime_facts");

            migrationBuilder.DropTable(
                name: "normalization_attempts");
        }
    }
}
