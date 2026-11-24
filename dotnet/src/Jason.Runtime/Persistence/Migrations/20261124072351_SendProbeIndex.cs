using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jason.Runtime.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SendProbeIndex : Migration
    {
        /// <summary>The index of a campaign's work items by their operation and the send each names.</summary>
        private const string Index = "ix_work_items_campaign_id_operation_send";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Which of a campaign's work items name a send, its cancels and its reads, is asked for one send at a time by
            // every touch that counts, the line's root, the hold and the people list: the items whose input names the send.
            // The indexes the work items already have stop at the campaign and the status, so without this one each probe
            // reads the campaign's succeeded work and parses its context. An expression index over the campaign, the
            // operation and the send taken out of the context makes it a seek; the queries write the expression in the
            // same words, which a test holds this index's text to. It adds an index and nothing else: the work items'
            // columns, their other indexes and their triggers are as they were, and the model knows nothing of it, since an
            // expression is not a property. An older executable started on this database writes items as before, and
            // SQLite keeps the index for it. The index is outside the model, so a later migration that rebuilds the work
            // items' table must create it again.
            migrationBuilder.Sql(
                $"CREATE INDEX {Index} ON work_items (campaign_id, operation, json_extract(context_json, '$.input.send'));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"DROP INDEX IF EXISTS {Index};");
        }
    }
}
