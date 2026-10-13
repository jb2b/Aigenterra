using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aigenterra.Infrastructure.Executions.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class RuntimeProjectionGenerations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "runtime_projection_generations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    runtime_namespace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    base_generation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    versions = table.Column<string>(type: "jsonb", nullable: false),
                    state = table.Column<string>(type: "text", nullable: false),
                    input_count = table.Column<int>(type: "integer", nullable: false),
                    manifest_sha256 = table.Column<string>(type: "text", nullable: false),
                    projection_sha256 = table.Column<string>(type: "text", nullable: true),
                    document = table.Column<string>(type: "jsonb", nullable: true),
                    created_at_ticks = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_runtime_projection_generations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "runtime_projection_active",
                columns: table => new
                {
                    runtime_namespace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    generation_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_runtime_projection_active", x => x.runtime_namespace_id);
                    table.ForeignKey(
                        name: "FK_runtime_projection_active_runtime_projection_generations_ge~",
                        column: x => x.generation_id,
                        principalTable: "runtime_projection_generations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "runtime_projection_changes",
                columns: table => new
                {
                    runtime_namespace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    generation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    previous_generation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    projection_sha256 = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_runtime_projection_changes", x => new { x.runtime_namespace_id, x.revision });
                    table.ForeignKey(
                        name: "FK_runtime_projection_changes_runtime_projection_generations_g~",
                        column: x => x.generation_id,
                        principalTable: "runtime_projection_generations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "runtime_projection_inputs",
                columns: table => new
                {
                    generation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    capture_id = table.Column<Guid>(type: "uuid", nullable: false),
                    envelope_fingerprint = table.Column<string>(type: "text", nullable: false),
                    complete = table.Column<bool>(type: "boolean", nullable: false),
                    result = table.Column<string>(type: "jsonb", nullable: true),
                    result_sha256 = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_runtime_projection_inputs", x => new { x.generation_id, x.capture_id });
                    table.ForeignKey(
                        name: "FK_runtime_projection_inputs_capture_sources_capture_id",
                        column: x => x.capture_id,
                        principalTable: "capture_sources",
                        principalColumn: "capture_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_runtime_projection_inputs_runtime_projection_generations_ge~",
                        column: x => x.generation_id,
                        principalTable: "runtime_projection_generations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_runtime_projection_active_generation_id",
                table: "runtime_projection_active",
                column: "generation_id");

            migrationBuilder.CreateIndex(
                name: "IX_runtime_projection_changes_generation_id",
                table: "runtime_projection_changes",
                column: "generation_id");

            migrationBuilder.CreateIndex(
                name: "IX_runtime_projection_generations_runtime_namespace_id_state",
                table: "runtime_projection_generations",
                columns: new[] { "runtime_namespace_id", "state" });

            migrationBuilder.CreateIndex(
                name: "IX_runtime_projection_inputs_capture_id",
                table: "runtime_projection_inputs",
                column: "capture_id");
            migrationBuilder.Sql("""
                CREATE FUNCTION protect_runtime_projection_generation() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP = 'DELETE' OR OLD.state = 'Ready' THEN RAISE EXCEPTION 'Completed projection generations are immutable'; END IF;
                    IF NEW.id <> OLD.id OR NEW.runtime_namespace_id <> OLD.runtime_namespace_id
                       OR NEW.versions <> OLD.versions OR NEW.input_count <> OLD.input_count
                       OR NEW.manifest_sha256 <> OLD.manifest_sha256
                       OR NEW.base_generation_id IS DISTINCT FROM OLD.base_generation_id
                       OR NEW.created_at_ticks <> OLD.created_at_ticks THEN RAISE EXCEPTION 'Frozen manifest cannot change'; END IF;
                    RETURN NEW;
                END $$;
                CREATE TRIGGER projection_generation_immutable BEFORE UPDATE OR DELETE ON runtime_projection_generations
                    FOR EACH ROW EXECUTE FUNCTION protect_runtime_projection_generation();
                CREATE FUNCTION protect_runtime_projection_input() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP = 'DELETE' THEN RAISE EXCEPTION 'Frozen projection inputs cannot be deleted'; END IF;
                    IF TG_OP = 'UPDATE' THEN
                        IF OLD.complete OR NEW.generation_id <> OLD.generation_id OR NEW.capture_id <> OLD.capture_id
                           OR NEW.envelope_fingerprint <> OLD.envelope_fingerprint THEN RAISE EXCEPTION 'Frozen projection input is immutable'; END IF;
                    END IF;
                    IF (SELECT state FROM runtime_projection_generations WHERE id = NEW.generation_id) <> 'Building'
                        THEN RAISE EXCEPTION 'Projection generation is closed'; END IF;
                    RETURN NEW;
                END $$;
                CREATE TRIGGER projection_input_immutable BEFORE INSERT OR UPDATE OR DELETE ON runtime_projection_inputs
                    FOR EACH ROW EXECUTE FUNCTION protect_runtime_projection_input();
                CREATE TRIGGER projection_changes_append_only BEFORE UPDATE OR DELETE ON runtime_projection_changes
                    FOR EACH ROW EXECUTE FUNCTION reject_raw_capture_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "runtime_projection_active");

            migrationBuilder.DropTable(
                name: "runtime_projection_changes");

            migrationBuilder.DropTable(
                name: "runtime_projection_inputs");

            migrationBuilder.DropTable(
                name: "runtime_projection_generations");
            migrationBuilder.Sql("DROP FUNCTION protect_runtime_projection_input(); DROP FUNCTION protect_runtime_projection_generation();");
        }
    }
}
