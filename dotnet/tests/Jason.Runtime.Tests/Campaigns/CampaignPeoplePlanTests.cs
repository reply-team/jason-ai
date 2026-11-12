using System.Data.Common;
using System.Text;
using System.Text.RegularExpressions;
using Jason.Contracts.Api;
using Jason.Runtime.Campaigns;
using Jason.Runtime.Contacts;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Campaigns;

/// <summary>
/// Every count and list of the Campaigns screen reads one campaign's rows through an index: SQLite's plan for each never
/// scans a table of the installation, never reads every journal line of one kind, and never finds approvals or questions
/// by their status alone — each of those grows with the installation rather than with the campaigns asked.
/// </summary>
public partial class CampaignPeoplePlanTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task One_campaign_is_counted_and_listed_without_reading_the_whole_installation()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        await db.Database.OpenConnectionAsync(Ct);
        var scans = new StringBuilder();

        string[] why = [CampaignWhy.FirstReplies, CampaignWhy.FinishedWork, CampaignWhy.Additions, CampaignWhy.Removals, CampaignWhy.OptOuts];
        foreach (var sql in CampaignPeople.Everyone.All.Concat(CampaignPeople.OneAudience.All).Append(CampaignPeople.UntouchedCountSql).Append(CampaignPeople.StandingSql).Concat(why))
        {
            // {0} is the campaigns asked (or the one campaign), {1} and {2} the period, or {1} the page's people and {2}
            // and {3} the period; one audience's reads take the audience last, as {1} or {3}. Finished work read by status
            // and finish alone is every campaign's, the whole installation's.
            var one = sql.Replace("{0}", "'[1]'", StringComparison.Ordinal)
                .Replace("IN (SELECT value FROM json_each({1}))", "IN (SELECT value FROM json_each('[1]'))", StringComparison.Ordinal)
                .Replace("{1}", "'2026-10-09 00:00:00'", StringComparison.Ordinal)
                .Replace("{2}", "'2026-10-16 09:00:00'", StringComparison.Ordinal)
                .Replace("{3}", "'2026-10-16 09:00:00'", StringComparison.Ordinal);
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = one;
            await PlanAsync(command, scans);
        }

        foreach (var page in ComposedPages(db))
        {
            await using var command = page.CreateDbCommand();
            await PlanAsync(command, scans);
        }

        // The people a Summary count counts, as contact.list reads them in the order of activity: the count's people first,
        // each then found by their key and their newest line through their own index.
        int[] campaign = [1];
        var counted = ContactService.Counted(db, db.Contacts.Where(contact => contact.ArchivedAt == null), campaign, CampaignSummaryFilter.Waiting);
        await using (var command = ContactActivity.Ordered(ContactActivity.Rows(counted, db)).Take(201).CreateDbCommand())
        {
            await PlanAsync(command, scans);
        }

        Assert.True(scans.Length == 0, scans.ToString());
    }

    /// <summary>
    /// One audience's people are found off the landing rows' index on the audience: every read over one audience's base set
    /// searches the landing rows by that index — once, or once in each arm of a union SQLite carries the join into — and reads
    /// them no other way, so the filter costs what the audience landed rather than what the installation holds.
    /// </summary>
    [Fact]
    public async Task One_audience_s_people_are_found_through_the_landing_rows_index_on_the_audience()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        await db.Database.OpenConnectionAsync(Ct);

        foreach (var sql in CampaignPeople.OneAudience.All)
        {
            var bound = sql.Replace("audience_id = {1}", "audience_id = 7", StringComparison.Ordinal)
                .Replace("audience_id = {3}", "audience_id = 7", StringComparison.Ordinal)
                .Replace("{0}", "'[1]'", StringComparison.Ordinal)
                .Replace("{1}", "'2026-10-09 00:00:00'", StringComparison.Ordinal)
                .Replace("{2}", "'2026-10-16 09:00:00'", StringComparison.Ordinal);
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = "EXPLAIN QUERY PLAN " + bound;
            var landings = new List<string>();
            await using (var reader = await command.ExecuteReaderAsync(Ct))
            {
                while (await reader.ReadAsync(Ct))
                {
                    var step = reader.GetString(reader.GetOrdinal("detail"));
                    if (step.Contains("audience_landings", StringComparison.Ordinal))
                    {
                        landings.Add(step);
                    }
                }
            }

            Assert.True(
                landings.Count > 0 && landings.All(step => step == "SEARCH audience_landings USING INDEX ix_audience_landings_audience_id (audience_id=?)"),
                $"The landing rows are read otherwise than by the audience's index:{Environment.NewLine}{string.Join(Environment.NewLine, landings)}{Environment.NewLine}{bound}");
        }

        // The pages EF composes on one audience's memberships name the landing rows by an alias of its own; the walk above
        // holds them to no scan, and here each finds them by the audience's index.
        foreach (var page in ComposedPages(db).Where(page => page.ToQueryString().Contains("audience_landings", StringComparison.Ordinal)))
        {
            await using var command = page.CreateDbCommand();
            command.CommandText = "EXPLAIN QUERY PLAN " + command.CommandText;
            var steps = new List<string>();
            await using (var reader = await command.ExecuteReaderAsync(Ct))
            {
                while (await reader.ReadAsync(Ct))
                {
                    steps.Add(reader.GetString(reader.GetOrdinal("detail")));
                }
            }

            Assert.True(
                steps.Any(step => step.EndsWith("USING INDEX ix_audience_landings_audience_id (audience_id=?)", StringComparison.Ordinal)),
                $"A page of one audience's people does not find them by the audience's index:{Environment.NewLine}{string.Join(Environment.NewLine, steps)}");
        }
    }

    /// <summary>
    /// The pages of people EF composes from a filter's memberships, so no SQL string shows them: by id, the plain list past a
    /// cursor, a summary count's, a search and one audience's, and the memberships of one audience's page; by moment, an
    /// activity count's searched past a cursor, a plain one and one audience's.
    /// </summary>
    private static IEnumerable<IQueryable> ComposedPages(Jason.Runtime.Persistence.JasonDbContext db)
    {
        int[] campaign = [1];
        var members = db.CampaignContacts.Where(membership => campaign.Contains(membership.CampaignId));
        var since = new DateTime(2026, 10, 9, 0, 0, 0, DateTimeKind.Utc);
        var until = new DateTime(2026, 10, 16, 9, 0, 0, DateTimeKind.Utc);
        return
        [
            MembershipService.PageById(db, members, null, "cnt_01", 51),
            MembershipService.PageById(db, CampaignPeople.Matching(db, campaign, CampaignSummaryFilter.New), null, null, 51),
            MembershipService.PageById(db, members, "ada", null, 51),
            MembershipService.PageById(db, CampaignPeople.LandedUnder(db, members, 7), null, null, 51),
            MembershipService.Listed(CampaignPeople.LandedUnder(db, members, 7), [1, 2]),
            MembershipService.PageByMoment(db, [.. campaign], CampaignActivityFilter.People, since, until, "ada", (1_792_000_000_000, "cnt_01"), 51),
            MembershipService.PageByMoment(db, [.. campaign], CampaignActivityFilter.Replied, since, until, null, null, 51),
            MembershipService.PageByMoment(db, [.. campaign], CampaignActivityFilter.Replied, since, until, null, null, 51, audience: 7),
        ];
    }

    /// <summary>
    /// Where a page's people stand is read part by part through these indexes and no others: memberships, work items and
    /// suppressions by person, approvals and questions by their campaign, and reply and event lines by their campaign and
    /// kind, each then kept to the page's people. A part that came to read by another index, or a table read whole, changes
    /// this list, and with it what the read grows with.
    /// </summary>
    [Fact]
    public async Task The_standing_read_finds_each_part_s_rows_by_the_index_it_names()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        await db.Database.OpenConnectionAsync(Ct);
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = CampaignPeople.StandingSql
            .Replace("{0}", "'[1,2]'", StringComparison.Ordinal)
            .Replace("{1}", "'[1,2]'", StringComparison.Ordinal);

        var read = (await StepsAsync(command))
            .Select(step => Read().Match(step))
            .Where(match => match.Success)
            .Select(match => $"{match.Groups[1].Value} {match.Groups.Values.Skip(2).FirstOrDefault(group => group.Success)?.Value ?? "whole"}")
            .Distinct()
            .Order(StringComparer.Ordinal);

        Assert.Equal(
            [
                "a ix_approvals_campaign_id",
                "cc ix_campaign_contacts_contact_id",
                "ch ix_contact_channels_contact_id_channel_value",
                "d ix_decisions_campaign_id",
                "jl ix_journal_campaign_kind_key",
                "jl ix_journal_campaign_kind_key_events",
                "m ix_campaign_contacts_campaign_id_contact_id",
                "s ix_suppressions_channel_value",
                "w INTEGER PRIMARY KEY",
                "w ix_work_items_campaign_id_contact_id",
                "w ix_work_items_public_id",
            ],
            read);
    }

    private static async Task<List<string>> StepsAsync(DbCommand command)
    {
        command.CommandText = "EXPLAIN QUERY PLAN " + command.CommandText;
        var steps = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(Ct);
        while (await reader.ReadAsync(Ct))
        {
            steps.Add(reader.GetString(reader.GetOrdinal("detail")));
        }

        return steps;
    }

    /// <summary>
    /// The steps of a command's plan that read the installation rather than the campaigns asked. A subquery the plan builds
    /// is read whole and holds what its own searches found, and so is a list of ids the request sent; any other scan reads
    /// a table or an index whole.
    /// </summary>
    private static async Task PlanAsync(DbCommand command, StringBuilder scans)
    {
        var steps = await StepsAsync(command);
        var built = steps.Select(step => Built().Match(step)).Where(match => match.Success).Select(match => match.Groups[1].Value).ToHashSet();
        foreach (var step in steps)
        {
            // A table read through an index under a name the plan also gives a subquery: a scan of that table would pass
            // as the subquery's.
            if (Indexed().Match(step) is { Success: true } indexed && built.Contains(indexed.Groups[1].Value))
            {
                scans.AppendLine($"{step} — '{indexed.Groups[1].Value}' names a table and a subquery");
            }

            var whole = Scan().Match(step) is { Success: true } scan && !built.Contains(scan.Groups[1].Value) && !step.Contains("VIRTUAL TABLE", StringComparison.Ordinal);
            if (whole || Refused().IsMatch(step))
            {
                scans.AppendLine(step);
            }
        }
    }

    /// <summary>
    /// A table step and what it reads by: one of the table's own indexes or its key, or nothing, the table whole. A
    /// subquery searched through an automatic index and a list of ids are not tables of the installation.
    /// </summary>
    [GeneratedRegex("^(?:SEARCH|SCAN) (\\S+)(?: (?:EXISTS )?USING (?:COVERING INDEX (\\S+)|INDEX (\\S+)|(INTEGER PRIMARY KEY))|$)")]
    private static partial Regex Read();

    [GeneratedRegex("^(?:CO-ROUTINE|MATERIALIZE) (\\S+)")]
    private static partial Regex Built();

    [GeneratedRegex("^SCAN (\\S+)")]
    private static partial Regex Scan();

    /// <summary>A table read through one of its own indexes; SQLite searches a subquery it built through an automatic one.</summary>
    [GeneratedRegex("^(?:SEARCH|SCAN) (\\S+) (?:EXISTS )?USING (?:COVERING INDEX|INDEX|INTEGER PRIMARY KEY)")]
    private static partial Regex Indexed();

    /// <summary>Every journal line of one kind, approvals or questions found by their status alone, finished work by status and finish.</summary>
    [GeneratedRegex("USING (COVERING )?INDEX ix_journal_kind \\(kind=\\?\\)|USING (COVERING )?INDEX ix_(approvals|decisions)_status|USING (COVERING )?INDEX ix_work_items_status_finished_at")]
    private static partial Regex Refused();
}
