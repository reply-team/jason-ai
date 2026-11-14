using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jason.Runtime.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WorkItemDoubtNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ask_person_note",
                table: "work_items",
                type: "TEXT",
                maxLength: 280,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "review_note",
                table: "work_items",
                type: "TEXT",
                maxLength: 280,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Dropped in place rather than by rebuilding the table, which would lose the trigger an earlier migration
            // wrote on work_items to freeze a finished item's result, and the filtered index that allows one live attempt.
            migrationBuilder.Sql("ALTER TABLE work_items DROP COLUMN review_note;");
            migrationBuilder.Sql("ALTER TABLE work_items DROP COLUMN ask_person_note;");
        }
    }
}
