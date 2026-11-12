using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jason.Runtime.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ChannelRoutes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // A route may name a channel under its operation. Every route stored before this names none, so each keeps its
            // meaning as it stands: no row is rewritten, the unique index over (campaign, operation) becomes the filtered one
            // over the rows without a channel, and the rows with one get theirs. The check is added by SQLite's rebuild of
            // the table, which copies every row, its id included, into a table that has the check.
            migrationBuilder.DropIndex(
                name: "ix_campaign_routes_campaign_id_operation",
                table: "campaign_routes");

            migrationBuilder.AddColumn<string>(
                name: "channel",
                table: "campaign_routes",
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_campaign_routes_one_per_channel",
                table: "campaign_routes",
                columns: new[] { "campaign_id", "operation", "channel" },
                unique: true,
                filter: "channel IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_campaign_routes_one_per_operation",
                table: "campaign_routes",
                columns: new[] { "campaign_id", "operation" },
                unique: true,
                filter: "channel IS NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_campaign_routes_channel_on_an_operation",
                table: "campaign_routes",
                sql: "channel IS NULL OR operation IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_campaign_routes_one_per_channel",
                table: "campaign_routes");

            migrationBuilder.DropIndex(
                name: "ix_campaign_routes_one_per_operation",
                table: "campaign_routes");

            migrationBuilder.DropCheckConstraint(
                name: "ck_campaign_routes_channel_on_an_operation",
                table: "campaign_routes");

            migrationBuilder.DropColumn(
                name: "channel",
                table: "campaign_routes");

            migrationBuilder.CreateIndex(
                name: "ix_campaign_routes_campaign_id_operation",
                table: "campaign_routes",
                columns: new[] { "campaign_id", "operation" },
                unique: true);
        }
    }
}
