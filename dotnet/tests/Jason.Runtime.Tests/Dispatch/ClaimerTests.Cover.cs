using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Ids;
using Jason.Contracts.Plugins;
using Jason.Runtime.Approvals;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Routing;
using Jason.Runtime.Tests.Plugins;
using Jason.Runtime.WorkItems;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Dispatch;

/// <summary>
/// What a person's approval covers at the claim. A provider identifier the runtime learned where the approved subject held
/// none, for the person or for the campaign, by this item's earlier attempt or by another item, does not end the cover: the
/// work runs under the decision the person made, and the row keeps saying what they approved. A pin the subject held that
/// now holds another value or is gone, and every other change, parks the work again as <c>input_changed</c>.
/// </summary>
public partial class ClaimerTests
{
    private const string Covering = "stand-in-provider";

    [Theory]
    [InlineData("the failed attempt pinned the person")]
    [InlineData("the failed attempt pinned the campaign")]
    [InlineData("the failed attempt pinned both")]
    [InlineData("another item pinned the person")]
    [InlineData("another item pinned the campaign")]
    [InlineData("another item pinned both")]
    public async Task Cover_APinLearnedWhereTheApprovalHeldNone_RunsUnderTheSameApproval(string learned)
    {
        using var harness = CoverHarness();
        using var database = new TestDatabase();
        await using var db = database.Open();
        var (item, approved) = await ApprovedEnrolmentAsync(harness, db, personPin: null, campaignPin: null);
        var subject = approved.Subject.DeepClone();
        var hash = approved.SubjectHash;

        string learnedBy;
        if (learned.StartsWith("the failed attempt", StringComparison.Ordinal))
        {
            Assert.Single(await harness.Claimer.ClaimAsync(db, 4, Ct));
            var failed = await db.Attempts.SingleAsync(Ct);
            FailTransiently(item, failed);
            learnedBy = failed.PublicId;
        }
        else
        {
            learnedBy = await AnotherItemsAttemptAsync(db, item.Campaign!);
        }

        if (learned.EndsWith("the person", StringComparison.Ordinal) || learned.EndsWith("both", StringComparison.Ordinal))
        {
            item.Contact!.ExternalIds.Add(LearnedPin("contact", "p-48211", learnedBy));
        }

        if (learned.EndsWith("the campaign", StringComparison.Ordinal) || learned.EndsWith("both", StringComparison.Ordinal))
        {
            item.Campaign!.ExternalIds.Add(LearnedPin("campaign", "sq-1129", learnedBy));
        }

        await db.SaveChangesAsync(Ct);

        var work = Assert.Single(await harness.Claimer.ClaimAsync(db, 4, Ct));

        // What reaches the plugin carries what was learned.
        Assert.Equal(item.PublicId, work.WorkItemPublicId);
        var input = work.Plan!.Input;
        var pins = (Person: input["contacts"]![0]!["external_ids"]!.AsObject(), Campaign: input["campaign"]!["external_ids"]!.AsObject());
        Assert.Equal(learned.EndsWith("the campaign", StringComparison.Ordinal) ? null : "p-48211", (string?)pins.Person["contact"]);
        Assert.Equal(learned.EndsWith("the person", StringComparison.Ordinal) ? null : "sq-1129", (string?)pins.Campaign["campaign"]);

        // The record: one approval, still the person's, about what they approved; the attempt names it and what it sent.
        await using var fresh = database.Open();
        var row = Assert.Single(await fresh.Approvals.AsNoTracking().ToListAsync(Ct));
        Assert.Equal((approved.PublicId, ApprovalStatus.Approved, "approval_required"), (row.PublicId, row.Status, row.Reason));
        Assert.Equal((ActorType.Human, "operator"), (row.DecidedByType, row.DecidedById));
        Assert.Equal(hash, row.SubjectHash);
        Assert.True(JsonNode.DeepEquals(subject, row.Subject), "The approval's subject was rewritten.");
        var ran = await fresh.Attempts.AsNoTracking().Where(a => a.WorkItemId == item.Id).OrderBy(a => a.Number).LastAsync(Ct);
        Assert.Equal(work.AttemptPublicId, ran.PublicId);
        Assert.Equal(row.PublicId, ran.Provenance!.ApprovalId);
        Assert.Equal(CanonicalInput.Digest(input), ran.Provenance.InputDigest);
        var kinds = await fresh.Journal.AsNoTracking().Select(e => e.Kind).ToListAsync(Ct);
        Assert.DoesNotContain(JournalKinds.ApprovalSuperseded, kinds);
        Assert.Single(kinds, kind => kind == JournalKinds.ApprovalRequested);
    }

