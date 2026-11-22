using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jason.Runtime.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DeepReviewGrid : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "deep_review_due_at",
                table: "campaigns",
                type: "TEXT",
                nullable: true);

            // A campaign already live gets its grid a day from now, the default interval, since a migration reads no
            // settings: a grid left null would be no deep review ever, and one set to now would be a deep review of every
            // live campaign at once on the first scan. A draft, a paused and an archived campaign keep none; going live
            // writes it.
            migrationBuilder.Sql(
                "UPDATE campaigns SET deep_review_due_at = strftime('%Y-%m-%d %H:%M:%S', 'now', '+86400 seconds') WHERE status = 'active';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Dropped in place rather than by rebuilding the table, which keeps every index and trigger on campaigns as it stood.
            migrationBuilder.Sql("ALTER TABLE campaigns DROP COLUMN deep_review_due_at;");
        }
    }
}
