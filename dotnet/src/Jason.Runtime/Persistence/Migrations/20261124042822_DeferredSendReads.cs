using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jason.Runtime.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DeferredSendReads : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_journal_campaign_kind_key_unsent",
                table: "journal",
                columns: new[] { "campaign_id", "kind", "key" },
                unique: true,
                filter: "kind = 'message_unsent_observed'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_journal_campaign_kind_key_unsent",
                table: "journal");
        }
    }
}
