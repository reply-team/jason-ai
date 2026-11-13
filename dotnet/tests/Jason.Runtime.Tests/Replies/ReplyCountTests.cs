using System.Net;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Replies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jason.Runtime.Tests.Replies;

/// <summary>
/// <c>reply.count</c>: the replies roles have judged, counted by the category word each verdict that stands names, per
/// campaign and in total, over a window held to each reply's own moment. The runtime reads nothing into a category but
/// that it is one, so every word the verdicts use is counted, and no word is known in advance.
/// </summary>
public class ReplyCountTests
{
    private static readonly DateTime Noon = new(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);

    private static readonly DateTimeOffset DayStart = new(2026, 9, 14, 0, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset DayEnd = DayStart.AddDays(1);

    private static readonly ActorRef Ada = new(ActorType.Human, "ada");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Every_category_word_the_verdicts_use_is_counted_per_campaign_and_in_total()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var spring = await CampaignAsync(db, "Spring");
        var summer = await CampaignAsync(db, "Summer");
        await JudgeAsync(db, spring, "thr-1", "msg-1", At(9), "interested");
        await JudgeAsync(db, spring, "thr-2", "msg-2", At(10), "interested");
        await JudgeAsync(db, spring, "thr-3", "msg-3", At(11), "wants a call next quarter");
        await JudgeAsync(db, summer, "thr-4", "msg-4", At(9), "not_now");
        await JudgeAsync(db, summer, "thr-5", "msg-5", At(10), "interested");

        var counts = await Counts(db).CountAsync(new ReplyCountRequest([spring.PublicId, summer.PublicId], DayStart, DayEnd), Ct);

        Assert.Equal([spring.PublicId, summer.PublicId], counts.Campaigns.Select(row => row.CampaignId));
        Assert.Equal((3, 3), (counts.Campaigns[0].Replies, counts.Campaigns[0].Threads));
        Assert.Equal(
            [new("interested", 2, 2), new("wants a call next quarter", 1, 1)],
            counts.Campaigns[0].Categories);
        Assert.Equal([new("interested", 1, 1), new("not_now", 1, 1)], counts.Campaigns[1].Categories);
        Assert.Equal((5, 5), (counts.Total.Replies, counts.Total.Threads));
        Assert.Equal(
            [new("interested", 3, 3), new("not_now", 1, 1), new("wants a call next quarter", 1, 1)],
            counts.Total.Categories);
    }

    /// <summary>A reply judged again is counted once, under the word of the verdict that stands: the latest.</summary>
    [Fact]
    public async Task The_latest_verdict_on_a_reply_is_the_one_counted()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var spring = await CampaignAsync(db, "Spring");
        await JudgeAsync(db, spring, "thr-1", "msg-1", At(9), "interested");
        await JudgeAsync(db, spring, "thr-1", "msg-1", At(9), "not_now");

        var row = Assert.Single((await Counts(db).CountAsync(new ReplyCountRequest([spring.PublicId], DayStart, DayEnd), Ct)).Campaigns);

