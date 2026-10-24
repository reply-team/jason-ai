using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jason.Runtime.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WorkItemWaits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "work_item_waits",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    work_item_id = table.Column<int>(type: "INTEGER", nullable: false),
                    attempt_id = table.Column<int>(type: "INTEGER", nullable: true),
                    mode = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    until = table.Column<DateTime>(type: "TEXT", nullable: true),
                    deadline = table.Column<DateTime>(type: "TEXT", nullable: true),
                    status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    opened_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    released_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    release_reason = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    resumed_by_attempt_id = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_work_item_waits", x => x.id);
                    table.ForeignKey(
                        name: "fk_work_item_waits_attempts_attempt_id",
                        column: x => x.attempt_id,
                        principalTable: "attempts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_work_item_waits_attempts_resumed_by_attempt_id",
                        column: x => x.resumed_by_attempt_id,
                        principalTable: "attempts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_work_item_waits_work_items_work_item_id",
                        column: x => x.work_item_id,
                        principalTable: "work_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "work_item_wait_items",
                columns: table => new
                {
                    wait_id = table.Column<int>(type: "INTEGER", nullable: false),
                    child_work_item_id = table.Column<int>(type: "INTEGER", nullable: false),
                    position = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_work_item_wait_items", x => new { x.wait_id, x.child_work_item_id });
                    table.ForeignKey(
                        name: "fk_work_item_wait_items_work_item_waits_wait_id",
                        column: x => x.wait_id,
                        principalTable: "work_item_waits",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_work_item_wait_items_work_items_child_work_item_id",
                        column: x => x.child_work_item_id,
                        principalTable: "work_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_work_item_wait_items_child_work_item_id",
                table: "work_item_wait_items",
                column: "child_work_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_work_item_waits_attempt_id",
                table: "work_item_waits",
                column: "attempt_id");

            migrationBuilder.CreateIndex(
                name: "ix_work_item_waits_one_open_per_item",
                table: "work_item_waits",
                column: "work_item_id",
                unique: true,
                filter: "status = 'open'");

            migrationBuilder.CreateIndex(
                name: "ix_work_item_waits_resumed_by_attempt_id",
                table: "work_item_waits",
                column: "resumed_by_attempt_id");

            migrationBuilder.CreateIndex(
                name: "ix_work_item_waits_work_item_id_status",
                table: "work_item_waits",
                columns: new[] { "work_item_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "work_item_wait_items");

            migrationBuilder.DropTable(
                name: "work_item_waits");
        }
    }
}
