using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jason.Runtime.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AudienceReserve : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "audience_threshold",
                table: "campaigns",
                type: "INTEGER",
                nullable: true);

            // Every existing campaign starts at nothing, which costs nothing: with no threshold set, the step never looks at
            // it, and the line that sets one is the first change it counts after.
            migrationBuilder.AddColumn<int>(
                name: "audience_watermark",
                table: "campaigns",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "ix_journal_audience_reserve_low_campaign_id",
                table: "journal",
                columns: new[] { "campaign_id", "id" },
                filter: "kind = 'audience_reserve_low'");

            migrationBuilder.CreateIndex(
                name: "ix_journal_audience_reserve_recovered_campaign_id",
                table: "journal",
                columns: new[] { "campaign_id", "id" },
                filter: "kind = 'audience_reserve_recovered'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_journal_audience_reserve_low_campaign_id",
                table: "journal");

            migrationBuilder.DropIndex(
                name: "ix_journal_audience_reserve_recovered_campaign_id",
                table: "journal");

            // Dropped in place rather than by rebuilding the table, which would lose the triggers an earlier migration
            // wrote on campaigns and, with foreign keys on, the rows that are deleted with a campaign.
            migrationBuilder.Sql("ALTER TABLE campaigns DROP COLUMN audience_watermark;");
            migrationBuilder.Sql("ALTER TABLE campaigns DROP COLUMN audience_threshold;");
        }
    }
}
