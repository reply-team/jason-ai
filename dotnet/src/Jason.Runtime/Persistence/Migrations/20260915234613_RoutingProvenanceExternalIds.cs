using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jason.Runtime.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RoutingProvenanceExternalIds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "provenance_json",
                table: "attempts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "campaign_routes",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    campaign_id = table.Column<int>(type: "INTEGER", nullable: false),
                    operation = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    plugin_id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    binding_json = table.Column<string>(type: "TEXT", nullable: true),
                    updated_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_campaign_routes", x => x.id);
                    table.CheckConstraint("ck_campaign_routes_binding_json", "binding_json IS NULL OR json_valid(binding_json)");
                    table.ForeignKey(
                        name: "fk_campaign_routes_campaigns_campaign_id",
                        column: x => x.campaign_id,
                        principalTable: "campaigns",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "external_ids",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    contact_id = table.Column<int>(type: "INTEGER", nullable: true),
                    campaign_id = table.Column<int>(type: "INTEGER", nullable: true),
                    plugin_id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    kind = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    value = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    recorded_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    recorded_by_attempt_id = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    diverged_value = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    diverged_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    diverged_by_attempt_id = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_external_ids", x => x.id);
                    table.CheckConstraint("ck_external_ids_one_entity", "(contact_id IS NOT NULL AND campaign_id IS NULL) OR (contact_id IS NULL AND campaign_id IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_external_ids_campaigns_campaign_id",
                        column: x => x.campaign_id,
                        principalTable: "campaigns",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_external_ids_contacts_contact_id",
                        column: x => x.contact_id,
                        principalTable: "contacts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_campaign_routes_campaign_id_operation",
                table: "campaign_routes",
                columns: new[] { "campaign_id", "operation" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_campaign_routes_one_default_per_campaign",
                table: "campaign_routes",
                column: "campaign_id",
                unique: true,
                filter: "operation IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_external_ids_campaign_id_plugin_id_kind",
                table: "external_ids",
                columns: new[] { "campaign_id", "plugin_id", "kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_external_ids_contact_id_plugin_id_kind",
                table: "external_ids",
                columns: new[] { "contact_id", "plugin_id", "kind" },
                unique: true);

            // Every other JSON column of this schema is guarded by a json_valid check constraint, and
            // provenance_json is guarded by these two triggers instead: SQLite cannot add a constraint to a table
            // that already exists without rebuilding it, and rewriting every attempt row is too much to pay for a
            // guarantee a trigger gives exactly as well. Null is the ordinary case — an agent attempt has no
            // provenance and a pre-flight one has not resolved yet — so the guard only looks at a value that is
            // there. It closes the same door for a guarded UPDATE, which never passes through the value converter.
            migrationBuilder.Sql("CREATE TRIGGER attempts_provenance_valid_insert BEFORE INSERT ON attempts WHEN NEW.provenance_json IS NOT NULL AND json_valid(NEW.provenance_json) = 0 BEGIN SELECT RAISE(ABORT, 'the provenance of an attempt must be valid JSON'); END;");
            migrationBuilder.Sql("CREATE TRIGGER attempts_provenance_valid_update BEFORE UPDATE OF provenance_json ON attempts WHEN NEW.provenance_json IS NOT NULL AND json_valid(NEW.provenance_json) = 0 BEGIN SELECT RAISE(ABORT, 'the provenance of an attempt must be valid JSON'); END;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Before the column: SQLite refuses to drop a column a trigger still names.
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS attempts_provenance_valid_insert;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS attempts_provenance_valid_update;");

            migrationBuilder.DropTable(
                name: "campaign_routes");

            migrationBuilder.DropTable(
                name: "external_ids");

            migrationBuilder.DropColumn(
                name: "provenance_json",
                table: "attempts");
        }
    }
}
