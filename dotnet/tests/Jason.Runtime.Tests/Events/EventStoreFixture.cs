using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Ids;
using Jason.Runtime.Events;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Events;

/// <summary>
/// One temporary database with the events store over it, on a held clock: sources, campaigns and contacts with the pins
/// a provider's events are found by, and the events a page holds, each in one line.
/// </summary>
internal sealed class EventStoreFixture : IDisposable
{
    public const string Reader = "reader";

    public static readonly DateTime Noon = new(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);

    private readonly TestDatabase _database = new();

    public EventStoreFixture(IEventAttribution? attribution = null)
    {
        Clock = new FixedClock(Noon);
        Store = new EventStore(new JournalWriter(Clock), Clock, attribution ?? EventAttributionSequence.OfThisVersion());
    }

    public FixedClock Clock { get; }

    public EventStore Store { get; }

    public string File => _database.File;

    public JasonDbContext Open() => _database.Open();

    /// <summary>A context over the same database with interceptors of the test's beside the runtime's own.</summary>
    public JasonDbContext Open(params Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor[] interceptors)
    {
        var builder = new DbContextOptionsBuilder<JasonDbContext>();
        JasonDbContext.Configure(builder, _database.File, JournalLines.Strict);
        builder.AddInterceptors(interceptors);
        return new JasonDbContext(builder.Options);
    }

    /// <summary>An account events are read from, with the cursor it holds.</summary>
    public async Task<EventSource> SourceAsync(string plugin = Reader, string binding = EventSource.NoBinding, string? cursor = null)
    {
        await using var db = Open();
        var source = new EventSource { PublicId = PublicId.New("evs"), PluginId = plugin, BindingIdentity = binding, Cursor = cursor, CreatedAt = Noon };
        db.EventSources.Add(source);
        await db.SaveChangesAsync();
        return source;
    }

    /// <summary>A campaign, live from <paramref name="eventsFrom"/>, holding each pin given as (plugin, value).</summary>
    public async Task<Campaign> CampaignAsync(
        CampaignStatus status = CampaignStatus.Active,
        DateTime? eventsFrom = null,
        bool live = true,
        params (string Plugin, string Value)[] pins)
    {
        await using var db = Open();
        var campaign = WorkItemFactory.NewCampaign(status: status, now: Noon.AddDays(-2));
        campaign.EventsFrom = live ? eventsFrom ?? Noon.AddDays(-1) : null;
        if (status == CampaignStatus.Archived)
        {
            campaign.ArchivedAt = Noon.AddHours(-1);
        }

        db.Campaigns.Add(campaign);
        foreach (var (plugin, value) in pins)
        {
            db.ExternalIds.Add(new ExternalId
            {
                Campaign = campaign,
                PluginId = plugin,
                Kind = "campaign",
                Value = value,
                RecordedAt = Noon.AddDays(-2),
                RecordedByAttemptId = "att_01JB6K8TQ2W9V4MZ0C3Y7H5NRD",
            });
        }

        await db.SaveChangesAsync();
        return campaign;
    }

    /// <summary>A contact pinned at a plugin with the identifier the provider knows them by.</summary>
    public async Task<Contact> ContactAsync(string value, string plugin = Reader, Campaign? member = null)
    {
        await using var db = Open();
        var contact = new Contact { PublicId = PublicId.New("cnt"), FirstName = "Marta", CreatedAt = Noon, UpdatedAt = Noon };
        db.Contacts.Add(contact);
        db.ExternalIds.Add(new ExternalId
        {
            Contact = contact,
            PluginId = plugin,
            Kind = "contact",
            Value = value,
            RecordedAt = Noon.AddDays(-2),
            RecordedByAttemptId = "att_01JB6K8TQ2W9V4MZ0C3Y7H5NRD",
        });
        if (member is not null)
        {
            db.CampaignContacts.Add(new CampaignContact { CampaignId = member.Id, Contact = contact, AddedAt = Noon, UpdatedAt = Noon });
        }

        await db.SaveChangesAsync();
        return contact;
    }

    /// <summary>The contact made a member of the campaign, in the state given, since <paramref name="addedAt"/> — the day before noon unless said.</summary>
    public async Task MemberAsync(Contact contact, Campaign campaign, MembershipState state = MembershipState.Enrolled, DateTime? addedAt = null)
    {
        await using var db = Open();
        var since = addedAt ?? Noon.AddDays(-1);
        db.CampaignContacts.Add(new CampaignContact { CampaignId = campaign.Id, ContactId = contact.Id, State = state, AddedAt = since, UpdatedAt = since });
        await db.SaveChangesAsync();
    }

    /// <summary>Stores a page through a context of its own, as the reader will.</summary>
    public async Task<EventStoreResult> StoreAsync(EventSource source, string? heldCursor, EventPage page, string readId = "evr_01JB6K8TQ2W9V4MZ0C3Y7H5NRD")
    {
        await using var db = Open();
        return await Store.StoreAsync(db, source, heldCursor, page, readId, TestContext.Current.CancellationToken);
    }

