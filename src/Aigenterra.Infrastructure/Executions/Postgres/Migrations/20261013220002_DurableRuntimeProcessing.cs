using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aigenterra.Infrastructure.Executions.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class DurableRuntimeProcessing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "runtime_requests",
                columns: table => new
                {
                    response_capture_id = table.Column<Guid>(type: "uuid", nullable: false),
                    context_capture_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_capture_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_runtime_requests", x => x.response_capture_id);
                    table.ForeignKey(
                        name: "FK_runtime_requests_capture_sources_context_capture_id",
                        column: x => x.context_capture_id,
                        principalTable: "capture_sources",
                        principalColumn: "capture_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_runtime_requests_capture_sources_request_capture_id",
                        column: x => x.request_capture_id,
                        principalTable: "capture_sources",
                        principalColumn: "capture_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_runtime_requests_capture_sources_response_capture_id",
                        column: x => x.response_capture_id,
                        principalTable: "capture_sources",
                        principalColumn: "capture_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "runtime_work",
                columns: table => new
                {
                    capture_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lease_id = table.Column<Guid>(type: "uuid", nullable: true),
                    available_at_ticks = table.Column<long>(type: "bigint", nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    complete = table.Column<bool>(type: "boolean", nullable: false),
                    outcome = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_runtime_work", x => x.capture_id);
                    table.ForeignKey(
                        name: "FK_runtime_work_capture_sources_capture_id",
                        column: x => x.capture_id,
                        principalTable: "capture_sources",
                        principalColumn: "capture_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_runtime_requests_context_capture_id",
                table: "runtime_requests",
                column: "context_capture_id");

            migrationBuilder.CreateIndex(
                name: "IX_runtime_requests_request_capture_id",
                table: "runtime_requests",
                column: "request_capture_id");

            migrationBuilder.CreateIndex(
                name: "IX_runtime_work_complete_available_at_ticks",
                table: "runtime_work",
                columns: new[] { "complete", "available_at_ticks" });
            migrationBuilder.Sql("""
                CREATE FUNCTION enqueue_runtime_capture() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    INSERT INTO runtime_work (capture_id, available_at_ticks, attempts, complete)
                    VALUES (NEW.capture_id, 0, 0, false);
                    RETURN NEW;
                END $$;
                CREATE TRIGGER capture_source_work AFTER INSERT ON capture_sources
                    FOR EACH ROW EXECUTE FUNCTION enqueue_runtime_capture();
                INSERT INTO runtime_work (capture_id, available_at_ticks, attempts, complete)
                    SELECT capture_id, 0, 0, false FROM capture_sources;
                CREATE TRIGGER runtime_requests_append_only BEFORE UPDATE OR DELETE ON runtime_requests
                    FOR EACH ROW EXECUTE FUNCTION reject_raw_capture_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER capture_source_work ON capture_sources; DROP FUNCTION enqueue_runtime_capture();");
            migrationBuilder.DropTable(
                name: "runtime_requests");

            migrationBuilder.DropTable(
                name: "runtime_work");
        }
    }
}
