using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aigenterra.Infrastructure.Executions.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class ScopedRuntimeReconciliation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "runtime_reconciliation",
                columns: table => new
                {
                    capture_id = table.Column<Guid>(type: "uuid", nullable: false),
                    runtime_namespace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ingest_position = table.Column<long>(type: "bigint", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    record = table.Column<string>(type: "jsonb", nullable: false),
                    fingerprint = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_runtime_reconciliation", x => x.capture_id);
                    table.ForeignKey(
                        name: "FK_runtime_reconciliation_capture_sources_capture_id",
                        column: x => x.capture_id,
                        principalTable: "capture_sources",
                        principalColumn: "capture_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_runtime_reconciliation_runtime_namespace_id_source_id_inges~",
                table: "runtime_reconciliation",
                columns: new[] { "runtime_namespace_id", "source_id", "ingest_position" });
            migrationBuilder.Sql("""
                CREATE TRIGGER runtime_reconciliation_append_only BEFORE UPDATE OR DELETE ON runtime_reconciliation
                    FOR EACH ROW EXECUTE FUNCTION reject_raw_capture_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "runtime_reconciliation");
        }
    }
}
