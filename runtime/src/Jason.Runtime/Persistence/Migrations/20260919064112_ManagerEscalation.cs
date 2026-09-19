using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jason.Runtime.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ManagerEscalation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "decisions",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    public_id = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    campaign_id = table.Column<int>(type: "INTEGER", nullable: false),
                    work_item_id = table.Column<int>(type: "INTEGER", nullable: false),
                    attempt_id = table.Column<int>(type: "INTEGER", nullable: false),
                    question = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    options_json = table.Column<string>(type: "TEXT", nullable: true),
                    references_json = table.Column<string>(type: "TEXT", nullable: true),
                    status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    raised_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    answer = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    chosen_option = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    answered_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    answered_by_type = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    answered_by_id = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    answer_journal_entry_id = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_decisions", x => x.id);
                    table.CheckConstraint("ck_decisions_options_json", "options_json IS NULL OR json_valid(options_json)");
                    table.CheckConstraint("ck_decisions_references_json", "references_json IS NULL OR json_valid(references_json)");
                    table.ForeignKey(
                        name: "fk_decisions_attempts_attempt_id",
                        column: x => x.attempt_id,
                        principalTable: "attempts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_decisions_campaigns_campaign_id",
                        column: x => x.campaign_id,
                        principalTable: "campaigns",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_decisions_work_items_work_item_id",
                        column: x => x.work_item_id,
                        principalTable: "work_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_decisions_answer_journal_entry_id",
                table: "decisions",
                column: "answer_journal_entry_id");

            migrationBuilder.CreateIndex(
                name: "ix_decisions_attempt_id",
                table: "decisions",
                column: "attempt_id");

            migrationBuilder.CreateIndex(
                name: "ix_decisions_campaign_id",
                table: "decisions",
                column: "campaign_id");

            migrationBuilder.CreateIndex(
                name: "ix_decisions_public_id",
                table: "decisions",
                column: "public_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_decisions_status_raised_at",
                table: "decisions",
                columns: new[] { "status", "raised_at" });

            migrationBuilder.CreateIndex(
                name: "ix_decisions_work_item_id",
                table: "decisions",
                column: "work_item_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "decisions");
        }
    }
}
