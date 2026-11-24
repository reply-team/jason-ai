using Jason.Contracts.Api;
using Jason.Contracts.Ids;
using Jason.Runtime.Configuration;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Dispatch;

/// <summary>
/// A reply holds the person's line: a reply line naming, by its contact column, a person who is an enrolled member of the
/// campaign makes them <c>paused</c> in the transaction that reads the line, before any skip and whether or not a
/// responder is woken, and says so in one <c>contacts_paused</c> line as the dispatcher. A member whose line is held, ended
/// or removed is left as they are, and a line that names nobody holds nobody.
/// </summary>
public partial class ResponderTests
{
    /// <summary>
    /// Whatever the step does with the line — wakes a responder, is turned off, or passes it over as too old or as a thread
    /// the campaign may share — the person who wrote is held, and one line says it, naming them and the reply line.
    /// </summary>
    [Theory]
    [InlineData("a responder is woken")]
    [InlineData("the step is off")]
    [InlineData("the reply is too old")]
    [InlineData("the thread is not the campaign's own")]
    [InlineData("no responder could be launched")]
    public async Task A_reply_holds_an_enrolled_member_whatever_the_step_does_with_the_line(string what)
    {
        using var harness = what == "no responder could be launched"
            ? new DispatchHarness(Noon, roles: new RolesOptions())
            : new DispatchHarness(Noon);
        var campaign = await CampaignAsync(harness);
        RoutedTo(harness, Reader);
        if (what == "the step is off")
        {
            harness.Responder.CurrentValue = new ResponderOptions { OnReply = false };
        }

        var (row, person) = await MemberAsync(harness, campaign, MembershipState.Enrolled);
        var line = await LineAsync(
            harness,
            campaign,
            "th-1",
            what == "the reply is too old" ? Noon.AddDays(-4) : Noon.AddMinutes(-5),
            contact: person,
            attributedBy: what == "the thread is not the campaign's own" ? "contact_pin" : null,
            events: what == "the thread is not the campaign's own",
            contactRow: row);

        Assert.Equal(what == "a responder is woken" ? 1 : 0, await harness.RespondAsync(Ct));

        await using var db = harness.Open();
        Assert.Equal(MembershipState.Paused, await StateAsync(db, campaign, row));
        var held = Assert.Single(await db.Journal.AsNoTracking().Where(e => e.Kind == JournalKinds.ContactsPaused).ToListAsync(Ct));
        Assert.Equal((campaign, ActorType.System, "dispatcher", null), (held.CampaignId, held.ActorType, held.ActorId, held.Reason));
        Assert.Equal($$"""{"contact_ids":["{{person}}"],"line":"{{line}}"}""", held.New!.ToJsonString());
        Assert.Null(held.Key);
        Assert.Equal(await IdOfAsync(harness, line), await WatermarkAsync(harness, campaign));
    }

    /// <summary>
    /// A member whose line is held already, ended or removed is left as they are, and no line is written: a second reply of
    /// a held person holds nothing more, an ended line stays ended, and a removed person is no member to hold.
    /// </summary>
    [Theory]
    [InlineData(MembershipState.Paused)]
    [InlineData(MembershipState.Finished)]
    [InlineData(MembershipState.Excluded)]
    public async Task A_member_who_is_not_enrolled_is_left_as_they_are(MembershipState state)
    {
        using var harness = new DispatchHarness(Noon);
        var campaign = await CampaignAsync(harness);
        RoutedTo(harness, Reader);
        var (row, person) = await MemberAsync(harness, campaign, state);
        await LineAsync(harness, campaign, "th-1", Noon.AddMinutes(-5), contact: person, contactRow: row);

        await harness.RespondAsync(Ct);

        await using var db = harness.Open();
        Assert.Equal(state, await StateAsync(db, campaign, row));
        Assert.False(await db.Journal.AnyAsync(e => e.Kind == JournalKinds.ContactsPaused, Ct));
    }

