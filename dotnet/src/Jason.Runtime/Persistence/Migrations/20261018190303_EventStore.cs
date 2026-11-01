using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jason.Runtime.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EventStore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "events_from",
                table: "campaigns",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "event_sources",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    public_id = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    plugin_id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    binding_identity = table.Column<string>(type: "TEXT", maxLength: 71, nullable: false),
                    cursor = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    cursor_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_event_sources", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "events",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    public_id = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    source_id = table.Column<int>(type: "INTEGER", nullable: false),
                    plugin_id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    kind = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    occurred_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    stored_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    key = table.Column<string>(type: "TEXT", maxLength: 71, nullable: false),
                    contact_id = table.Column<int>(type: "INTEGER", nullable: true),
                    data_json = table.Column<string>(type: "TEXT", nullable: false),
                    read_id = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_events", x => x.id);
                    table.CheckConstraint("ck_events_data_json", "json_valid(data_json)");
                    table.ForeignKey(
                        name: "fk_events_contacts_contact_id",
                        column: x => x.contact_id,
                        principalTable: "contacts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_events_event_sources_source_id",
                        column: x => x.source_id,
                        principalTable: "event_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "event_attributions",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    event_id = table.Column<int>(type: "INTEGER", nullable: false),
                    campaign_id = table.Column<int>(type: "INTEGER", nullable: false),
                    rule = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_event_attributions", x => x.id);
                    table.ForeignKey(
                        name: "fk_event_attributions_campaigns_campaign_id",
                        column: x => x.campaign_id,
                        principalTable: "campaigns",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_event_attributions_events_event_id",
                        column: x => x.event_id,
                        principalTable: "events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_journal_campaign_kind_key_events",
                table: "journal",
                columns: new[] { "campaign_id", "kind", "key" },
                unique: true,
                filter: "kind = 'provider_event_observed'");

            // A provider's events, and which campaigns each concerns, are kept whole as the journal is: the database
            // refuses to rewrite or remove either, whatever writes to it.
            migrationBuilder.Sql("CREATE TRIGGER events_no_update BEFORE UPDATE ON events BEGIN SELECT RAISE(ABORT, 'events are append-only'); END;");
            migrationBuilder.Sql("CREATE TRIGGER events_no_delete BEFORE DELETE ON events BEGIN SELECT RAISE(ABORT, 'events are append-only'); END;");
            migrationBuilder.Sql("CREATE TRIGGER event_attributions_no_update BEFORE UPDATE ON event_attributions BEGIN SELECT RAISE(ABORT, 'event attributions are append-only'); END;");
            migrationBuilder.Sql("CREATE TRIGGER event_attributions_no_delete BEFORE DELETE ON event_attributions BEGIN SELECT RAISE(ABORT, 'event attributions are append-only'); END;");

            // An installation upgrading into the events store owes no campaign its history: a campaign that is live, or
            // paused and able to go live again, has its events said from now, as one that goes live after the upgrade has
            // them said from then. A draft and an archived campaign have nothing said until they go live.
            migrationBuilder.Sql(
                "UPDATE campaigns SET events_from = strftime('%Y-%m-%d %H:%M:%S', 'now') WHERE status IN ('active', 'paused');");

            migrationBuilder.CreateIndex(
                name: "ix_event_attributions_campaign_id_event_id",
                table: "event_attributions",
                columns: new[] { "campaign_id", "event_id" });

            migrationBuilder.CreateIndex(
                name: "ix_event_attributions_event_id_campaign_id",
                table: "event_attributions",
                columns: new[] { "event_id", "campaign_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_event_sources_plugin_id_binding_identity",
                table: "event_sources",
                columns: new[] { "plugin_id", "binding_identity" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_event_sources_public_id",
                table: "event_sources",
                column: "public_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_events_contact_id",
                table: "events",
                column: "contact_id");

            migrationBuilder.CreateIndex(
                name: "ix_events_key",
                table: "events",
                column: "key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_events_public_id",
                table: "events",
                column: "public_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_events_source_id_occurred_at",
                table: "events",
                columns: new[] { "source_id", "occurred_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS events_no_update;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS events_no_delete;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS event_attributions_no_update;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS event_attributions_no_delete;");

            migrationBuilder.DropTable(
                name: "event_attributions");

            migrationBuilder.DropTable(
                name: "events");

            migrationBuilder.DropTable(
                name: "event_sources");

            migrationBuilder.DropIndex(
                name: "ix_journal_campaign_kind_key_events",
                table: "journal");

            migrationBuilder.DropColumn(
                name: "events_from",
                table: "campaigns");
        }
    }
}
