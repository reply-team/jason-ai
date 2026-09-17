using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Json;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Reports;
using Jason.Runtime.WorkItems;

namespace Jason.Runtime.Tests.Reports;

/// <summary>
/// What a work item's own view says about effects nobody here performed. They are on the item, because that is
/// where somebody looking at the work would go to ask what else happened to this person — and they are in their
/// own list, because an attempt and an assertion are not the same kind of thing and a reader must never have to
/// tell them apart by squinting.
/// </summary>
public class WorkItemExternalReportsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_work_item_shows_externally_reported_effects_apart_from_its_attempts()
    {
        using var database = new TestDatabase();
        await using var seed = database.Open();
        var (campaign, contact, item) = await ReportedWorld.WriteAsync(seed, Ct);
        var attempt = WorkItemFactory.NewAttempt(item, 1, AttemptStatus.Running, ReportedWorld.Noon);
        await seed.SaveChangesAsync(Ct);

        await using var db = database.Open();
        var clock = new FixedClock(ReportedWorld.Noon);
        var reports = new ReportService(db, new JournalWriter(clock), clock);

        var earlier = await reports.SubmitAsync(Submission(campaign, contact, item, "A follow-up went out by hand."), Ct);
        clock.Advance(TimeSpan.FromMinutes(5));
        var later = await reports.SubmitAsync(Submission(campaign, contact, item, "They answered on the phone."), Ct);

        var service = new WorkItemService(db, new JournalWriter(clock), clock, TestCanceller.New(clock), TestOptions.PluginSettings());
        var fetched = await service.GetAsync(new WorkItemGetRequest(item.PublicId, null), Ct);

        // The work this runtime claimed and ran.
        Assert.Equal(attempt.PublicId, Assert.Single(fetched.Attempts!).Id);

        // And, beside it, what somebody says they did elsewhere — newest first.
        Assert.Equal([later.Id, earlier.Id], fetched.ExternalReports!.Select(report => report.Id));
        Assert.All(fetched.ExternalReports!, report => Assert.StartsWith("rpt_", report.Id, StringComparison.Ordinal));
        Assert.All(fetched.ExternalReports!, report => Assert.Equal(item.PublicId, report.Correlation.WorkItemId));
        Assert.All(fetched.ExternalReports!, report => Assert.Equal(new ActorRef(ActorType.Human, "person-1"), report.Reporter));

        // Nothing appears in both lists: an attempt is addressed as an attempt and a report as a report.
        Assert.Empty(fetched.ExternalReports!.Select(report => report.Id).Intersect(fetched.Attempts!.Select(a => a.Id), StringComparer.Ordinal));

        var got = JsonSerializer.Serialize(fetched, JasonJson.Options);
        Assert.Contains("\"external_reports\":[{\"id\":\"rpt_", got, StringComparison.Ordinal);

        // A listing does not grow by twenty assertions per row, so the key is not there at all.
        var listed = await service.ListAsync(
            new WorkItemListRequest(null, null, null, null, null, null, null, null), Ct);
        var page = JsonSerializer.Serialize(listed, JasonJson.Options);
        Assert.Contains(item.PublicId, page, StringComparison.Ordinal);
        Assert.DoesNotContain("external_reports", page, StringComparison.Ordinal);
    }

    /// <summary>An item nobody reported anything about says so, rather than leaving the reader to wonder
    /// whether the question was asked.</summary>
    [Fact]
    public async Task An_item_with_nothing_reported_about_it_answers_with_an_empty_list()
    {
        using var database = new TestDatabase();
        await using var seed = database.Open();
        var (_, _, item) = await ReportedWorld.WriteAsync(seed, Ct);

        await using var db = database.Open();
        var clock = new FixedClock(ReportedWorld.Noon);
        var service = new WorkItemService(db, new JournalWriter(clock), clock, TestCanceller.New(clock), TestOptions.PluginSettings());

        var fetched = await service.GetAsync(new WorkItemGetRequest(item.PublicId, null), Ct);

        Assert.Empty(fetched.ExternalReports!);
        Assert.Contains("\"external_reports\":[]", JsonSerializer.Serialize(fetched, JasonJson.Options), StringComparison.Ordinal);
    }

    /// <summary>Reports about other work stay on that work: correlation is what puts one here, not proximity.</summary>
    [Fact]
    public async Task Only_the_effects_reported_against_this_item_are_on_it()
    {
        using var database = new TestDatabase();
        await using var seed = database.Open();
        var (campaign, contact, item) = await ReportedWorld.WriteAsync(seed, Ct);
        var other = WorkItemFactory.NewProviderOp(campaign, "campaign.enroll", ReportedWorld.Noon);
        seed.WorkItems.Add(other);
        await seed.SaveChangesAsync(Ct);

        await using var db = database.Open();
        var clock = new FixedClock(ReportedWorld.Noon);
        var reports = new ReportService(db, new JournalWriter(clock), clock);

        var mine = await reports.SubmitAsync(Submission(campaign, contact, item, "About this item."), Ct);
        await reports.SubmitAsync(Submission(campaign, contact, other, "About the other item."), Ct);
        await reports.SubmitAsync(ReportedWorld.Submission(), Ct);

        var service = new WorkItemService(db, new JournalWriter(clock), clock, TestCanceller.New(clock), TestOptions.PluginSettings());
        var fetched = await service.GetAsync(new WorkItemGetRequest(item.PublicId, null), Ct);

        Assert.Equal(mine.Id, Assert.Single(fetched.ExternalReports!).Id);
    }

    private static JsonObject Submission(Campaign campaign, Contact contact, WorkItem item, string summary)
    {
        var submission = ReportedWorld.Submission();
        submission["summary"] = summary;
        submission["campaign_id"] = campaign.PublicId;
        submission["contact_id"] = contact.PublicId;
        submission["work_item_id"] = item.PublicId;
        return submission;
    }
}
