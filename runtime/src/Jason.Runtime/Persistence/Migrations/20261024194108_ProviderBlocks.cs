using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jason.Runtime.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProviderBlocks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "provider_blocks",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    public_id = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    plugin_id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    binding_identity = table.Column<string>(type: "TEXT", maxLength: 71, nullable: false),
                    scope = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    sender_external_id = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    reason = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    link = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    clears_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    opened_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    opened_by_attempt_id = table.Column<int>(type: "INTEGER", nullable: false),
                    last_seen_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    seen_count = table.Column<int>(type: "INTEGER", nullable: false),
                    next_trial_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    trial_work_item_id = table.Column<int>(type: "INTEGER", nullable: true),
                    cleared_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    cleared_by_attempt_id = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_provider_blocks", x => x.id);
                    table.CheckConstraint("ck_provider_blocks_sender", "(scope = 'sender') = (sender_external_id IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_provider_blocks_attempts_cleared_by_attempt_id",
                        column: x => x.cleared_by_attempt_id,
                        principalTable: "attempts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_provider_blocks_attempts_opened_by_attempt_id",
                        column: x => x.opened_by_attempt_id,
                        principalTable: "attempts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_provider_blocks_work_items_trial_work_item_id",
                        column: x => x.trial_work_item_id,
                        principalTable: "work_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            // The block a wait is parked on, added with its reference in one statement once the table it refers to exists:
            // the foreign key EF would add afterwards rebuilds the whole table outside this migration's transaction.
            // Waits written before this version wait on no block.
            migrationBuilder.Sql(
                "ALTER TABLE \"work_item_waits\" ADD \"provider_block_id\" INTEGER NULL "
                + "CONSTRAINT \"fk_work_item_waits_provider_blocks_provider_block_id\" REFERENCES \"provider_blocks\" (\"id\") ON DELETE RESTRICT;");

            migrationBuilder.CreateIndex(
                name: "ix_work_item_waits_provider_block_id_status",
                table: "work_item_waits",
                columns: new[] { "provider_block_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_provider_blocks_cleared_by_attempt_id",
                table: "provider_blocks",
                column: "cleared_by_attempt_id");

            migrationBuilder.CreateIndex(
                name: "ix_provider_blocks_one_open_per_account",
                table: "provider_blocks",
                columns: new[] { "plugin_id", "binding_identity" },
                unique: true,
                filter: "status = 'open' AND scope = 'account'");

            migrationBuilder.CreateIndex(
                name: "ix_provider_blocks_one_open_per_sender",
                table: "provider_blocks",
                columns: new[] { "plugin_id", "binding_identity", "sender_external_id" },
                unique: true,
                filter: "status = 'open' AND scope = 'sender'");

            migrationBuilder.CreateIndex(
                name: "ix_provider_blocks_opened_by_attempt_id",
                table: "provider_blocks",
                column: "opened_by_attempt_id");

            migrationBuilder.CreateIndex(
                name: "ix_provider_blocks_public_id",
                table: "provider_blocks",
                column: "public_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_provider_blocks_status_next_trial_at",
                table: "provider_blocks",
                columns: new[] { "status", "next_trial_at" });

            migrationBuilder.CreateIndex(
                name: "ix_provider_blocks_trial_work_item_id",
                table: "provider_blocks",
                column: "trial_work_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_journal_kind_key_campaign_blocks",
                table: "journal",
                columns: new[] { "kind", "key", "campaign_id" },
                unique: true,
                filter: "kind = 'provider_blocked'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_journal_kind_key_campaign_blocks",
                table: "journal");

            migrationBuilder.DropForeignKey(
                name: "fk_work_item_waits_provider_blocks_provider_block_id",
                table: "work_item_waits");

            migrationBuilder.DropTable(
                name: "provider_blocks");

            migrationBuilder.DropIndex(
                name: "ix_work_item_waits_provider_block_id_status",
                table: "work_item_waits");

            migrationBuilder.DropColumn(
                name: "provider_block_id",
                table: "work_item_waits");
        }
    }
}
