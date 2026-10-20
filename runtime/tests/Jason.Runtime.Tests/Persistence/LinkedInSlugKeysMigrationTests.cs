using System.Text.RegularExpressions;
using Jason.Runtime.Contacts;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;
using LinkedInSlugKeys = Jason.Runtime.Persistence.Migrations.LinkedInSlugKeys;

namespace Jason.Runtime.Tests.Persistence;

/// <summary>
/// A LinkedIn profile link names a person by its slug whatever its case, so the stored keys written before that rule are
/// rewritten to it: the do-not-contact register's and the contacts' channels together, because the claim asks the
/// register about a contact's stored value as it stands, and two stored forms of one rule would stop it refusing a
/// person who said stop. Two spellings of one person become that person's one stop: the earliest stays, and no stop is
/// lost. Each case seeds a database at the migration before, as an installation upgraded from it holds one.
/// </summary>
public partial class LinkedInSlugKeysMigrationTests
{
    private const string PreviousMigration = "20261018230454_EventReads";

    private const string ThisMigration = "20261019053859_LinkedInSlugKeys";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void A_stored_linkedin_key_is_lowercased_on_linkedin_hosts_and_nothing_else_is_touched()
    {
        using var dir = Seeded(
            """
            INSERT INTO suppressions (public_id, channel, value, reason, created_at) VALUES
              ('sup_a', 'linkedin', 'https://www.linkedin.com/in/Marta-Example', NULL, '2026-01-05 09:00:00'),
              ('sup_b', 'linkedin', 'https://uk.linkedin.com/in/Ana-Sample?Trk=Feed', NULL, '2026-01-05 09:00:00'),
              ('sup_c', 'linkedin', 'https://www.linkedin.com:8443/in/Ola-Sample', NULL, '2026-01-05 09:00:00'),
              ('sup_d', 'linkedin', 'https://linkedin.com', NULL, '2026-01-05 09:00:00'),
              ('sup_e', 'linkedin', 'https://www.linkedin.com?Ref=Feed', NULL, '2026-01-05 09:00:00'),
              ('sup_f', 'linkedin', 'https://notlinkedin.com/in/Marta-Example', NULL, '2026-01-05 09:00:00'),
              ('sup_g', 'linkedin', 'https://linkedin.com.example.org/in/Marta-Example', NULL, '2026-01-05 09:00:00'),
              ('sup_h', 'linkedin', 'https://example.com/In/Marta', NULL, '2026-01-05 09:00:00'),
              ('sup_i', 'website', 'https://www.linkedin.com/in/Keep-Case', NULL, '2026-01-05 09:00:00'),
              ('sup_j', 'email', 'marta@example.com', NULL, '2026-01-05 09:00:00');
            INSERT INTO contacts (public_id, custom_json, created_at, updated_at) VALUES
              ('cnt_a', '{}', '2026-01-01', '2026-01-01'),
              ('cnt_b', '{}', '2026-01-01', '2026-01-01');
            INSERT INTO contact_channels (contact_id, channel, value, is_primary) VALUES
              (1, 'linkedin', 'https://www.linkedin.com/in/Marta-Example', 1),
              (2, 'linkedin', 'https://www.linkedin.com/in/marta-EXAMPLE', 1);
            """);

        Assert.Equal(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["sup_a"] = "https://www.linkedin.com/in/marta-example",
                ["sup_b"] = "https://uk.linkedin.com/in/ana-sample?Trk=Feed",
                ["sup_c"] = "https://www.linkedin.com:8443/in/ola-sample",
                ["sup_d"] = "https://linkedin.com",
                ["sup_e"] = "https://www.linkedin.com?Ref=Feed",
                ["sup_f"] = "https://notlinkedin.com/in/Marta-Example",
                ["sup_g"] = "https://linkedin.com.example.org/in/Marta-Example",
                ["sup_h"] = "https://example.com/In/Marta",
                ["sup_i"] = "https://www.linkedin.com/in/Keep-Case",
                ["sup_j"] = "marta@example.com",
            },
            Rows(dir, "SELECT public_id, value FROM suppressions"));

