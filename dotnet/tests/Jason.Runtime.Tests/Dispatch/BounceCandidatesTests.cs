using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Dispatch;

/// <summary>
/// Rule 6's query: the sends of a campaign one bounce of its contact could be of. The campaign's own emails to that contact
/// through the plugin the bounce was read at, succeeded with a reference, finished in the 45 days before the bounce, with
/// no bounce said of them yet; newest first, at most three. A send of another campaign, another person, another plugin or
/// channel, one with no reference, one older than the window, or one already said bounced is none of them.
/// </summary>
public class BounceCandidatesTests
{
    private static readonly DateTime Bounced = new(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_newest_three_of_the_campaign_s_own_emails_to_the_contact_through_the_plugin()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var scene = await SceneAsync(db);
        var sends = new List<WorkItem>();
        for (var day = 1; day <= 4; day++)
        {
            sends.Add(Send(db, scene.Campaign, scene.Ada, Bounced.AddDays(-day)));
        }

        await db.SaveChangesAsync(Ct);

        var found = await BounceCandidates.ReadAsync(db, scene.Campaign.Id, scene.Ada.Id, "reply", Bounced, Ct);

        Assert.Equal(sends.Take(3).Select(send => send.PublicId), found.Select(candidate => candidate.WorkItem));
        Assert.All(found, candidate => Assert.Equal(("message.send", "reply", "sha256:seat"), (candidate.Operation, candidate.PluginId, candidate.BindingIdentity)));
    }

    [Fact]
    public async Task A_send_that_could_not_be_the_one_is_none_of_them()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var scene = await SceneAsync(db);
        var other = WorkItemFactory.NewCampaign("Other", now: Bounced);
        var grace = new Contact { PublicId = "cnt_GRACE", CreatedAt = Bounced, UpdatedAt = Bounced };
        db.Campaigns.Add(other);
        db.Contacts.Add(grace);
        var kept = Send(db, scene.Campaign, scene.Ada, Bounced.AddDays(-2));
        Send(db, other, scene.Ada, Bounced.AddDays(-1));
        Send(db, scene.Campaign, grace, Bounced.AddDays(-1));
        Send(db, scene.Campaign, scene.Ada, Bounced.AddDays(-1), plugin: "mailer");
        Send(db, scene.Campaign, scene.Ada, Bounced.AddDays(-1), channel: "linkedin");
        Send(db, scene.Campaign, scene.Ada, Bounced.AddDays(-1), reference: null);
        Send(db, scene.Campaign, scene.Ada, Bounced.AddDays(-1), status: WorkItemStatus.Failed);
        Send(db, scene.Campaign, scene.Ada, Bounced.AddDays(-1), operation: "social_invitation.send");
        Send(db, scene.Campaign, scene.Ada, Bounced.AddDays(-45).AddMinutes(-1));
        var said = Send(db, scene.Campaign, scene.Ada, Bounced.AddHours(-1));
        await db.SaveChangesAsync(Ct);
        new JournalWriter(new FixedClock(new DateTimeOffset(Bounced))).Append(
            db, Actors.Dispatcher, JournalKinds.MessageBounceObserved, campaign: null, key: said.PublicId,
            updated: new JsonObject
            {
                ["plugin"] = "reply", ["reference"] = "email-1001", ["channel"] = "email", ["message_id"] = null, ["bounce"] = "soft",
                ["bounce_code"] = "soft", ["bounced_at"] = null, ["read_work_item"] = "wi_01JC000000000000000000000E",
            },
            workItem: said);
        await db.SaveChangesAsync(Ct);

        var found = await BounceCandidates.ReadAsync(db, scene.Campaign.Id, scene.Ada.Id, "reply", Bounced, Ct);

