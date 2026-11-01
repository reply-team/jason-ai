using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jason.Runtime.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CampaignManagerLoop : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "manager_event_watermark",
                table: "campaigns",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "manager_review_anchor",
                table: "campaigns",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "manager_review_seconds",
                table: "campaigns",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_work_items_one_open_check_in_per_campaign",
                table: "work_items",
                column: "campaign_id",
                unique: true,
                filter: "role = 'manager' AND created_by_type = 'system' AND status IN ('created','scheduled','processing','awaiting_approval')");

            migrationBuilder.CreateIndex(
                name: "ix_journal_campaign_id_id",
                table: "journal",
                columns: new[] { "campaign_id", "id" });

            // An installation upgrading into the loop owes no review of its history: every campaign starts
            // level with the chronicle as it stands, so the first thing any manager is asked about is something
            // that happened after the upgrade.
            migrationBuilder.Sql(
                "UPDATE campaigns SET manager_event_watermark = COALESCE((SELECT MAX(id) FROM journal), 0);");

            // And a campaign that is already live gets an anchor now, or it would never be due: the cadence is
            // measured from the campaign going live, and one that went live before this migration existed has
            // no such moment on record. The first review after an upgrade therefore falls one interval from
            // here, which is what the cadence means everywhere else.
            migrationBuilder.Sql(
                "UPDATE campaigns SET manager_review_anchor = strftime('%Y-%m-%d %H:%M:%S', 'now') WHERE status = 'active';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_work_items_one_open_check_in_per_campaign",
                table: "work_items");

            migrationBuilder.DropIndex(
                name: "ix_journal_campaign_id_id",
                table: "journal");

            migrationBuilder.DropColumn(
                name: "manager_event_watermark",
                table: "campaigns");

            migrationBuilder.DropColumn(
                name: "manager_review_anchor",
                table: "campaigns");

            migrationBuilder.DropColumn(
                name: "manager_review_seconds",
                table: "campaigns");
        }
    }
}
