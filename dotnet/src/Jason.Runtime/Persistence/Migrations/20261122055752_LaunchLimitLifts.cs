using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jason.Runtime.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LaunchLimitLifts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "launch_limit_lifts",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    local_date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    time_zone = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    actor_type = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    actor_id = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    reason = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_launch_limit_lifts", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_launch_limit_lifts_local_date_time_zone",
                table: "launch_limit_lifts",
                columns: new[] { "local_date", "time_zone" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "launch_limit_lifts");
        }
    }
}
