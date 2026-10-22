using System;
using Jason.Contracts.Ids;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jason.Runtime.Persistence.Migrations
{
    /// <summary>
    /// Projects, and every campaign in one. The migration makes the projects table with the installation's default project
    /// in it — <c>Default</c>, slug <c>default</c>, under an id made when the migration runs, so no two installations share
    /// one — and puts every campaign there, archived ones included. It also makes the preferences table and gives a role's
    /// note three levels: a campaign's, a project's, or — naming neither — the client's, one per role at each, and never
    /// naming both, since a campaign's project is the campaign's; nothing writes either yet.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A campaign's project comes in place, and <c>campaigns</c> is never rebuilt. SQLite adds a column with its reference
    /// in one statement only when the column may be null, so <c>project_id</c> is added that way, every campaign is put in
    /// the default project, and two triggers refuse a campaign written or changed without one: what NOT NULL would say,
    /// said without a rebuild. The model says the column is required, which it is; the database says it with the triggers,
    /// and there is no default, so a campaign made without a project is refused rather than put on one that does not
    /// exist. EF Core's own way would copy <c>campaigns</c>, then drop and rename it with the foreign keys off and outside
    /// this migration's transaction, with every table that refers to a campaign under it. A later migration that does
    /// rebuild <c>campaigns</c> makes the column NOT NULL from the model, and the triggers go with the old table.
    /// </para>
    /// <para>
    /// <c>role_notes</c> is rebuilt, because SQLite cannot let a column that was required be null in place, nor add a
    /// reference or a check to a table that stands — a note's project and the rule that a note is at one level: copied into a
    /// new table inside the migration's transaction, with the foreign keys on, so the copy is checked as it is written;
    /// then dropped and renamed with them off, outside a transaction; then its indexes are made again. Nothing refers to a
    /// role note, and there are few. That rebuild makes this migration run in more than one transaction, and a start it
    /// fails in puts back the backup it took (<see cref="DatabaseMigrator"/>). It also sets SQLite's sequence for role notes
    /// to the highest id copied, so the id of a note deleted before it could be given again; nothing deletes one.
    /// </para>
    /// <para>
    /// Every campaign is put in the default project by <see cref="Backfill"/>, once the column is there and before the
    /// triggers hold it: one pass over <c>campaigns</c>, the default's id read once through the index that holds there is
    /// one. The runtime starts on the far side of this, so its cost is the start's, in proportion to the campaigns. Nothing
    /// is written to the chronicle: neither the default project nor a campaign's place in it is an act anyone took.
    /// </para>
    /// </remarks>
    public partial class ProjectsAndPreferences : Migration
    {
        /// <summary>Every campaign there is, in the default project: the statement exactly as <see cref="Up"/> runs it.</summary>
        internal const string Backfill = "UPDATE campaigns SET project_id = (SELECT id FROM projects WHERE is_default = 1);";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "projects",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    public_id = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    slug = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    is_default = table.Column<bool>(type: "INTEGER", nullable: false),
                    folder_path = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    updated_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_projects", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_projects_one_default",
                table: "projects",
                column: "is_default",
                unique: true,
                filter: "is_default = 1");

            migrationBuilder.CreateIndex(
                name: "ix_projects_public_id",
                table: "projects",
                column: "public_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_projects_slug",
                table: "projects",
                column: "slug",
                unique: true);

            // The default project, made here so that it exists on every installation, fresh or upgraded, before any campaign
            // needs it. Its id and moments are this installation's own: they are made as the migration runs.
            var now = DateTime.UtcNow;
            migrationBuilder.InsertData(
                table: "projects",
                columns: new[] { "public_id", "name", "slug", "is_default", "created_at", "updated_at" },
                values: new object[] { PublicId.New("prj"), "Default", "default", true, now, now });

            // The column and its reference in one statement, which SQLite allows for a column that may be null: the
            // foreign key EF would add afterwards rebuilds the whole table outside this migration's transaction, with every
            // table that refers to a campaign under it.
            migrationBuilder.Sql(
                "ALTER TABLE \"campaigns\" ADD \"project_id\" INTEGER NULL "
                + "CONSTRAINT \"fk_campaigns_projects_project_id\" REFERENCES \"projects\" (\"id\") ON DELETE RESTRICT;");

            migrationBuilder.Sql(Backfill);

            // A campaign is always in a project. The column may be null only because SQLite adds a reference in place no
            // other way, so these refuse the null that NOT NULL would refuse, on a campaign written and on one changed.
            migrationBuilder.Sql("CREATE TRIGGER campaigns_project_required_insert BEFORE INSERT ON campaigns WHEN NEW.project_id IS NULL BEGIN SELECT RAISE(ABORT, 'a campaign is always in a project: campaigns.project_id may not be null'); END;");
            migrationBuilder.Sql("CREATE TRIGGER campaigns_project_required_update BEFORE UPDATE OF project_id ON campaigns WHEN NEW.project_id IS NULL BEGIN SELECT RAISE(ABORT, 'a campaign is always in a project: campaigns.project_id may not be null'); END;");

            migrationBuilder.AddColumn<string>(
                name: "folder_path",
                table: "campaigns",
                type: "TEXT",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_campaigns_project",
                table: "campaigns",
                columns: new[] { "project_id", "public_id" });

            migrationBuilder.DropIndex(
                name: "ix_role_notes_one_per_campaign_role",
                table: "role_notes");

            migrationBuilder.AlterColumn<int>(
                name: "campaign_id",
                table: "role_notes",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "INTEGER");

            migrationBuilder.AddColumn<int>(
                name: "project_id",
                table: "role_notes",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_role_notes_one_per_campaign_role",
                table: "role_notes",
                columns: new[] { "campaign_id", "role" },
                unique: true,
                filter: "campaign_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_role_notes_one_per_client_role",
                table: "role_notes",
                column: "role",
                unique: true,
                filter: "campaign_id IS NULL AND project_id IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_role_notes_one_per_project_role",
                table: "role_notes",
                columns: new[] { "project_id", "role" },
                unique: true,
                filter: "project_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_role_notes_one_level",
                table: "role_notes",
                sql: "campaign_id IS NULL OR project_id IS NULL");

            migrationBuilder.AddForeignKey(
                name: "fk_role_notes_projects_project_id",
                table: "role_notes",
                column: "project_id",
                principalTable: "projects",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.CreateTable(
                name: "preferences",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    key = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    value_json = table.Column<string>(type: "TEXT", nullable: false),
                    value_hash = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    value_bytes = table.Column<long>(type: "INTEGER", nullable: false),
                    updated_by_type = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    updated_by_id = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    updated_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_preferences", x => x.id);
                    table.CheckConstraint("ck_preferences_value_json", "json_valid(value_json)");
                });

            migrationBuilder.CreateIndex(
                name: "ix_preferences_key",
                table: "preferences",
                column: "key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The campaigns' reference to their project goes first, in place, so that the projects it refers to can go after
            // it: the triggers and the index that name the column, then the column with its reference.
            migrationBuilder.Sql("DROP TRIGGER campaigns_project_required_insert;");
            migrationBuilder.Sql("DROP TRIGGER campaigns_project_required_update;");

            migrationBuilder.DropIndex(
                name: "ix_campaigns_project",
                table: "campaigns");

            migrationBuilder.Sql("ALTER TABLE \"campaigns\" DROP COLUMN \"project_id\";");
            migrationBuilder.Sql("ALTER TABLE \"campaigns\" DROP COLUMN \"folder_path\";");

            migrationBuilder.DropTable(
                name: "preferences");

            // A role's note at a project's level or the client's has nowhere to go in the schema before this one, and one
            // at a project's refers to the project: they go before the projects do.
            migrationBuilder.Sql("DELETE FROM role_notes WHERE campaign_id IS NULL;");

            migrationBuilder.DropTable(
                name: "projects");

            migrationBuilder.DropForeignKey(
                name: "fk_role_notes_projects_project_id",
                table: "role_notes");

            migrationBuilder.DropIndex(
                name: "ix_role_notes_one_per_campaign_role",
                table: "role_notes");

            migrationBuilder.DropIndex(
                name: "ix_role_notes_one_per_client_role",
                table: "role_notes");

            migrationBuilder.DropIndex(
                name: "ix_role_notes_one_per_project_role",
                table: "role_notes");

            migrationBuilder.DropCheckConstraint(
                name: "ck_role_notes_one_level",
                table: "role_notes");

            migrationBuilder.DropColumn(
                name: "project_id",
                table: "role_notes");

            migrationBuilder.AlterColumn<int>(
                name: "campaign_id",
                table: "role_notes",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_role_notes_one_per_campaign_role",
                table: "role_notes",
                columns: new[] { "campaign_id", "role" },
                unique: true);
        }
    }
}
