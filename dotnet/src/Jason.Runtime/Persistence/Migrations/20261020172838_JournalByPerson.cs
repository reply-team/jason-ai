using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jason.Runtime.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class JournalByPerson : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The person a line is about, added with its reference in one statement: the foreign key EF would add
            // afterwards rebuilds the whole table, and the journal's rebuild would also drop its append-only triggers.
            // Lines written before this version keep no person; nothing here writes to them.
            migrationBuilder.Sql(
                "ALTER TABLE \"journal\" ADD \"contact_id\" INTEGER NULL "
                + "CONSTRAINT \"fk_journal_contacts_contact_id\" REFERENCES \"contacts\" (\"id\") ON DELETE RESTRICT;");

            migrationBuilder.CreateIndex(
                name: "ix_journal_contact_id_public_id",
                table: "journal",
                columns: new[] { "contact_id", "public_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_journal_contacts_contact_id",
                table: "journal");

            migrationBuilder.DropIndex(
                name: "ix_journal_contact_id_public_id",
                table: "journal");

            migrationBuilder.DropColumn(
                name: "contact_id",
                table: "journal");
        }
    }
}
