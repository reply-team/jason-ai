using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jason.Runtime.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ResponderWatermark : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "responder_watermark",
                table: "campaigns",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            // An installation upgrading into the respond step owes no answer to a reply said before the upgrade: every
            // campaign starts level with the chronicle as it stands, so the first reply that wakes a responder is one said
            // after it. A campaign made later starts at nothing, and has no older line.
            migrationBuilder.Sql(
                "UPDATE campaigns SET responder_watermark = COALESCE((SELECT MAX(id) FROM journal), 0);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Dropped in place rather than by rebuilding the table, which would lose the triggers an earlier migration
            // wrote on campaigns and, with foreign keys on, the rows that are deleted with a campaign.
            migrationBuilder.Sql("ALTER TABLE campaigns DROP COLUMN responder_watermark;");
        }
    }
}
