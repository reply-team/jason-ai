using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jason.Runtime.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Audience : Migration
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

            migrationBuilder.CreateTable(
                name: "audiences",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    public_id = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    campaign_id = table.Column<int>(type: "INTEGER", nullable: false),
                    number = table.Column<int>(type: "INTEGER", nullable: false),
                    definition_json = table.Column<string>(type: "TEXT", nullable: false),
                    definition_hash = table.Column<string>(type: "TEXT", maxLength: 71, nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audiences", x => x.id);
                    table.CheckConstraint("ck_audiences_definition_json", "json_valid(definition_json)");
                    table.ForeignKey(
                        name: "fk_audiences_campaigns_campaign_id",
                        column: x => x.campaign_id,
                        principalTable: "campaigns",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "audience_landings",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    campaign_contact_id = table.Column<int>(type: "INTEGER", nullable: false),
                    audience_id = table.Column<int>(type: "INTEGER", nullable: false),
                    search_work_item_id = table.Column<int>(type: "INTEGER", nullable: false),
                    read_work_item_id = table.Column<int>(type: "INTEGER", nullable: false),
                    landed_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audience_landings", x => x.id);
                    table.ForeignKey(
                        name: "fk_audience_landings_audiences_audience_id",
                        column: x => x.audience_id,
                        principalTable: "audiences",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_audience_landings_campaign_contacts_campaign_contact_id",
                        column: x => x.campaign_contact_id,
                        principalTable: "campaign_contacts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_audience_landings_work_items_read_work_item_id",
                        column: x => x.read_work_item_id,
                        principalTable: "work_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_audience_landings_work_items_search_work_item_id",
                        column: x => x.search_work_item_id,
                        principalTable: "work_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "audience_searches",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    campaign_id = table.Column<int>(type: "INTEGER", nullable: false),
                    audience_id = table.Column<int>(type: "INTEGER", nullable: false),
                    search_work_item_id = table.Column<int>(type: "INTEGER", nullable: false),
                    plugin_id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    reference = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    cursor = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    state = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    more = table.Column<bool>(type: "INTEGER", nullable: false),
                    asked = table.Column<int>(type: "INTEGER", nullable: false),
                    found = table.Column<int>(type: "INTEGER", nullable: true),
                    landed = table.Column<int>(type: "INTEGER", nullable: false),
                    quiet_since = table.Column<DateTime>(type: "TEXT", nullable: false),
                    read_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    read_work_item_id = table.Column<int>(type: "INTEGER", nullable: true),
                    failures = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ended_as = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    ended_at = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audience_searches", x => x.id);
                    table.CheckConstraint("ck_audience_searches_ended_as", "(ended_as IS NULL AND ended_at IS NULL) OR (ended_as IN ('finished', 'exhausted', 'stalled', 'cancelled') AND ended_at IS NOT NULL)");
                    table.CheckConstraint("ck_audience_searches_state", "state IS NULL OR state IN ('in_progress', 'finished', 'exhausted', 'paused', 'paused_for_credits', 'paused_for_limit')");
                    table.ForeignKey(
                        name: "fk_audience_searches_audiences_audience_id",
                        column: x => x.audience_id,
                        principalTable: "audiences",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_audience_searches_campaigns_campaign_id",
                        column: x => x.campaign_id,
                        principalTable: "campaigns",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_audience_searches_work_items_read_work_item_id",
                        column: x => x.read_work_item_id,
                        principalTable: "work_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_audience_searches_work_items_search_work_item_id",
                        column: x => x.search_work_item_id,
                        principalTable: "work_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

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

            migrationBuilder.CreateIndex(
                name: "ix_journal_campaign_kind_key_searches_ended",
                table: "journal",
                columns: new[] { "campaign_id", "kind", "key" },
                unique: true,
                filter: "kind = 'audience_search_ended'");

            migrationBuilder.CreateIndex(
                name: "ix_audience_landings_audience_id",
                table: "audience_landings",
                column: "audience_id");

            migrationBuilder.CreateIndex(
                name: "ix_audience_landings_campaign_contact_id",
                table: "audience_landings",
                column: "campaign_contact_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_audience_landings_read_work_item_id",
                table: "audience_landings",
                column: "read_work_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_audience_landings_search_work_item_id",
                table: "audience_landings",
                column: "search_work_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_audience_searches_audience_id",
                table: "audience_searches",
                column: "audience_id");

            migrationBuilder.CreateIndex(
                name: "ix_audience_searches_in_flight",
                table: "audience_searches",
                column: "campaign_id",
                filter: "ended_as IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_audience_searches_read_work_item_id",
                table: "audience_searches",
                column: "read_work_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_audience_searches_search_work_item_id",
                table: "audience_searches",
                column: "search_work_item_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_audiences_campaign_id_definition_hash",
                table: "audiences",
                columns: new[] { "campaign_id", "definition_hash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_audiences_campaign_id_number",
                table: "audiences",
                columns: new[] { "campaign_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_audiences_public_id",
                table: "audiences",
                column: "public_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audience_landings");

            migrationBuilder.DropTable(
                name: "audience_searches");

            migrationBuilder.DropTable(
                name: "audiences");

            migrationBuilder.DropIndex(
                name: "ix_journal_audience_reserve_low_campaign_id",
                table: "journal");

            migrationBuilder.DropIndex(
                name: "ix_journal_audience_reserve_recovered_campaign_id",
                table: "journal");

            migrationBuilder.DropIndex(
                name: "ix_journal_campaign_kind_key_searches_ended",
                table: "journal");

            // Dropped in place rather than by rebuilding the table, which would lose the triggers an earlier migration
            // wrote on campaigns and, with foreign keys on, the rows that are deleted with a campaign.
            migrationBuilder.Sql("ALTER TABLE campaigns DROP COLUMN audience_watermark;");
            migrationBuilder.Sql("ALTER TABLE campaigns DROP COLUMN audience_threshold;");
        }
    }
}
