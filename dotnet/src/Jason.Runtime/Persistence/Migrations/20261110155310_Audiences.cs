using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jason.Runtime.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Audiences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audiences",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    public_id = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    campaign_id = table.Column<int>(type: "INTEGER", nullable: false),
                    number = table.Column<int>(type: "INTEGER", nullable: false),
                    definition_json = table.Column<string>(type: "TEXT", nullable: false),
                    definition_hash = table.Column<string>(type: "TEXT", maxLength: 71, nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audiences", x => x.id);
                    table.CheckConstraint("ck_audiences_definition_json", "json_valid(definition_json)");
                    table.ForeignKey(
                        name: "fk_audiences_campaigns_campaign_id",
                        column: x => x.campaign_id,
                        principalTable: "campaigns",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_audiences_campaign_id_definition_hash",
                table: "audiences",
                columns: new[] { "campaign_id", "definition_hash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_audiences_campaign_id_number",
                table: "audiences",
                columns: new[] { "campaign_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_audiences_public_id",
                table: "audiences",
                column: "public_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audiences");
        }
    }
}
