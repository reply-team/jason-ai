using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jason.Runtime.Persistence.Migrations
{
    /// <summary>
    /// A send seen to have gone out is said once per campaign, under the key of the work item that accepted it: a unique
    /// index of its own on <c>journal</c>, filtered to <c>message_sent_observed</c>, beside the inbound read's and the
    /// events store's, which are left as they were. Nothing is rebuilt and no row is touched.
    /// </summary>
    public partial class SentFacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_journal_campaign_kind_key_sends",
                table: "journal",
                columns: new[] { "campaign_id", "kind", "key" },
                unique: true,
                filter: "kind = 'message_sent_observed'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_journal_campaign_kind_key_sends",
                table: "journal");
        }
    }
}
