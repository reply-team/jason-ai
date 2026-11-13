using System;
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
            // A channel route is a table of its own, and campaign_routes is left exactly as it was: an older executable
            // started on this database reads every row of campaign_routes as an operation's own route or the default, so
            // a channel route kept there would stand in for the operation's route or overwrite it. Kept here, it is a
            // table the older executable never reads, and it routes as it did. Up is a plain create, rebuilding nothing.
            migrationBuilder.CreateTable(
                name: "campaign_channel_routes",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    campaign_id = table.Column<int>(type: "INTEGER", nullable: false),
                    operation = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    channel = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    plugin_id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    binding_json = table.Column<string>(type: "TEXT", nullable: true),
                    updated_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_campaign_channel_routes", x => x.id);
                    table.CheckConstraint("ck_campaign_channel_routes_binding_json", "binding_json IS NULL OR json_valid(binding_json)");
                    table.ForeignKey(
                        name: "fk_campaign_channel_routes_campaigns_campaign_id",
                        column: x => x.campaign_id,
                        principalTable: "campaigns",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_campaign_channel_routes_campaign_id_operation_channel",
                table: "campaign_channel_routes",
                columns: new[] { "campaign_id", "operation", "channel" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Down drops the new table and with it every channel route, a stated loss: what is left is the database as the
            // previous migration had it, whose campaign_routes this migration never touched, so the operations' own routes
            // and the defaults route as they did before any channel route was set.
            migrationBuilder.DropTable(
                name: "campaign_channel_routes");
        }
    }
}
