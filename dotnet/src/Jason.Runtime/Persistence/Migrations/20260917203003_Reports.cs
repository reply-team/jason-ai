using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jason.Runtime.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Reports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "reports",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    public_id = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    reporter_type = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    reporter_id = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    effect = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    tool = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    provider = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    account = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    occurred_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    observed_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    campaign_id = table.Column<int>(type: "INTEGER", nullable: true),
                    contact_id = table.Column<int>(type: "INTEGER", nullable: true),
                    work_item_id = table.Column<int>(type: "INTEGER", nullable: true),
                    operation = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    operation_known = table.Column<bool>(type: "INTEGER", nullable: false),
                    contact_in_campaign = table.Column<bool>(type: "INTEGER", nullable: true),
                    summary = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    assertion_json = table.Column<string>(type: "TEXT", nullable: false),
                    assertion_hash = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    idempotency_key = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    reason = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    received_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reports", x => x.id);
                    table.CheckConstraint("ck_reports_assertion_json", "json_valid(assertion_json)");
                    table.ForeignKey(
                        name: "fk_reports_campaigns_campaign_id",
                        column: x => x.campaign_id,
                        principalTable: "campaigns",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_reports_contacts_contact_id",
                        column: x => x.contact_id,
                        principalTable: "contacts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_reports_work_items_work_item_id",
                        column: x => x.work_item_id,
                        principalTable: "work_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_reports_campaign_id",
                table: "reports",
                column: "campaign_id");

            migrationBuilder.CreateIndex(
                name: "ix_reports_contact_id",
                table: "reports",
                column: "contact_id");

            migrationBuilder.CreateIndex(
                name: "ix_reports_one_per_reporter_content",
                table: "reports",
                columns: new[] { "reporter_type", "reporter_id", "assertion_hash" },
                unique: true,
                filter: "idempotency_key IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_reports_one_per_reporter_key",
                table: "reports",
                columns: new[] { "reporter_type", "reporter_id", "idempotency_key" },
                unique: true,
                filter: "idempotency_key IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_reports_public_id",
                table: "reports",
                column: "public_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_reports_received_at",
                table: "reports",
                column: "received_at");

            migrationBuilder.CreateIndex(
                name: "ix_reports_work_item_id",
                table: "reports",
                column: "work_item_id");

            // An admitted report is somebody's word about something that already happened. There is nothing in it
            // for the runtime to correct, and a runtime able to rewrite one could make a reporter say what it
            // wanted; the database is where that stops being a matter of trust.
            migrationBuilder.Sql("CREATE TRIGGER reports_no_update BEFORE UPDATE ON reports BEGIN SELECT RAISE(ABORT, 'an admitted report is immutable'); END;");
            migrationBuilder.Sql("CREATE TRIGGER reports_no_delete BEFORE DELETE ON reports BEGIN SELECT RAISE(ABORT, 'an admitted report is immutable'); END;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS reports_no_update;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS reports_no_delete;");

            migrationBuilder.DropTable(
                name: "reports");
        }
    }
}