    /// <summary>
    /// A line that names nobody — the thread's person is pinned to no contact, or to more than one — holds nobody; nor does
    /// one naming a person who is no member of the campaign, or who is a member of another campaign only, which keeps its
    /// own line as it was.
    /// </summary>
    [Theory]
    [InlineData("names nobody")]
    [InlineData("names someone the campaign does not hold")]
    public async Task A_line_that_names_no_member_holds_nobody(string what)
    {
        using var harness = new DispatchHarness(Noon);
        var campaign = await CampaignAsync(harness);
        var other = await CampaignAsync(harness);
        RoutedTo(harness, Reader);
        var (row, person) = await MemberAsync(harness, other, MembershipState.Enrolled);
        await LineAsync(
            harness,
            campaign,
            "th-1",
            Noon.AddMinutes(-5),
            contact: what == "names nobody" ? null : person,
            contactRow: what == "names nobody" ? null : row);

        Assert.Equal(1, await harness.RespondAsync(Ct));

        await using var db = harness.Open();
        Assert.Equal(MembershipState.Enrolled, await StateAsync(db, other, row));
        Assert.False(await db.Journal.AnyAsync(e => e.Kind == JournalKinds.ContactsPaused, Ct));
    }

    /// <summary>
    /// Two replies of one person in one scan hold them once: the second finds the line held already. A reply in another
    /// campaign the person is in holds them there, and only there.
    /// </summary>
    [Fact]
    public async Task Two_replies_hold_once_and_each_campaign_holds_its_own_line()
    {
        using var harness = new DispatchHarness(Noon);
        var campaign = await CampaignAsync(harness);
        var other = await CampaignAsync(harness);
        RoutedTo(harness, Reader);
        var (row, person) = await MemberAsync(harness, campaign, MembershipState.Enrolled);
        await using (var db = harness.Open())
        {
            db.CampaignContacts.Add(new CampaignContact { CampaignId = other, ContactId = row, State = MembershipState.Enrolled, AddedAt = Noon, UpdatedAt = Noon });
            await db.SaveChangesAsync(Ct);
        }

        await LineAsync(harness, campaign, "th-1", Noon.AddMinutes(-9), contact: person, contactRow: row);
        await LineAsync(harness, campaign, "th-2", Noon.AddMinutes(-5), contact: person, contactRow: row);
        await harness.RespondAsync(Ct);

        await using (var db = harness.Open())
        {
            Assert.Equal(MembershipState.Paused, await StateAsync(db, campaign, row));
            Assert.Equal(MembershipState.Enrolled, await StateAsync(db, other, row));
            Assert.Equal(1, await db.Journal.CountAsync(e => e.Kind == JournalKinds.ContactsPaused, Ct));
        }

        await LineAsync(harness, other, "th-3", Noon.AddMinutes(-2), contact: person, contactRow: row);
        await harness.RespondAsync(Ct);

        await using var after = harness.Open();
        Assert.Equal(MembershipState.Paused, await StateAsync(after, other, row));
        Assert.Equal([campaign, other], await after.Journal.Where(e => e.Kind == JournalKinds.ContactsPaused).OrderBy(e => e.Id).Select(e => e.CampaignId!.Value).ToListAsync(Ct));
    }

    /// <summary>A person, reachable by email, who is a member of the campaign in <paramref name="state"/>: their row and public id.</summary>
    private static async Task<(int Row, string PublicId)> MemberAsync(DispatchHarness harness, int campaignId, MembershipState state)
    {
        await using var db = harness.Open();
        var contact = new Contact { PublicId = PublicId.New("cnt"), FirstName = "Ines", CreatedAt = Noon.AddDays(-2), UpdatedAt = Noon.AddDays(-2) };
        contact.Channels.Add(new ContactChannel { Channel = "email", Value = $"{contact.PublicId[^6..].ToLowerInvariant()}@example.test", IsPrimary = true });
        db.Contacts.Add(contact);
        await db.SaveChangesAsync(Ct);
        db.CampaignContacts.Add(new CampaignContact { CampaignId = campaignId, ContactId = contact.Id, State = state, AddedAt = Noon.AddDays(-2), UpdatedAt = Noon.AddDays(-2) });
        await db.SaveChangesAsync(Ct);
        return (contact.Id, contact.PublicId);
    }

    private static Task<MembershipState> StateAsync(JasonDbContext db, int campaignId, int contactRow) =>
        db.CampaignContacts.AsNoTracking().Where(m => m.CampaignId == campaignId && m.ContactId == contactRow).Select(m => m.State).SingleAsync(Ct);
}
