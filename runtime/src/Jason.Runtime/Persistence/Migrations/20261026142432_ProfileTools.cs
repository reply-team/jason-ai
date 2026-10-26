using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jason.Runtime.Persistence.Migrations
{
    /// <summary>
    /// The tools a person grants by name on a profile revision: <c>tools_json</c> beside <c>deny_json</c>, a JSON list,
    /// <c>[]</c> on every revision already written — none of them granted anything — and checked to be JSON by
    /// <c>ck_execution_profile_revisions_tools_json</c>, like the two lists beside it.
    /// </summary>
    /// <remarks>
    /// The column comes in place, in one statement, and <c>execution_profile_revisions</c> is never rebuilt. EF Core's own
    /// way to add a check copies the table, then drops and renames it, and the old table's two triggers go with it: every
    /// revision would be open to a rewrite that the database no longer refused. SQLite adds a column with a default and a
    /// check of its own in place, and tests the check against the rows already there, so the triggers stay exactly as they
    /// were and nothing else is touched. Down drops the column the same way; the check is the column's and goes with it.
    /// </remarks>
    public partial class ProfileTools : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE \"execution_profile_revisions\" ADD COLUMN \"tools_json\" TEXT NOT NULL DEFAULT '[]' "
                + "CONSTRAINT \"ck_execution_profile_revisions_tools_json\" CHECK (json_valid(tools_json));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE \"execution_profile_revisions\" DROP COLUMN \"tools_json\";");
        }
    }
}
