using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Ids;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Events;

/// <summary>What storing one page did: the events it added, those the store already held, the lines it said, and whether another writer had moved the cursor first.</summary>
public sealed record EventStoreResult(int Stored, int Duplicates, int Said, bool Moved);

/// <summary>
/// Whose cursor a page moves, and from where — the fencing token of a read. The account's own reading moves the source's
/// cursor, and may say a gap with its page; a span asked for again moves the span's own cursor and counts on the span,
/// only while the span waits, and never touches the source's. One or the other: a page never moves both.
/// </summary>
public readonly record struct EventFence
{
    private EventFence(int? backfillId, string? held, EventGap? gap)
    {
        BackfillId = backfillId;
        Held = held;
        Gap = gap;
    }

    /// <summary>The span whose cursor the page moves; null for the account's own reading.</summary>
    public int? BackfillId { get; }

    /// <summary>The cursor the read found: the page's cursor replaces it only if it still stands.</summary>
    public string? Held { get; }

    /// <summary>A span nobody will read, said with this page: the account's own reading's alone.</summary>
    public EventGap? Gap { get; }

    /// <summary>The account's own reading, from the cursor the source held when the read was handed out.</summary>
    public static EventFence OfSource(string? held, EventGap? gap = null) => new(null, held, gap);

    /// <summary>A span asked for again, from the cursor its own reading held.</summary>
    public static EventFence OfBackfill(int backfillId, string? held) => new(backfillId, held, null);
}

/// <summary>
/// Stores one page a read of events returned, whole or not at all: the events the store does not hold yet, which
/// campaigns each concerns, the lines that tell those campaigns, and the cursor the page returned — in one transaction,
/// so the cursor moves only with the page it ends and a page that does not land leaves the cursor where it was.
/// </summary>
/// <remarks>
/// <para>
/// Nothing here reads meaning. An event's kind, identifiers, moment and the provider's own flags decide everything: which
/// campaigns hold its identifiers pinned, and which line, if any, says it. The page was held to the events contract's
/// output schema before it got here, so there is nothing else in it to read.
/// </para>
/// <para>
/// Every event is kept, whether or not anything it names is pinned: an event that reaches no campaign by either rule
/// concerns nobody, and stays. An event read twice — a page read again after a crash, a range a person asked for again
/// — is stored once: the keys the store already holds are read first, and the store's unique index refuses the rest. A
/// campaign says a line once under its key, the same way.
/// </para>
/// </remarks>
public sealed class EventStore(JournalWriter journal, TimeProvider clock, IEventAttribution attribution)
{
    /// <summary>The kind of pin that names a person at a plugin.</summary>
    private const string ContactPinKind = "contact";

