using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jason.Runtime.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Approvals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "approvals",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    public_id = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    work_item_id = table.Column<int>(type: "INTEGER", nullable: false),
                    campaign_id = table.Column<int>(type: "INTEGER", nullable: false),
                    operation = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    operation_version = table.Column<int>(type: "INTEGER", nullable: false),
                    subject_json = table.Column<string>(type: "TEXT", nullable: false),
                    subject_hash = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    preview_json = table.Column<string>(type: "TEXT", nullable: false),
                    plugin_id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    binding_identity = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                    route_scope = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    plugin_snapshot_id = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    routing_snapshot_id = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    reason = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    requested_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    decided_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    decided_by_type = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    decided_by_id = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    decision_reason = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_approvals", x => x.id);
                    table.CheckConstraint("ck_approvals_preview_json", "json_valid(preview_json)");
                    table.CheckConstraint("ck_approvals_subject_json", "json_valid(subject_json)");
                    table.ForeignKey(
                        name: "fk_approvals_work_items_work_item_id",
                        column: x => x.work_item_id,
                        principalTable: "work_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_approvals_campaign_id",
                table: "approvals",
                column: "campaign_id");

            migrationBuilder.CreateIndex(
                name: "ix_approvals_one_pending_per_item",
                table: "approvals",
                column: "work_item_id",
                unique: true,
                filter: "status = 'pending'");

            migrationBuilder.CreateIndex(
                name: "ix_approvals_public_id",
                table: "approvals",
                column: "public_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_approvals_status_requested_at",
                table: "approvals",
                columns: new[] { "status", "requested_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "approvals");
        }
    }
}