    /// <summary>
    /// A pin the approved subject held is held to its value. Another value, or no value any more, is not what the person
    /// approved, and neither is any other change beside a pin added: each parks the work again, the first row superseded and
    /// still the person's, and nothing runs.
    /// </summary>
    [Theory]
    [InlineData("person", "the person's pin holds another value")]
    [InlineData("person", "the person's pin is gone")]
    [InlineData("campaign", "the campaign's pin holds another value")]
    [InlineData("campaign", "the campaign's pin is gone")]
    [InlineData("campaign", "the person's pin added beside the campaign's changed")]
    [InlineData("person", "the campaign's pin added beside the person's changed")]
    [InlineData("person", "the campaign's pin added beside the person's gone")]
    [InlineData("none", "an argument edited beside a pin added")]
    [InlineData("none", "the person's name beside a pin added")]
    [InlineData("none", "the person's address beside a pin added")]
    [InlineData("none", "the campaign renamed beside a pin added")]
    public async Task Cover_APinChangedOrGoneOrAnythingElseBesideAPinAdded_ParksAgainAsInputChanged(string held, string since)
    {
        using var harness = CoverHarness();
        using var database = new TestDatabase();
        await using var db = database.Open();
        var (item, approved) = await ApprovedEnrolmentAsync(
            harness,
            db,
            personPin: held == "person" ? "p-48211" : null,
            campaignPin: held == "campaign" ? "sq-1129" : null,
            named: held != "campaign" || since == "the campaign's pin is gone");
        var subject = approved.Subject.DeepClone();
        var contact = item.Contact!;
        var campaign = item.Campaign!;
        var learnedBy = await AnotherItemsAttemptAsync(db, campaign);
        ExternalId PersonPin() => contact.ExternalIds.Single(pin => pin.Kind == "contact");
        ExternalId CampaignPin() => campaign.ExternalIds.Single(pin => pin.Kind == "campaign");

        switch (since)
        {
            case "the person's pin holds another value":
                PersonPin().Value = "p-59322";
                break;
            case "the person's pin is gone":
                db.ExternalIds.Remove(PersonPin());
                break;
            case "the campaign's pin holds another value":
                CampaignPin().Value = "sq-2240";
                break;
            case "the campaign's pin is gone":
                db.ExternalIds.Remove(CampaignPin());
                break;
            case "the person's pin added beside the campaign's changed":
                contact.ExternalIds.Add(LearnedPin("contact", "p-48211", learnedBy));
                CampaignPin().Value = "sq-2240";
                break;
            case "the campaign's pin added beside the person's changed":
                campaign.ExternalIds.Add(LearnedPin("campaign", "sq-1129", learnedBy));
                PersonPin().Value = "p-59322";
                break;
            case "the campaign's pin added beside the person's gone":
                campaign.ExternalIds.Add(LearnedPin("campaign", "sq-1129", learnedBy));
                db.ExternalIds.Remove(PersonPin());
                break;
            default:
                contact.ExternalIds.Add(LearnedPin("contact", "p-48211", learnedBy));
                switch (since)
                {
                    case "an argument edited beside a pin added":
                        item.Context = new JsonObject { [WorkItemService.InputKey] = Enrolling("refuse", named: true) };
                        break;
                    case "the person's name beside a pin added":
                        contact.FirstName = "Adah";
                        break;
                    case "the person's address beside a pin added":
                        contact.Channels.Single().Value = "ada.lovelace@example.test";
                        break;
                    default:
                        campaign.Name = "Q4 founders";
                        break;
                }

                break;
        }

        await db.SaveChangesAsync(Ct);

        Assert.Empty(await harness.Claimer.ClaimAsync(db, 4, Ct));

        await using var fresh = database.Open();
        Assert.Equal(WorkItemStatus.AwaitingApproval, (await fresh.WorkItems.AsNoTracking().SingleAsync(w => w.Id == item.Id, Ct)).Status);
        Assert.Empty(await fresh.Attempts.AsNoTracking().Where(a => a.WorkItemId == item.Id).ToListAsync(Ct));
        var approvals = await fresh.Approvals.AsNoTracking().OrderBy(a => a.Id).ToListAsync(Ct);
        Assert.Equal([ApprovalStatus.Superseded, ApprovalStatus.Pending], approvals.Select(a => a.Status));
        Assert.Equal((approved.PublicId, ActorType.Human, "operator"), (approvals[0].PublicId, approvals[0].DecidedByType, approvals[0].DecidedById));
        Assert.True(JsonNode.DeepEquals(subject, approvals[0].Subject), "The superseded approval's subject was rewritten.");
        Assert.Equal("input_changed", approvals[1].Reason);
    }

