using System.Net;
using Jason.Contracts.Api;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests.Contacts;

/// <summary>
/// A person's line through the Runtime API, recorded under <c>docs/api/fixtures/</c>: ended with a reason and without one,
/// a held person continued, and an ended line reopened by the person and refused to a role, with the people list and the
/// journal read after each act.
/// </summary>
public class LineEndpointTests
{
    private static readonly DateTimeOffset Nine = new(2026, 10, 16, 9, 0, 0, TimeSpan.Zero);

    private static readonly object Ada = new { type = "human", id = "ada" };

    private static readonly object Manager = new { type = "role", id = "manager" };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_line_is_ended_continued_and_reopened_and_each_act_reads_back()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct, clock: new FixedClock(Nine));
        var campaign = (await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { name = "Nordic pilot" }, Ct)).Id;
        var added = await api.PostOkAsync<AddContactsResult>(
            Operations.CampaignAddContacts,
            new { campaign_id = campaign, contacts = new[] { new { first_name = "Ingrid" }, new { first_name = "Sven" } } },
            Ct);
        var ingrid = added.Items[0].ContactId!;
        var sven = added.Items[1].ContactId!;

        var unexplained = await api.RecordErrorAsync(
            Operations.CampaignFinishContacts, "reason-required", new { campaign_id = campaign, contact_ids = new[] { ingrid }, actor = Manager }, HttpStatusCode.BadRequest, Ct);
        Assert.Equal(("validation_failed", "reason", "required"), (unexplained.Code, unexplained.Details![0].Field, unexplained.Details[0].Code));

        var finished = await api.RecordAsync<FinishContactsResult>(
            Operations.CampaignFinishContacts, "finish-one", new { campaign_id = campaign, contact_ids = new[] { ingrid }, reason = "No answer after four touches.", actor = Manager }, Ct);
        Assert.Equal(new FinishContactsSummary(1, 0, 0, 0, 0), finished.Summary);
        var listed = await api.PostOkAsync<Page<MembershipItemDto>>(Operations.CampaignListContacts, new { campaign_id = campaign, state = "finished" }, Ct);
        Assert.Equal([ingrid], listed.Items.Select(item => item.Contact.Id));
        Assert.Equal(MembershipState.Finished, listed.Items[0].State);

        await HoldAsync(api, sven);
        var continued = await api.RecordAsync<ContinueContactsResult>(
            Operations.CampaignContinueContacts, "continue-held", new { campaign_id = campaign, contact_ids = new[] { sven, ingrid }, reason = "The reply was answered.", actor = Manager }, Ct);
        Assert.Equal([ContinueContactsItemStatus.Continued, ContinueContactsItemStatus.NotPaused], continued.Items.Select(item => item.Status));

        var refused = await api.RecordErrorAsync(
            Operations.CampaignReopenContacts, "not-human", new { campaign_id = campaign, contact_ids = new[] { ingrid }, actor = Manager }, HttpStatusCode.BadRequest, Ct);
        Assert.Equal("reopen_not_human", refused.Code);

        var reopened = await api.RecordAsync<ReopenContactsResult>(
            Operations.CampaignReopenContacts, "by-person", new { campaign_id = campaign, contact_ids = new[] { ingrid }, reason = "She wrote to say the timing is better now.", actor = Ada }, Ct);
        Assert.Equal(new ReopenContactsSummary(1, 0, 0, 0, 0, 0), reopened.Summary);
        var enrolled = await api.PostOkAsync<Page<MembershipItemDto>>(Operations.CampaignListContacts, new { campaign_id = campaign, state = "enrolled" }, Ct);
        Assert.Equal(new[] { ingrid, sven }.Order(StringComparer.Ordinal), enrolled.Items.Select(item => item.Contact.Id).Order(StringComparer.Ordinal));

        var lines = await api.PostOkAsync<Page<JournalEntryDto>>(Operations.JournalList, new { campaign_id = campaign }, Ct);
        var kinds = lines.Items.Select(entry => entry.Kind).ToList();
        Assert.Contains(JournalKinds.ContactsFinished, kinds);
        Assert.Contains(JournalKinds.ContactsContinued, kinds);
        Assert.Contains(JournalKinds.ContactsReopened, kinds);
        var ended = Assert.Single(lines.Items, entry => entry.Kind == JournalKinds.ContactsFinished);
        Assert.Equal("No answer after four touches.", ended.Reason);
    }

    /// <summary>Nothing holds a person before the hold at a reply arrives, so the test holds one itself.</summary>
    private static async Task HoldAsync(RuntimeApiFixture api, string contact)
    {
        using var scope = api.Resolve<IServiceScopeFactory>().CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JasonDbContext>();
        var membership = await db.CampaignContacts.SingleAsync(m => m.Contact!.PublicId == contact, Ct);
        membership.State = MembershipState.Paused;
        await db.SaveChangesAsync(Ct);
    }
}