    /// <summary>
    /// The fields each kind other than a moved thread carries into its line beside the ones every line has: the kind's
    /// members in the contract but its kind, ids, moment, the two identifiers every line resolves (campaign and contact)
    /// and the channel. A test holds the list to the contract's branches.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, string[]> Carried = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        [ReadEvent.ReplyDetected] = ["thread"],
        [ReadEvent.InvitationAccepted] = ["account"],
        [ReadEvent.Bounce] = ["message", "bounce"],
    };

    /// <param name="db">The context the page is written through.</param>
    /// <param name="source">The account the page was read from.</param>
    /// <param name="heldCursor">
    /// The cursor the source held when the read was handed out — the fencing token: the page's cursor replaces it only
    /// if it is still the one the source holds.
    /// </param>
    /// <param name="page">The page, as the events contract's output schema accepted it.</param>
    /// <param name="readId">The public id of the read, kept with each event it stores.</param>
    /// <param name="ct">Stops the store before it commits.</param>
    public Task<EventStoreResult> StoreAsync(
        JasonDbContext db,
        EventSource source,
        string? heldCursor,
        EventPage page,
        string readId,
        CancellationToken ct) => StoreAsync(db, source, heldCursor, page, readId, gap: null, ct);

    /// <summary>
    /// The same, for the page of a read that skips a span of the account's events nobody will read: the gap is said in
    /// this page's transaction, once under its key, so a page that does not land says nothing and a read tried again says
    /// nothing twice. An empty page that lands says it too: the cursor it returns starts every read after it.
    /// </summary>
    public Task<EventStoreResult> StoreAsync(
        JasonDbContext db,
        EventSource source,
        string? heldCursor,
        EventPage page,
        string readId,
        EventGap? gap,
        CancellationToken ct) => StoreAsync(db, source, EventFence.OfSource(heldCursor, gap), page, readId, ct);

    /// <summary>
    /// The same, under the fence the read holds: the account's own cursor, or a span's. A span's page moves the span's
    /// cursor and adds what it stored and found held to the span's counts, in this page's transaction, only while the span
    /// waits; the source's cursor, and when it was stored, stay as they were.
    /// </summary>
    public async Task<EventStoreResult> StoreAsync(
        JasonDbContext db,
        EventSource source,
        EventFence fence,
        EventPage page,
        string readId,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(page);
        ArgumentException.ThrowIfNullOrWhiteSpace(readId);

        var now = clock.GetUtcNow().UtcDateTime;
        await using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);

        // Each event once, in the page's order: a page that lists one event twice holds it once.
        var keyed = new List<(ReadEvent Read, string Key)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var read in page.Events)
        {
            var key = EventKeys.Store(source.PluginId, read);
            if (seen.Add(key))
            {
                keyed.Add((read, key));
            }
        }

        var held = await HeldAsync(db, [.. keyed.Select(entry => entry.Key)], ct).ConfigureAwait(false);
        var fresh = keyed.Where(entry => !held.Contains(entry.Key)).ToList();
        var pins = await PinsAsync(db, source.PluginId, [.. fresh.Select(entry => entry.Read)], ct).ConfigureAwait(false);

        // Which campaigns each event concerns, decided by the pins as they stand now and never again. A campaign two rules
        // both find is one campaign, told once, by the first rule that found it.
        var found = fresh
            .Select(entry => (entry.Read, entry.Key, Campaigns: Distinct(attribution.Attribute(entry.Read, pins))))
            .ToList();
        var campaignIds = found.SelectMany(entry => entry.Campaigns.Select(campaign => campaign.CampaignId)).Distinct().ToList();
        var campaigns = await db.Campaigns
            .Where(campaign => campaignIds.Contains(campaign.Id))
            .ToDictionaryAsync(campaign => campaign.Id, ct)
            .ConfigureAwait(false);
        var said = await SaidAsync(db, campaignIds, [.. found.Select(entry => EventKeys.Line(source.PluginId, entry.Read))], ct).ConfigureAwait(false);

        var rows = new List<(ReadEvent Read, List<Attributed> Campaigns, ProviderEvent Row, ContactPin? Contact)>();
        foreach (var (read, key, concerned) in found)
        {
            var contact = pins.ContactOf(read);
            var row = new ProviderEvent
            {
                PublicId = PublicId.New("evt"),
                SourceId = source.Id,
                PluginId = source.PluginId,
                Kind = read.Kind,
                OccurredAt = read.OccurredAt.UtcDateTime,
                StoredAt = now,
                Key = key,
                ContactId = contact?.ContactId,
                Data = (JsonObject)read.Data.DeepClone(),
                ReadId = readId,
            };
            db.Events.Add(row);
            rows.Add((read, concerned, row, contact));
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        var lines = 0;
        foreach (var (read, concerned, row, contact) in rows)
        {
            foreach (var attributed in concerned)
            {
                db.EventAttributions.Add(new EventAttribution { EventId = row.Id, CampaignId = attributed.CampaignId, Rule = attributed.Rule });

                // What happened before the campaign went live, or before the person it was found by joined it, is history:
                // kept, and found to concern it, and said by no line.
                var campaign = campaigns[attributed.CampaignId];
                if (campaign.EventsFrom is not { } from
                    || row.OccurredAt < from
                    || (attributed.SaidFrom is { } joined && row.OccurredAt < joined)
                    || Line(source.PluginId, read, row, contact, attributed.Rule) is not { } line)
                {
                    continue;
                }

                if (said.Add((campaign.Id, line.Kind, line.Key)))
                {
                    journal.Append(db, Actors.Dispatcher, line.Kind, campaign, line.Key, updated: line.New, contactId: contact?.ContactId);
                    lines++;
                }
            }
        }

        if (fence.Gap is { } gap)
        {
            var key = gap.Key(source.PublicId);
            var gapSaid = await db.Journal.AsNoTracking().AnyAsync(entry => entry.Kind == JournalKinds.EventsGapObserved && entry.Key == key, ct).ConfigureAwait(false);
            if (!gapSaid)
            {
                journal.Append(db, Actors.Dispatcher, JournalKinds.EventsGapObserved, campaign: null, key, updated: new JsonObject
                {
                    ["source"] = source.PublicId,
                    ["plugin"] = source.PluginId,
                    ["from"] = Instant(gap.From),
                    ["to"] = Instant(gap.To),
                });
            }
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        // The cursor moves with the page it ends, and only from where the read found it.
        var stored = fresh.Count;
        var duplicates = page.Events.Count - fresh.Count;
        var advanced = await AdvanceAsync(db, source.Id, fence, page.Cursor, stored, duplicates, now, ct).ConfigureAwait(false);
        if (advanced == 0)
        {
            await transaction.RollbackAsync(ct).ConfigureAwait(false);
            db.ChangeTracker.Clear();
            return new EventStoreResult(0, 0, 0, Moved: true);
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return new EventStoreResult(stored, duplicates, lines, Moved: false);
    }

    /// <summary>
    /// The one guarded update a page's cursor moves by: the source's, or the waiting span's — never both, and only from
    /// the cursor the fence holds. None moved is another writer having been first, and the page is rolled back.
    /// </summary>
    private static Task<int> AdvanceAsync(JasonDbContext db, int sourceId, EventFence fence, string cursor, int stored, int duplicates, DateTime now, CancellationToken ct)
    {
        var held = fence.Held;
        if (fence.BackfillId is { } backfillId)
        {
            return db.EventBackfills
                .Where(row => row.Id == backfillId && row.Cursor == held && row.Status == EventBackfillStatus.Pending)
                .ExecuteUpdateAsync(
                    set => set
                        .SetProperty(row => row.Cursor, cursor)
                        .SetProperty(row => row.Stored, row => row.Stored + stored)
                        .SetProperty(row => row.Duplicates, row => row.Duplicates + duplicates),
                    ct);
        }

        return db.EventSources
            .Where(row => row.Id == sourceId && row.Cursor == held)
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(row => row.Cursor, cursor)
                    .SetProperty(row => row.CursorAt, now),
                ct);
    }

    /// <summary>
    /// The line an event says in a campaign, or none. A thread the other side moved — or one the provider cannot say who
    /// moved — is said as the inbound read says a reply, with the same fields and under the same key; a thread the
    /// account or the provider moved is no reply and says nothing; every other kind is said as a provider event, with
    /// its kind. Either says which rule found the campaign, under <c>attributed_by</c>.
    /// </summary>
    private static (string Kind, string Key, JsonObject New)? Line(string pluginId, ReadEvent read, ProviderEvent row, ContactPin? contact, string rule)
    {
        if (read.Kind == ReadEvent.ThreadMoved)
        {
            return read.MovedByTheOtherSide
                ? (JournalKinds.InboundReplyObserved, EventKeys.Line(pluginId, read), new JsonObject
                {
                    ["plugin"] = pluginId,
                    ["thread"] = read.Text("thread"),
                    ["channel"] = read.Text("channel"),
                    ["last_activity_at"] = EventKeys.Moment(read),
                    ["unread"] = read.Data["unread"]?.DeepClone(),
                    ["has_meeting_intent"] = read.Data["has_meeting_intent"]?.DeepClone(),
                    ["contact_id"] = contact?.ContactPublicId,
                    ["event_id"] = row.PublicId,
                    ["attributed_by"] = rule,
                })
                : null;
        }

        var updated = new JsonObject
        {
            ["kind"] = read.Kind,
            ["plugin"] = pluginId,
            ["event_id"] = row.PublicId,
            ["occurred_at"] = EventKeys.Moment(read),
            ["channel"] = read.Text("channel"),
            ["contact_id"] = contact?.ContactPublicId,
            ["attributed_by"] = rule,
        };
        foreach (var name in Carried[read.Kind])
        {
            updated[name] = read.Data[name]?.DeepClone();
        }

        return (JournalKinds.ProviderEventObserved, EventKeys.Line(pluginId, read), updated);
    }

    private static string Instant(DateTime moment) => Dispatch.InboundSync.Instant(new DateTimeOffset(DateTime.SpecifyKind(moment, DateTimeKind.Utc)));

    /// <summary>Each campaign once, in the order the rules found them: the first rule that found a campaign is the one recorded.</summary>
    private static List<Attributed> Distinct(IReadOnlyList<Attributed> attributed) =>
        [.. attributed.DistinctBy(campaign => campaign.CampaignId)];

    /// <summary>Which of these keys the store already holds: one query for the page, on the store's own unique index.</summary>
    private static async Task<HashSet<string>> HeldAsync(JasonDbContext db, List<string> keys, CancellationToken ct)
    {
        if (keys.Count == 0)
        {
            return [];
        }

        var held = await db.Events
            .AsNoTracking()
            .Where(stored => keys.Contains(stored.Key))
            .Select(stored => stored.Key)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return new HashSet<string>(held, StringComparer.Ordinal);
    }

    /// <summary>
    /// The pins the page's events can be found by: the plugin's own, under the two kinds an event names, for the values the
    /// page holds, and the local memberships of the contacts those name — one contact a value, as an event names its
    /// person. A campaign that is archived is told nothing, and a value kept only as a divergence is not a pin; an
    /// archived contact is a member of nothing here and is still the person an event names. The members are read here, for
    /// every person the page names, before any rule runs, because the rules are pure over these facts and read nothing
    /// themselves.
    /// </summary>
    private static async Task<PinFacts> PinsAsync(JasonDbContext db, string pluginId, List<ReadEvent> reads, CancellationToken ct)
    {
        var campaignValues = reads.Select(read => read.Campaign).OfType<string>().Distinct(StringComparer.Ordinal).ToList();
        var contactValues = reads.Select(read => read.Contact).OfType<string>().Distinct(StringComparer.Ordinal).ToList();

        var campaigns = await CampaignPinRule.HoldersAsync(db, pluginId, campaignValues, ct).ConfigureAwait(false);
        List<ContactPin> contacts = contactValues.Count == 0
            ? []
            : await db.ExternalIds
                .AsNoTracking()
                .Where(pin => pin.PluginId == pluginId && pin.Kind == ContactPinKind && pin.ContactId != null && contactValues.Contains(pin.Value))
                .Select(pin => new ContactPin(pin.ContactId!.Value, pin.Contact!.PublicId, pin.Value))
                .ToListAsync(ct)
                .ConfigureAwait(false);

        var named = new PinFacts(campaigns, contacts, []);
        var people = reads.Select(named.ContactOf).OfType<ContactPin>().Select(pin => pin.ContactId).Distinct().ToList();
        List<ContactMembership> members = people.Count == 0
            ? []
            : await db.CampaignContacts
                .AsNoTracking()
                .Where(member => people.Contains(member.ContactId)
                    && member.State != MembershipState.Excluded
                    && member.Campaign!.Status != CampaignStatus.Archived
                    && member.Contact!.ArchivedAt == null)
                .Select(member => new ContactMembership(member.ContactId, member.CampaignId, member.AddedAt))
                .ToListAsync(ct)
                .ConfigureAwait(false);
        return new PinFacts(campaigns, contacts, members);
    }

    /// <summary>Which of these lines these campaigns have already said, by kind and key: one query for the page.</summary>
    private static async Task<HashSet<(int Campaign, string Kind, string Key)>> SaidAsync(
        JasonDbContext db,
        List<int> campaignIds,
        List<string> keys,
        CancellationToken ct)
    {
        if (campaignIds.Count == 0 || keys.Count == 0)
        {
            return [];
        }

        var said = await db.Journal
            .AsNoTracking()
            .Where(entry => entry.CampaignId != null
                && campaignIds.Contains(entry.CampaignId.Value)
                && (entry.Kind == JournalKinds.InboundReplyObserved || entry.Kind == JournalKinds.ProviderEventObserved)
                && keys.Contains(entry.Key!))
            .Select(entry => new { Campaign = entry.CampaignId!.Value, entry.Kind, Key = entry.Key! })
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return [.. said.Select(entry => (entry.Campaign, entry.Kind, entry.Key))];
    }
}
