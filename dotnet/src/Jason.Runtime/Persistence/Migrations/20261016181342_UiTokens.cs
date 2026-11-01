using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jason.Runtime.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UiTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ui_tokens",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    public_id = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    token_hash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    issued_to = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    issued_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    last_used_at = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ui_tokens", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_ui_tokens_public_id",
                table: "ui_tokens",
                column: "public_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ui_tokens_token_hash",
                table: "ui_tokens",
                column: "token_hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ui_tokens");
        }
    }
}
