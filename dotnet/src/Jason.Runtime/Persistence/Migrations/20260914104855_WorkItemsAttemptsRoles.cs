using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jason.Runtime.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WorkItemsAttemptsRoles : Migration
    {
        /// <summary>Fixed, so the seeded roster is byte for byte the same in every installation.</summary>
        private static readonly DateTime Seeded = new DateTime(2026, 9, 14, 0, 0, 0, DateTimeKind.Utc);

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "attempt_id",
                table: "journal",
                type: "TEXT",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "work_item_id",
                table: "journal",
                type: "TEXT",
                maxLength: 40,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "roles",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    public_id = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    name = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    builtin = table.Column<bool>(type: "INTEGER", nullable: false),
                    description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    entry_command_json = table.Column<string>(type: "TEXT", nullable: false, defaultValueSql: "'[]'"),
                    profile_defaults_json = table.Column<string>(type: "TEXT", nullable: false, defaultValueSql: "'{}'"),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    updated_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_roles", x => x.id);
                    table.CheckConstraint("ck_roles_entry_command_json", "json_valid(entry_command_json)");
                    table.CheckConstraint("ck_roles_profile_defaults_json", "json_valid(profile_defaults_json)");
                });

            migrationBuilder.CreateTable(
                name: "work_items",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    public_id = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    campaign_id = table.Column<int>(type: "INTEGER", nullable: false),
                    contact_id = table.Column<int>(type: "INTEGER", nullable: true),
                    kind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    role = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    operation = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    execution_profile = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    priority = table.Column<int>(type: "INTEGER", nullable: false),
                    not_before = table.Column<DateTime>(type: "TEXT", nullable: true),
                    due_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    retry_after = table.Column<DateTime>(type: "TEXT", nullable: true),
                    timeout_seconds = table.Column<int>(type: "INTEGER", nullable: true),
                    heartbeat_seconds = table.Column<int>(type: "INTEGER", nullable: true),
                    max_attempts = table.Column<int>(type: "INTEGER", nullable: true),
                    created_by_type = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    created_by_id = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    context_json = table.Column<string>(type: "TEXT", nullable: false, defaultValueSql: "'{}'"),
                    result_format_json = table.Column<string>(type: "TEXT", nullable: true),
                    result_json = table.Column<string>(type: "TEXT", nullable: true),
                    attempt_count = table.Column<int>(type: "INTEGER", nullable: false),
                    last_error_json = table.Column<string>(type: "TEXT", nullable: true),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    updated_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    finished_at = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_work_items", x => x.id);
                    table.CheckConstraint("ck_work_items_context_json", "json_valid(context_json)");
                    table.CheckConstraint("ck_work_items_last_error_json", "last_error_json IS NULL OR json_valid(last_error_json)");
                    table.CheckConstraint("ck_work_items_result_format_json", "result_format_json IS NULL OR json_valid(result_format_json)");
                    table.CheckConstraint("ck_work_items_result_json", "result_json IS NULL OR json_valid(result_json)");
                    table.ForeignKey(
                        name: "fk_work_items_campaigns_campaign_id",
                        column: x => x.campaign_id,
                        principalTable: "campaigns",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_work_items_contacts_contact_id",
                        column: x => x.contact_id,
                        principalTable: "contacts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "attempts",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    public_id = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    work_item_id = table.Column<int>(type: "INTEGER", nullable: false),
                    number = table.Column<int>(type: "INTEGER", nullable: false),
                    command = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    execution_profile = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    context_snapshot_json = table.Column<string>(type: "TEXT", nullable: false, defaultValueSql: "'{}'"),
                    error_json = table.Column<string>(type: "TEXT", nullable: true),
                    launch_json = table.Column<string>(type: "TEXT", nullable: true),
                    claimed_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    started_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    finished_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    last_heartbeat_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    lock_until = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_attempts", x => x.id);
                    table.CheckConstraint("ck_attempts_context_snapshot_json", "json_valid(context_snapshot_json)");
                    table.CheckConstraint("ck_attempts_error_json", "error_json IS NULL OR json_valid(error_json)");
                    table.CheckConstraint("ck_attempts_launch_json", "launch_json IS NULL OR json_valid(launch_json)");
                    table.ForeignKey(
                        name: "fk_attempts_work_items_work_item_id",
                        column: x => x.work_item_id,
                        principalTable: "work_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_journal_work_item_id_public_id",
                table: "journal",
                columns: new[] { "work_item_id", "public_id" });

            migrationBuilder.CreateIndex(
                name: "ix_attempts_one_live_per_item",
                table: "attempts",
                column: "work_item_id",
                unique: true,
                filter: "status IN ('scheduled','running')");

            migrationBuilder.CreateIndex(
                name: "ix_attempts_public_id",
                table: "attempts",
                column: "public_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_attempts_status",
                table: "attempts",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_attempts_work_item_id_number",
                table: "attempts",
                columns: new[] { "work_item_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_roles_name",
                table: "roles",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_roles_public_id",
                table: "roles",
                column: "public_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_work_items_campaign_id_status",
                table: "work_items",
                columns: new[] { "campaign_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_work_items_contact_id",
                table: "work_items",
                column: "contact_id");

            migrationBuilder.CreateIndex(
                name: "ix_work_items_public_id",
                table: "work_items",
                column: "public_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_work_items_status_due_at",
                table: "work_items",
                columns: new[] { "status", "due_at" });

            migrationBuilder.CreateIndex(
                name: "ix_work_items_status_not_before",
                table: "work_items",
                columns: new[] { "status", "not_before" });

            // A finished work item's result is what everyone downstream reads; the database refuses to let anyone
            // rewrite it, the same way it refuses to let anyone rewrite the chronicle. The terminal transition
            // itself carries the result, so writing status and result together is still allowed.
            migrationBuilder.Sql("CREATE TRIGGER work_items_result_frozen BEFORE UPDATE OF result_json ON work_items WHEN OLD.status IN ('succeeded','failed','cancelled','expired') BEGIN SELECT RAISE(ABORT, 'the result of a finished work item is frozen'); END;");

            // The roster an SDR practice has. Builtin roles are vocabulary, not processes: none of them is
            // launchable until a command exists for it.
            migrationBuilder.InsertData(
                table: "roles",
                columns: new[] { "public_id", "name", "builtin", "description", "entry_command_json", "profile_defaults_json", "created_at", "updated_at" },
                values: new object[,]
                {
                    { "rol_01K52JR0SEED00000000000001", "manager", true, "Runs the campaign: reviews progress, repairs failed work, replans.", "[]", "{}", Seeded, Seeded },
                    { "rol_01K52JR0SEED00000000000002", "planner", true, "Plans the next short horizon of work.", "[]", "{}", Seeded, Seeded },
                    { "rol_01K52JR0SEED00000000000003", "researcher", true, "Finds and verifies facts about accounts and people.", "[]", "{}", Seeded, Seeded },
                    { "rol_01K52JR0SEED00000000000004", "copywriter", true, "Writes campaign-level messaging.", "[]", "{}", Seeded, Seeded },
                    { "rol_01K52JR0SEED00000000000005", "personalizer", true, "Adapts messaging to one contact.", "[]", "{}", Seeded, Seeded },
                    { "rol_01K52JR0SEED00000000000006", "critic", true, "Reviews drafts against the brief and the guardrails.", "[]", "{}", Seeded, Seeded },
                    { "rol_01K52JR0SEED00000000000007", "responder", true, "Handles replies.", "[]", "{}", Seeded, Seeded },
                    { "rol_01K52JR0SEED00000000000008", "analyst", true, "Reads outcomes and tells what is working.", "[]", "{}", Seeded, Seeded },
                    { "rol_01K52JR0SEED00000000000009", "deliverability-specialist", true, "Warm-up and deliverability diagnosis.", "[]", "{}", Seeded, Seeded },
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS work_items_result_frozen;");

            migrationBuilder.DropTable(
                name: "attempts");

            migrationBuilder.DropTable(
                name: "roles");

            migrationBuilder.DropTable(
                name: "work_items");

            migrationBuilder.DropIndex(
                name: "ix_journal_work_item_id_public_id",
                table: "journal");

            migrationBuilder.DropColumn(
                name: "attempt_id",
                table: "journal");

            migrationBuilder.DropColumn(
                name: "work_item_id",
                table: "journal");
        }
    }
}