    /// <summary>
    /// A divergence never changes what is composed: the pin stands and the plugin keeps receiving the value Jason recorded,
    /// so the approval keeps covering the work, whether the pin it disputes was held when the person approved or was added
    /// since.
    /// </summary>
    [Theory]
    [InlineData("on the pin the approval held")]
    [InlineData("on a pin added since")]
    public async Task Cover_ADivergence_ChangesNothingTheApprovalCovers(string where)
    {
        using var harness = CoverHarness();
        using var database = new TestDatabase();
        await using var db = database.Open();
        var (item, approved) = await ApprovedEnrolmentAsync(harness, db, personPin: where == "on the pin the approval held" ? "p-48211" : null, campaignPin: null);
        var learnedBy = await AnotherItemsAttemptAsync(db, item.Campaign!);
        if (where == "on a pin added since")
        {
            item.Contact!.ExternalIds.Add(LearnedPin("contact", "p-48211", learnedBy));
        }

        var pin = item.Contact!.ExternalIds.Single(p => p.Kind == "contact");
        pin.DivergedValue = "p-59322";
        pin.DivergedAt = Noon;
        pin.DivergedByAttemptId = learnedBy;
        await db.SaveChangesAsync(Ct);

        var work = Assert.Single(await harness.Claimer.ClaimAsync(db, 4, Ct));

        Assert.Equal("p-48211", (string?)work.Plan!.Input["contacts"]![0]!["external_ids"]!["contact"]);
        await using var fresh = database.Open();
        var row = Assert.Single(await fresh.Approvals.AsNoTracking().ToListAsync(Ct));
        Assert.Equal((approved.PublicId, ApprovalStatus.Approved), (row.PublicId, row.Status));
    }

    /// <summary>
    /// A review asks the claim's own question: under an approved row that a pin learned since leaves standing, the claim would
    /// run the work and read no review; under one a changed pin ended, it would decide anew and read them.
    /// </summary>
    [Theory]
    [InlineData("a pin added", ReviewHearing.NotRead)]
    [InlineData("a pin changed", ReviewHearing.Read)]
    public async Task Cover_TheReviewHearing_AsksTheSameQuestion(string since, ReviewHearing hearing)
    {
        using var harness = CoverHarness();
        using var database = new TestDatabase();
        await using var db = database.Open();
        var (item, approved) = await ApprovedEnrolmentAsync(harness, db, personPin: since == "a pin changed" ? "p-48211" : null, campaignPin: null);
        if (since == "a pin added")
        {
            item.Contact!.ExternalIds.Add(LearnedPin("contact", "p-48211", await AnotherItemsAttemptAsync(db, item.Campaign!)));
        }
        else
        {
            item.Contact!.ExternalIds.Single().Value = "p-59322";
        }

        await db.SaveChangesAsync(Ct);

        var reading = await harness.Claimer.RunsUnderAsync(db, item, Ct);

        Assert.Equal((hearing, approved.PublicId), (reading.Hearing, reading.Approval?.PublicId));
    }

    private static Harness CoverHarness() => new(
        plugin: TestPlugins.Loaded(Covering, ["campaign.enroll"]),
        route: new Route(Covering, null, null));