        Assert.Equal((1, 1), (row.Replies, row.Threads));
        Assert.Equal([new("not_now", 1, 1)], row.Categories);
    }

    /// <summary>Two replies in one conversation are two replies and one thread: a thread is a conversation, not a message.</summary>
    [Fact]
    public async Task A_thread_with_two_interested_replies_is_two_replies_and_one_thread()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var spring = await CampaignAsync(db, "Spring");
        await JudgeAsync(db, spring, "thr-1", "msg-1", At(9), "interested");
        await JudgeAsync(db, spring, "thr-1", "msg-2", At(10), "interested");

        var counts = await Counts(db).CountAsync(new ReplyCountRequest([spring.PublicId], DayStart, DayEnd), Ct);

        var row = Assert.Single(counts.Campaigns);
        Assert.Equal((2, 1), (row.Replies, row.Threads));
        Assert.Equal([new("interested", 2, 1)], row.Categories);
        Assert.Equal((2, 1), (counts.Total.Replies, counts.Total.Threads));
        Assert.Equal([new("interested", 2, 1)], counts.Total.Categories);
    }

    /// <summary>
    /// One conversation that holds replies of two categories is a thread of each, and one thread of the campaign: the
    /// categories' threads add up to more than the campaign's.
    /// </summary>
    [Fact]
    public async Task A_thread_under_two_categories_is_one_thread_of_the_campaign()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var spring = await CampaignAsync(db, "Spring");
        await JudgeAsync(db, spring, "thr-1", "msg-1", At(9), "not_now");
        await JudgeAsync(db, spring, "thr-1", "msg-2", At(10), "interested");

        var counts = await Counts(db).CountAsync(new ReplyCountRequest([spring.PublicId], DayStart, DayEnd), Ct);

        var row = Assert.Single(counts.Campaigns);
        Assert.Equal((2, 1), (row.Replies, row.Threads));
        Assert.Equal([new("interested", 1, 1), new("not_now", 1, 1)], row.Categories);
        Assert.Equal(2, row.Categories.Sum(category => category.Threads));
        Assert.Equal((2, 1), (counts.Total.Replies, counts.Total.Threads));
    }

    /// <summary>
    /// Two threads of the same identifiers under two plugins are two conversations, and a thread is never read as the
    /// plugin's name followed by the thread's.
    /// </summary>
    [Fact]
    public async Task A_thread_is_known_by_its_plugin_and_its_identifier_together()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var spring = await CampaignAsync(db, "Spring");
        await JudgeAsync(db, spring, "thr-1", "msg-1", At(9), "interested", plugin: "mailbox");
        await JudgeAsync(db, spring, "thr-1", "msg-1", At(9), "interested", plugin: "chat");
        await JudgeAsync(db, spring, "xthr-1", "msg-2", At(9), "interested", plugin: "mailbo");

        var row = Assert.Single((await Counts(db).CountAsync(new ReplyCountRequest([spring.PublicId], DayStart, DayEnd), Ct)).Campaigns);

        Assert.Equal((3, 3), (row.Replies, row.Threads));
    }

    /// <summary>
    /// The window is held to the reply's own moment, <c>since</c> included and <c>until</c> not, to the millisecond, so two
    /// windows that meet count each reply once between them, and one may be subtracted from the other.
    /// </summary>
    [Fact]
    public async Task The_window_holds_each_reply_s_moment_since_included_until_not_to_the_millisecond()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var spring = await CampaignAsync(db, "Spring");
        var ten = DayStart.AddHours(10);
        var eleven = DayStart.AddHours(11);
        await JudgeAsync(db, spring, "thr-1", "msg-1", ten.AddMilliseconds(-1), "interested");
        await JudgeAsync(db, spring, "thr-2", "msg-2", ten, "interested");
        await JudgeAsync(db, spring, "thr-3", "msg-3", eleven.AddMilliseconds(-1), "interested");
        await JudgeAsync(db, spring, "thr-4", "msg-4", eleven, "interested");

        async Task<int> InAsync(DateTimeOffset since, DateTimeOffset until) =>
            Assert.Single((await Counts(db).CountAsync(new ReplyCountRequest([spring.PublicId], since, until), Ct)).Campaigns).Replies;

        Assert.Equal(2, await InAsync(ten, eleven));
        Assert.Equal(1, await InAsync(ten.AddHours(-1), ten));
        Assert.Equal(1, await InAsync(eleven, eleven.AddHours(1)));
        Assert.Equal(4, await InAsync(ten.AddHours(-1), eleven.AddHours(1)));
        Assert.Equal(0, await InAsync(ten, ten));
        Assert.Equal(1, await InAsync(ten.AddSeconds(-1), ten.AddSeconds(1)) - await InAsync(ten.AddSeconds(-1), ten));
    }

    /// <summary>A verdict recorded days after the reply counts in the window of the reply's moment, never in the window it was recorded in.</summary>
    [Fact]
    public async Task A_verdict_recorded_late_counts_in_the_window_of_its_reply()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var spring = await CampaignAsync(db, "Spring");
        var replied = DayStart.AddDays(-3).AddHours(9);
        await JudgeAsync(db, spring, "thr-1", "msg-1", replied, "interested");

        var then = await Counts(db).CountAsync(new ReplyCountRequest([spring.PublicId], replied.AddHours(-1), replied.AddHours(1)), Ct);
        var recorded = await Counts(db).CountAsync(new ReplyCountRequest([spring.PublicId], DayStart, DayEnd), Ct);

        Assert.Equal(1, Assert.Single(then.Campaigns).Replies);
        Assert.Equal(0, Assert.Single(recorded.Campaigns).Replies);
    }

    /// <summary>
    /// A verdict line that names no category, whether the field is missing or null, is a reply all the same: counted in
    /// <c>replies</c> and listed as one entry whose category is null, so the categories always add up to the replies.
    /// </summary>
    [Fact]
    public async Task A_verdict_without_a_category_is_counted_under_one_null_entry()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var spring = await CampaignAsync(db, "Spring");
        await JudgeAsync(db, spring, "thr-1", "msg-1", At(9), "interested");
        await UncategorisedAsync(database, spring, "thr-2", "msg-2", At(10), withField: false);
        await UncategorisedAsync(database, spring, "thr-3", "msg-3", At(11), withField: true);

        var counts = await Counts(db).CountAsync(new ReplyCountRequest([spring.PublicId], DayStart, DayEnd), Ct);

        var row = Assert.Single(counts.Campaigns);
        Assert.Equal((3, 3), (row.Replies, row.Threads));
        Assert.Equal([new(null, 2, 2), new("interested", 1, 1)], row.Categories);
        Assert.Equal(row.Replies, row.Categories.Sum(category => category.Replies));
        Assert.Equal([new(null, 2, 2), new("interested", 1, 1)], counts.Total.Categories);
        Assert.Equal(counts.Total.Replies, counts.Total.Categories.Sum(category => category.Replies));
    }

    /// <summary>The categories are listed by their replies, most first, then by the word, ordinal, and a null category after every word it ties with.</summary>
    [Fact]
    public async Task Categories_are_listed_by_replies_then_by_word_with_no_category_after_its_ties()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var spring = await CampaignAsync(db, "Spring");
        await JudgeAsync(db, spring, "thr-1", "msg-1", At(9), "b");
        await JudgeAsync(db, spring, "thr-2", "msg-2", At(9), "a");
        await JudgeAsync(db, spring, "thr-3", "msg-3", At(9), "B");
        await JudgeAsync(db, spring, "thr-4", "msg-4", At(9), "c");
        await JudgeAsync(db, spring, "thr-5", "msg-5", At(9), "c");
        await UncategorisedAsync(database, spring, "thr-6", "msg-6", At(9), withField: true);

        var row = Assert.Single((await Counts(db).CountAsync(new ReplyCountRequest([spring.PublicId], DayStart, DayEnd), Ct)).Campaigns);

        Assert.Equal(["c", "B", "a", "b", null], row.Categories.Select(category => category.Category));
    }

    /// <summary>
    /// With no campaign named, the campaigns campaign.list shows are read, archived ones left out, in its order; an
    /// archived campaign named is counted like any other, and one with no judged reply in the window answers zeros.
    /// </summary>
    [Fact]
    public async Task Unnamed_means_the_campaigns_not_archived_and_a_named_archived_one_is_counted()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var spring = await CampaignAsync(db, "Spring");
        var quiet = await CampaignAsync(db, "Quiet");
        var autumn = await CampaignAsync(db, "Autumn", CampaignStatus.Archived);
        await JudgeAsync(db, spring, "thr-1", "msg-1", At(9), "interested");
        await JudgeAsync(db, autumn, "thr-2", "msg-2", At(9), "not_now");

        var unnamed = await Counts(db).CountAsync(new ReplyCountRequest(null, DayStart, DayEnd), Ct);
        var named = await Counts(db).CountAsync(new ReplyCountRequest([autumn.PublicId], DayStart, DayEnd), Ct);

        Assert.Equal(
            new[] { spring.PublicId, quiet.PublicId }.Order(StringComparer.Ordinal),
            unnamed.Campaigns.Select(row => row.CampaignId));
        var silent = unnamed.Campaigns.Single(row => row.CampaignId == quiet.PublicId);
        Assert.Equal((0, 0), (silent.Replies, silent.Threads));
        Assert.Empty(silent.Categories);
        Assert.Equal([new("interested", 1, 1)], unnamed.Total.Categories);
        Assert.Equal([new("not_now", 1, 1)], Assert.Single(named.Campaigns).Categories);
    }

    /// <summary>A reply's verdict is counted however its person stands in the campaign now: one removed since still replied.</summary>
    [Fact]
    public async Task A_reply_of_a_person_removed_since_is_counted()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var spring = await CampaignAsync(db, "Spring");
        var contact = new Contact { PublicId = "cnt_01K52JR0000000000000000001", FirstName = "Ola", CreatedAt = Noon, UpdatedAt = Noon };
        db.Contacts.Add(contact);
        var membership = new CampaignContact { Campaign = spring, Contact = contact, AddedAt = Noon, UpdatedAt = Noon };
        db.CampaignContacts.Add(membership);
        await db.SaveChangesAsync(Ct);
        await JudgeAsync(db, spring, "thr-1", "msg-1", At(9), "interested", contact: contact.PublicId);
        membership.State = MembershipState.Excluded;
        await db.SaveChangesAsync(Ct);

        var row = Assert.Single((await Counts(db).CountAsync(new ReplyCountRequest([spring.PublicId], DayStart, DayEnd), Ct)).Campaigns);

        Assert.Equal([new("interested", 1, 1)], row.Categories);
    }

    public static TheoryData<string, string, string> Refusals() => new()
    {
        { "since missing", "since", "required" },
        { "until missing", "until", "required" },
        { "until before since", "until", "invalid" },
        { "no campaign named in the list", "campaign_ids", "invalid" },
    };

    [Theory]
    [MemberData(nameof(Refusals))]
    public async Task A_request_outside_the_rules_is_refused_naming_its_field(string refusal, string field, string code)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var request = refusal switch
        {
            "since missing" => new ReplyCountRequest(null, null, DayEnd),
            "until missing" => new ReplyCountRequest(null, DayStart, null),
            "until before since" => new ReplyCountRequest(null, DayEnd, DayStart),
            _ => new ReplyCountRequest([], DayStart, DayEnd),
        };

        var refused = await Assert.ThrowsAsync<ValidationException>(() => Counts(db).CountAsync(request, Ct));

        Assert.Contains(refused.Details!, detail => detail.Field == field && detail.Code == code);
    }

    [Fact]
    public async Task A_campaign_that_does_not_exist_is_not_found()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();

        var refused = await Assert.ThrowsAsync<NotFoundException>(
            () => Counts(db).CountAsync(new ReplyCountRequest(["cmp_01K52JR0000000000000000009"], DayStart, DayEnd), Ct));

        Assert.Equal("campaign_not_found", refused.Code);
    }

    /// <summary>
    /// Over the wire, and the exchanges a consumer renders: every campaign not archived, with a person removed since whose
    /// reply still counts; an archived campaign named; and a window that ends before it starts.
    /// </summary>
    [Fact]
    public async Task The_exchanges_are_recorded()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct, clock: new FixedClock(new DateTimeOffset(Noon)));
        var spring = await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { name = "Spring outreach" }, Ct);
        var summer = await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { name = "Summer partners" }, Ct);
        var autumn = await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { name = "Autumn webinar" }, Ct);
        var ola = await api.PostOkAsync<ContactDto>(Operations.ContactCreate, new { first_name = "Ola" }, Ct);
        await api.PostOkAsync<JsonObject>(Operations.CampaignAddContacts, new { campaign_id = spring.Id, contacts = new[] { new { contact_id = ola.Id } } }, Ct);

        async Task JudgeAsync(string campaign, string thread, string message, string at, string category) =>
            await api.PostOkAsync<JournalEntryDto>(
                Operations.ReplyRecord,
                new
                {
                    campaign_id = campaign,
                    plugin_id = "mailbox",
                    thread_id = thread,
                    message_id = message,
                    message_at = at,
                    category,
                    confidence = 0.9,
                    reaction = "draft",
                    actor = new { type = "role", id = "responder" },
                },
                Ct);

        await JudgeAsync(spring.Id, "thr-1", "msg-1", "2026-09-14T08:15:00.000Z", "interested");
        await JudgeAsync(spring.Id, "thr-1", "msg-2", "2026-09-14T09:40:00.000Z", "interested");
        await JudgeAsync(spring.Id, "thr-2", "msg-3", "2026-09-14T10:05:00.000Z", "not_now");
        await JudgeAsync(spring.Id, "thr-2", "msg-3", "2026-09-14T10:05:00.000Z", "wrong_person");
        await JudgeAsync(spring.Id, "thr-3", "msg-4", "2026-09-13T22:00:00.000Z", "interested");
        await JudgeAsync(autumn.Id, "thr-9", "msg-9", "2026-09-14T07:30:00.000Z", "unsubscribe");
        await api.PostOkAsync<JsonObject>(Operations.CampaignRemoveContacts, new { campaign_id = spring.Id, contact_ids = new[] { ola.Id } }, Ct);
        await api.PostOkAsync<JsonObject>(Operations.CampaignArchive, new { campaign_id = autumn.Id }, Ct);

        var every = await api.RecordAsync<ReplyCountResponse>(
            Operations.ReplyCount, "every-campaign", new { since = "2026-09-14T00:00:00Z", until = "2026-09-14T12:00:00Z" }, Ct);
        var archived = await api.RecordAsync<ReplyCountResponse>(
            Operations.ReplyCount, "archived-named", new { campaign_ids = new[] { autumn.Id }, since = "2026-09-14T00:00:00Z", until = "2026-09-14T12:00:00Z" }, Ct);
        var backwards = await api.RecordErrorAsync(
            Operations.ReplyCount, "until-before-since", new { since = "2026-09-14T12:00:00Z", until = "2026-09-14T00:00:00Z" }, HttpStatusCode.BadRequest, Ct);

        Assert.Equal(new[] { spring.Id, summer.Id }.Order(StringComparer.Ordinal), every.Campaigns.Select(row => row.CampaignId));
        var springRow = every.Campaigns.Single(row => row.CampaignId == spring.Id);
        Assert.Equal((3, 2), (springRow.Replies, springRow.Threads));
        Assert.Equal([new("interested", 2, 1), new("wrong_person", 1, 1)], springRow.Categories);
        Assert.Equal((3, 2), (every.Total.Replies, every.Total.Threads));
        Assert.Equal([new("unsubscribe", 1, 1)], Assert.Single(archived.Campaigns).Categories);
        Assert.Equal(["until/invalid"], backwards.Details!.Select(detail => $"{detail.Field}/{detail.Code}"));
    }

    private static DateTimeOffset At(int hour) => DayStart.AddHours(hour);

    private static ReplyCounts Counts(JasonDbContext db) => new(db);

    private static async Task<Campaign> CampaignAsync(JasonDbContext db, string name, CampaignStatus status = CampaignStatus.Active)
    {
        var campaign = WorkItemFactory.NewCampaign(name, status, Noon);
        db.Campaigns.Add(campaign);
        await db.SaveChangesAsync(Ct);
        return campaign;
    }

    private static Task<JournalEntryDto> JudgeAsync(
        JasonDbContext db, Campaign campaign, string thread, string message, DateTimeOffset at, string category, string plugin = "mailbox", string? contact = null) =>
        new ReplyService(db, new JournalWriter(new FixedClock(new DateTimeOffset(Noon))))
            .RecordAsync(new ReplyRecordRequest(campaign.PublicId, plugin, thread, message, at, category, 0.8, ReplyReaction.None, ContactId: contact, Actor: Ada), Ct);

    /// <summary>
    /// A verdict line naming no category, which <c>reply.record</c> never writes and its document does not allow: written as
    /// a running installation keeps a line off its document, logged rather than refused, the field left out or null, to
    /// hold the count to what it does with such a line.
    /// </summary>
    private static async Task UncategorisedAsync(TestDatabase database, Campaign campaign, string thread, string message, DateTimeOffset at, bool withField)
    {
        var options = new DbContextOptionsBuilder<JasonDbContext>();
        JasonDbContext.Configure(options, database.File, JournalLines.Logged(NullLogger.Instance));
        await using var db = new JasonDbContext(options.Options);
        var line = new JsonObject
        {
            ["plugin"] = "mailbox",
            ["thread"] = thread,
            ["message_id"] = message,
            ["message_at"] = InboundSync.Instant(at),
            ["contact_id"] = null,
            ["confidence"] = 0.5,
            ["reaction"] = "none",
            ["goal_id"] = null,
            ["goal_kind"] = null,
            ["decision_id"] = null,
            ["answered_by"] = null,
        };
        if (withField)
        {
            line["category"] = null;
        }

        new JournalWriter(new FixedClock(new DateTimeOffset(Noon))).Append(
            db, Ada, JournalKinds.ReplyClassified, null, key: InboundSync.VerdictKey("mailbox", thread, message), updated: line, campaignId: campaign.Id);
        await db.SaveChangesAsync(Ct);
    }
}
