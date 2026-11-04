using System.Data.Common;
using Jason.Contracts.Api;
using Jason.Runtime.Campaigns;
using Jason.Runtime.Journal;
using Jason.Runtime.Notes;
using Jason.Runtime.Persistence;
using Jason.Runtime.Projects;
using Jason.Runtime.Tests.Harness;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Projects;

/// <summary>
/// The reads projects add, and the one they changed the index of, held by the plans SQLite makes for the very commands
/// the services send — recorded as they are sent, with their parameters, and planned on the same database. A plan is
/// pinned here rather than a time: a read that walks a table is wrong however fast the table is today.
/// </summary>
public class ProjectQueryPlanTests
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// A campaign created without a project looks the default up through the index that holds there is one, which SQLite
    /// reads only for a query whose condition implies the index's own.
    /// </summary>
    [Fact]
    public async Task The_default_project_is_found_through_the_index_that_holds_there_is_one()
    {
        using var database = new TestDatabase();
        var recorder = new Recorder();
        using (var db = Open(database, recorder))
        {
            await Campaigns(db).CreateAsync(new CampaignCreateRequest("Autumn outreach", null, null, null), Ct);
        }

        var lookup = Assert.Single(recorder.Commands, command => command.Text.Contains(ProjectService.DefaultQuery, StringComparison.Ordinal));
        var plan = PlanOf(database, lookup);

        Assert.True(
            plan.SequenceEqual(["SEARCH projects USING INDEX ix_projects_one_default (is_default=?)"]),
            $"The default is not read through its index alone:{Environment.NewLine}{string.Join(Environment.NewLine, plan)}{Environment.NewLine}{lookup.Text}");
    }

    /// <summary>
    /// A project's campaigns are read through the index by project and id: the project's rows only, already in the order a
    /// page is cut in, from the cursor on — never the whole table, and never sorted afterwards. The page is cut first, in a
    /// subquery, and each of its campaigns' project read by its key after, so the one pass the plan makes is over the page.
    /// </summary>
    [Fact]
    public async Task A_project_s_campaigns_are_listed_through_the_index_by_project()
    {
        using var database = new TestDatabase();
        var recorder = new Recorder();
        using (var db = Open(database, recorder))
        {
            var acme = await new ProjectService(db, new JournalWriter(new FixedClock(Noon)), new FixedClock(Noon), TestHarness.FoldersFor(db))
                .CreateAsync(new ProjectCreateRequest("Acme", null, null, null), Ct);
            var service = Campaigns(db);
            for (var n = 0; n < 3; n++)
            {
                await service.CreateAsync(new CampaignCreateRequest($"Campaign {n}", null, null, null, ProjectId: acme.Id), Ct);
            }

            recorder.Commands.Clear();
            var first = await service.ListAsync(new CampaignListRequest(null, 2, null, ProjectId: acme.Id), Ct);
            await service.ListAsync(new CampaignListRequest(null, 2, first.NextCursor, ProjectId: acme.Id), Ct);
        }

        var pages = recorder.Commands.Where(command => command.Text.Contains("FROM \"campaigns\"", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, pages.Count);
        foreach (var (page, search) in pages.Zip(["(project_id=?)", "(project_id=? AND public_id>?)"]))
        {
            var plan = PlanOf(database, page);
            var described = $"{string.Join(Environment.NewLine, plan)}{Environment.NewLine}{page.Text}";
            Assert.True(
                plan.Count(step => step.StartsWith("SEARCH c USING INDEX ix_campaigns_project " + search, StringComparison.Ordinal)) == 1,
                $"The page is not read through the index by project, {search}:{Environment.NewLine}{described}");
            var subqueries = plan.Where(step => step.StartsWith("CO-ROUTINE ", StringComparison.Ordinal)).Select(step => step["CO-ROUTINE ".Length..]).ToHashSet(StringComparer.Ordinal);
            Assert.True(
                !plan.Any(step => (step.StartsWith("SCAN ", StringComparison.Ordinal) && !subqueries.Contains(step["SCAN ".Length..]))
                    || step.Contains("TEMP B-TREE", StringComparison.Ordinal)),
                $"The page walks a table or sorts what it read:{Environment.NewLine}{described}");
            Assert.True(
                plan.Count(step => step.StartsWith("SEARCH p USING INTEGER PRIMARY KEY", StringComparison.Ordinal)) == 1,
                $"Each campaign's project is not read by its key:{Environment.NewLine}{described}");
        }
    }

    /// <summary>
    /// A campaign's note is still found through the index of one per campaign and role, which a note at the client level
    /// made partial: a condition on the campaign implies that it is not null.
    /// </summary>
    [Fact]
    public async Task A_campaign_s_note_is_still_found_through_its_campaign_and_role()
    {
        using var database = new TestDatabase();
        var recorder = new Recorder();
        using (var db = Open(database, recorder))
        {
            var campaign = await Campaigns(db).CreateAsync(new CampaignCreateRequest("Autumn outreach", null, null, null), Ct);
            recorder.Commands.Clear();
            await new RoleNoteService(db, new JournalWriter(new FixedClock(Noon)), new FixedClock(Noon))
                .GetAsync(new RoleNoteGetRequest(campaign.Id, "researcher"), Ct);
        }

        var read = Assert.Single(recorder.Commands, command => command.Text.Contains("FROM \"role_notes\"", StringComparison.Ordinal));
        var plan = PlanOf(database, read);

        Assert.True(
            plan.SequenceEqual(["SEARCH r USING INDEX ix_role_notes_one_per_campaign_role (campaign_id=? AND role=?)"]),
            $"The note is not read through its campaign and role:{Environment.NewLine}{string.Join(Environment.NewLine, plan)}{Environment.NewLine}{read.Text}");
    }

    /// <summary>
    /// A note at the client's and at a project's level is read, and each level listed, through that level's own index of
    /// one per role — never a walk of every note, and a listing never sorted after it is read: the condition each read
    /// sends is the index's filter, or implies it, and the index already holds the roles in a page's order.
    /// </summary>
    [Fact]
    public async Task A_client_s_and_a_project_s_notes_are_read_and_listed_through_their_own_indexes()
    {
        using var database = new TestDatabase();
        var recorder = new Recorder();
        using (var db = Open(database, recorder))
        {
            var acme = await new ProjectService(db, new JournalWriter(new FixedClock(Noon)), new FixedClock(Noon), TestHarness.FoldersFor(db))
                .CreateAsync(new ProjectCreateRequest("Acme", null, null, null), Ct);
            var notes = new RoleNoteService(db, new JournalWriter(new FixedClock(Noon)), new FixedClock(Noon));
            recorder.Commands.Clear();
            await notes.GetAsync(new RoleNoteGetRequest(null, "researcher"), Ct);
            await notes.GetAsync(new RoleNoteGetRequest(null, "researcher", acme.Id), Ct);
            await notes.ListAsync(new RoleNoteListRequest(null, 2, null), Ct);
            await notes.ListAsync(new RoleNoteListRequest(null, 2, "cmVzZWFyY2hlcg", acme.Id), Ct);
        }

        var reads = recorder.Commands.Where(command => command.Text.Contains("FROM \"role_notes\"", StringComparison.Ordinal)).ToList();
        string[] expected =
        [
            "SEARCH r USING INDEX ix_role_notes_one_per_client_role (role=?)",
            "SEARCH r USING INDEX ix_role_notes_one_per_project_role (project_id=? AND role=?)",
            "SCAN r USING INDEX ix_role_notes_one_per_client_role",
            "SEARCH r USING INDEX ix_role_notes_one_per_project_role (project_id=? AND role>?)",
        ];
        Assert.Equal(expected.Length, reads.Count);
        foreach (var (read, step) in reads.Zip(expected))
        {
            var plan = PlanOf(database, read);
            Assert.True(
                plan.SequenceEqual([step]),
                $"The note's read is not '{step}':{Environment.NewLine}{string.Join(Environment.NewLine, plan)}{Environment.NewLine}{read.Text}");
        }
    }

    private sealed record Command(string Text, IReadOnlyList<(string Name, object Value)> Parameters);

    /// <summary>Every query a context sends, with its parameters, as it is sent.</summary>
    private sealed class Recorder : DbCommandInterceptor
    {
        public List<Command> Commands { get; } = [];

        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            Record(command);
            return base.ReaderExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Record(command);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        private void Record(DbCommand command) =>
            Commands.Add(new Command(command.CommandText, [.. command.Parameters.Cast<DbParameter>().Select(parameter => (parameter.ParameterName, parameter.Value ?? DBNull.Value))]));
    }

    private static JasonDbContext Open(TestDatabase database, Recorder recorder)
    {
        var builder = new DbContextOptionsBuilder<JasonDbContext>();
        JasonDbContext.Configure(builder, database.File, JournalLines.Strict).AddInterceptors(recorder);
        return new JasonDbContext(builder.Options);
    }

    /// <summary>What SQLite plans for a recorded command, bound to the values it was sent with; one step a line.</summary>
    private static List<string> PlanOf(TestDatabase database, Command command)
    {
        using var connection = new SqliteConnection(JasonDbContext.ConnectionString(database.File));
        connection.Open();
        using var explain = connection.CreateCommand();
        explain.CommandText = "EXPLAIN QUERY PLAN " + command.Text;
        foreach (var (name, value) in command.Parameters)
        {
            explain.Parameters.AddWithValue(name, value);
        }

        using var reader = explain.ExecuteReader();
        var plan = new List<string>();
        while (reader.Read())
        {
            plan.Add(reader.GetString(3));
        }

        return plan;
    }

    private static CampaignService Campaigns(JasonDbContext db)
    {
        var clock = new FixedClock(Noon);
        return new CampaignService(db, new JournalWriter(clock), clock, TestCanceller.New(clock), TestHarness.FoldersFor(db));
    }
}
