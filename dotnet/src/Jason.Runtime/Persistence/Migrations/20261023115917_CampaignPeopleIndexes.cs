using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jason.Runtime.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CampaignPeopleIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_work_items_campaign_id_contact_id",
                table: "work_items",
                columns: new[] { "campaign_id", "contact_id" });

            migrationBuilder.CreateIndex(
                name: "ix_journal_contacts_added_campaign_ts",
                table: "journal",
                columns: new[] { "campaign_id", "ts" },
                filter: "kind = 'contacts_added'");

            migrationBuilder.CreateIndex(
                name: "ix_journal_contacts_removed_campaign_ts",
                table: "journal",
                columns: new[] { "campaign_id", "ts" },
                filter: "kind = 'contacts_removed'");

            migrationBuilder.CreateIndex(
                name: "ix_journal_suppression_added_ts",
                table: "journal",
                column: "ts",
                filter: "kind = 'suppression_added'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_work_items_campaign_id_contact_id",
                table: "work_items");

            migrationBuilder.DropIndex(
                name: "ix_journal_contacts_added_campaign_ts",
                table: "journal");

            migrationBuilder.DropIndex(
                name: "ix_journal_contacts_removed_campaign_ts",
                table: "journal");

            migrationBuilder.DropIndex(
                name: "ix_journal_suppression_added_ts",
                table: "journal");
        }
    }
}
