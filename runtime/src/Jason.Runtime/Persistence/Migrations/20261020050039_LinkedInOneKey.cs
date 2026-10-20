using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jason.Runtime.Persistence.Migrations
{
    /// <summary>
    /// A LinkedIn profile link is keyed by its profile's name, decoded once and lowercased, under the canonical host
    /// (<c>ChannelRules</c>), so the keys stored under the rules before are rewritten to it: the do-not-contact register's
    /// and the contacts' channels in one step, because the claim asks the register about a contact's stored value as it
    /// stands. The rewrite is the rule itself, called as the database function <c>jason_linkedin_key</c>, not a second copy
    /// of it here: SQL could neither decode an escape nor check the letters a name is made of. A stored link is keyed with
    /// no bound on its length, because a rule before could store one longer than a value may be given, and the rule bounds
    /// a key by its name.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Nothing is deleted and nothing is merged. A value the rule refuses — a link to another kind of page, another host,
    /// a name outside the rule's letters — is left as it stands, and still matches what is stored under exactly that text.
    /// Where several rows of one contact, or several entries of the register, come to share a key, one holds it and the
    /// others are left as they stand: the row already equal to its key, else the one the claim reads — for a contact the
    /// primary, then the lowest id; for the register the earliest entry. Whichever entry holds a key covers the person, and
    /// an entry left beside it can still be listed and lifted by its exact text. Where the row the claim reads for a
    /// contact is left beside another that holds the key, the primary flag moves to the holder, demoted first and then
    /// promoted, because the claim asks the register about that row: both rows keep their values, labels and data. Two
    /// contacts holding one profile both take its key; contacts are not merged here.
    /// </para>
    /// <para>
    /// The function is live and this script is frozen, so an installation that skips releases runs it with a later rule.
    /// That is harmless: every rule's key is its own key, and a later rule gets its own migration. A connection that did
    /// not register the function fails with "no such function" rather than doing nothing.
    /// </para>
    /// <para>
    /// It runs as the runtime starts, so the table of keys is read by identifier and by key through its two indexes:
    /// every statement passes over it once and finds the rest of what it needs by index, and the whole costs in proportion
    /// to the rows rather than to their square. The chronicle keeps every line it had, and nothing is written to it here.
    /// </para>
    /// </remarks>
    public partial class LinkedInOneKey : Migration
    {
        /// <summary>
        /// The statements <see cref="Up"/> runs, in order and as one script: exposed so that the plans SQLite is asked for
        /// are the plans of this very text. No statement holds a <c>;</c> of its own.
        /// </summary>
        internal static IReadOnlyList<string> Statements { get; } =
            Script.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        internal const string Script =
            """
            CREATE TEMP TABLE linkedin_one_key (source TEXT NOT NULL, id INTEGER NOT NULL, owner INTEGER NOT NULL, key TEXT NOT NULL, held INTEGER NOT NULL, reach INTEGER NOT NULL, place INTEGER NOT NULL, PRIMARY KEY (source, id));

            INSERT INTO linkedin_one_key (source, id, owner, key, held, reach, place)
            SELECT source, id, owner, key, held, reach,
                   row_number() OVER (PARTITION BY source, owner, key ORDER BY held DESC, reach DESC, at, id)
            FROM (
                SELECT 'suppressions' AS source, id, 0 AS owner, key, value = key AS held, 0 AS reach, created_at AS at
                FROM (SELECT id, value, created_at, jason_linkedin_key(value) AS key FROM suppressions WHERE channel = 'linkedin')
                WHERE key IS NOT NULL
                UNION ALL
                SELECT 'contact_channels', id, contact_id, key, value = key, reach, ''
                FROM (
                    SELECT id, contact_id, value, jason_linkedin_key(value) AS key,
                           row_number() OVER (PARTITION BY contact_id ORDER BY is_primary DESC, id) = 1 AS reach
                    FROM contact_channels
                    WHERE channel = 'linkedin'
                )
                WHERE key IS NOT NULL
            );

            CREATE INDEX linkedin_one_key_by_key ON linkedin_one_key (source, owner, key, place);

            UPDATE contact_channels SET is_primary = 0
            WHERE is_primary = 1
              AND id IN (SELECT id FROM linkedin_one_key WHERE source = 'contact_channels' AND reach = 1 AND place > 1);

            UPDATE contact_channels SET is_primary = 1
            WHERE id IN (
                SELECT holder.id
                FROM linkedin_one_key reader
                JOIN linkedin_one_key holder
                  ON holder.source = reader.source AND holder.owner = reader.owner AND holder.key = reader.key AND holder.place = 1
                WHERE reader.source = 'contact_channels' AND reader.reach = 1 AND reader.place > 1
            );

            UPDATE suppressions
            SET value = (SELECT key FROM linkedin_one_key k WHERE k.source = 'suppressions' AND k.id = suppressions.id)
            WHERE id IN (SELECT id FROM linkedin_one_key WHERE source = 'suppressions' AND place = 1 AND held = 0);

            UPDATE contact_channels
            SET value = (SELECT key FROM linkedin_one_key k WHERE k.source = 'contact_channels' AND k.id = contact_channels.id)
            WHERE id IN (SELECT id FROM linkedin_one_key WHERE source = 'contact_channels' AND place = 1 AND held = 0);

            DROP TABLE linkedin_one_key;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(Script);

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The spellings before cannot be recovered, and are not needed: every key this wrote is a link to the same
            // profile, and nothing was deleted.
        }
    }
}