        // The index on a channel's value is not unique, so two contacts holding one slug in two cases both survive,
        // lowercased: the migration does not fail on them, and contacts are not merged here.
        Assert.Equal(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["1"] = "https://www.linkedin.com/in/marta-example",
                ["2"] = "https://www.linkedin.com/in/marta-example",
            },
            Rows(dir, "SELECT contact_id, value FROM contact_channels"));
    }

    /// <summary>
    /// Three spellings of one person's slug were three stops under the rule before, and are one stop now: the earliest,
    /// with its own identifier and reason, under the new key. Where two were written at the same moment the one written
    /// first stays. The chronicle keeps every `suppression_added` line it had, and the migration writes none: the seeded
    /// database held no line, and holds none after it.
    /// </summary>
    [Fact]
    public void Two_spellings_of_one_person_become_that_person_s_one_stop_and_the_earliest_stays()
    {
        using var dir = Seeded(
            """
            INSERT INTO suppressions (public_id, channel, value, reason, created_at) VALUES
              ('sup_feb', 'linkedin', 'https://www.linkedin.com/in/marta-example', 'asked again', '2026-02-05 09:00:00'),
              ('sup_jan', 'linkedin', 'https://www.linkedin.com/in/Marta-Example', 'said stop on LinkedIn', '2026-01-05 09:00:00'),
              ('sup_mar', 'linkedin', 'https://www.linkedin.com/in/MARTA-example', NULL, '2026-03-05 09:00:00'),
              ('sup_tie1', 'linkedin', 'https://www.linkedin.com/in/Ola-Sample', 'first written', '2026-01-05 09:00:00'),
              ('sup_tie2', 'linkedin', 'https://www.linkedin.com/in/ola-SAMPLE', 'second written', '2026-01-05 09:00:00');
            """);

        Assert.Equal(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["sup_jan"] = "https://www.linkedin.com/in/marta-example|said stop on LinkedIn",
                ["sup_tie1"] = "https://www.linkedin.com/in/ola-sample|first written",
            },
            Rows(dir, "SELECT public_id, value || '|' || coalesce(reason, '') FROM suppressions"));
        Assert.Equal(0, Count(dir, "SELECT count(*) FROM journal"));
    }

    /// <summary>
    /// A contact's LinkedIn channels are rewritten with the register, so the claim compares one stored form with one. Two
    /// channels of one contact that become one value become one channel — the primary where one of them is, else the one
    /// written first — and the same value on two contacts stays on both: contacts are not merged here.
    /// </summary>
    [Fact]
    public void A_contact_s_linkedin_channels_are_rewritten_with_the_register_and_one_contact_keeps_one_of_two_spellings()
    {
        using var dir = Seeded(
            """
            INSERT INTO contacts (public_id, custom_json, created_at, updated_at) VALUES
              ('cnt_a', '{}', '2026-01-01', '2026-01-01'),
              ('cnt_b', '{}', '2026-01-01', '2026-01-01'),
              ('cnt_c', '{}', '2026-01-01', '2026-01-01');
            INSERT INTO contact_channels (contact_id, channel, value, label, is_primary) VALUES
              (1, 'linkedin', 'https://www.linkedin.com/in/Marta-Example', NULL, 1),
              (1, 'linkedin', 'https://www.linkedin.com/in/marta-example', 'work', 0),
              (1, 'email', 'Marta@Example.com', NULL, 1),
              (2, 'linkedin', 'https://www.linkedin.com/in/Marta-Example', NULL, 1),
              (3, 'linkedin', 'https://www.linkedin.com/in/Ana-Sample', 'old', 0),
              (3, 'linkedin', 'https://www.linkedin.com/in/ana-sample', NULL, 1);
            """);

        Assert.Equal(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["1"] = "1|linkedin|https://www.linkedin.com/in/marta-example|1",
                ["3"] = "1|email|Marta@Example.com|1",
                ["4"] = "2|linkedin|https://www.linkedin.com/in/marta-example|1",
                ["6"] = "3|linkedin|https://www.linkedin.com/in/ana-sample|1",
            },
            Rows(dir, "SELECT id, contact_id || '|' || channel || '|' || value || '|' || is_primary FROM contact_channels"));
    }

    /// <summary>
    /// A contact holds at most one primary value on a channel, and the index that says so is filtered to primaries. The
    /// collapse deletes only a value another of the same contact's values outranks — the primary, else the one written
    /// first — and never makes one primary, so every contact holds after it exactly the primaries it held before: never
    /// two, and never none where it had one, whichever of the spellings was primary and whatever else it holds.
    /// </summary>
    [Fact]
    public void The_collapse_leaves_every_contact_exactly_the_one_primary_it_had()
    {
        using var dir = Seeded(
            """
            INSERT INTO contacts (public_id, custom_json, created_at, updated_at) VALUES
              ('cnt_first', '{}', '2026-01-01', '2026-01-01'),
              ('cnt_later', '{}', '2026-01-01', '2026-01-01'),
              ('cnt_other', '{}', '2026-01-01', '2026-01-01'),
              ('cnt_three', '{}', '2026-01-01', '2026-01-01');
            INSERT INTO contact_channels (contact_id, channel, value, is_primary) VALUES
              (1, 'linkedin', 'https://www.linkedin.com/in/Marta-Example', 1),
              (1, 'linkedin', 'https://www.linkedin.com/in/marta-example', 0),
              (2, 'linkedin', 'https://www.linkedin.com/in/Ana-Sample', 0),
              (2, 'linkedin', 'https://www.linkedin.com/in/ANA-sample', 0),
              (2, 'linkedin', 'https://www.linkedin.com/in/ana-sample', 1),
              (3, 'linkedin', 'https://www.linkedin.com/in/eva-sample', 1),
              (3, 'linkedin', 'https://www.linkedin.com/in/Ola-Sample', 0),
              (3, 'linkedin', 'https://www.linkedin.com/in/ola-sample', 0),
              (4, 'linkedin', 'https://www.linkedin.com/in/Ida-Sample', 0),
              (4, 'linkedin', 'https://www.linkedin.com/in/ida-sample', 0);
            """);

        Assert.Contains(
            "WHERE is_primary = 1",
            Rows(dir, "SELECT name, sql FROM sqlite_master WHERE name = 'ix_contact_channels_one_primary_per_channel' AND sql LIKE 'CREATE UNIQUE INDEX%'")
                .GetValueOrDefault("ix_contact_channels_one_primary_per_channel", string.Empty),
            StringComparison.Ordinal);
        Assert.Equal(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["1"] = "1|linkedin|https://www.linkedin.com/in/marta-example|1",
                ["5"] = "2|linkedin|https://www.linkedin.com/in/ana-sample|1",
                ["6"] = "3|linkedin|https://www.linkedin.com/in/eva-sample|1",
                ["7"] = "3|linkedin|https://www.linkedin.com/in/ola-sample|0",
                ["9"] = "4|linkedin|https://www.linkedin.com/in/ida-sample|0",
            },
            Rows(dir, "SELECT id, contact_id || '|' || channel || '|' || value || '|' || is_primary FROM contact_channels"));
    }

    /// <summary>
    /// The claim's one question, asked after the upgrade: is the value this contact is reached at on the register? It
    /// is, for a stop and a contact written under one mixed-case spelling — which a rewrite of the register alone would
    /// have stopped refusing — and for two spellings the rule before kept apart.
    /// </summary>
    [Fact]
    public async Task After_the_rewrite_the_register_refuses_a_contact_whose_stop_shares_its_slug_in_any_case()
    {
        using var dir = Seeded(
            """
            INSERT INTO contacts (public_id, custom_json, created_at, updated_at) VALUES
              ('cnt_same', '{}', '2026-01-01', '2026-01-01'),
              ('cnt_apart', '{}', '2026-01-01', '2026-01-01');
            INSERT INTO contact_channels (contact_id, channel, value, is_primary) VALUES
              (1, 'linkedin', 'https://www.linkedin.com/in/Ola-Sample', 1),
              (2, 'linkedin', 'https://www.linkedin.com/in/Eva-Sample', 1);
            INSERT INTO suppressions (public_id, channel, value, reason, created_at) VALUES
              ('sup_same', 'linkedin', 'https://www.linkedin.com/in/Ola-Sample', NULL, '2026-01-05 09:00:00'),
              ('sup_apart', 'linkedin', 'https://www.linkedin.com/in/eva-sample', NULL, '2026-01-05 09:00:00');
            """);

        using var db = new JasonDbContext(JasonDbContext.CreateOptions(dir.Paths.DatabaseFile));
        var clock = new FixedClock(new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero));
        var register = new SuppressionService(db, new JournalWriter(clock), clock);

        foreach (var stored in await db.ContactChannels.Select(channel => channel.Value).ToListAsync(Ct))
        {
            Assert.True(await register.IsSuppressedAsync(db, "linkedin", stored, Ct), $"'{stored}' is no longer refused.");
        }
    }

    /// <summary>
    /// How a LinkedIn value was keyed around this migration, frozen here because the rule has moved on since: as it is
    /// written in, what the rule before this migration stored, and what the rule this migration rewrote to stored. That
    /// rule lowercased the path on linkedin.com and its subdomains and nowhere else, kept the query as written, and kept a
    /// letter written as a percent-escape in the escape's case.
    /// </summary>
    private static readonly (string Raw, string Before, string Then)[] KeysAroundThisMigration =
    [
        ("HTTPS://WWW.LinkedIn.com/in/Jane/#x", "https://www.linkedin.com/in/Jane", "https://www.linkedin.com/in/jane"),
        ("https://www.linkedin.com/in/Marta-Example", "https://www.linkedin.com/in/Marta-Example", "https://www.linkedin.com/in/marta-example"),
        ("https://uk.linkedin.com/in/Marta-Example/", "https://uk.linkedin.com/in/Marta-Example", "https://uk.linkedin.com/in/marta-example"),
        ("https://linkedin.com/in/Marta-Example", "https://linkedin.com/in/Marta-Example", "https://linkedin.com/in/marta-example"),
        ("https://www.linkedin.com/in/Marta-Example?Trk=Profile", "https://www.linkedin.com/in/Marta-Example?Trk=Profile", "https://www.linkedin.com/in/marta-example?Trk=Profile"),
        ("https://www.linkedin.com:443/in/Marta-Example", "https://www.linkedin.com/in/Marta-Example", "https://www.linkedin.com/in/marta-example"),
        ("https://www.linkedin.com:8443/in/Marta-Example", "https://www.linkedin.com:8443/in/Marta-Example", "https://www.linkedin.com:8443/in/marta-example"),
        ("http://www.linkedin.com/in/Marta-Example", "http://www.linkedin.com/in/Marta-Example", "http://www.linkedin.com/in/marta-example"),
        ("https://marta@www.linkedin.com/in/Marta-Example", "https://www.linkedin.com/in/Marta-Example", "https://www.linkedin.com/in/marta-example"),
        ("https://www.linkedin.com/in/Marta-Example//", "https://www.linkedin.com/in/Marta-Example/", "https://www.linkedin.com/in/marta-example/"),
        ("https://www.linkedin.com/in/JOSÉ-Example", "https://www.linkedin.com/in/JOS%C3%89-Example", "https://www.linkedin.com/in/jos%c3%89-example"),
        ("https://LinkedIn.com/", "https://linkedin.com", "https://linkedin.com"),
        ("https://www.linkedin.com/?Ref=Feed", "https://www.linkedin.com?Ref=Feed", "https://www.linkedin.com?Ref=Feed"),
        ("https://notlinkedin.com/in/Marta-Example", "https://notlinkedin.com/in/Marta-Example", "https://notlinkedin.com/in/Marta-Example"),
        ("https://linkedin.com.example.org/in/Marta-Example", "https://linkedin.com.example.org/in/Marta-Example", "https://linkedin.com.example.org/in/Marta-Example"),
        ("http://x.example.com:8080/A?b=1#z", "http://x.example.com:8080/A?b=1", "http://x.example.com:8080/A?b=1"),
    ];

    /// <summary>
    /// The rewrite was the rule of its day, and not a second one: every value the rule before stored became exactly what
    /// the rule this migration rewrote to stored for the same link as it was written, one contact a row.
    /// </summary>
    [Fact]
    public void Every_key_the_rule_before_stored_becomes_the_key_the_rule_of_this_migration_stored()
    {
        var rows = KeysAroundThisMigration;
        var seed = new System.Text.StringBuilder();
        for (var at = 0; at < rows.Length; at++)
        {
            seed.Append(System.FormattableString.Invariant(
                $"INSERT INTO contacts (public_id, custom_json, created_at, updated_at) VALUES ('cnt_{at}', '{{}}', '2026-01-01', '2026-01-01');\n"));
            seed.Append(System.FormattableString.Invariant(
                $"INSERT INTO contact_channels (contact_id, channel, value, is_primary) VALUES ({at + 1}, 'linkedin', '{rows[at].Before}', 1);\n"));
        }

        using var dir = Seeded(seed.ToString());
        var migrated = Rows(dir, "SELECT contact_id, value FROM contact_channels");

        for (var at = 0; at < rows.Length; at++)
        {
            var (raw, before, then) = rows[at];
            var stored = migrated[System.FormattableString.Invariant($"{at + 1}")];
            Assert.True(stored == then, $"'{before}', stored for '{raw}', became '{stored}', where the rule of this migration stored '{then}'.");
        }
    }

    /// <summary>
    /// The migration runs as the runtime starts, so its cost has to grow with the rows it reads and no faster: a start
    /// that outlasts the CLI's wait is a runtime that did not come up. Each statement that reads the table of keys is
    /// asked for SQLite's own plan of the very text the migration runs. Every read of that table is a search through
    /// one of the table's own two indexes, never a scan and never an index SQLite builds for the one statement; at most
    /// one of them passes over the rows of one source, and never inside a subquery that runs per row; every other read
    /// finds its rows by identifier or by key. A second pass, or a pass per row, costs the square of the rows. And no
    /// step of the plan scans any table, the register's and the contacts' included: a statement that read the keys
    /// well could still scan another table once for each of them.
    /// </summary>
    [Fact]
    public void Every_statement_over_the_keys_reads_them_through_an_index_and_never_once_per_row()
    {
        var statements = LinkedInSlugKeys.Statements;
        Assert.Equal(
            statements,
            new LinkedInSlugKeys().UpOperations.OfType<SqlOperation>().Single().Sql
                .Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));

        using var dir = AtTheMigrationBefore();
        using var connection = Open(dir);
        var problems = new List<string>();
        var read = 0;
        foreach (var statement in statements)
        {
            if (statement.StartsWith("DELETE", StringComparison.Ordinal) || statement.StartsWith("UPDATE", StringComparison.Ordinal))
            {
                read++;
                var plan = PlanOf(connection, statement);
                var names = KeyTableNames().Matches(statement)
                    .Select(match => match.Groups["alias"].Success ? match.Groups["alias"].Value : "linkedin_keys")
                    .Append("linkedin_keys")
                    .ToHashSet(StringComparer.Ordinal);
                var reads = plan.Where(step => Access().Match(step.Detail) is { Success: true } access && names.Contains(access.Groups["name"].Value)).ToList();
                var passes = reads.Where(step => !ByIdentifierOrKey().IsMatch(step.Detail)).ToList();

                var wrong = new List<string>();
                if (plan.Any(step => step.Detail.StartsWith("SCAN ", StringComparison.Ordinal)))
                {
                    wrong.Add("a step of its plan scans a table");
                }

                if (reads.Any(step => !step.Detail.StartsWith("SEARCH ", StringComparison.Ordinal)))
                {
                    wrong.Add("it scans the table of keys");
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

            Execute(connection, statement);
        }

        Assert.Equal(4, read);
        Assert.True(problems.Count == 0, string.Join("\n\n", problems));
    }

    /// <summary>
    /// The table of keys and both of its indexes are made in the connection's temporary schema — the index is named
    /// plain, and SQLite puts it where its table is — and the script's last statement drops the table and its indexes
    /// with it, so the database the runtime keeps holds none of them, and neither does the connection that ran it.
    /// </summary>
    [Fact]
    public void The_table_of_keys_and_its_indexes_are_temporary_and_the_script_leaves_nothing_behind()
    {
        using var dir = AtTheMigrationBefore();
        using var connection = Open(dir);
        foreach (var statement in LinkedInSlugKeys.Statements)
        {
            if (statement.StartsWith("DROP", StringComparison.Ordinal))
            {
                Assert.Equal(
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["linkedin_keys"] = "table",
                        ["linkedin_keys_by_key"] = "index",
                        ["sqlite_autoindex_linkedin_keys_1"] = "index",
                    },
                    Rows(connection, "SELECT name, type FROM sqlite_temp_master WHERE tbl_name = 'linkedin_keys'"));
                Assert.Empty(Rows(connection, "SELECT name, type FROM sqlite_master WHERE name LIKE 'linkedin_keys%' OR tbl_name = 'linkedin_keys'"));
            }

            Execute(connection, statement);
        }

        Assert.Empty(Rows(connection, "SELECT name, type FROM sqlite_temp_master"));
        Assert.Empty(Rows(connection, "SELECT name, type FROM sqlite_master WHERE name LIKE 'linkedin_keys%' OR tbl_name = 'linkedin_keys'"));

        using var migrated = Seeded(string.Empty);
        Assert.Empty(Rows(migrated, "SELECT name, type FROM sqlite_master WHERE name LIKE 'linkedin_keys%' OR tbl_name = 'linkedin_keys'"));
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

    /// <summary>The table of keys as a statement names it: bare, or under the alias that follows it.</summary>
    [GeneratedRegex(@"\blinkedin_keys\b(?:\s+(?!WHERE\b|JOIN\b|ON\b|SET\b|AS\b)(?<alias>[a-z_][a-z0-9_]*))?")]
    private static partial Regex KeyTableNames();

    /// <summary>One step of a plan that reads a table, and the name the statement gives it.</summary>
    [GeneratedRegex(@"^(?:SCAN|SEARCH) (?:TABLE )?(?<name>[a-z_][a-z0-9_]*)\b")]
    private static partial Regex Access();

    /// <summary>A read through the table's primary key or its index by key: never an index SQLite builds for one statement.</summary>
    [GeneratedRegex(@" USING (?:COVERING )?INDEX (?:sqlite_autoindex_linkedin_keys_1|linkedin_keys_by_key) \(")]
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

    /// <summary>
    /// A database at the migration before this one, the rows given written into it, then the migrations up to this one
    /// applied: the later ones re-key what this one wrote, and their own tests hold what they do.
    /// </summary>
    private static TempDataDir Seeded(string sql)
    {
        var dir = AtTheMigrationBefore();
        var options = JasonDbContext.CreateOptions(dir.Paths.DatabaseFile);

        using (var connection = Open(dir))
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }

        using (var db = new JasonDbContext(options))
        {
            db.GetInfrastructure().GetRequiredService<IMigrator>().Migrate(ThisMigration);
        }

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

    private static long Count(TempDataDir dir, string sql)
    {
        using var connection = Open(dir);
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (long)command.ExecuteScalar()!;
    }
}
