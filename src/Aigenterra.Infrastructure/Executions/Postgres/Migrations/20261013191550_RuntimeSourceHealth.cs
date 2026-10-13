using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aigenterra.Infrastructure.Executions.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class RuntimeSourceHealth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "source_health_events",
                columns: table => new
                {
                    capture_id = table.Column<Guid>(type: "uuid", nullable: false),
                    detector_version = table.Column<string>(type: "text", nullable: false),
                    ingest_position = table.Column<long>(type: "bigint", nullable: false),
                    health = table.Column<string>(type: "jsonb", nullable: false),
                    fingerprint = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_source_health_events", x => new { x.capture_id, x.detector_version });
                    table.ForeignKey(
                        name: "FK_source_health_events_capture_sources_capture_id",
                        column: x => x.capture_id,
                        principalTable: "capture_sources",
                        principalColumn: "capture_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "observation_sources",
                columns: table => new
                {
                    runtime_namespace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    runtime_instance_id = table.Column<Guid>(type: "uuid", nullable: true),
                    latest_position = table.Column<long>(type: "bigint", nullable: false),
                    latest_capture_id = table.Column<Guid>(type: "uuid", nullable: false),
                    detector_version = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_observation_sources", x => new { x.runtime_namespace_id, x.source_id });
                    table.ForeignKey(
                        name: "FK_observation_sources_source_health_events_latest_capture_id_~",
                        columns: x => new { x.latest_capture_id, x.detector_version },
                        principalTable: "source_health_events",
                        principalColumns: new[] { "capture_id", "detector_version" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "runtime_instances",
                columns: table => new
                {
                    runtime_namespace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    runtime_instance_id = table.Column<Guid>(type: "uuid", nullable: false),
                    latest_position = table.Column<long>(type: "bigint", nullable: false),
                    latest_capture_id = table.Column<Guid>(type: "uuid", nullable: false),
                    detector_version = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_runtime_instances", x => new { x.runtime_namespace_id, x.runtime_instance_id });
                    table.ForeignKey(
                        name: "FK_runtime_instances_source_health_events_latest_capture_id_de~",
                        columns: x => new { x.latest_capture_id, x.detector_version },
                        principalTable: "source_health_events",
                        principalColumns: new[] { "capture_id", "detector_version" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_observation_sources_latest_capture_id_detector_version",
                table: "observation_sources",
                columns: new[] { "latest_capture_id", "detector_version" });

            migrationBuilder.CreateIndex(
                name: "IX_runtime_instances_latest_capture_id_detector_version",
                table: "runtime_instances",
                columns: new[] { "latest_capture_id", "detector_version" });

            migrationBuilder.CreateIndex(
                name: "IX_runtime_instances_runtime_namespace_id_latest_position",
                table: "runtime_instances",
                columns: new[] { "runtime_namespace_id", "latest_position" });
            migrationBuilder.Sql("""
                CREATE TRIGGER source_health_events_append_only BEFORE UPDATE OR DELETE ON source_health_events
                FOR EACH ROW EXECUTE FUNCTION reject_raw_capture_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "observation_sources");

            migrationBuilder.DropTable(
                name: "runtime_instances");

            migrationBuilder.DropTable(
                name: "source_health_events");
        }
    }
}