        Assert.Equal([kept.PublicId], found.Select(candidate => candidate.WorkItem));
    }

    /// <summary>The window is 45 days before the bounce, its first moment included.</summary>
    [Fact]
    public async Task The_window_opens_forty_five_days_before_the_bounce()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var scene = await SceneAsync(db);
        var edge = Send(db, scene.Campaign, scene.Ada, Bounced.AddDays(-45));
        await db.SaveChangesAsync(Ct);

        var found = await BounceCandidates.ReadAsync(db, scene.Campaign.Id, scene.Ada.Id, "reply", Bounced, Ct);

        Assert.Equal([edge.PublicId], found.Select(candidate => candidate.WorkItem));
    }

    /// <summary>The reference and the plugin are the latest succeeded attempt's, as the claim reads them for a read of the send.</summary>
    [Fact]
    public async Task Where_a_send_ran_is_read_from_its_latest_succeeded_attempt()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var scene = await SceneAsync(db);
        var send = Send(db, scene.Campaign, scene.Ada, Bounced.AddDays(-1), plugin: "mailer");
        var later = WorkItemFactory.NewAttempt(send, 2, AttemptStatus.Succeeded, Bounced.AddDays(-1));
        later.Provenance = Provenance("reply", "email-2002");
        db.Attempts.Add(later);
        await db.SaveChangesAsync(Ct);

        Assert.Equal(
            [send.PublicId],
            (await BounceCandidates.ReadAsync(db, scene.Campaign.Id, scene.Ada.Id, "reply", Bounced, Ct)).Select(candidate => candidate.WorkItem));
        Assert.Empty(await BounceCandidates.ReadAsync(db, scene.Campaign.Id, scene.Ada.Id, "mailer", Bounced, Ct));
    }

    /// <summary>
    /// Rule 6: never a second read for a send that already has an open read the system made. A finished read, or one a
    /// person or a role made, is not one.
    /// </summary>
    [Fact]
    public async Task Only_an_open_read_the_system_made_counts_as_one_already_there()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var scene = await SceneAsync(db);
        var open = Send(db, scene.Campaign, scene.Ada, Bounced.AddDays(-1));
        var ended = Send(db, scene.Campaign, scene.Ada, Bounced.AddDays(-2));
        var persons = Send(db, scene.Campaign, scene.Ada, Bounced.AddDays(-3));
        Read(db, scene.Campaign, open, WorkItemStatus.Waiting, ActorType.System);
        Read(db, scene.Campaign, ended, WorkItemStatus.Succeeded, ActorType.System);
        Read(db, scene.Campaign, persons, WorkItemStatus.Created, ActorType.Human);
        await db.SaveChangesAsync(Ct);

        var held = await BounceCandidates.ReadOpenAsync(db, scene.Campaign.Id, [open.PublicId, ended.PublicId, persons.PublicId], Ct);

        Assert.Equal([open.PublicId], held);
    }

    private static async Task<(Campaign Campaign, Contact Ada)> SceneAsync(JasonDbContext db)
    {
        var campaign = WorkItemFactory.NewCampaign(now: Bounced);
        var ada = new Contact { PublicId = "cnt_ADA", CreatedAt = Bounced, UpdatedAt = Bounced };
        db.Campaigns.Add(campaign);
        db.Contacts.Add(ada);
        await db.SaveChangesAsync(Ct);
        return (campaign, ada);
    }

    /// <summary>A send of the campaign to the contact, finished at <paramref name="finished"/>, with its one attempt.</summary>
    private static WorkItem Send(
        JasonDbContext db,
        Campaign campaign,
        Contact contact,
        DateTime finished,
        string plugin = "reply",
        string channel = "email",
        string? reference = "email-1001",
        WorkItemStatus status = WorkItemStatus.Succeeded,
        string operation = "message.send")
    {
        var send = WorkItemFactory.NewProviderOp(campaign, operation, finished.AddMinutes(-5));
        send.Contact = contact;
        send.Status = status;
        send.FinishedAt = finished;
        send.Context = new JsonObject { ["input"] = new JsonObject { ["channel"] = channel } };
        var attempt = WorkItemFactory.NewAttempt(send, 1, status == WorkItemStatus.Succeeded ? AttemptStatus.Succeeded : AttemptStatus.Failed, finished);
        attempt.Provenance = Provenance(plugin, reference);
        db.WorkItems.Add(send);
        db.Attempts.Add(attempt);
        return send;
    }

    private static void Read(JasonDbContext db, Campaign campaign, WorkItem send, WorkItemStatus status, ActorType by)
    {
        var read = WorkItemFactory.NewProviderOp(campaign, "message.status", Bounced);
        read.Status = status;
        read.CreatedByType = by;
        read.Context = new JsonObject { ["input"] = new JsonObject { ["send"] = send.PublicId } };
        db.WorkItems.Add(read);
    }

    private static AttemptProvenanceDto Provenance(string plugin, string? reference) => new(
        plugin, "1.0.0", "sha256:0", 1, 1, "message.send", 1, null, null, RouteScope.GlobalDefault, "sha256:seat",
        ExternalIdsReturned: reference is null ? null : new Dictionary<string, string>(StringComparer.Ordinal) { ["send"] = reference });
}
