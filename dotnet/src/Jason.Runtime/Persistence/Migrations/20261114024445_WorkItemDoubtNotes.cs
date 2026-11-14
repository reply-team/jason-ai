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
            migrationBuilder.DropColumn(
                name: "ask_person_note",
                table: "work_items");

            migrationBuilder.DropColumn(
                name: "review_note",
                table: "work_items");
        }
    }
}
