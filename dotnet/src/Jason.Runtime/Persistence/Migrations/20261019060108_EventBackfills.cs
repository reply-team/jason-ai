using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jason.Runtime.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EventBackfills : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "signal_at",
                table: "event_sources",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "event_backfills",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    public_id = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    source_id = table.Column<int>(type: "INTEGER", nullable: false),
                    range_from = table.Column<DateTime>(type: "TEXT", nullable: false),
                    range_to = table.Column<DateTime>(type: "TEXT", nullable: false),
                    status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    cursor = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    failures = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    attempted_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    requested_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    actor_type = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    actor_id = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    reason = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    stored = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    duplicates = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    ended_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    last_code = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    last_class = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_event_backfills", x => x.id);
                    table.ForeignKey(
                        name: "fk_event_backfills_event_sources_source_id",
                        column: x => x.source_id,
                        principalTable: "event_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_event_backfills_one_pending_range",
                table: "event_backfills",
                columns: new[] { "source_id", "range_from", "range_to" },
                unique: true,
                filter: "status = 'pending'");

            migrationBuilder.CreateIndex(
                name: "ix_event_backfills_public_id",
                table: "event_backfills",
                column: "public_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_event_backfills_source_id_status",
                table: "event_backfills",
                columns: new[] { "source_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "event_backfills");

            migrationBuilder.DropColumn(
                name: "signal_at",
                table: "event_sources");
        }
    }
}
