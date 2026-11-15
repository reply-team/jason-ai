using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Journal;
using Jason.Runtime.Execution;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Execution;

/// <summary>
/// What a read of a send says when it finds the message came back as a bounce. Every read decides alone, at its own end:
/// one <c>message_bounce_observed</c> line per send, whoever made the read, and once more where the first said soft and a
/// later read says hard or names no kind; for a hard bounce or one with no kind the stop, the contact's email put on the
/// register by <c>system:bounce</c> in the same change set. A soft bounce stops nothing; a read that says false, or cannot
/// say, decides nothing.
/// </summary>
public class BounceFactsTests
{
    private static readonly DateTimeOffset Noon = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);

    private const string Primary = "ada@northwind.example";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Rule 2: the line, about the send, keyed by it, naming the read, with identifiers, the kind and moments only.</summary>
    [Fact]
    public async Task A_read_that_says_the_send_bounced_writes_one_line_about_the_send()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var (send, read) = await SeedAsync(db);

        Assert.True(await Facts().SayBounceAsync(db, read, "reply", Input(send), Bounced("hard", "hard"), Ct));
        await db.SaveChangesAsync(Ct);

        var line = await db.Journal.AsNoTracking().SingleAsync(entry => entry.Kind == JournalKinds.MessageBounceObserved, Ct);
        Assert.Equal(new ActorRef(ActorType.System, "dispatcher"), new ActorRef(line.ActorType, line.ActorId));
        Assert.Equal((send.CampaignId, send.PublicId, send.PublicId, send.ContactId), (line.CampaignId, line.WorkItemId, line.Key, line.ContactId));
        Assert.True(JsonNode.DeepEquals(
            new JsonObject
            {
                ["plugin"] = "reply",
                ["reference"] = "email-1001",
                ["channel"] = "email",
                ["message_id"] = "msg-7001",
                ["bounce"] = "hard",
                ["bounce_code"] = "hard",
                ["bounced_at"] = "2026-11-14T11:59:00Z",
                ["read_work_item"] = read.PublicId,
            },
            line.New),
            line.New!.ToJsonString());

        // The line is the one its document publishes.
        Assert.Empty(JournalKindCatalog.Find(JournalKinds.MessageBounceObserved)!.CheckBody(line.Key, line.Old, line.New));
    }

    /// <summary>Rule 3: a hard bounce, and one the provider gave no kind for, put the contact's primary email on the register.</summary>
    [Theory]
    [InlineData("hard", "hard", "a hard bounce")]
    [InlineData(null, "unknown", "a bounce the provider named no kind for")]
    [InlineData(null, null, "a bounce the provider named no kind for")]
    public async Task A_hard_bounce_or_one_with_no_kind_stops_the_contact_s_primary_email(string? kind, string? word, string said)
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var (send, read) = await SeedAsync(db);

        Assert.True(await Facts().SayBounceAsync(db, read, "reply", Input(send), Bounced(kind, word), Ct));
        await db.SaveChangesAsync(Ct);

        var line = await db.Journal.AsNoTracking().SingleAsync(entry => entry.Kind == JournalKinds.MessageBounceObserved, Ct);
        var entry = await db.Suppressions.AsNoTracking().SingleAsync(Ct);
        var campaign = await db.Campaigns.AsNoTracking().SingleAsync(Ct);
        Assert.Equal(("email", Primary), (entry.Channel, entry.Value));
        Assert.Equal(
            $"Bounced: {said}, line {line.PublicId} of send {send.PublicId} in campaign {campaign.PublicId}. The address taken is the email Jason would send this contact to when the bounce was read.",
            entry.Reason);
        Assert.Equal(line.Ts, entry.CreatedAt);

        var added = await db.Journal.AsNoTracking().SingleAsync(e => e.Kind == JournalKinds.SuppressionAdded, Ct);
        Assert.Equal((ActorType.System, "bounce", entry.Reason), (added.ActorType, added.ActorId, added.Reason));
    }

    /// <summary>The reason names no address and no word of the provider's: the value is the entry itself.</summary>
    [Fact]
    public async Task The_reason_names_no_address_and_no_word_of_the_provider_s()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var (send, read) = await SeedAsync(db);

        await Facts().SayBounceAsync(db, read, "reply", Input(send), Bounced(null, "spamRejectionLater"), Ct);
        await db.SaveChangesAsync(Ct);

        var reason = (await db.Suppressions.AsNoTracking().SingleAsync(Ct)).Reason!;
        Assert.DoesNotContain("@", reason, StringComparison.Ordinal);
        Assert.DoesNotContain("spamRejectionLater", reason, StringComparison.Ordinal);
    }

    /// <summary>Rule 3: a soft bounce is recorded and stops nothing.</summary>
    [Fact]
    public async Task A_soft_bounce_is_recorded_and_stops_nothing()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var (send, read) = await SeedAsync(db);

        Assert.True(await Facts().SayBounceAsync(db, read, "reply", Input(send), Bounced("soft", "mailboxFull"), Ct));
        await db.SaveChangesAsync(Ct);

        var line = await db.Journal.AsNoTracking().SingleAsync(entry => entry.Kind == JournalKinds.MessageBounceObserved, Ct);
        Assert.Equal(("soft", "mailboxFull"), ((string?)line.New!["bounce"], (string?)line.New["bounce_code"]));
        Assert.False(await db.Suppressions.AnyAsync(Ct));
        Assert.False(await db.Journal.AnyAsync(entry => entry.Kind == JournalKinds.SuppressionAdded, Ct));
    }

    /// <summary>Rule 4: a read that says the send did not bounce, or cannot say, decides nothing and stops nothing.</summary>
    [Theory]
    [InlineData("false")]
    [InlineData("null")]
    [InlineData("absent")]
    public async Task A_read_that_says_no_bounce_or_cannot_say_decides_nothing(string bounced)
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var (send, read) = await SeedAsync(db);
        var result = Bounced("hard", "hard");
        if (bounced == "absent")
        {
            result.Remove("bounced");
        }
        else
        {
            result["bounced"] = bounced == "false" ? false : null;
        }

        Assert.False(await Facts().SayBounceAsync(db, read, "reply", Input(send), result, Ct));
        await db.SaveChangesAsync(Ct);

        Assert.False(await db.Journal.AnyAsync(entry => entry.Kind == JournalKinds.MessageBounceObserved, Ct));
        Assert.False(await db.Suppressions.AnyAsync(Ct));
    }

    /// <summary>
    /// Once, and once more: a send first said soft whose later read, anybody's, says hard or names no kind is said a second
    /// time, under <c>&lt;send&gt;/stop</c>, with the later read's members, and that line stops the address, its reason
    /// naming it. A delay notice before the final failure is the ordinary case, and the provider's latest notice wins.
    /// </summary>
    [Theory]
    [InlineData("hard", "hard", "a hard bounce")]
    [InlineData(null, "unknown", "a bounce the provider named no kind for")]
    public async Task A_send_said_soft_and_then_found_hard_is_said_once_more_and_stopped(string? kind, string? word, string said)
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var (send, read) = await SeedAsync(db);
        Assert.True(await Facts().SayBounceAsync(db, read, "reply", Input(send), Bounced("soft", "mailboxFull"), Ct));
        await db.SaveChangesAsync(Ct);
        Assert.False(await db.Suppressions.AnyAsync(Ct));

        var again = await AnotherReadAsync(db, send);
        Assert.True(await Facts().SayBounceAsync(db, again, "reply", Input(send), Bounced(kind, word), Ct));
        await db.SaveChangesAsync(Ct);

        var lines = await db.Journal.AsNoTracking().Where(entry => entry.Kind == JournalKinds.MessageBounceObserved).OrderBy(entry => entry.Id).ToListAsync(Ct);
        Assert.Equal([send.PublicId, send.PublicId + SentFacts.StopKeySuffix], lines.Select(line => line.Key));
        var second = lines[1];
        Assert.Equal((send.PublicId, send.ContactId), (second.WorkItemId, second.ContactId));
        Assert.Equal((kind, word, again.PublicId), ((string?)second.New!["bounce"], (string?)second.New["bounce_code"], (string?)second.New["read_work_item"]));
        Assert.Empty(JournalKindCatalog.Find(JournalKinds.MessageBounceObserved)!.CheckBody(second.Key, second.Old, second.New));

        var entry = await db.Suppressions.AsNoTracking().SingleAsync(Ct);
        var campaign = await db.Campaigns.AsNoTracking().SingleAsync(Ct);
        Assert.Equal(
            $"Bounced: {said}, line {second.PublicId} of send {send.PublicId} in campaign {campaign.PublicId}. The address taken is the email Jason would send this contact to when the bounce was read.",
            entry.Reason);
    }

    /// <summary>A send said soft and read soft again is said once: the second read writes nothing and stops nothing.</summary>
    [Fact]
    public async Task A_send_said_soft_and_read_soft_again_is_said_once()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var (send, read) = await SeedAsync(db);
        Assert.True(await Facts().SayBounceAsync(db, read, "reply", Input(send), Bounced("soft", "mailboxFull"), Ct));
        await db.SaveChangesAsync(Ct);

        Assert.False(await Facts().SayBounceAsync(db, await AnotherReadAsync(db, send), "reply", Input(send), Bounced("soft", "spamRejection"), Ct));
        await db.SaveChangesAsync(Ct);

        Assert.Equal(1, await db.Journal.CountAsync(entry => entry.Kind == JournalKinds.MessageBounceObserved, Ct));
        Assert.False(await db.Suppressions.AnyAsync(Ct));
    }

    /// <summary>
    /// After a line that stops, the first or the second, nothing more is said of the send, whatever a later read finds,
    /// and nothing more is staged. The kinds read before are listed in order, an empty one for no kind.
    /// </summary>
    [Theory]
    [InlineData("hard", "soft")]
    [InlineData("hard", "hard")]
    [InlineData("hard", null)]
    [InlineData("", "hard")]
    [InlineData("soft,hard", "hard")]
    [InlineData("soft,hard", "soft")]
    [InlineData("soft,", null)]
    public async Task After_a_line_that_stops_nothing_more_is_said_of_the_send(string before, string? later)
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var (send, _) = await SeedAsync(db);
        var reads = before.Split(',');
        foreach (var said in reads)
        {
            var kind = said.Length == 0 ? null : said;
            Assert.True(await Facts().SayBounceAsync(db, await AnotherReadAsync(db, send), "reply", Input(send), Bounced(kind, kind ?? "unknown"), Ct));
            await db.SaveChangesAsync(Ct);
        }

        Assert.False(await Facts().SayBounceAsync(db, await AnotherReadAsync(db, send), "reply", Input(send), Bounced(later, later ?? "unknown"), Ct));
        await db.SaveChangesAsync(Ct);

        Assert.Equal(reads.Length, await db.Journal.CountAsync(entry => entry.Kind == JournalKinds.MessageBounceObserved, Ct));
        Assert.Equal(1, await db.Suppressions.CountAsync(Ct));
        Assert.Equal(1, await db.Journal.CountAsync(entry => entry.Kind == JournalKinds.SuppressionAdded, Ct));
    }

    /// <summary>A stop a person lifted is never put back by the same send: a later read that finds it hard again says nothing.</summary>
    [Fact]
    public async Task A_stop_a_person_lifted_is_never_put_back_by_the_same_send()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var (send, read) = await SeedAsync(db);
        Assert.True(await Facts().SayBounceAsync(db, read, "reply", Input(send), Bounced("soft", "mailboxFull"), Ct));
        await db.SaveChangesAsync(Ct);
        Assert.True(await Facts().SayBounceAsync(db, await AnotherReadAsync(db, send), "reply", Input(send), Bounced("hard", "hard"), Ct));
        await db.SaveChangesAsync(Ct);
        await db.Suppressions.ExecuteDeleteAsync(Ct);

        Assert.False(await Facts().SayBounceAsync(db, await AnotherReadAsync(db, send), "reply", Input(send), Bounced("hard", "hard"), Ct));
        await db.SaveChangesAsync(Ct);

        Assert.False(await db.Suppressions.AnyAsync(Ct));
        Assert.Equal(2, await db.Journal.CountAsync(entry => entry.Kind == JournalKinds.MessageBounceObserved, Ct));
        Assert.Equal(1, await db.Journal.CountAsync(entry => entry.Kind == JournalKinds.SuppressionAdded, Ct));
    }

    /// <summary>
    /// Risk 5: the send's record never holds the address it went to, so the stop takes the email Jason would send the
    /// contact to when the read ends: an address changed since the send is the new one.
    /// </summary>
    [Fact]
    public async Task The_address_taken_is_the_one_the_contact_holds_when_the_read_ends()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var (send, read) = await SeedAsync(db);
        var email = await db.ContactChannels.SingleAsync(channel => channel.ContactId == send.ContactId && channel.Channel == "email", Ct);
        email.Value = "ada.new@northwind.example";
        await db.SaveChangesAsync(Ct);

        await Facts().SayBounceAsync(db, read, "reply", Input(send), Bounced("hard", "hard"), Ct);
        await db.SaveChangesAsync(Ct);

        Assert.Equal("ada.new@northwind.example", (await db.Suppressions.AsNoTracking().SingleAsync(Ct)).Value);
    }

    /// <summary>An address already on the register is left as it is: one entry, the first one's, and no second line.</summary>
    [Fact]
    public async Task An_address_already_on_the_register_is_left_as_it_is()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var (send, read) = await SeedAsync(db);
        db.Suppressions.Add(new Suppression { PublicId = "sup_FIRST", Channel = "email", Value = Primary, Reason = "A person's own.", CreatedAt = Noon.UtcDateTime });
        await db.SaveChangesAsync(Ct);

        Assert.True(await Facts().SayBounceAsync(db, read, "reply", Input(send), Bounced("hard", "hard"), Ct));
        await db.SaveChangesAsync(Ct);

        var entry = await db.Suppressions.AsNoTracking().SingleAsync(Ct);
        Assert.Equal(("sup_FIRST", "A person's own."), (entry.PublicId, entry.Reason));
        Assert.False(await db.Journal.AnyAsync(e => e.Kind == JournalKinds.SuppressionAdded, Ct));
        Assert.True(await db.Journal.AnyAsync(e => e.Kind == JournalKinds.MessageBounceObserved, Ct));
    }

    /// <summary>A bounce is an email's: a LinkedIn read that said one is recorded, and no address is stopped.</summary>
    [Fact]
    public async Task A_read_of_a_linkedin_send_stops_no_address()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var (send, read) = await SeedAsync(db);
        var result = Bounced("hard", "hard");
        result["channel"] = "linkedin";

        Assert.True(await Facts().SayBounceAsync(db, read, "reply", Input(send), result, Ct));
        await db.SaveChangesAsync(Ct);

        Assert.False(await db.Suppressions.AnyAsync(Ct));
    }

    /// <summary>A contact who holds no email has nothing to stop: the line is the record.</summary>
    [Fact]
    public async Task A_contact_with_no_email_has_nothing_to_stop()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var (send, read) = await SeedAsync(db);
        db.ContactChannels.RemoveRange(await db.ContactChannels.ToListAsync(Ct));
        await db.SaveChangesAsync(Ct);

        Assert.True(await Facts().SayBounceAsync(db, read, "reply", Input(send), Bounced("hard", "hard"), Ct));
        await db.SaveChangesAsync(Ct);

        Assert.False(await db.Suppressions.AnyAsync(Ct));
    }

    /// <summary>The send a read names is one of the read's own campaign, or nothing is said.</summary>
    [Fact]
    public async Task A_send_of_another_campaign_is_never_said()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var (send, _) = await SeedAsync(db);
        var elsewhere = WorkItemFactory.NewCampaign("Elsewhere", now: Noon.UtcDateTime);
        var read = WorkItemFactory.NewProviderOp(elsewhere, "message.status", Noon.UtcDateTime);
        db.Campaigns.Add(elsewhere);
        db.WorkItems.Add(read);
        await db.SaveChangesAsync(Ct);

        Assert.False(await Facts().SayBounceAsync(db, read, "reply", Input(send), Bounced("hard", "hard"), Ct));
        await db.SaveChangesAsync(Ct);
        Assert.False(await db.Journal.AnyAsync(entry => entry.Kind == JournalKinds.MessageBounceObserved, Ct));
        Assert.False(await db.Suppressions.AnyAsync(Ct));
    }

    private static SentFacts Facts() => new(new JournalWriter(new FixedClock(Noon)));

    /// <summary>One more read of the send, made by anybody.</summary>
    private static async Task<WorkItem> AnotherReadAsync(JasonDbContext db, WorkItem send)
    {
        var again = WorkItemFactory.NewProviderOp(send.Campaign!, "message.status", Noon.UtcDateTime);
        db.WorkItems.Add(again);
        await db.SaveChangesAsync(Ct);
        return again;
    }

    internal static async Task<(WorkItem Send, WorkItem Read)> SeedAsync(JasonDbContext db)
    {
        var campaign = WorkItemFactory.NewCampaign(now: Noon.UtcDateTime);
        var ada = new Contact { PublicId = "cnt_ADA", FirstName = "Ada", CreatedAt = Noon.UtcDateTime, UpdatedAt = Noon.UtcDateTime };
        ada.Channels.Add(new ContactChannel { Channel = "email", Value = Primary, IsPrimary = true });
        ada.Channels.Add(new ContactChannel { Channel = "linkedin", Value = "https://www.linkedin.com/in/ada-example", IsPrimary = true });
        var send = WorkItemFactory.NewProviderOp(campaign, "message.send", Noon.UtcDateTime);
        send.Contact = ada;
        var read = WorkItemFactory.NewProviderOp(campaign, "message.status", Noon.UtcDateTime);
        db.Campaigns.Add(campaign);
        db.Contacts.Add(ada);
        db.WorkItems.AddRange(send, read);
        await db.SaveChangesAsync(Ct);
        return (send, read);
    }

    /// <summary>The input the claim composes for a read: the send it names, with the reference the send's attempt returned.</summary>
    internal static JsonObject Input(WorkItem send) => new()
    {
        ["args"] = new JsonObject { ["send"] = send.PublicId },
        ["send"] = new JsonObject
        {
            ["work_item"] = send.PublicId,
            ["operation"] = send.Operation,
            ["reference"] = "email-1001",
        },
    };

    /// <summary>A read's answer that the send went and came back, of <paramref name="kind"/>, in the provider's <paramref name="word"/>.</summary>
    internal static JsonObject Bounced(string? kind, string? word) => new()
    {
        ["state"] = "sent",
        ["channel"] = "email",
        ["cancelable"] = false,
        ["accepted_for"] = null,
        ["sent_at"] = "2026-11-14T11:58:00Z",
        ["message_id"] = "msg-7001",
        ["rendered_subject"] = null,
        ["rendered_body"] = null,
        ["failure_reason"] = null,
        ["failure_code"] = null,
        ["cancelled_at"] = null,
        ["bounced"] = true,
        ["bounce"] = kind,
        ["bounced_at"] = "2026-11-14T11:59:00Z",
        ["bounce_code"] = word,
    };
}