    /// <summary>Stores a page under a fence the test names — a backfill's among them — through a context of its own.</summary>
    public async Task<EventStoreResult> StoreAsync(EventSource source, EventFence fence, EventPage page, string readId = "evr_01JB6K8TQ2W9V4MZ0C3Y7H5NRD")
    {
        await using var db = Open();
        return await Store.StoreAsync(db, source, fence, page, readId, TestContext.Current.CancellationToken);
    }

    /// <summary>A span of the source asked for again, waiting, with the cursor its own reading holds.</summary>
    public async Task<EventBackfill> BackfillAsync(EventSource source, string? cursor = null, EventBackfillStatus status = EventBackfillStatus.Pending)
    {
        await using var db = Open();
        var backfill = new EventBackfill
        {
            PublicId = PublicId.New("evb"),
            SourceId = source.Id,
            From = Noon.AddDays(-60),
            To = Noon.AddDays(-30),
            Status = status,
            Cursor = cursor,
            RequestedAt = Noon.AddHours(-1),
            ActorType = ActorType.Human,
            ActorId = "marta",
        };
        db.EventBackfills.Add(backfill);
        await db.SaveChangesAsync();
        return backfill;
    }

    /// <summary>Stores a page that skips a gap, through a context of its own, as the reader will.</summary>
    public async Task<EventStoreResult> StoreAsync(EventSource source, string? heldCursor, EventPage page, EventGap gap, string readId = "evr_01JB6K8TQ2W9V4MZ0C3Y7H5NRD")
    {
        await using var db = Open();
        return await Store.StoreAsync(db, source, heldCursor, page, readId, gap, TestContext.Current.CancellationToken);
    }

    public static EventPage Page(string cursor, params JsonObject[] events) =>
        EventPage.Read(new JsonObject { ["events"] = new JsonArray([.. events]), ["cursor"] = cursor, ["has_more"] = false })
            ?? throw new InvalidOperationException("The test built something that is not a page.");

    public static JsonObject Moved(string thread = "t_501", string? campaign = "p_1", string? contact = "c_1001", string? by = "other_side", DateTime? at = null) => new()
    {
        ["kind"] = "thread_moved",
        ["id"] = null,
        ["occurred_at"] = Stamp(at ?? Noon.AddHours(-3)),
        ["thread"] = thread,
        ["contact"] = contact,
        ["campaign"] = campaign,
        ["moved_by"] = by,
        ["channel"] = "email",
        ["unread"] = true,
        ["has_meeting_intent"] = null,
    };

    public static JsonObject Detected(string? campaign = "p_1", string contact = "c_1001", DateTime? at = null) => new()
    {
        ["kind"] = "reply_detected",
        ["id"] = "e_77",
        ["occurred_at"] = Stamp(at ?? Noon.AddHours(-3)),
        ["contact"] = contact,
        ["campaign"] = campaign,
        ["thread"] = "t_501",
        ["channel"] = "email",
    };

    public static JsonObject Accepted(string? campaign = "p_1", string contact = "c_1001", DateTime? at = null) => new()
    {
        ["kind"] = "invitation_accepted",
        ["id"] = null,
        ["occurred_at"] = Stamp(at ?? Noon.AddHours(-3)),
        ["contact"] = contact,
        ["campaign"] = campaign,
        ["account"] = "li_9",
    };

    public static JsonObject Bounced(string? campaign = "p_1", string contact = "c_1001", DateTime? at = null) => new()
    {
        ["kind"] = "bounce",
        ["id"] = null,
        ["occurred_at"] = Stamp(at ?? Noon.AddHours(-3)),
        ["contact"] = contact,
        ["campaign"] = campaign,
        ["message"] = "m_88",
        ["bounce"] = "hard",
    };

    public static string Stamp(DateTime moment) => moment.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The lines of one kind a campaign holds, oldest first.</summary>
    public async Task<List<JournalEntry>> LinesAsync(Campaign campaign, string kind)
    {
        await using var db = Open();
        return await db.Journal.AsNoTracking().Where(entry => entry.CampaignId == campaign.Id && entry.Kind == kind).OrderBy(entry => entry.Id).ToListAsync();
    }

    /// <summary>Every line of the two kinds the store writes, in every campaign.</summary>
    public async Task<List<JournalEntry>> AllLinesAsync()
    {
        await using var db = Open();
        return await db.Journal.AsNoTracking()
            .Where(entry => entry.Kind == JournalKinds.InboundReplyObserved || entry.Kind == JournalKinds.ProviderEventObserved)
            .OrderBy(entry => entry.Id)
            .ToListAsync();
    }

    public void Dispose() => _database.Dispose();
}
