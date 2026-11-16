using Jason.Contracts.Api;
using Jason.Runtime.Execution;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests.Api;

public partial class CardExchangesTests
{
    /// <summary>
    /// What the agent page's archive reads and sends, as one story: a campaign linked to the provider's, holding a send
    /// approved and gone, a role at work with a question raised, one send waiting on a person and one approved and not yet
    /// run, and a search's stop in the queue; a draft beside it. The confirmation's reads are recorded before the archive,
    /// then the archive with a reason, the same archive sent again as a second window would send it, and what the page
    /// reads after. The test holds what
    /// the archive cancelled: every open item, the role's running one among them, the approval still asked and the one
    /// already given, and the question; the approval of the send that went, and the search's stop, are left as they were.
    /// </summary>
    [Fact]
    public async Task An_agent_is_archived_after_its_confirmation_reads_what_the_archive_cancels()
    {
        var role = new HoldingRole();
        var clock = new FixedClock(Nine);
        await using var api = await RuntimeApiFixture.StartAsync(
            Ct,
            prepare: paths => File.WriteAllText(paths.UserSettingsFile, RoleSettings),
            clock: clock,
            configureServices: services =>
            {
                services.AddSingleton<ICommand>(role);
                services.AddSingleton<ICommand>(Provider());
            });
        api.ScrubCursors = true;
        var spring = await Scene.OpenAsync(api, "Spring launch", more: ["campaign.pause"]);
        var autumn = await spring.AnotherAsync("Autumn outreach");
        var ines = await spring.PersonAsync("Ines", "Duarte", "email", "ines@example.test");
        var omar = await spring.PersonAsync("Omar", "Reyes", "email", "omar@example.test");
        var rafa = await spring.PersonAsync("Rafa", "Costa", "email", "rafa@example.test");
        var tomas = await spring.PersonAsync("Tomas", "Berg", "email", "tomas@example.test");
        await autumn.PersonAsync("Kai", "Lindt", "email", "kai@example.test");

        // Linked at the provider, and a send to Rafa approved and gone: its approval stays approved and is no open work.
        await spring.StartAsync();
        var made = await api.PostOkAsync<WorkItemDto>(Operations.WorkItemCreate, new { campaign_id = spring.Campaign, kind = "provider_op", operation = "campaign.create", context = new { input = new { name = "Spring launch" } }, actor = Ada }, Ct);
        Assert.Equal(made.Id, Assert.Single(await spring.ClaimAndRunAsync()).WorkItemPublicId);
        var gone = await spring.SendAsync(rafa, "message.send", EmailArgs("Hi Rafa, a note after the meetup."));
        await spring.ClaimAsync();
        var goneApproval = Assert.Single((await api.PostOkAsync<Page<ApprovalSummaryDto>>(Operations.ApprovalList, new { status = "pending", campaign_id = spring.Campaign, limit = 50 }, Ct)).Items);
        await api.PostOkAsync<ApprovalDto>(Operations.ApprovalApprove, new { approval_id = goneApproval.Id, actor = Ada }, Ct);
        Assert.Equal(gone, Assert.Single(await spring.ClaimAndRunAsync()).WorkItemPublicId);

        // Ines's send and Omar's wait on a person, one parked by each scan; then a role at work on Tomas, which a running
        // attempt keeps from any further claim of the campaign, asks a question; and Omar's send is approved and not yet run.
        var asked = await spring.SendAsync(ines, "message.send", EmailArgs("Hi Ines, thanks for the talk at the meetup."));
        await spring.ClaimAsync();
        var given = await spring.SendAsync(omar, "message.send", EmailArgs("Hi Omar, a note after the meetup."));
        await spring.ClaimAsync();
        var work = await spring.RunRoleAsync(role, tomas);
        try
        {
            var question = await api.PostOkAsync<DecisionDto>(
                Operations.DecisionRaise,
                new { work_item_id = work.WorkItemPublicId, attempt_id = work.AttemptPublicId, question = "Should Tomas hear about the autumn pricing first?" },
                Ct);
            var toApprove = Assert.Single(
                (await api.PostOkAsync<Page<ApprovalSummaryDto>>(Operations.ApprovalList, new { status = "pending", campaign_id = spring.Campaign, limit = 50 }, Ct)).Items,
                a => a.WorkItemId == given);
            await api.PostOkAsync<ApprovalDto>(Operations.ApprovalApprove, new { approval_id = toApprove.Id, actor = Ada }, Ct);

            // A search's stop, put in the queue while the role holds the campaign's claim: the one work an archive leaves.
            var stop = (await api.PostOkAsync<WorkItemDto>(Operations.WorkItemCreate, new
            {
                campaign_id = spring.Campaign,
                kind = "provider_op",
                operation = "audience.search_cancel",
                context = new { input = new { search = gone } },
                actor = Ada,
            }, Ct)).Id;
            clock.Advance(TimeSpan.FromMinutes(5));

            // The page as it opens: the list, its counts, and the agent's own record and numbers.
            Assert.Equal(2, (await api.RecordAsync<Page<CampaignSummaryDto>>(Operations.CampaignList, "archive-before", new { limit = 100 }, Ct)).Items.Count);
            await api.RecordAsync<CampaignSummaryResponse>(Operations.CampaignSummary, "archive-before", new { campaign_ids = new[] { spring.Campaign, autumn.Campaign }, exclude_archived_contacts = true }, Ct);
            await api.RecordAsync<CampaignSummaryResponse>(Operations.CampaignSummary, "archive-spring", new { campaign_ids = new[] { spring.Campaign }, exclude_archived_contacts = true }, Ct);
            var record = await api.RecordAsync<CampaignDto>(Operations.CampaignGet, "archive-spring", new { campaign_id = spring.Campaign }, Ct);
            Assert.Equal((CampaignStatus.Active, "c_7714"), (record.Status, Assert.Single(record.ExternalIds, p => p.Kind == "campaign").Value));

            // What the confirmation reads as it opens: the open work, the approvals asked and given, the questions pending,
            // and whether the provider's campaign can be paused by Jason first.
            var open = await api.RecordAsync<Page<WorkItemSummaryDto>>(Operations.WorkItemList, "archive-open", new { campaign_id = spring.Campaign, status = new[] { "created", "awaiting_approval", "scheduled", "processing", "waiting" }, order = "created", limit = 200 }, Ct);
            Assert.Equal([asked, given, work.WorkItemPublicId, stop], open.Items.Select(i => i.Id));
            var approvals = await api.RecordAsync<Page<ApprovalSummaryDto>>(Operations.ApprovalList, "archive-asked-and-given", new { campaign_id = spring.Campaign, statuses = new[] { "pending", "approved" }, limit = 200 }, Ct);
            Assert.Equal(
                [(gone, ApprovalStatus.Approved), (asked, ApprovalStatus.Pending), (given, ApprovalStatus.Approved)],
                approvals.Items.Select(a => (a.WorkItemId, a.Status)).OrderBy(a => a.WorkItemId == gone ? 0 : a.WorkItemId == asked ? 1 : 2));
            var questions = await api.RecordAsync<Page<DecisionSummaryDto>>(Operations.DecisionList, "archive-pending", new { campaign_id = spring.Campaign, status = "pending", limit = 200 }, Ct);
            Assert.Equal(question.Id, Assert.Single(questions.Items).Id);
            var pause = await api.RecordAsync<RouteResolutionDto>(Operations.RouteResolve, "archive-pause", new { campaign_id = spring.Campaign, operation = "campaign.pause" }, Ct);
            Assert.True(pause.Usable);

            // The archive, with the person's reason; then the same archive again, as a second window sends it: answered with
            // the campaign as it is, and nothing written twice.
            var archived = await api.RecordAsync<CampaignDto>(Operations.CampaignArchive, "archive-spring", new { campaign_id = spring.Campaign, reason = "The spring offer has ended.", actor = Ada }, Ct);
            Assert.Equal(CampaignStatus.Archived, archived.Status);
            Assert.Equal(archived.ArchivedAt, (await api.RecordAsync<CampaignDto>(Operations.CampaignArchive, "archive-spring-again", new { campaign_id = spring.Campaign, reason = "The spring offer has ended.", actor = Ada }, Ct)).ArchivedAt);

            // What the archive cancelled: every open item, the role's among them, both approvals on open work, and the question.
            foreach (var item in new[] { work.WorkItemPublicId, asked, given })
            {
                Assert.Equal(WorkItemStatus.Cancelled, (await api.PostOkAsync<WorkItemDto>(Operations.WorkItemGet, new { work_item_id = item }, Ct)).Status);
            }

            Assert.Equal(WorkItemStatus.Created, (await api.PostOkAsync<WorkItemDto>(Operations.WorkItemGet, new { work_item_id = stop }, Ct)).Status);

            var after = (await api.PostOkAsync<Page<ApprovalSummaryDto>>(Operations.ApprovalList, new { campaign_id = spring.Campaign, statuses = new[] { "pending", "approved", "cancelled" }, limit = 200 }, Ct)).Items.ToDictionary(a => a.WorkItemId, a => a.Status);
            Assert.Equal((ApprovalStatus.Approved, ApprovalStatus.Cancelled, ApprovalStatus.Cancelled), (after[gone], after[asked], after[given]));
            Assert.Equal(DecisionStatus.Cancelled, (await api.PostOkAsync<DecisionDto>(Operations.DecisionGet, new { decision_id = question.Id }, Ct)).Status);

            // The page after: the list without the archived agent, and the agent itself opened by its address.
            Assert.Equal([autumn.Campaign], (await api.RecordAsync<Page<CampaignSummaryDto>>(Operations.CampaignList, "archive-after", new { limit = 100 }, Ct)).Items.Select(c => c.Id));
            await api.RecordAsync<CampaignSummaryResponse>(Operations.CampaignSummary, "archive-after", new { campaign_ids = new[] { autumn.Campaign }, exclude_archived_contacts = true }, Ct);
            await api.RecordAsync<CampaignSummaryResponse>(Operations.CampaignSummary, "archive-spring-archived", new { campaign_ids = new[] { spring.Campaign }, exclude_archived_contacts = true }, Ct);
            Assert.Equal(CampaignStatus.Archived, (await api.RecordAsync<CampaignDto>(Operations.CampaignGet, "archive-spring-archived", new { campaign_id = spring.Campaign }, Ct)).Status);
        }
        finally
        {
            role.Release();
            await work.Ended;
        }
    }

    /// <summary>
    /// The confirmation's read of the provider's pause where the plugin the campaign is routed to does not perform it:
    /// the route resolves and cannot run, so the way to stop the provider's campaign first is the provider's own.
    /// </summary>
    [Fact]
    public async Task An_agent_whose_route_cannot_pause_the_providers_campaign_reads_it_as_unusable()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct, clock: new FixedClock(Nine));
        var spring = await Scene.OpenAsync(api, "Spring launch");

        var pause = await api.RecordAsync<RouteResolutionDto>(Operations.RouteResolve, "archive-pause-unusable", new { campaign_id = spring.Campaign, operation = "campaign.pause" }, Ct);

        Assert.False(pause.Usable);
        Assert.Equal("reply", pause.PluginId);
    }
}
