using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aigenterra.Infrastructure.Executions.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class InitialExecutionPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "conversations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    started_at_ticks = table.Column<long>(type: "bigint", nullable: false),
                    runtime = table.Column<string>(type: "text", nullable: true, collation: "C"),
                    resource_kind = table.Column<string>(type: "text", nullable: true, collation: "C"),
                    external_id = table.Column<string>(type: "text", nullable: true, collation: "C")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_conversations", x => x.id);
                    table.CheckConstraint("ck_conversations_id", "id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_conversations_reference", "(runtime IS NULL AND resource_kind IS NULL AND external_id IS NULL) OR (runtime IS NOT NULL AND resource_kind IS NOT NULL AND external_id IS NOT NULL AND length(btrim(runtime)) > 0 AND length(btrim(resource_kind)) > 0 AND length(btrim(external_id)) > 0)");
                    table.CheckConstraint("ck_conversations_started_at_ticks", "started_at_ticks BETWEEN 0 AND 3155378975999999999");
                });

            migrationBuilder.CreateTable(
                name: "task_executions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    objective = table.Column<string>(type: "text", nullable: false),
                    started_at_ticks = table.Column<long>(type: "bigint", nullable: false),
                    ended_at_ticks = table.Column<long>(type: "bigint", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false),
                    runtime = table.Column<string>(type: "text", nullable: true, collation: "C"),
                    resource_kind = table.Column<string>(type: "text", nullable: true, collation: "C"),
                    external_id = table.Column<string>(type: "text", nullable: true, collation: "C"),
                    last_sequence = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_task_executions", x => x.id);
                    table.CheckConstraint("ck_task_executions_ended_at_ticks", "ended_at_ticks BETWEEN 0 AND 3155378975999999999");
                    table.CheckConstraint("ck_task_executions_id", "id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_task_executions_lifecycle", "(status = 'running' AND ended_at_ticks IS NULL) OR (status IN ('completed','failed','interrupted') AND ended_at_ticks IS NOT NULL AND ended_at_ticks >= started_at_ticks)");
                    table.CheckConstraint("ck_task_executions_reference", "(runtime IS NULL AND resource_kind IS NULL AND external_id IS NULL) OR (runtime IS NOT NULL AND resource_kind IS NOT NULL AND external_id IS NOT NULL AND length(btrim(runtime)) > 0 AND length(btrim(resource_kind)) > 0 AND length(btrim(external_id)) > 0)");
                    table.CheckConstraint("ck_task_executions_started_at_ticks", "started_at_ticks BETWEEN 0 AND 3155378975999999999");
                    table.CheckConstraint("ck_task_objective", "length(btrim(objective)) > 0");
                    table.CheckConstraint("ck_task_sequence", "last_sequence >= 0");
                    table.ForeignKey(
                        name: "FK_task_executions_conversations_conversation_id",
                        column: x => x.conversation_id,
                        principalTable: "conversations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "agent_executions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_execution_id = table.Column<Guid>(type: "uuid", nullable: false),
                    parent_agent_execution_id = table.Column<Guid>(type: "uuid", nullable: true),
                    started_at_ticks = table.Column<long>(type: "bigint", nullable: false),
                    ended_at_ticks = table.Column<long>(type: "bigint", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false),
                    runtime = table.Column<string>(type: "text", nullable: true, collation: "C"),
                    resource_kind = table.Column<string>(type: "text", nullable: true, collation: "C"),
                    external_id = table.Column<string>(type: "text", nullable: true, collation: "C")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_agent_executions", x => x.id);
                    table.UniqueConstraint("AK_agent_executions_task_execution_id_id", x => new { x.task_execution_id, x.id });
                    table.CheckConstraint("ck_agent_executions_ended_at_ticks", "ended_at_ticks BETWEEN 0 AND 3155378975999999999");
                    table.CheckConstraint("ck_agent_executions_id", "id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_agent_executions_lifecycle", "(status = 'running' AND ended_at_ticks IS NULL) OR (status IN ('completed','failed','interrupted') AND ended_at_ticks IS NOT NULL AND ended_at_ticks >= started_at_ticks)");
                    table.CheckConstraint("ck_agent_executions_reference", "(runtime IS NULL AND resource_kind IS NULL AND external_id IS NULL) OR (runtime IS NOT NULL AND resource_kind IS NOT NULL AND external_id IS NOT NULL AND length(btrim(runtime)) > 0 AND length(btrim(resource_kind)) > 0 AND length(btrim(external_id)) > 0)");
                    table.CheckConstraint("ck_agent_executions_started_at_ticks", "started_at_ticks BETWEEN 0 AND 3155378975999999999");
                    table.CheckConstraint("ck_agent_parent", "parent_agent_execution_id <> id");
                    table.ForeignKey(
                        name: "FK_agent_executions_agent_executions_task_execution_id_parent_~",
                        columns: x => new { x.task_execution_id, x.parent_agent_execution_id },
                        principalTable: "agent_executions",
                        principalColumns: new[] { "task_execution_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_agent_executions_task_executions_task_execution_id",
                        column: x => x.task_execution_id,
                        principalTable: "task_executions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "observations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_execution_id = table.Column<Guid>(type: "uuid", nullable: false),
                    agent_execution_id = table.Column<Guid>(type: "uuid", nullable: true),
                    observed_at_ticks = table.Column<long>(type: "bigint", nullable: false),
                    recorded_at_ticks = table.Column<long>(type: "bigint", nullable: false),
                    sequence = table.Column<long>(type: "bigint", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    summary = table.Column<string>(type: "text", nullable: false),
                    runtime = table.Column<string>(type: "text", nullable: true, collation: "C"),
                    resource_kind = table.Column<string>(type: "text", nullable: true, collation: "C"),
                    external_id = table.Column<string>(type: "text", nullable: true, collation: "C"),
                    media_type = table.Column<string>(type: "text", nullable: true),
                    evidence = table.Column<byte[]>(type: "bytea", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_observations", x => x.id);
                    table.CheckConstraint("ck_observation_evidence", "(evidence IS NULL AND media_type IS NULL) OR (evidence IS NOT NULL AND octet_length(evidence) > 0 AND media_type IS NOT NULL AND length(btrim(media_type)) > 0)");
                    table.CheckConstraint("ck_observation_kind", "kind IN ('message','toolinvocation','toolresult','statustransition','error','approval','testexecution','measurement')");
                    table.CheckConstraint("ck_observation_sequence", "sequence > 0");
                    table.CheckConstraint("ck_observation_summary", "length(btrim(summary)) > 0");
                    table.CheckConstraint("ck_observations_id", "id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_observations_observed_at_ticks", "observed_at_ticks BETWEEN 0 AND 3155378975999999999");
                    table.CheckConstraint("ck_observations_recorded_at_ticks", "recorded_at_ticks BETWEEN 0 AND 3155378975999999999");
                    table.CheckConstraint("ck_observations_reference", "(runtime IS NULL AND resource_kind IS NULL AND external_id IS NULL) OR (runtime IS NOT NULL AND resource_kind IS NOT NULL AND external_id IS NOT NULL AND length(btrim(runtime)) > 0 AND length(btrim(resource_kind)) > 0 AND length(btrim(external_id)) > 0)");
                    table.ForeignKey(
                        name: "FK_observations_agent_executions_task_execution_id_agent_execu~",
                        columns: x => new { x.task_execution_id, x.agent_execution_id },
                        principalTable: "agent_executions",
                        principalColumns: new[] { "task_execution_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_observations_task_executions_task_execution_id",
                        column: x => x.task_execution_id,
                        principalTable: "task_executions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_agent_executions_task_execution_id_parent_agent_execution_id",
                table: "agent_executions",
                columns: new[] { "task_execution_id", "parent_agent_execution_id" });

            migrationBuilder.CreateIndex(
                name: "ix_agents_timeline",
                table: "agent_executions",
                columns: new[] { "task_execution_id", "started_at_ticks", "id" });

            migrationBuilder.CreateIndex(
                name: "ux_agent_executions_runtime_reference",
                table: "agent_executions",
                columns: new[] { "runtime", "resource_kind", "external_id" },
                unique: true,
                filter: "runtime IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_conversations_runtime_reference",
                table: "conversations",
                columns: new[] { "runtime", "resource_kind", "external_id" },
                unique: true,
                filter: "runtime IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_observations_task_execution_id_agent_execution_id",
                table: "observations",
                columns: new[] { "task_execution_id", "agent_execution_id" });

            migrationBuilder.CreateIndex(
                name: "ix_observations_timeline",
                table: "observations",
                columns: new[] { "task_execution_id", "observed_at_ticks", "sequence" });

            migrationBuilder.CreateIndex(
                name: "ux_observations_runtime_reference",
                table: "observations",
                columns: new[] { "runtime", "resource_kind", "external_id" },
                unique: true,
                filter: "runtime IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_observations_sequence",
                table: "observations",
                columns: new[] { "task_execution_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_task_executions_conversation_id",
                table: "task_executions",
                column: "conversation_id");

            migrationBuilder.CreateIndex(
                name: "ux_task_executions_runtime_reference",
                table: "task_executions",
                columns: new[] { "runtime", "resource_kind", "external_id" },
                unique: true,
                filter: "runtime IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "observations");

            migrationBuilder.DropTable(
                name: "agent_executions");

            migrationBuilder.DropTable(
                name: "task_executions");

            migrationBuilder.DropTable(
                name: "conversations");
        }
    }
}
