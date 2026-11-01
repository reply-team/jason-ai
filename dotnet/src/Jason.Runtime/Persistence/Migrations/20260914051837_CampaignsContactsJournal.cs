using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jason.Runtime.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CampaignsContactsJournal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "context_json",
                table: "campaigns",
                type: "TEXT",
                nullable: false,
                defaultValueSql: "'{}'");

            migrationBuilder.CreateTable(
                name: "contacts",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    public_id = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    first_name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    last_name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    company = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    time_zone = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    custom_json = table.Column<string>(type: "TEXT", nullable: false, defaultValueSql: "'{}'"),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    updated_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    archived_at = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contacts", x => x.id);
                    table.CheckConstraint("ck_contacts_custom_json", "json_valid(custom_json)");
                });

            migrationBuilder.CreateTable(
                name: "journal",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    public_id = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    ts = table.Column<DateTime>(type: "TEXT", nullable: false),
                    actor_type = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    actor_id = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    kind = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    campaign_id = table.Column<int>(type: "INTEGER", nullable: true),
                    key = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    old_json = table.Column<string>(type: "TEXT", nullable: true),
                    new_json = table.Column<string>(type: "TEXT", nullable: true),
                    reason = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_journal", x => x.id);
                    table.CheckConstraint("ck_journal_new_json", "new_json IS NULL OR json_valid(new_json)");
                    table.CheckConstraint("ck_journal_old_json", "old_json IS NULL OR json_valid(old_json)");
                    table.ForeignKey(
                        name: "fk_journal_campaigns_campaign_id",
                        column: x => x.campaign_id,
                        principalTable: "campaigns",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "suppressions",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    public_id = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    channel = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    value = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    reason = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_suppressions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "campaign_contacts",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    campaign_id = table.Column<int>(type: "INTEGER", nullable: false),
                    contact_id = table.Column<int>(type: "INTEGER", nullable: false),
                    state = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    added_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    updated_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_campaign_contacts", x => x.id);
                    table.ForeignKey(
                        name: "fk_campaign_contacts_campaigns_campaign_id",
                        column: x => x.campaign_id,
                        principalTable: "campaigns",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_campaign_contacts_contacts_contact_id",
                        column: x => x.contact_id,
                        principalTable: "contacts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "contact_channels",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    contact_id = table.Column<int>(type: "INTEGER", nullable: false),
                    channel = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    value = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    label = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    is_primary = table.Column<bool>(type: "INTEGER", nullable: false),
                    data_json = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contact_channels", x => x.id);
                    table.CheckConstraint("ck_contact_channels_data_json", "data_json IS NULL OR json_valid(data_json)");
                    table.ForeignKey(
                        name: "fk_contact_channels_contacts_contact_id",
                        column: x => x.contact_id,
                        principalTable: "contacts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_campaigns_context_json",
                table: "campaigns",
                sql: "json_valid(context_json)");

            migrationBuilder.CreateIndex(
                name: "ix_campaign_contacts_campaign_id_contact_id",
                table: "campaign_contacts",
                columns: new[] { "campaign_id", "contact_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_campaign_contacts_contact_id",
                table: "campaign_contacts",
                column: "contact_id");

            migrationBuilder.CreateIndex(
                name: "ix_contact_channels_channel_value",
                table: "contact_channels",
                columns: new[] { "channel", "value" });

            migrationBuilder.CreateIndex(
                name: "ix_contact_channels_contact_id_channel_value",
                table: "contact_channels",
                columns: new[] { "contact_id", "channel", "value" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_contact_channels_one_primary_per_channel",
                table: "contact_channels",
                columns: new[] { "contact_id", "channel" },
                unique: true,
                filter: "is_primary = 1");

            migrationBuilder.CreateIndex(
                name: "ix_contacts_public_id",
                table: "contacts",
                column: "public_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_journal_campaign_id_public_id",
                table: "journal",
                columns: new[] { "campaign_id", "public_id" });

            migrationBuilder.CreateIndex(
                name: "ix_journal_kind",
                table: "journal",
                column: "kind");

            migrationBuilder.CreateIndex(
                name: "ix_journal_public_id",
                table: "journal",
                column: "public_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_suppressions_channel_value",
                table: "suppressions",
                columns: new[] { "channel", "value" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_suppressions_public_id",
                table: "suppressions",
                column: "public_id",
                unique: true);

            // The chronicle is append-only, and the database is the last line of defence: not even a raw
            // connection or a future bug in the runtime can rewrite what was recorded.
            migrationBuilder.Sql("CREATE TRIGGER journal_no_update BEFORE UPDATE ON journal BEGIN SELECT RAISE(ABORT, 'journal entries are append-only'); END;");
            migrationBuilder.Sql("CREATE TRIGGER journal_no_delete BEFORE DELETE ON journal BEGIN SELECT RAISE(ABORT, 'journal entries are append-only'); END;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS journal_no_update;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS journal_no_delete;");

            migrationBuilder.DropTable(
                name: "campaign_contacts");

            migrationBuilder.DropTable(
                name: "contact_channels");

            migrationBuilder.DropTable(
                name: "journal");

            migrationBuilder.DropTable(
                name: "suppressions");

            migrationBuilder.DropTable(
                name: "contacts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_campaigns_context_json",
                table: "campaigns");

            migrationBuilder.DropColumn(
                name: "context_json",
                table: "campaigns");
        }
    }
}
