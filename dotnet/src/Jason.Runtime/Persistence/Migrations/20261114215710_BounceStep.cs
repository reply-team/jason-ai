using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jason.Runtime.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BounceStep : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "bounce_watermark",
                table: "campaigns",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            // An installation upgrading into the bounce step owes no read to a bounce said before the upgrade: every
            // campaign starts level with the chronicle as it stands, so the first bounce that asks for a read is one said
            // after it. A campaign made later starts at nothing, and has no older line.
            migrationBuilder.Sql(
                "UPDATE campaigns SET bounce_watermark = COALESCE((SELECT MAX(id) FROM journal), 0);");

            migrationBuilder.CreateIndex(
                name: "ix_journal_campaign_kind_key_bounces",
                table: "journal",
                columns: new[] { "campaign_id", "kind", "key" },
                unique: true,
                filter: "kind = 'message_bounce_observed'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_journal_campaign_kind_key_bounces",
                table: "journal");

            // Dropped in place rather than by rebuilding the table, which would lose the triggers an earlier migration
            // wrote on campaigns and, with foreign keys on, the rows that are deleted with a campaign.
            migrationBuilder.Sql("ALTER TABLE campaigns DROP COLUMN bounce_watermark;");
        }
    }
}
