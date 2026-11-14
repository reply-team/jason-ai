using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jason.Runtime.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class JournalVerdictsByThread : Migration
    {
        /// <summary>The index of a campaign's verdicts on replies by the thread each names, and under it by the key.</summary>
        private const string Index = "ix_journal_campaign_verdict_thread";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Whether a contact is cold, warm or waits on a verdict is read at the claim of every automatic send, inside the
            // claim's write transaction: the verdicts on one thread of the campaign, and for each the one that replaced it
            // under its key. No index held a campaign's verdicts by their thread, so the read visited every line of the
            // campaign, of every kind, and parsed its body. A partial expression index over the campaign, the thread taken
            // out of the body and the key, for that kind only, makes a thread's verdicts and a key's later verdict a seek;
            // the observed lines of a thread already have ix_journal_campaign_thread. It adds an index and nothing else: the
            // journal's columns, its other indexes and its append-only triggers are as they were, and the model knows
            // nothing of it, since an expression is not a property. An older executable started on this database writes
            // lines as before, and SQLite keeps the index for it. The index is outside the model, as the journal's triggers
            // are, so a later migration that rebuilds the journal table must create it again.
            migrationBuilder.Sql(
                $"CREATE INDEX {Index} ON journal (campaign_id, json_extract(new_json, '$.thread'), key) WHERE kind = 'reply_classified';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"DROP INDEX IF EXISTS {Index};");
        }
    }
}
