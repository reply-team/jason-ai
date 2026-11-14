using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jason.Runtime.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProviderBlockChannels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // A sender's block names the channel it holds the sender on, since a provider may number its mailboxes apart
            // from its social accounts and one identifier then names two accounts. The column may be null, and null is what
            // every block already here means: the sender on every channel. So nothing is rewritten: the column is added in
            // place, no table is rebuilt, and the sender's half of the one-open rule is made again over the channel, with
            // the block on every channel held unique by an index of its own, since SQLite holds nulls distinct.
            migrationBuilder.DropIndex(
                name: "ix_provider_blocks_one_open_per_sender",
                table: "provider_blocks");

            migrationBuilder.AddColumn<string>(
                name: "channel",
                table: "provider_blocks",
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_provider_blocks_one_open_per_sender",
                table: "provider_blocks",
                columns: new[] { "plugin_id", "binding_identity", "sender_external_id", "channel" },
                unique: true,
                filter: "status = 'open' AND scope = 'sender'");

            migrationBuilder.CreateIndex(
                name: "ix_provider_blocks_one_open_per_sender_on_every_channel",
                table: "provider_blocks",
                columns: new[] { "plugin_id", "binding_identity", "sender_external_id" },
                unique: true,
                filter: "status = 'open' AND scope = 'sender' AND channel IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Down cannot make the old index again once one sender has two blocks open, on two channels or on one and on
            // every channel, a mailbox and a LinkedIn account of one identifier say: the old index holds one open block a
            // sender, and the create fails. Where it succeeds, the column goes, and every block that named a channel names
            // none again: each becomes its sender's block on every channel.
            migrationBuilder.DropIndex(
                name: "ix_provider_blocks_one_open_per_sender",
                table: "provider_blocks");

            migrationBuilder.DropIndex(
                name: "ix_provider_blocks_one_open_per_sender_on_every_channel",
                table: "provider_blocks");

            migrationBuilder.DropColumn(
                name: "channel",
                table: "provider_blocks");

            migrationBuilder.CreateIndex(
                name: "ix_provider_blocks_one_open_per_sender",
                table: "provider_blocks",
                columns: new[] { "plugin_id", "binding_identity", "sender_external_id" },
                unique: true,
                filter: "status = 'open' AND scope = 'sender'");
        }
    }
}
