using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Json;
using Jason.Runtime.Reports;

namespace Jason.Runtime.Tests.Reports;

/// <summary>
/// Reading admitted reports back. What a read has to prove is that nothing on the way out edits the assertion:
/// the document comes back as it was sent, and the receipt beside it says what it said at admission.
/// </summary>
public class ReportReadTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_report_reads_back_exactly_as_it_was_submitted()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var (campaign, contact, item) = await ReportedWorld.WriteAsync(db, Ct);
        var service = ReportedWorld.Service(db);

        var submitted = ReportedWorld.Submission();
        submitted["provider"] = "some-provider";
        submitted["campaign_id"] = campaign.PublicId;
        submitted["contact_id"] = contact.PublicId;
        submitted["work_item_id"] = item.PublicId;
        submitted["operation"] = "campaign.enroll";
        submitted["external_ids"] = new JsonArray(new JsonObject { ["kind"] = "message", ["value"] = "m-1" });
        submitted["unknown_fields"] = new JsonArray("observed_at");
        submitted["uncertainty"] = "The send was queued; nobody watched it leave.";
        submitted["reason"] = "Reporting it the moment I noticed.";

        var expected = submitted.DeepClone().AsObject();
        expected.Remove("actor");
        expected.Remove("reason");

        var admitted = await service.SubmitAsync(submitted, Ct);
        var read = await service.GetAsync(new ReportGetRequest(admitted.Id), Ct);

        Assert.Equal(admitted.Id, read.Id);
        Assert.Equal(ReportedWorld.Noon, read.ReceivedAt.UtcDateTime);
        Assert.Equal(new ActorRef(ActorType.Human, "person-1"), read.Reporter);
        Assert.Equal("Reporting it the moment I noticed.", read.Reason);

        // The two fields a reporter uses to say what they are unsure of are the ones a careless read would drop,
        // because nothing in the runtime interprets either of them.
        Assert.True(JsonNode.DeepEquals(expected, read.Assertion));
        Assert.Equal("observed_at", read.Assertion["unknown_fields"]!.AsArray()[0]!.GetValue<string>());
        Assert.Equal("The send was queued; nobody watched it leave.", read.Assertion["uncertainty"]!.GetValue<string>());
        Assert.Equal(CanonicalJson.Hash(expected), read.AssertionHash);
        Assert.Equal(admitted.AssertionHash, read.AssertionHash);

        Assert.Equal(campaign.PublicId, read.Correlation.CampaignId);
        Assert.Equal(contact.PublicId, read.Correlation.ContactId);
        Assert.Equal(item.PublicId, read.Correlation.WorkItemId);
        Assert.Equal("campaign.enroll", read.Correlation.Operation);
        Assert.True(read.Correlation.OperationKnown);
        Assert.True(read.Correlation.ContactInCampaign);
        Assert.False(read.Correlation.Verified);

        // A read admitted nothing, so it repeats nothing either.
        Assert.Equal(ReportDedupOutcome.Admitted, read.Dedup.Outcome);
        Assert.Null(read.Dedup.Matched);
    }

    [Fact]
    public async Task Reports_list_newest_last_and_page_by_cursor()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var service = ReportedWorld.Service(db);

        var admitted = new List<string>();
        foreach (var nth in (string[])["first", "second", "third", "fourth", "fifth"])
        {
            var submission = ReportedWorld.Submission();
            submission["summary"] = $"The {nth} thing somebody did outside the runtime.";
            admitted.Add((await service.SubmitAsync(submission, Ct)).Id);
        }

        var seen = new List<string>();
        string? cursor = null;
        var pages = 0;
        do
        {
            var page = await service.ListAsync(new ReportListRequest(null, null, null, null, null, 2, cursor), Ct);
            seen.AddRange(page.Items.Select(report => report.Id));
            cursor = page.NextCursor;
            pages++;
        }
        while (cursor is not null);

        Assert.Equal(admitted, seen);
        Assert.Equal(3, pages);
    }

    [Fact]
    public async Task Reports_can_be_narrowed_to_one_campaign_a_contact_a_work_item_or_an_operation()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var (campaign, contact, item) = await ReportedWorld.WriteAsync(db, Ct);
        var stranger = ReportedWorld.NewContact(db);
        await db.SaveChangesAsync(Ct);
        var service = ReportedWorld.Service(db);

        var everything = ReportedWorld.Submission();
        everything["summary"] = "The managed work, done by hand instead.";
        everything["campaign_id"] = campaign.PublicId;
        everything["contact_id"] = contact.PublicId;
        everything["work_item_id"] = item.PublicId;
        everything["operation"] = "campaign.enroll";

        var campaignOnly = ReportedWorld.Submission();
        campaignOnly["summary"] = "Something about the campaign, about nobody in particular.";
        campaignOnly["campaign_id"] = campaign.PublicId;

        var strangerOnly = ReportedWorld.Submission();
        strangerOnly["summary"] = "Somebody the campaign has never heard of was written to.";
        strangerOnly["contact_id"] = stranger.PublicId;

        var elsewhere = ReportedWorld.Submission();
        elsewhere["summary"] = "An effect that names nothing local at all.";
        elsewhere["operation"] = "carrier_pigeon.dispatch";

        var all = await service.SubmitAsync(everything, Ct);
        var aboutTheCampaign = await service.SubmitAsync(campaignOnly, Ct);
        var aboutTheStranger = await service.SubmitAsync(strangerOnly, Ct);
        var unlocated = await service.SubmitAsync(elsewhere, Ct);

        var inCampaign = await service.ListAsync(new ReportListRequest(campaign.PublicId, null, null, null, null, null, null), Ct);
        var forContact = await service.ListAsync(new ReportListRequest(null, contact.PublicId, null, null, null, null, null), Ct);
        var forStranger = await service.ListAsync(new ReportListRequest(null, stranger.PublicId, null, null, null, null, null), Ct);
        var forItem = await service.ListAsync(new ReportListRequest(null, null, item.PublicId, null, null, null, null), Ct);
        var enrolments = await service.ListAsync(new ReportListRequest(null, null, null, "campaign.enroll", null, null, null), Ct);
        var pigeons = await service.ListAsync(new ReportListRequest(null, null, null, "carrier_pigeon.dispatch", null, null, null), Ct);

        Assert.Equal([all.Id, aboutTheCampaign.Id], inCampaign.Items.Select(report => report.Id));
        Assert.Equal(all.Id, Assert.Single(forContact.Items).Id);
        Assert.Equal(aboutTheStranger.Id, Assert.Single(forStranger.Items).Id);
        Assert.Equal(all.Id, Assert.Single(forItem.Items).Id);
        Assert.Equal(all.Id, Assert.Single(enrolments.Items).Id);

        // An operation nobody modelled still narrows a listing: the filter asks what the reporter said, not what
        // the catalog knows.
        Assert.Equal(unlocated.Id, Assert.Single(pigeons.Items).Id);
        Assert.False(Assert.Single(pigeons.Items).Correlation.OperationKnown);
    }

    [Fact]
    public async Task An_unknown_report_id_is_a_not_found_answer()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var service = ReportedWorld.Service(db);

        var missing = await Assert.ThrowsAsync<NotFoundException>(
            () => service.GetAsync(new ReportGetRequest("rpt_01JASONNOTHERE"), Ct));

        Assert.Equal("report_not_found", missing.Code);
    }

    /// <summary>An id nobody sent is the caller's mistake, not a report that happens not to exist.</summary>
    [Fact]
    public async Task A_read_without_a_report_id_is_the_callers_mistake()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var service = ReportedWorld.Service(db);

        var error = await Assert.ThrowsAsync<ValidationException>(() => service.GetAsync(new ReportGetRequest("  "), Ct));

        var detail = Assert.Single(error.Details!);
        Assert.Equal("report_id", detail.Field);
        Assert.Equal("required", detail.Code);
    }

    /// <summary>
    /// The moment a caller names is inside the window. Anything else loses whatever landed exactly on the
    /// boundary to a walk of successive windows.
    /// </summary>
    [Fact]
    public async Task A_listing_since_a_moment_keeps_the_moment_it_names()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var clock = new FixedClock(ReportedWorld.Noon);
        var service = new ReportService(db, new JournalWriter(clock), clock);

        var early = ReportedWorld.Submission();
        early["summary"] = "Done before the window opens.";
        var earlier = await service.SubmitAsync(early, Ct);

        clock.Advance(TimeSpan.FromHours(1));
        var late = ReportedWorld.Submission();
        late["summary"] = "Done exactly as the window opens.";
        var onTheBoundary = await service.SubmitAsync(late, Ct);

        var window = await service.ListAsync(
            new ReportListRequest(null, null, null, null, ReportedWorld.Noon.AddHours(1), null, null), Ct);
        var everything = await service.ListAsync(new ReportListRequest(null, null, null, null, null, null, null), Ct);

        Assert.Equal(onTheBoundary.Id, Assert.Single(window.Items).Id);
        Assert.Equal([earlier.Id, onTheBoundary.Id], everything.Items.Select(report => report.Id));
    }

    /// <summary>
    /// A filter that names nothing real is answered by the loader that owns the id, not by an empty page: a
    /// caller who mistyped a campaign would otherwise read "no reports" as a fact about the world.
    /// </summary>
    [Fact]
    public async Task A_filter_naming_something_that_does_not_exist_is_that_loaders_own_refusal()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var service = ReportedWorld.Service(db);

        var campaign = await Assert.ThrowsAsync<NotFoundException>(
            () => service.ListAsync(new ReportListRequest("cmp_01JASONNOTHERE", null, null, null, null, null, null), Ct));
        var contact = await Assert.ThrowsAsync<NotFoundException>(
            () => service.ListAsync(new ReportListRequest(null, "cnt_01JASONNOTHERE", null, null, null, null, null), Ct));
        var item = await Assert.ThrowsAsync<NotFoundException>(
            () => service.ListAsync(new ReportListRequest(null, null, "wi_01JASONNOTHERE", null, null, null, null), Ct));

        Assert.Equal("campaign_not_found", campaign.Code);
        Assert.Equal("contact_not_found", contact.Code);
        Assert.Equal("work_item_not_found", item.Code);
    }

    /// <summary>A listing stays small on purpose: the assertion and whatever evidence came with it are worth
    /// carrying only once somebody asks for the one report they are about.</summary>
    [Fact]
    public async Task A_listing_carries_the_shape_of_a_report_and_not_its_contents()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var service = ReportedWorld.Service(db);

        var submitted = ReportedWorld.Submission();
        submitted["provider"] = "some-provider";
        submitted["evidence"] = new JsonObject { ["subject"] = "Following up" };
        var admitted = await service.SubmitAsync(submitted, Ct);

        var listed = await service.ListAsync(new ReportListRequest(null, null, null, null, null, null, null), Ct);
        var summary = Assert.Single(listed.Items);

        Assert.Equal(admitted.Id, summary.Id);
        Assert.Equal(ReportedWorld.Noon, summary.ReceivedAt.UtcDateTime);
        Assert.Equal(new ActorRef(ActorType.Human, "person-1"), summary.Reporter);
        Assert.Equal("email_sent", summary.Effect);
        Assert.Equal("some-other-cli 1.2.3", summary.Tool);
        Assert.Equal("some-provider", summary.Provider);
        Assert.Equal("A follow-up was sent by hand while the runtime was not involved.", summary.Summary);
        Assert.False(summary.Correlation.Verified);
    }
}
