using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jason.Runtime.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CampaignSendModes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "send_mode_cold",
                table: "campaigns",
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "send_mode_cold_set_by",
                table: "campaigns",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "send_mode_warm",
                table: "campaigns",
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "send_mode_warm_set_by",
                table: "campaigns",
                type: "TEXT",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "send_mode_cold",
                table: "campaigns");

            migrationBuilder.DropColumn(
                name: "send_mode_cold_set_by",
                table: "campaigns");

            migrationBuilder.DropColumn(
                name: "send_mode_warm",
                table: "campaigns");

            migrationBuilder.DropColumn(
                name: "send_mode_warm_set_by",
                table: "campaigns");
        }
    }
}
