using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jason.Runtime.Persistence.Migrations
{
    /// <summary>
    /// A LinkedIn profile link is keyed by its lowercased slug on linkedin.com and its subdomains (<c>ChannelRules</c>), so
    /// the keys stored under the rule before are rewritten to it: the do-not-contact register's and the contacts'
    /// channels in one step, because the claim asks the register about a contact's stored value as it stands, and two
    /// stored forms of one rule would stop it refusing a person who said stop.
    /// </summary>
    /// <remarks>
    /// A stored value is the normalized link — scheme, <c>://</c>, the lowercased host and a port that is not the default,
    /// the escaped path with no trailing slash, then the query — so the rewrite lowercases the path alone, where the host
    /// is linkedin.com or ends in <c>.linkedin.com</c>. The escaped path is ASCII, which SQLite's <c>lower()</c> folds
    /// exactly as <c>ToLowerInvariant</c> does. Rows that come to share a key are collapsed first: two spellings of one
    /// person's stop become that person's one stop, the earliest, with its own identifier and reason, so no stop is lost;
    /// and two channels of one contact become one, the primary where one of them is, else the one written first. The
    /// chronicle keeps every line it had, and nothing is written to it here. It runs as the runtime starts, so the table of
    /// keys is read by identifier and by key through its two indexes: every statement passes over it once and finds the
    /// rest of what it needs by index, and the whole costs in proportion to the rows rather than to their square.
    /// </remarks>
    public partial class LinkedInSlugKeys : Migration
    {
        /// <summary>
        /// The statements <see cref="Up"/> runs, in order and as one script: exposed so that the plans SQLite is asked for
        /// are the plans of this very text. No statement holds a <c>;</c> of its own.
        /// </summary>
        internal static IReadOnlyList<string> Statements { get; } =
            Script.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        private const string Script =
            """
            CREATE TEMP TABLE linkedin_keys (source TEXT NOT NULL, id INTEGER NOT NULL, key TEXT NOT NULL, PRIMARY KEY (source, id));

            INSERT INTO linkedin_keys (source, id, key)
            SELECT source, id,
                   scheme || authority
                   || CASE WHEN host = 'linkedin.com' OR substr(host, -13) = '.linkedin.com' THEN lower(path) ELSE path END
                   || query
            FROM (
                SELECT source, id, scheme, authority,
                       CASE WHEN instr(authority, ':') > 0 THEN substr(authority, 1, instr(authority, ':') - 1) ELSE authority END AS host,
                       CASE WHEN instr(tail, '?') > 0 THEN substr(tail, 1, instr(tail, '?') - 1) ELSE tail END AS path,
                       CASE WHEN instr(tail, '?') > 0 THEN substr(tail, instr(tail, '?')) ELSE '' END AS query
                FROM (
                    SELECT source, id, scheme, substr(rest, 1, cut) AS authority, substr(rest, cut + 1) AS tail
                    FROM (
                        SELECT source, id, scheme, rest,
                               CASE
                                   WHEN instr(rest, '/') > 0 AND (instr(rest, '?') = 0 OR instr(rest, '/') < instr(rest, '?')) THEN instr(rest, '/') - 1
                                   WHEN instr(rest, '?') > 0 THEN instr(rest, '?') - 1
                                   ELSE length(rest)
                               END AS cut
                        FROM (
                            SELECT 'suppressions' AS source, id,
                                   substr(value, 1, instr(value, '://') + 2) AS scheme,
                                   substr(value, instr(value, '://') + 3) AS rest
                            FROM suppressions
                            WHERE channel = 'linkedin' AND instr(value, '://') > 0
                            UNION ALL
                            SELECT 'contact_channels', id,
                                   substr(value, 1, instr(value, '://') + 2),
                                   substr(value, instr(value, '://') + 3)
                            FROM contact_channels
                            WHERE channel = 'linkedin' AND instr(value, '://') > 0
                        )
                    )
                )
            );

            CREATE INDEX linkedin_keys_by_key ON linkedin_keys (source, key);

            DELETE FROM suppressions
            WHERE id IN (
                SELECT later.id
                FROM linkedin_keys later
                JOIN linkedin_keys earlier
                  ON earlier.source = 'suppressions' AND later.source = 'suppressions'
                 AND earlier.key = later.key AND earlier.id <> later.id
                JOIN suppressions l ON l.id = later.id
                JOIN suppressions e ON e.id = earlier.id
                WHERE e.created_at < l.created_at OR (e.created_at = l.created_at AND e.id < l.id)
            );

            DELETE FROM contact_channels
            WHERE id IN (
                SELECT later.id
                FROM linkedin_keys later
                JOIN linkedin_keys earlier
                  ON earlier.source = 'contact_channels' AND later.source = 'contact_channels'
                 AND earlier.key = later.key AND earlier.id <> later.id
                JOIN contact_channels l ON l.id = later.id
                JOIN contact_channels e ON e.id = earlier.id
                WHERE e.contact_id = l.contact_id
                  AND (e.is_primary > l.is_primary OR (e.is_primary = l.is_primary AND e.id < l.id))
            );

            UPDATE suppressions
            SET value = (SELECT key FROM linkedin_keys k WHERE k.source = 'suppressions' AND k.id = suppressions.id)
            WHERE id IN (SELECT id FROM linkedin_keys WHERE source = 'suppressions');

            UPDATE contact_channels
            SET value = (SELECT key FROM linkedin_keys k WHERE k.source = 'contact_channels' AND k.id = contact_channels.id)
            WHERE id IN (SELECT id FROM linkedin_keys WHERE source = 'contact_channels');

            DROP TABLE linkedin_keys;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(Script);

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The spellings before cannot be recovered, and are not needed: a lowercased slug is a key the rule before
            // also wrote for a link given in lower case, and the stops collapsed into one are one person's.
        }
    }
}
