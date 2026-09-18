using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jason.Runtime.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RoleNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "role_notes",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    campaign_id = table.Column<int>(type: "INTEGER", nullable: false),
                    role = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    note_json = table.Column<string>(type: "TEXT", nullable: false, defaultValueSql: "'{}'"),
                    note_hash = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    note_bytes = table.Column<long>(type: "INTEGER", nullable: false),
                    updated_by_type = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    updated_by_id = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    updated_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_role_notes", x => x.id);
                    table.CheckConstraint("ck_role_notes_note_json", "json_valid(note_json)");
                    table.ForeignKey(
                        name: "fk_role_notes_campaigns_campaign_id",
                        column: x => x.campaign_id,
                        principalTable: "campaigns",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_role_notes_one_per_campaign_role",
                table: "role_notes",
                columns: new[] { "campaign_id", "role" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "role_notes");
        }
    }
}
