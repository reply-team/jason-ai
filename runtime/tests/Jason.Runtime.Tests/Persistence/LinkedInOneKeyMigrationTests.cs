using System.Text.RegularExpressions;
using Jason.Contracts.Api;
using Jason.Contracts.Channels;
using Jason.Contracts.Tests.Channels;
using Jason.Runtime.Contacts;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Routing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;
using LinkedInOneKey = Jason.Runtime.Persistence.Migrations.LinkedInOneKey;

namespace Jason.Runtime.Tests.Persistence;

/// <summary>
/// A LinkedIn profile link is keyed by its profile's name, so the links stored under the rules before are re-keyed by the
/// rule itself: the register's entries and the contacts' channels together, because the claim asks the register about a
/// contact's stored value as it stands. Nothing is deleted. What the rule refuses is left as it stands; of several rows
/// that come to share a key one holds it and the rest are left as they stood. Each case seeds a database at the
/// migration before, as an installation upgraded from it holds one.
/// </summary>
public partial class LinkedInOneKeyMigrationTests
{
    private const string PreviousMigration = "20261020003554_WorkItemsByFinish";

    private const string ThisMigration = "20261020050039_LinkedInOneKey";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// Every spelling of a profile becomes its key; a link the rule refuses — another kind of page, a stray percent sign,
    /// another host — is left exactly as it was written; and nothing but a LinkedIn value is read: another channel holding
    /// a LinkedIn-looking value, and an email, are not touched.
    /// </summary>
    [Fact]
    public void Stored_links_become_their_keys_and_what_the_rule_refuses_is_left_as_it_stands()
    {
        using var dir = Seeded(
            """
            INSERT INTO suppressions (public_id, channel, value, reason, created_at) VALUES
              ('sup_doubled', 'linkedin', 'https://www.linkedin.com/in/ana-sample/', NULL, '2026-01-05 09:00:00'),
              ('sup_query', 'linkedin', 'https://uk.linkedin.com/in/ola-sample?Trk=Feed', NULL, '2026-01-05 09:00:00'),
              ('sup_upper', 'linkedin', 'https://www.linkedin.com/in/jos%c3%89-sample', NULL, '2026-01-05 09:00:00'),
              ('sup_key', 'linkedin', 'https://www.linkedin.com/in/eva-sample', NULL, '2026-01-05 09:00:00'),
              ('sup_pub', 'linkedin', 'https://www.linkedin.com/pub/ida-sample/1/2/3', NULL, '2026-01-05 09:00:00'),
              ('sup_stray', 'linkedin', 'https://www.linkedin.com/in/50%25off', NULL, '2026-01-05 09:00:00'),
              ('sup_host', 'linkedin', 'https://example.com/in/ida-sample', NULL, '2026-01-05 09:00:00'),
              ('sup_website', 'website', 'https://www.linkedin.com/in/Keep-Case/', NULL, '2026-01-05 09:00:00'),
              ('sup_email', 'email', 'marta@example.com', NULL, '2026-01-05 09:00:00');
            """);

        Assert.Equal(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["sup_doubled"] = "https://www.linkedin.com/in/ana-sample",
                ["sup_query"] = "https://www.linkedin.com/in/ola-sample",
                ["sup_upper"] = "https://www.linkedin.com/in/josé-sample",
                ["sup_key"] = "https://www.linkedin.com/in/eva-sample",
                ["sup_pub"] = "https://www.linkedin.com/pub/ida-sample/1/2/3",
                ["sup_stray"] = "https://www.linkedin.com/in/50%25off",
                ["sup_host"] = "https://example.com/in/ida-sample",
                ["sup_website"] = "https://www.linkedin.com/in/Keep-Case/",
                ["sup_email"] = "marta@example.com",
            },
            Rows(dir, "SELECT public_id, value FROM suppressions"));
    }

    /// <summary>
    /// Entries of the register that come to share a key: one holds it — the one already equal to it, else the earliest —
    /// and the others are left as they stood, with their identifiers and reasons. Nothing is deleted, so no stop is lost:
    /// whichever entry holds the key covers the person. The chronicle is not written to.
    /// </summary>
    [Fact]
    public void Entries_that_share_a_key_leave_one_holding_it_and_the_others_as_they_stood()
    {
        using var dir = Seeded(
            """
            INSERT INTO suppressions (public_id, channel, value, reason, created_at) VALUES
              ('sup_old_spelling', 'linkedin', 'https://www.linkedin.com/in/marta-example/', 'said stop', '2026-01-05 09:00:00'),
              ('sup_newer_key', 'linkedin', 'https://www.linkedin.com/in/marta-example', 'asked again', '2026-02-05 09:00:00'),
              ('sup_ola_later', 'linkedin', 'https://uk.linkedin.com/in/ola-sample', 'second', '2026-03-05 09:00:00'),
              ('sup_ola_first', 'linkedin', 'https://www.linkedin.com/in/ola-sample?trk=feed', 'first', '2026-01-05 09:00:00'),
              ('sup_tie_one', 'linkedin', 'https://www.linkedin.com/in/eva-sample/', 'tie, written first', '2026-01-05 09:00:00'),
              ('sup_tie_two', 'linkedin', 'https://www.linkedin.com/in/eva-sample//', 'tie, written second', '2026-01-05 09:00:00');
            """);

        Assert.Equal(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["sup_old_spelling"] = "https://www.linkedin.com/in/marta-example/|said stop",
                ["sup_newer_key"] = "https://www.linkedin.com/in/marta-example|asked again",
                ["sup_ola_later"] = "https://uk.linkedin.com/in/ola-sample|second",
                ["sup_ola_first"] = "https://www.linkedin.com/in/ola-sample|first",
                ["sup_tie_one"] = "https://www.linkedin.com/in/eva-sample|tie, written first",
                ["sup_tie_two"] = "https://www.linkedin.com/in/eva-sample//|tie, written second",
            },
            Rows(dir, "SELECT public_id, value || '|' || coalesce(reason, '') FROM suppressions"));
        Assert.Equal(0, Count(dir, "SELECT count(*) FROM journal"));
    }

    /// <summary>
    /// A contact's rows that come to share a key: the one already equal to it holds it, else the one the claim reads — the
    /// primary, then the lowest id — and the others are left as they stood, labels and data with them. Nothing is deleted.
    /// </summary>
    [Fact]
    public void A_contact_s_rows_that_share_a_key_leave_one_holding_it_and_the_others_as_they_stood()
    {
        using var dir = Seeded(
            """
            INSERT INTO contacts (public_id, custom_json, created_at, updated_at) VALUES
              ('cnt_primary', '{}', '2026-01-01', '2026-01-01'),
              ('cnt_lowest', '{}', '2026-01-01', '2026-01-01'),
              ('cnt_other', '{}', '2026-01-01', '2026-01-01');
            INSERT INTO contact_channels (contact_id, channel, value, label, is_primary) VALUES
              (1, 'linkedin', 'https://www.linkedin.com/in/marta-example?trk=feed', 'work', 0),
              (1, 'linkedin', 'https://www.linkedin.com/in/Marta-Example/', 'home', 1),
              (2, 'linkedin', 'https://www.linkedin.com/in/ana-sample/', 'first', 0),
              (2, 'linkedin', 'https://uk.linkedin.com/in/ana-sample', 'second', 0),
              (3, 'linkedin', 'https://www.linkedin.com/in/ola-sample/', NULL, 1),
              (3, 'email', 'Ola@Example.com', NULL, 1);
            """);

        Assert.Equal(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["1"] = "1|https://www.linkedin.com/in/marta-example?trk=feed|work|0",
                ["2"] = "1|https://www.linkedin.com/in/marta-example|home|1",
                ["3"] = "2|https://www.linkedin.com/in/ana-sample|first|0",
                ["4"] = "2|https://uk.linkedin.com/in/ana-sample|second|0",
                ["5"] = "3|https://www.linkedin.com/in/ola-sample||1",
                ["6"] = "3|Ola@Example.com||1",
            },
            Rows(dir, "SELECT id, contact_id || '|' || value || '|' || coalesce(label, '') || '|' || is_primary FROM contact_channels"));
    }

    /// <summary>
    /// The case the claim decides: the row the claim reads for a contact is left beside another row that already holds the
    /// key. The claim asks the register about the row it reads, so the primary flag moves to the holder — demoted, then
    /// promoted, never two at once — and the row left keeps its value, label and data. Where the contact had no primary,
    /// the claim read the lowest id, so the holder is made the primary. Every contact keeps at most one primary.
    /// </summary>
    [Fact]
    public void The_primary_moves_to_the_row_holding_the_key_so_the_claim_reads_the_key()
    {
        using var dir = Seeded(
            """
            INSERT INTO contacts (public_id, custom_json, created_at, updated_at) VALUES
              ('cnt_moved', '{}', '2026-01-01', '2026-01-01'),
              ('cnt_none', '{}', '2026-01-01', '2026-01-01'),
              ('cnt_kept', '{}', '2026-01-01', '2026-01-01');
            INSERT INTO contact_channels (contact_id, channel, value, label, is_primary, data_json) VALUES
              (1, 'linkedin', 'https://www.linkedin.com/in/Marta-Example/', 'old', 1, '{"seen":1}'),
              (1, 'linkedin', 'https://www.linkedin.com/in/marta-example', 'key', 0, NULL),
              (2, 'linkedin', 'https://www.linkedin.com/in/Ana-Sample/', 'lowest', 0, NULL),
              (2, 'linkedin', 'https://www.linkedin.com/in/ana-sample', 'key', 0, NULL),
              (3, 'linkedin', 'https://www.linkedin.com/pub/ola-sample/1/2/3', 'refused', 1, NULL),
              (3, 'linkedin', 'https://www.linkedin.com/in/Ola-Sample/', 'other', 0, NULL);
            """);

        Assert.Equal(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["1"] = "1|https://www.linkedin.com/in/Marta-Example/|old|0|{\"seen\":1}",
                ["2"] = "1|https://www.linkedin.com/in/marta-example|key|1|",
                ["3"] = "2|https://www.linkedin.com/in/Ana-Sample/|lowest|0|",
                ["4"] = "2|https://www.linkedin.com/in/ana-sample|key|1|",
                ["5"] = "3|https://www.linkedin.com/pub/ola-sample/1/2/3|refused|1|",
                ["6"] = "3|https://www.linkedin.com/in/ola-sample|other|0|",
            },
            Rows(dir, "SELECT id, contact_id || '|' || value || '|' || coalesce(label, '') || '|' || is_primary || '|' || coalesce(data_json, '') FROM contact_channels"));
    }

    /// <summary>
    /// A contact whose primary the rule refuses, and whose other rows come to share a key: the claim reads the refused
    /// primary, which holds no key, so the primary stays where it is; of the other two the key holds it, and the row beside
    /// it is left as it stood. The contact keeps exactly one primary, and the start does not fail.
    /// </summary>
    [Fact]
    public void A_contact_whose_primary_the_rule_refuses_and_whose_other_rows_share_a_key_keeps_its_one_primary()
    {
        using var dir = Seeded(
            """
            INSERT INTO contacts (public_id, custom_json, created_at, updated_at) VALUES ('cnt_shape', '{}', '2026-01-01', '2026-01-01');
            INSERT INTO contact_channels (contact_id, channel, value, label, is_primary) VALUES
              (1, 'linkedin', 'https://www.linkedin.com/pub/ola-sample/1/2/3', 'refused', 1),
              (1, 'linkedin', 'https://www.linkedin.com/in/ola-sample/', 'old', 0),
              (1, 'linkedin', 'https://www.linkedin.com/in/ola-sample', 'key', 0);
            """);

        Assert.Equal(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["1"] = "1|https://www.linkedin.com/pub/ola-sample/1/2/3",
                ["2"] = "0|https://www.linkedin.com/in/ola-sample/",
                ["3"] = "0|https://www.linkedin.com/in/ola-sample",
            },
            Rows(dir, "SELECT id, is_primary || '|' || value FROM contact_channels"));
    }

    /// <summary>
    /// A link stored longer than a value may be given: the rules before wrote each letter outside ASCII as its escapes and
    /// kept the query, so a link given within 500 characters was stored as 513. It names its profile all the same, so it
    /// is re-keyed by the channel's own rule, which bounds a key by its name, and its stop covers the contact written now.
    /// </summary>
    [Fact]
    public async Task A_link_stored_longer_than_a_value_may_be_given_becomes_its_key_and_its_stop_covers_the_person()
    {
        var stored = "https://www.linkedin.com/in/" + string.Concat(Enumerable.Repeat("%d0%b0", 30)) + "?trk=" + new string('x', 300);
        var key = "https://www.linkedin.com/in/" + new string('\u0430', 30);
        Assert.True(stored.Length == 513, $"the stored link is {stored.Length} characters.");
        using var dir = Seeded(
            $$"""
            INSERT INTO contacts (public_id, custom_json, created_at, updated_at) VALUES ('cnt_long', '{}', '2026-01-01', '2026-01-01');
            INSERT INTO contact_channels (contact_id, channel, value, is_primary) VALUES (1, 'linkedin', '{{stored}}', 1);
            INSERT INTO suppressions (public_id, channel, value, reason, created_at) VALUES ('sup_long', 'linkedin', '{{stored}}', 'said stop', '2026-01-05 09:00:00');
            """);

        Assert.Equal(key, Rows(dir, "SELECT public_id, value FROM suppressions")["sup_long"]);
        Assert.Equal(key, Rows(dir, "SELECT contact_id, value FROM contact_channels")["1"]);

        using var db = new JasonDbContext(JasonDbContext.CreateOptions(dir.Paths.DatabaseFile));
        var written = ChannelRules.KeyOf(ChannelRules.LinkedIn, "https://www.linkedin.com/in/" + new string('\u0430', 30) + "?trk=" + new string('x', 300));
        Assert.True(
            await Register(db).IsSuppressedAsync(db, ChannelRules.LinkedIn, written!, Ct),
            $"a contact written now is stored under '{written}', and the stop recorded before the upgrade does not cover it.");
    }

    /// <summary>
    /// An entry the start left beside the one holding its key is found by its own text, and only by it: listed by it, and
    /// lifted by it, and the person is still out of reach afterwards, because the entry holding the key still stands.
    /// Lifting by the key's own text then lifts the stop.
    /// </summary>
    [Fact]
    public async Task An_entry_left_beside_its_key_is_listed_and_lifted_by_its_own_text_and_the_stop_stays()
    {
        const string Key = "https://www.linkedin.com/in/marta-example";
        const string Beside = "https://www.linkedin.com/in/marta-example?trk=feed";
        using var dir = Seeded(
            $"""
            INSERT INTO suppressions (public_id, channel, value, reason, created_at) VALUES
              ('sup_key', 'linkedin', '{Key}', 'said stop', '2026-01-05 09:00:00'),
              ('sup_beside', 'linkedin', '{Beside}', 'said stop again', '2026-01-06 09:00:00');
            """);
        UpToDate(dir);
        using var db = new JasonDbContext(JasonDbContext.CreateOptions(dir.Paths.DatabaseFile));
        var register = Register(db);

        var listed = await register.ListAsync(new SuppressionListRequest(ChannelRules.LinkedIn, " " + Beside + " ", null, null), Ct);
        var ids = string.Join(",", listed.Items.Select(item => item.Id));
        Assert.True(ids == "sup_beside", $"listing by the left entry's own text answered [{ids}].");

        var removed = await register.RemoveAsync(new SuppressionRemoveRequest(ChannelRules.LinkedIn, Beside, null, "the stop was recorded twice"), Ct);
        Assert.Equal(new SuppressionRemovedDto(ChannelRules.LinkedIn, Beside, true), removed);
        Assert.Equal("sup_key", Assert.Single(await db.Suppressions.AsNoTracking().ToListAsync(Ct)).PublicId);
        Assert.True(await register.IsSuppressedAsync(db, ChannelRules.LinkedIn, Key, Ct), "after the left entry was lifted the person is no longer out of reach.");

        var lifted = await register.RemoveAsync(new SuppressionRemoveRequest(ChannelRules.LinkedIn, Key + "/", null, "asked to be in touch again"), Ct);
        Assert.Equal(new SuppressionRemovedDto(ChannelRules.LinkedIn, Key, true), lifted);
        Assert.False(await register.IsSuppressedAsync(db, ChannelRules.LinkedIn, Key, Ct), "the stop was lifted by the key's spelling and still holds.");
    }

    /// <summary>Two contacts holding one profile both take its key: contacts are not merged here, and a stop covers both.</summary>
    [Fact]
    public void Two_contacts_holding_one_profile_both_take_its_key()
    {
        using var dir = Seeded(
            """
            INSERT INTO contacts (public_id, custom_json, created_at, updated_at) VALUES
              ('cnt_a', '{}', '2026-01-01', '2026-01-01'),
              ('cnt_b', '{}', '2026-01-01', '2026-01-01');
            INSERT INTO contact_channels (contact_id, channel, value, is_primary) VALUES
              (1, 'linkedin', 'https://www.linkedin.com/in/marta-example/', 1),
              (2, 'linkedin', 'https://uk.linkedin.com/in/Marta-Example?trk=feed', 1);
            """);

        Assert.Equal(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["1"] = "https://www.linkedin.com/in/marta-example",
                ["2"] = "https://www.linkedin.com/in/marta-example",
            },
            Rows(dir, "SELECT contact_id, value FROM contact_channels"));
    }

    /// <summary>
    /// The claim's one question, asked after the upgrade for every contact: is the value it is reached at on the register?
    /// It is, for every contact whose stop was recorded under another spelling of its profile — a doubled slash, a query,
    /// another host, a letter written as an escape or in another case — and for the contact whose claimed row was left
    /// beside the key, because the primary moved to the key.
    /// </summary>
    [Fact]
    public async Task After_the_upgrade_the_register_refuses_every_contact_whose_stop_names_its_profile()
    {
        using var dir = Seeded(
            """
            INSERT INTO contacts (public_id, custom_json, created_at, updated_at) VALUES
              ('cnt_slash', '{}', '2026-01-01', '2026-01-01'),
              ('cnt_query', '{}', '2026-01-01', '2026-01-01'),
              ('cnt_host', '{}', '2026-01-01', '2026-01-01'),
              ('cnt_escape', '{}', '2026-01-01', '2026-01-01'),
              ('cnt_moved', '{}', '2026-01-01', '2026-01-01');
            INSERT INTO contact_channels (contact_id, channel, value, is_primary) VALUES
              (1, 'linkedin', 'https://www.linkedin.com/in/marta-example/', 1),
              (2, 'linkedin', 'https://www.linkedin.com/in/ana-sample?trk=feed', 1),
              (3, 'linkedin', 'https://uk.linkedin.com/in/ola-sample', 1),
              (4, 'linkedin', 'https://www.linkedin.com/in/jos%c3%89-sample', 1),
              (5, 'linkedin', 'https://www.linkedin.com/in/Eva-Sample/', 1),
              (5, 'linkedin', 'https://www.linkedin.com/in/eva-sample', 0);
            INSERT INTO suppressions (public_id, channel, value, reason, created_at) VALUES
              ('sup_slash', 'linkedin', 'https://www.linkedin.com/in/marta-example', NULL, '2026-01-05 09:00:00'),
              ('sup_query', 'linkedin', 'https://www.linkedin.com/in/ana-sample', NULL, '2026-01-05 09:00:00'),
              ('sup_host', 'linkedin', 'https://www.linkedin.com/in/ola-sample/', NULL, '2026-01-05 09:00:00'),
              ('sup_escape', 'linkedin', 'https://www.linkedin.com/in/jos%c3%a9-sample', NULL, '2026-01-05 09:00:00'),
              ('sup_moved', 'linkedin', 'https://www.linkedin.com/in/eva-sample', NULL, '2026-01-05 09:00:00');
            """);

        using var db = new JasonDbContext(JasonDbContext.CreateOptions(dir.Paths.DatabaseFile));
        var clock = new FixedClock(new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero));
        var register = new SuppressionService(db, new JournalWriter(clock), clock);

        foreach (var contact in await db.Contacts.Include(person => person.Channels).ToListAsync(Ct))
        {
            var reached = CanonicalInput.Reachable(contact, ChannelRules.LinkedIn);
            Assert.True(reached is not null, $"{contact.PublicId} is reached at no LinkedIn value.");
            Assert.True(await register.IsSuppressedAsync(db, ChannelRules.LinkedIn, reached.Value, Ct), $"{contact.PublicId}, reached at '{reached.Value}', is not refused.");
        }
    }

    /// <summary>
    /// The rewrite is the rule, and not a second one: every row of the rule's own table, stored as one contact's channel,
    /// becomes exactly the key the rule gives it, or stays exactly as it was where the rule refuses it.
    /// </summary>
    [Fact]
    public void Every_row_of_the_rule_s_table_becomes_its_key_or_stays_as_it_was()
    {
        var rows = LinkedInKeyVectors.Rows;
        using var dir = AtTheMigrationBefore();
        using (var connection = Open(dir))
        {
            for (var at = 0; at < rows.Count; at++)
            {
                Execute(connection, System.FormattableString.Invariant(
                    $"INSERT INTO contacts (public_id, custom_json, created_at, updated_at) VALUES ('cnt_{at}', '{{}}', '2026-01-01', '2026-01-01')"));
                using var insert = connection.CreateCommand();
                insert.CommandText = "INSERT INTO contact_channels (contact_id, channel, value, is_primary) VALUES ($contact, 'linkedin', $value, 1)";
                insert.Parameters.AddWithValue("$contact", at + 1);
                insert.Parameters.AddWithValue("$value", rows[at].Raw);
                insert.ExecuteNonQuery();
            }
        }

        MigrateToThis(dir);
        var migrated = Rows(dir, "SELECT contact_id, value FROM contact_channels");

        for (var at = 0; at < rows.Count; at++)
        {
            var (raw, key) = rows[at];
            var stored = migrated[System.FormattableString.Invariant($"{at + 1}")];
            Assert.True(stored == (key ?? raw), $"'{raw}' became '{stored}', where the rule keys it '{key ?? "(refused: left as it was)"}'.");
        }
    }

    /// <summary>
    /// A start that applied the re-key says what it left, from the final state and by count alone — every link stored,
    /// those that are keys, those the rule refuses, those left beside the one holding their key, and the keys more than one
    /// contact holds — so a person knows whether there is anything to look at. No two of the counts are equal here, so
    /// none can stand in for another unnoticed. A start that applied nothing says nothing.
    /// </summary>
    [Fact]
    public void The_start_that_re_keys_counts_what_it_left_and_a_later_start_counts_nothing()
    {
        using var dir = AtTheMigrationBefore();
        using (var connection = Open(dir))
        {
            Execute(
                connection,
                """
                INSERT INTO contacts (public_id, custom_json, created_at, updated_at) VALUES
                  ('cnt_a', '{}', '2026-01-01', '2026-01-01'),
                  ('cnt_b', '{}', '2026-01-01', '2026-01-01'),
                  ('cnt_c', '{}', '2026-01-01', '2026-01-01');
                INSERT INTO contact_channels (contact_id, channel, value, is_primary) VALUES
                  (1, 'linkedin', 'https://www.linkedin.com/in/marta-example/', 1),
                  (1, 'linkedin', 'https://www.linkedin.com/in/Marta-Example', 0),
                  (2, 'linkedin', 'https://uk.linkedin.com/in/marta-example', 1),
                  (2, 'linkedin', 'https://www.linkedin.com/company/example', 0),
                  (2, 'email', 'marta@example.com', 1),
                  (3, 'linkedin', 'https://www.linkedin.com/in/ana-sample', 1);
                INSERT INTO suppressions (public_id, channel, value, reason, created_at) VALUES
                  ('sup_a', 'linkedin', 'https://www.linkedin.com/in/marta-example', NULL, '2026-01-05 09:00:00'),
                  ('sup_b', 'linkedin', 'https://www.linkedin.com/in/Marta-Example/', NULL, '2026-01-06 09:00:00'),
                  ('sup_c', 'linkedin', 'https://www.linkedin.com/in/marta-example?trk=feed', NULL, '2026-01-07 09:00:00'),
                  ('sup_d', 'linkedin', 'https://www.linkedin.com/in/eva-sample/', NULL, '2026-01-05 09:00:00'),
                  ('sup_e', 'linkedin', 'https://www.linkedin.com/pub/ola-sample/1/2/3', NULL, '2026-01-05 09:00:00');
                """);
        }

        var first = new DatabaseMigrator(dir.Paths).Migrate();
        var again = new DatabaseMigrator(dir.Paths).Migrate();

        Assert.Contains(ThisMigration, first.NewlyApplied);
        Assert.Equal(new LinkedInKeyCounts(Stored: 10, Keys: 5, Refused: 2, LeftBeside: 3, SharedKeys: 1), first.LinkedInKeys);
        Assert.Null(again.LinkedInKeys);
    }

    /// <summary>A database this start makes holds nothing to re-key, so a first start counts nothing.</summary>
    [Fact]
    public void A_first_start_counts_nothing()
    {
        using var dir = new TempDataDir();

        var report = new DatabaseMigrator(dir.Paths).Migrate();

        Assert.Contains(ThisMigration, report.NewlyApplied);
        Assert.Null(report.LinkedInKeys);
    }

    /// <summary>
    /// The script calls the rule through a function the runtime registers on every connection it opens, and through
    /// no other: a function the runtime did not register would fail the start rather than run. A connection that did not
    /// register it is refused loudly, by name, and changes nothing.
    /// </summary>
    [Fact]
    public void The_script_calls_only_functions_the_runtime_registers_and_fails_loudly_without_them()
    {
        var called = FunctionCall().Matches(LinkedInOneKey.Script).Select(match => match.Value).ToHashSet(StringComparer.Ordinal);
        string[] registered = [LinkedInKeyFunction.FunctionName, TextFold.FunctionName];

        Assert.Equal([LinkedInKeyFunction.FunctionName], called);
        Assert.All(called, name => Assert.Contains(name, registered));

        using var dir = AtTheMigrationBefore();
        using var bare = Open(dir);
        Execute(bare, "INSERT INTO suppressions (public_id, channel, value, reason, created_at) VALUES ('sup_a', 'linkedin', 'https://www.linkedin.com/in/marta-example/', NULL, '2026-01-05 09:00:00')");
        var refused = Assert.Throws<SqliteException>(() =>
        {
            foreach (var statement in LinkedInOneKey.Statements)
            {
                Execute(bare, statement);
            }
        });

        Assert.Contains("no such function: " + LinkedInKeyFunction.FunctionName, refused.Message, StringComparison.Ordinal);
        Assert.Equal("https://www.linkedin.com/in/marta-example/", Rows(bare, "SELECT public_id, value FROM suppressions")["sup_a"]);
    }

    /// <summary>
    /// The migration runs as the runtime starts, so its cost has to grow with the rows it reads and no faster: a start
    /// that outlasts the CLI's wait is a runtime that did not come up. Each statement is asked for SQLite's own plan of the
    /// very text the migration runs, and so are the two counts the start makes after it. No step scans a table: the
    /// register and the contacts' channels are read through their index by channel, or by identifier. Every read of the
    /// table of keys is a search through one of its own two indexes, never an index SQLite builds for one statement; at
    /// most one of them passes over a source's rows, and never inside a subquery that runs once per row; every other read
    /// finds its rows by identifier or by key.
    /// </summary>
    [Fact]
    public void Every_statement_reads_through_an_index_and_never_once_per_row()
    {
        Assert.Equal(
            LinkedInOneKey.Statements,
            new LinkedInOneKey().UpOperations.OfType<SqlOperation>().Single().Sql
                .Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));

        using var dir = AtTheMigrationBefore();
        using var connection = Open(dir);
        connection.CreateFunction<string?, string?>(LinkedInKeyFunction.FunctionName, LinkedInKeyFunction.Apply, isDeterministic: true);
        var problems = new List<string>();
        var planned = 0;
        foreach (var statement in LinkedInOneKey.Statements.Concat([DatabaseMigrator.LinkedInCounts, DatabaseMigrator.LinkedInSharedKeys]))
        {
            if (!statement.StartsWith("CREATE", StringComparison.Ordinal) && !statement.StartsWith("DROP", StringComparison.Ordinal))
            {
                planned++;
                var plan = PlanOf(connection, statement);
                var names = KeyTableNames().Matches(statement)
                    .Select(match => match.Groups["alias"].Success ? match.Groups["alias"].Value : "linkedin_one_key")
                    .Append("linkedin_one_key")
                    .ToHashSet(StringComparer.Ordinal);
                var reads = plan.Where(step => Access().Match(step.Detail) is { Success: true } access && names.Contains(access.Groups["name"].Value)).ToList();
                var passes = reads.Where(step => !ByIdentifierOrKey().IsMatch(step.Detail)).ToList();

                var wrong = new List<string>();
                if (plan.Any(step => TableScan().IsMatch(step.Detail)))
                {
                    wrong.Add("a step of its plan scans a table");
                }

                if (plan.Any(step => step.Detail.Contains("AUTOMATIC", StringComparison.Ordinal)))
                {
                    wrong.Add("it builds an index for itself");
                }

                if (reads.Any(step => !TheTablesOwnIndex().IsMatch(step.Detail)))
                {
                    wrong.Add("it reads the table of keys other than through one of the table's own indexes");
                }

                if (passes.Count > 1)
                {
                    wrong.Add("it passes over the table of keys more than once");
                }

                if (passes.Any(pass => Ancestors(plan, pass).Any(step => step.Detail.StartsWith("CORRELATED", StringComparison.Ordinal))))
                {
                    wrong.Add("it passes over the table of keys once for every row");
                }

                if (wrong.Count > 0)
                {
                    problems.Add($"'{statement.Split('\n')[0]}': {string.Join("; ", wrong)}. Its plan:\n{string.Join('\n', plan.Select(step => "  " + step.Detail))}");
                }
            }

            if (LinkedInOneKey.Statements.Contains(statement))
            {
                Execute(connection, statement);
            }
        }

        Assert.Equal(7, planned);
        Assert.True(problems.Count == 0, string.Join("\n\n", problems));
    }

    /// <summary>
    /// The table of keys and both of its indexes are made in the connection's temporary schema, and the script's last
    /// statement drops them, so the database the runtime keeps holds none of them, and neither does the connection that
    /// ran it.
    /// </summary>
    [Fact]
    public void The_table_of_keys_and_its_indexes_are_temporary_and_the_script_leaves_nothing_behind()
    {
        using var dir = AtTheMigrationBefore();
        using var connection = Open(dir);
        connection.CreateFunction<string?, string?>(LinkedInKeyFunction.FunctionName, LinkedInKeyFunction.Apply, isDeterministic: true);
        foreach (var statement in LinkedInOneKey.Statements)
        {
            if (statement.StartsWith("DROP", StringComparison.Ordinal))
            {
                Assert.Equal(
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["linkedin_one_key"] = "table",
                        ["linkedin_one_key_by_key"] = "index",
                        ["sqlite_autoindex_linkedin_one_key_1"] = "index",
                    },
                    Rows(connection, "SELECT name, type FROM sqlite_temp_master WHERE tbl_name = 'linkedin_one_key'"));
                Assert.Empty(Rows(connection, "SELECT name, type FROM sqlite_master WHERE name LIKE 'linkedin_one_key%' OR tbl_name = 'linkedin_one_key'"));
            }

            Execute(connection, statement);
        }

        Assert.Empty(Rows(connection, "SELECT name, type FROM sqlite_temp_master"));

        using var migrated = Seeded(string.Empty);
        Assert.Empty(Rows(migrated, "SELECT name, type FROM sqlite_master WHERE name LIKE 'linkedin_one_key%' OR tbl_name = 'linkedin_one_key'"));
    }

    private sealed record PlanStep(long Id, long Parent, string Detail);

    private static List<PlanStep> PlanOf(SqliteConnection connection, string statement)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "EXPLAIN QUERY PLAN " + statement;
        using var reader = command.ExecuteReader();
        var plan = new List<PlanStep>();
        while (reader.Read())
        {
            plan.Add(new PlanStep(reader.GetInt64(0), reader.GetInt64(1), reader.GetString(3)));
        }

        return plan;
    }

    private static void Execute(SqliteConnection connection, string statement)
    {
        using var command = connection.CreateCommand();
        command.CommandText = statement;
        command.ExecuteNonQuery();
    }

    private static IEnumerable<PlanStep> Ancestors(List<PlanStep> plan, PlanStep step)
    {
        for (var parent = plan.FirstOrDefault(other => other.Id == step.Parent); parent is not null; parent = plan.FirstOrDefault(other => other.Id == parent.Parent))
        {
            yield return parent;
        }
    }

    /// <summary>A call of one of the runtime's own database functions, by its name.</summary>
    [GeneratedRegex(@"\bjason_[a-z_]+(?=\()")]
    private static partial Regex FunctionCall();

    /// <summary>The table of keys as a statement names it: bare, or under the alias that follows it.</summary>
    [GeneratedRegex(@"\blinkedin_one_key\b(?:\s+(?!WHERE\b|JOIN\b|ON\b|SET\b|AS\b)(?<alias>[a-z_][a-z0-9_]*))?")]
    private static partial Regex KeyTableNames();

    /// <summary>One step of a plan that reads a table, and the name the statement gives it.</summary>
    [GeneratedRegex(@"^(?:SCAN|SEARCH) (?:TABLE )?(?<name>[a-z_][a-z0-9_]*)\b")]
    private static partial Regex Access();

    /// <summary>A step that scans a table by name, rather than a subquery or a list it made.</summary>
    [GeneratedRegex(@"^SCAN (?:TABLE )?[a-z_][a-z0-9_]*\b")]
    private static partial Regex TableScan();

    /// <summary>A read through the table's primary key or its index by key: never an index SQLite builds for one statement.</summary>
    [GeneratedRegex(@" USING (?:COVERING )?INDEX (?:sqlite_autoindex_linkedin_one_key_1|linkedin_one_key_by_key) \(")]
    private static partial Regex TheTablesOwnIndex();

    /// <summary>A read that binds the row's identifier or its key, so it reaches the rows it needs alone.</summary>
    [GeneratedRegex(@"\((?:[^)]*\b(?:id|key)=\?)")]
    private static partial Regex ByIdentifierOrKey();

    /// <summary>A database at the migration before this one, as an installation upgraded from it holds one.</summary>
    private static TempDataDir AtTheMigrationBefore()
    {
        var dir = new TempDataDir();
        Directory.CreateDirectory(dir.Paths.StateDirectory);
        using var db = new JasonDbContext(JasonDbContext.CreateOptions(dir.Paths.DatabaseFile));
        db.GetInfrastructure().GetRequiredService<IMigrator>().Migrate(PreviousMigration);
        return dir;
    }

    private static void MigrateToThis(TempDataDir dir)
    {
        using var db = new JasonDbContext(JasonDbContext.CreateOptions(dir.Paths.DatabaseFile));
        db.GetInfrastructure().GetRequiredService<IMigrator>().Migrate(ThisMigration);
    }

    /// <summary>The migrations after this one as well, for a case that goes on to write through today's services.</summary>
    private static void UpToDate(TempDataDir dir)
    {
        using var db = new JasonDbContext(JasonDbContext.CreateOptions(dir.Paths.DatabaseFile));
        db.Database.Migrate();
    }

    /// <summary>A database at the migration before this one, the rows given written into it, then this migration applied.</summary>
    private static TempDataDir Seeded(string sql)
    {
        var dir = AtTheMigrationBefore();
        if (sql.Length > 0)
        {
            using var connection = Open(dir);
            Execute(connection, sql);
        }

        MigrateToThis(dir);
        return dir;
    }

    private static SqliteConnection Open(TempDataDir dir)
    {
        var connection = new SqliteConnection(JasonDbContext.ConnectionString(dir.Paths.DatabaseFile));
        connection.Open();
        return connection;
    }

    /// <summary>The first column of every row as the key, the second as the value.</summary>
    private static Dictionary<string, string> Rows(TempDataDir dir, string sql)
    {
        using var connection = Open(dir);
        return Rows(connection, sql);
    }

    /// <summary>The first column of every row as the key, the second as the value, read on the connection given.</summary>
    private static Dictionary<string, string> Rows(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        var rows = new Dictionary<string, string>(StringComparer.Ordinal);
        while (reader.Read())
        {
            rows[Convert.ToString(reader.GetValue(0), System.Globalization.CultureInfo.InvariantCulture)!] = reader.GetString(1);
        }

        return rows;
    }

    private static SuppressionService Register(JasonDbContext db)
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero));
        return new SuppressionService(db, new JournalWriter(clock), clock);
    }

    private static long Count(TempDataDir dir, string sql)
    {
        using var connection = Open(dir);
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (long)command.ExecuteScalar()!;
    }
}
