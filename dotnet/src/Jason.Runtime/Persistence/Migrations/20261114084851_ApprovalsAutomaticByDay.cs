using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jason.Runtime.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ApprovalsAutomaticByDay : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The daily ceiling of automatic sends counts, at the claim of every send the automatic mode would release and
            // inside the claim's write transaction, how many sends the mode released from the campaign since 00:00Z. No index
            // held a campaign's approvals by their moment, and the table only grows, one row per send in the campaign's
            // history. A partial index over the campaign and the decided moment, for the automatic releases only, makes the
            // count a seek over one campaign's releases of the day. It adds an index and nothing else: no table is rebuilt,
            // so no column, constraint or trigger changes, and Down drops the index alone.
            migrationBuilder.CreateIndex(
                name: "ix_approvals_automatic_campaign_decided_at",
                table: "approvals",
                columns: new[] { "campaign_id", "decided_at" },
                filter: "reason = 'automatic_mode'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_approvals_automatic_campaign_decided_at",
                table: "approvals");
        }
    }
}
