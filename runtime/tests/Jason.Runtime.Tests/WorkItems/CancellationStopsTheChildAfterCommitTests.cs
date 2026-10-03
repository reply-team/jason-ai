using Jason.Contracts.Api;
using Jason.Contracts.Ids;
using Jason.Runtime.Campaigns;
using Jason.Runtime.Contacts;
using Jason.Runtime.Execution;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.WorkItems;

/// <summary>
/// Every way work is cancelled in bulk stops a running child only once the cancellation has committed. The moment
/// a child is told to stop is the moment the handler watching it can start writing an ending of its own, so what
/// that moment can read from a connection of its own is the whole question: the cancellation, or the attempt
/// still running. <c>workitem.cancel</c> itself is held by <c>AttemptHandlerTests</c>, against a handler.
/// </summary>
public class CancellationStopsTheChildAfterCommitTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("the person is removed from the campaign")]
    [InlineData("the contact is archived")]
    [InlineData("the campaign is archived")]
    public async Task A_running_child_is_stopped_once_the_cancellation_has_committed(string how)
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var clock = new FixedClock(Noon);
        var campaign = WorkItemFactory.NewCampaign();
        var contact = new Contact
        {
            PublicId = PublicId.New("cnt"),
            FirstName = "Member",
            CreatedAt = Noon.UtcDateTime,
            UpdatedAt = Noon.UtcDateTime,
        };
        db.Contacts.Add(contact);
        db.CampaignContacts.Add(new CampaignContact
        {
            Campaign = campaign,
            Contact = contact,
            State = MembershipState.Enrolled,
            AddedAt = Noon.UtcDateTime,
            UpdatedAt = Noon.UtcDateTime,
        });
        var item = WorkItemFactory.NewAiRole(campaign, configure: w =>
        {
            w.Contact = contact;
            w.Status = WorkItemStatus.Processing;
        });
        var attempt = WorkItemFactory.NewAttempt(item, 1, AttemptStatus.Running, Noon.UtcDateTime);
        db.WorkItems.Add(item);
        await db.SaveChangesAsync(Ct);

        var registry = new RunningAttemptRegistry();
        var seen = new List<(WorkItemStatus Item, AttemptStatus Attempt)>();
        using var running = registry.Register(attempt.PublicId, () =>
        {
            using var reader = database.Open();
            seen.Add((
                reader.WorkItems.AsNoTracking().Single(w => w.Id == item.Id).Status,
                reader.Attempts.AsNoTracking().Single(a => a.Id == attempt.Id).Status));
        });
        var canceller = TestCanceller.New(clock, registry);
        var journal = new JournalWriter(clock);

        switch (how)
        {
            case "the person is removed from the campaign":
                await new MembershipService(db, journal, clock, canceller)
                    .RemoveAsync(new RemoveContactsRequest(campaign.PublicId, [contact.PublicId], null, "taken out"), Ct);
                break;
            case "the contact is archived":
                await new ContactService(db, journal, clock, canceller)
                    .ArchiveAsync(new ContactArchiveRequest(contact.PublicId, null, "asked to be forgotten"), Ct);
                break;
            default:
                await new CampaignService(db, journal, clock, canceller)
                    .ArchiveAsync(new CampaignTransitionRequest(campaign.PublicId, null, "finished"), Ct);
                break;
        }

        Assert.Equal([(WorkItemStatus.Cancelled, AttemptStatus.Cancelled)], seen);
    }
}
