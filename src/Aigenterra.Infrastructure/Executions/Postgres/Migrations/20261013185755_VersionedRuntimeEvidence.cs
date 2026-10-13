using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Aigenterra.Infrastructure.Executions.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class VersionedRuntimeEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "capture_sources",
                columns: table => new
                {
                    capture_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ingest_position = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    envelope = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_capture_sources", x => x.capture_id);
                    table.ForeignKey(
                        name: "FK_capture_sources_raw_captures_capture_id",
                        column: x => x.capture_id,
                        principalTable: "raw_captures",
                        principalColumn: "capture_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_capture_sources_ingest_position",
                table: "capture_sources",
                column: "ingest_position",
                unique: true);
            migrationBuilder.Sql("""
                CREATE TRIGGER capture_sources_append_only BEFORE UPDATE OR DELETE ON capture_sources
                FOR EACH ROW EXECUTE FUNCTION reject_raw_capture_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "capture_sources");
        }
    }
}