    /// <summary>
    /// An enrollment parked and approved by a person, with the pins given held when they approved. Its arguments name the
    /// provider's campaign unless <paramref name="named"/> says otherwise, and by default only where no campaign pin is held:
    /// a campaign neither named nor pinned is refused before anybody is asked, and one named beside a pin that changed is
    /// refused as a contradiction rather than parked.
    /// </summary>
    private static async Task<(WorkItem Item, Approval Approved)> ApprovedEnrolmentAsync(
        Harness harness,
        JasonDbContext db,
        string? personPin,
        string? campaignPin,
        bool? named = null)
    {
        var campaign = WorkItemFactory.NewCampaign("Q3 founders", now: Noon);
        db.Campaigns.Add(campaign);
        var contact = new Contact { PublicId = PublicId.New("cnt"), FirstName = "Ada", CreatedAt = Noon, UpdatedAt = Noon };
        contact.Channels.Add(new ContactChannel { Channel = "email", Value = "ada@example.test", IsPrimary = true });
        db.Contacts.Add(contact);
        var item = WorkItemFactory.NewProviderOp(campaign, "campaign.enroll", Noon);
        item.Contact = contact;
        item.Context = new JsonObject { [WorkItemService.InputKey] = Enrolling("skip", named ?? campaignPin is null) };
        db.WorkItems.Add(item);
        if (personPin is not null)
        {
            contact.ExternalIds.Add(LearnedPin("contact", personPin, PublicId.New("att")));
        }

        if (campaignPin is not null)
        {
            campaign.ExternalIds.Add(LearnedPin("campaign", campaignPin, PublicId.New("att")));
        }

        await db.SaveChangesAsync(Ct);
        Assert.Empty(await harness.Claimer.ClaimAsync(db, 4, Ct));
        var approved = await Decide(db, ApprovalStatus.Approved);
        Assert.Equal(personPin, (string?)approved.Subject["input"]!["contacts"]![0]!["external_ids"]!["contact"]);
        Assert.Equal(campaignPin, (string?)approved.Subject["input"]!["campaign"]!["external_ids"]!["campaign"]);
        return (item, approved);
    }

    /// <summary>
    /// The enrollment's arguments: naming the provider's campaign where it is not linked yet, the value a campaign pin added
    /// since then carries too, and naming nothing where it is, so the pin alone says which and may change.
    /// </summary>
    private static JsonObject Enrolling(string collision, bool named)
    {
        var arguments = new JsonObject
        {
            ["channel"] = "email",
            ["collision"] = collision,
            ["start"] = new JsonObject { ["position"] = "first_step" },
            ["first_touch"] = "authored_delay",
        };
        if (named)
        {
            arguments["campaign"] = new JsonObject { ["external_id"] = "sq-1129" };
        }

        return arguments;
    }

    /// <summary>
    /// The attempt of another, finished item of the same campaign, whose answer is where a pin learned "by another item"
    /// comes from.
    /// </summary>
    private static async Task<string> AnotherItemsAttemptAsync(JasonDbContext db, Campaign campaign)
    {
        var other = WorkItemFactory.NewProviderOp(campaign, "campaign.get", Noon);
        other.Status = WorkItemStatus.Cancelled;
        var attempt = AttemptOf(other, 1);
        attempt.Status = AttemptStatus.Succeeded;
        attempt.FinishedAt = Noon;
        other.Attempts.Add(attempt);
        db.WorkItems.Add(other);
        await db.SaveChangesAsync(Ct);
        return attempt.PublicId;
    }

    private static void FailTransiently(WorkItem item, Attempt attempt)
    {
        WorkItemTransitions.Apply(item, WorkItemStatus.Processing, Noon);
        attempt.StartedAt = Noon;
        attempt.Status = AttemptStatus.Failed;
        attempt.FinishedAt = Noon;
        attempt.Error = new AttemptErrorDto("provider_unavailable", "The provider did not answer in time.", Retriable: true, Class: FailureClass.Transient);
        WorkItemTransitions.Apply(item, WorkItemStatus.Created, Noon);
    }

    private static ExternalId LearnedPin(string kind, string value, string attempt) => new()
    {
        PluginId = Covering,
        Kind = kind,
        Value = value,
        RecordedAt = Noon,
        RecordedByAttemptId = attempt,
    };
}
