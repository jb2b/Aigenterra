using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aigenterra.Infrastructure.Executions.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class RecordingHardening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "raw_captures",
                columns: table => new
                {
                    capture_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    captured_at_ticks = table.Column<long>(type: "bigint", nullable: false),
                    recorded_at_ticks = table.Column<long>(type: "bigint", nullable: false),
                    payload = table.Column<byte[]>(type: "bytea", nullable: false),
                    fingerprint = table.Column<string>(type: "text", nullable: false),
                    context_phase = table.Column<string>(type: "text", nullable: true),
                    repository_context = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_raw_captures", x => x.capture_id);
                });

            migrationBuilder.CreateTable(
                name: "capture_outcomes",
                columns: table => new
                {
                    capture_id = table.Column<Guid>(type: "uuid", nullable: false),
                    disposition = table.Column<string>(type: "text", nullable: false),
                    reason = table.Column<string>(type: "text", nullable: true),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    task_execution_id = table.Column<Guid>(type: "uuid", nullable: true),
                    agent_execution_id = table.Column<Guid>(type: "uuid", nullable: true),
                    observation_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_capture_outcomes", x => x.capture_id);
                    table.ForeignKey(
                        name: "FK_capture_outcomes_agent_executions_agent_execution_id",
                        column: x => x.agent_execution_id,
                        principalTable: "agent_executions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_capture_outcomes_conversations_conversation_id",
                        column: x => x.conversation_id,
                        principalTable: "conversations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_capture_outcomes_observations_observation_id",
                        column: x => x.observation_id,
                        principalTable: "observations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_capture_outcomes_raw_captures_capture_id",
                        column: x => x.capture_id,
                        principalTable: "raw_captures",
                        principalColumn: "capture_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_capture_outcomes_task_executions_task_execution_id",
                        column: x => x.task_execution_id,
                        principalTable: "task_executions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_capture_outcomes_agent_execution_id",
                table: "capture_outcomes",
                column: "agent_execution_id");

            migrationBuilder.CreateIndex(
                name: "IX_capture_outcomes_conversation_id",
                table: "capture_outcomes",
                column: "conversation_id");

            migrationBuilder.CreateIndex(
                name: "IX_capture_outcomes_observation_id",
                table: "capture_outcomes",
                column: "observation_id");

            migrationBuilder.CreateIndex(
                name: "IX_capture_outcomes_task_execution_id",
                table: "capture_outcomes",
                column: "task_execution_id");

            migrationBuilder.Sql("""
                CREATE FUNCTION reject_raw_capture_mutation() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN RAISE EXCEPTION 'Raw captures are append-only'; END;
                $$;
                CREATE TRIGGER raw_captures_append_only BEFORE UPDATE OR DELETE ON raw_captures
                FOR EACH ROW EXECUTE FUNCTION reject_raw_capture_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "capture_outcomes");

            migrationBuilder.DropTable(
                name: "raw_captures");
            migrationBuilder.Sql("DROP FUNCTION reject_raw_capture_mutation();");
        }
    }
}
