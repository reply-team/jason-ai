using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jason.Runtime.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class JournalByThread : Migration
    {
        /// <summary>The index of a campaign's observed-reply lines by the thread each names.</summary>
        private const string Index = "ix_journal_campaign_thread";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Where a campaign saw a thread is read at the claim of every operation that names one, inside the claim's
            // write transaction: the campaign's inbound_reply_observed lines whose body names the thread. The index the
            // kind already has is ordered by the line's key, a digest, so without this one the read visits every such line
            // of the campaign and parses its body. A partial expression index over the campaign and the thread taken out
            // of the body, for that kind only, makes it a seek. It adds an index and nothing else: the journal's columns,
            // its other indexes and its append-only triggers are as they were, and the model knows nothing of it, since
            // an expression is not a property. An older executable started on this database writes lines as before, and
            // SQLite keeps the index for it.
            migrationBuilder.Sql(
                $"CREATE INDEX {Index} ON journal (campaign_id, json_extract(new_json, '$.thread')) WHERE kind = 'inbound_reply_observed';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"DROP INDEX IF EXISTS {Index};");
        }
    }
}
