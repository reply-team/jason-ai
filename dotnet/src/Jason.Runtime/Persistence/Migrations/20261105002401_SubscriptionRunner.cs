using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jason.Runtime.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SubscriptionRunner : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "subscription_cursors",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    kind = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    after_line = table.Column<int>(type: "INTEGER", nullable: false),
                    revision = table.Column<long>(type: "INTEGER", nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    updated_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_subscription_cursors", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "subscription_runs",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    public_id = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    kind = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    line_id = table.Column<int>(type: "INTEGER", nullable: false),
                    line_public_id = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    handlers_json = table.Column<string>(type: "TEXT", nullable: false),
                    envelope_json = table.Column<string>(type: "TEXT", nullable: true),
                    failed_plugin_id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    failure_class = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    failure_code = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    started_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ended_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    retry_at = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_subscription_runs", x => x.id);
                    table.CheckConstraint("ck_subscription_runs_envelope_json", "envelope_json IS NULL OR json_valid(envelope_json)");
                    table.CheckConstraint("ck_subscription_runs_handlers_json", "json_valid(handlers_json)");
                });

            migrationBuilder.CreateIndex(
                name: "ix_subscription_cursors_kind",
                table: "subscription_cursors",
                column: "kind",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_subscription_runs_kind_id",
                table: "subscription_runs",
                columns: new[] { "kind", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_subscription_runs_one_running_per_kind",
                table: "subscription_runs",
                column: "kind",
                unique: true,
                filter: "status = 'running'");

            migrationBuilder.CreateIndex(
                name: "ix_subscription_runs_public_id",
                table: "subscription_runs",
                column: "public_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "subscription_cursors");

            migrationBuilder.DropTable(
                name: "subscription_runs");
        }
    }
}
