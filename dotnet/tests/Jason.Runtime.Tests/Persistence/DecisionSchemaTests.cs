using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests.Persistence;

/// <summary>
/// What the database guarantees about a question a person has to answer: that it is a row of its own with
/// the campaign, the item and the attempt it came from, that its references are identifiers rather than
/// copies, and that nothing outside the runtime can write the three lines a question's life leaves.
/// </summary>
public class DecisionSchemaTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string PreviousMigration = "20260919040652_CampaignManagerLoop";

    private static readonly DateTime Noon = new(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// A decision outlives the attempt that raised it — that is the whole point of it — so it is a row, and
    /// this is that row read back exactly as it was written.
    /// </summary>
    [Fact]
    public async Task A_pending_decision_keeps_the_campaign_the_item_the_attempt_and_its_references()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var (campaign, item, attempt) = await SeedAsync(db);

        db.Decisions.Add(new Decision
        {
            PublicId = "dec_one",
            CampaignId = campaign.Id,
            WorkItemId = item.Id,
            AttemptId = attempt.Id,
            Question = "do we keep calling this account?",
            Options = new JsonArray(new JsonObject { ["label"] = "keep going" }, new JsonObject { ["label"] = "stop" }),
            References = new JsonArray(new JsonObject { ["kind"] = "work_item", ["id"] = item.PublicId }),
            Status = DecisionStatus.Pending,
            RaisedAt = Noon,
        });
        await db.SaveChangesAsync(Ct);

        await using var fresh = database.Open();
        var stored = await fresh.Decisions.AsNoTracking().SingleAsync(Ct);

        Assert.Equal(DecisionStatus.Pending, stored.Status);
        Assert.Equal(campaign.Id, stored.CampaignId);
        Assert.Equal(item.Id, stored.WorkItemId);
        Assert.Equal(attempt.Id, stored.AttemptId);
        Assert.Equal(2, stored.Options!.AsArray().Count);

        // Identifiers, never copies: a fresh session reads what is true now rather than what was true when
        // somebody asked.
        Assert.Equal(item.PublicId, (string?)stored.References!.AsArray()[0]!["id"]);
        Assert.Null(stored.AnsweredAt);
        Assert.Null(stored.AnswerJournalEntryId);
    }

    /// <summary>
    /// The three kinds are the runtime's own, so a caller cannot forge one. A role that could append
    /// <c>decision_answered</c> could summon a manager by naming it, which is the rule the whole trigger
    /// vocabulary rests on.
    /// </summary>
    [Theory]
    [InlineData(JournalKinds.DecisionRaised)]
    [InlineData(JournalKinds.DecisionAnswered)]
    [InlineData(JournalKinds.DecisionCancelled)]
    public async Task Journal_append_refuses_a_decision_kind(string kind)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var (campaign, _, _) = await SeedAsync(db);

        var service = new JournalService(db, new JournalWriter(new FixedClock(Noon)));
        var refused = await Assert.ThrowsAsync<InvalidRequestException>(
            () => service.AppendAsync(new JournalAppendRequest(campaign.PublicId, kind, null, null, null, null), Ct));

        Assert.Equal("reserved_kind", refused.Code);
    }

    /// <summary>
    /// The upgrade adds a table and touches nothing else. A campaign's watermark and anchor are 12a's, and a
    /// migration that moved either would have every installation owing a review it had already had.
    /// </summary>
    [Fact]
    public void An_upgrade_adds_the_table_and_leaves_the_loop_where_it_was()
    {
        using var dir = new TempDataDir();
        Directory.CreateDirectory(dir.Paths.StateDirectory);
        var options = JasonDbContext.CreateOptions(dir.Paths.DatabaseFile);

        using (var db = new JasonDbContext(options))
        {
            db.GetInfrastructure().GetRequiredService<IMigrator>().Migrate(PreviousMigration);
        }

        Execute(
            dir.Paths.DatabaseFile,
            """
            INSERT INTO campaigns (public_id, name, status, context_json, created_at, updated_at, manager_event_watermark, manager_review_anchor)
            VALUES ('cmp_live', 'already running', 'active', '{}', '2026-09-01T00:00:00Z', '2026-09-01T00:00:00Z', 17, '2026-09-01 09:00:00');
            """);

        using (var db = new JasonDbContext(options))
        {
            db.Database.Migrate();
        }

        using (var after = new JasonDbContext(options))
        {
            var campaign = after.Campaigns.Single();
            Assert.Equal(17, campaign.ManagerEventWatermark);
            Assert.Equal(new DateTime(2026, 9, 1, 9, 0, 0, DateTimeKind.Unspecified), campaign.ManagerReviewAnchor);
            Assert.Empty(after.Decisions);
        }
    }

    private static void Execute(string databaseFile, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={databaseFile}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static async Task<(Campaign Campaign, WorkItem Item, Attempt Attempt)> SeedAsync(JasonDbContext db)
    {
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        var item = WorkItemFactory.NewAiRole(campaign, now: Noon);
        item.Status = WorkItemStatus.Processing;
        var attempt = new Attempt
        {
            PublicId = "att_one",
            WorkItem = item,
            Number = 1,
            Status = AttemptStatus.Running,
            StartedAt = Noon,
            LockUntil = Noon.AddMinutes(30),
        };

        db.Campaigns.Add(campaign);
        db.WorkItems.Add(item);
        db.Attempts.Add(attempt);
        await db.SaveChangesAsync(Ct);
        return (campaign, item, attempt);
    }
}
