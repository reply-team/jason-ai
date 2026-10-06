using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jason.Runtime.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InboundSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "inbound_failures",
                table: "campaigns",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "inbound_offset",
                table: "campaigns",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            // The read still outstanding is a work item that exists. Added with its reference in one statement, which
            // SQLite allows for a column whose default is null: the foreign key EF would add afterwards rebuilds the whole
            // table outside this migration's transaction, and every table that refers to a campaign would be rebuilt
            // under it.
            migrationBuilder.Sql(
                "ALTER TABLE \"campaigns\" ADD \"inbound_sync_item_id\" INTEGER NULL "
                + "CONSTRAINT \"fk_campaigns_work_items_inbound_sync_item_id\" REFERENCES \"work_items\" (\"id\") ON DELETE RESTRICT;");

            migrationBuilder.AddColumn<DateTime>(
                name: "inbound_synced_at",
                table: "campaigns",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "inbound_watermark",
                table: "campaigns",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_journal_campaign_kind_key",
                table: "journal",
                columns: new[] { "campaign_id", "kind", "key" },
                unique: true,
                filter: "kind = 'inbound_reply_observed'");

            migrationBuilder.CreateIndex(
                name: "ix_external_ids_plugin_kind_value",
                table: "external_ids",
                columns: new[] { "plugin_id", "kind", "value" });

            migrationBuilder.CreateIndex(
                name: "ix_campaigns_inbound_sync_item_id",
                table: "campaigns",
                column: "inbound_sync_item_id");

            // An installation upgrading into the inbound read owes no read of its history: a campaign that is live, or
            // paused and able to go live again, starts from now, as one that goes live after the upgrade starts from
            // then. A draft and an archived campaign have nothing to start from until they go live.
            migrationBuilder.Sql(
                "UPDATE campaigns SET inbound_watermark = strftime('%Y-%m-%d %H:%M:%S', 'now') WHERE status IN ('active', 'paused');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_campaigns_work_items_inbound_sync_item_id",
                table: "campaigns");

            migrationBuilder.DropIndex(
                name: "ix_journal_campaign_kind_key",
                table: "journal");

            migrationBuilder.DropIndex(
                name: "ix_external_ids_plugin_kind_value",
                table: "external_ids");

            migrationBuilder.DropIndex(
                name: "ix_campaigns_inbound_sync_item_id",
                table: "campaigns");

            migrationBuilder.DropColumn(
                name: "inbound_failures",
                table: "campaigns");

            migrationBuilder.DropColumn(
                name: "inbound_offset",
                table: "campaigns");

            migrationBuilder.DropColumn(
                name: "inbound_sync_item_id",
                table: "campaigns");

            migrationBuilder.DropColumn(
                name: "inbound_synced_at",
                table: "campaigns");

            migrationBuilder.DropColumn(
                name: "inbound_watermark",
                table: "campaigns");
        }
    }
}
