using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jason.Runtime.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ExecutionProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "lineage_from_attempt_id",
                table: "work_items",
                type: "TEXT",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "lineage_profile_name",
                table: "work_items",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "lineage_profile_revision",
                table: "work_items",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "lineage_state",
                table: "work_items",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "root");

            migrationBuilder.AddColumn<string>(
                name: "execution_profile",
                table: "roles",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "execution_profile",
                table: "campaigns",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "execution_profiles",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    public_id = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    name = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    current_revision = table.Column<int>(type: "INTEGER", nullable: false),
                    disabled_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    updated_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_execution_profiles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "execution_profile_revisions",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    profile_id = table.Column<int>(type: "INTEGER", nullable: false),
                    number = table.Column<int>(type: "INTEGER", nullable: false),
                    host = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    program = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    args_json = table.Column<string>(type: "TEXT", nullable: false, defaultValueSql: "'[]'"),
                    deny_json = table.Column<string>(type: "TEXT", nullable: false, defaultValueSql: "'[]'"),
                    cli_command = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    host_version_verified = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    created_by_type = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    created_by_id = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_execution_profile_revisions", x => x.id);
                    table.CheckConstraint("ck_execution_profile_revisions_args_json", "json_valid(args_json)");
                    table.CheckConstraint("ck_execution_profile_revisions_deny_json", "json_valid(deny_json)");
                    table.ForeignKey(
                        name: "fk_execution_profile_revisions_execution_profiles_profile_id",
                        column: x => x.profile_id,
                        principalTable: "execution_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_execution_profile_revisions_one_per_number",
                table: "execution_profile_revisions",
                columns: new[] { "profile_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_execution_profiles_name",
                table: "execution_profiles",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_execution_profiles_public_id",
                table: "execution_profiles",
                column: "public_id",
                unique: true);

            // An attempt names the revision it ran under for ever, so a revision that could be edited would let a
            // later change rewrite what an earlier attempt says it did. The interceptor refuses this too; this is
            // the layer that also answers a hand on the database file.
            migrationBuilder.Sql("CREATE TRIGGER execution_profile_revisions_no_update BEFORE UPDATE ON execution_profile_revisions BEGIN SELECT RAISE(ABORT, 'an execution-profile revision is immutable'); END;");
            migrationBuilder.Sql("CREATE TRIGGER execution_profile_revisions_no_delete BEFORE DELETE ON execution_profile_revisions BEGIN SELECT RAISE(ABORT, 'an execution-profile revision is immutable'); END;");

            // Work that existed before profiles did gets the lineage it can be given honestly. Anything a person,
            // a role or the runtime itself created is root work and resolves to the global default as it always
            // would have. Anything an attempt created has ancestry that pinned no profile — there was none to
            // pin — so it is unresolved and blocks visibly rather than quietly changing executor. The column
            // default covers the first case; this covers the second.
            migrationBuilder.Sql("UPDATE work_items SET lineage_state = 'unresolved' WHERE created_by_type = 'attempt';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS execution_profile_revisions_no_update;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS execution_profile_revisions_no_delete;");

            migrationBuilder.DropTable(
                name: "execution_profile_revisions");

            migrationBuilder.DropTable(
                name: "execution_profiles");

            migrationBuilder.DropColumn(
                name: "lineage_from_attempt_id",
                table: "work_items");

            migrationBuilder.DropColumn(
                name: "lineage_profile_name",
                table: "work_items");

            migrationBuilder.DropColumn(
                name: "lineage_profile_revision",
                table: "work_items");

            migrationBuilder.DropColumn(
                name: "lineage_state",
                table: "work_items");

            migrationBuilder.DropColumn(
                name: "execution_profile",
                table: "roles");

            migrationBuilder.DropColumn(
                name: "execution_profile",
                table: "campaigns");
        }
    }
}
