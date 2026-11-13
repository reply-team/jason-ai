using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Channels;
using Jason.Runtime.Contacts;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Plugins;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jason.Runtime.Audiences;

/// <summary>
/// What an answer of the audience family does to the campaign, in the change set that ends the answer's work item
/// <c>succeeded</c>, so the people, the row and the lines commit with the answer or not at all. A search the provider took
/// gets its row; a search it finished inside its call gets its row ended, and its people land from its result; a read of
/// a search lands its page and moves the row; a stop ends the row. Whoever made the read or the stop, a role or the
/// dispatcher, it lands the same: a page a read consumed at the provider is never one nobody lands. A read or a stop that
/// fails finding no search under the reference ends the row too, in the change set that ends it failed.
/// </summary>
/// <remarks>
/// <para>
/// Landing a page: each person once, by the provider's identifier. A person is found by the first of their keys that
/// finds anybody: that identifier pinned at the plugin, then each address they carry, then each LinkedIn profile, so a
/// profile the directory holds finds a person whose address is new to it; where two contacts answer one key, the oldest
/// live one is the person, since the landing asks nobody and a third contact would only deepen the duplicate. One the
/// directory archived is skipped, never made again. A person found is never changed: what the provider says of them
/// decides who they are and nothing else, as in an import. A person found nowhere is made, with what the contact's own
/// rules take of their names, title, company and channels, and the rest under <c>custom</c>. Each is pinned under
/// <c>contact</c> by the attempt that answered; each not already a member, nor taken out of the campaign, becomes one,
/// with a row saying which search made them.
/// </para>
/// <para>
/// Lines are facts, never a person's words: one for each page that carried anybody, with the counts and the people's
/// ids, and one when the search ends, which the row and a unique index hold to once. A campaign has at most one item
/// running at a time, so two answers about one search never land at once; and an answer that lands people opens the
/// write transaction before it reads who is known, which the attempt's handler commits with the save, so an import into
/// the campaign at that moment waits for it rather than falling between the read and the save.
/// </para>
/// </remarks>
public sealed class SearchLanding(JournalWriter journal, ExternalIdStore identifiers, TimeProvider clock, ILogger<SearchLanding>? logger = null)
{
    private readonly ILogger _logger = logger ?? (ILogger)NullLogger.Instance;

    private static readonly Action<ILogger, string, Exception?> NoRow = LoggerMessage.Define<string>(
        LogLevel.Warning,
        new EventId(1, nameof(NoRow)),
        "Work item {WorkItemId} read or stopped a search that has no row; it lands and ends nothing");

    public const string Search = "audience.search";

    public const string Status = "audience.search_status";

    public const string Stop = "audience.search_cancel";

    /// <summary>What a read or a stop fails with where the provider holds no search under the reference.</summary>
    public const string NotFound = "search_not_found";

    /// <summary>The kind a person is pinned under: what every operation that names a person pins.</summary>
    public const string ContactPin = "contact";

    /// <summary>The two states that end a search, once a read says either with nothing more to read.</summary>
    private static readonly string[] Final = ["finished", "exhausted"];

    /// <summary>The states in which the provider holds a search until a person acts there: paused, out of credits, at the plan's limit.</summary>
    public static readonly string[] Held = ["paused", "paused_for_credits", "paused_for_limit"];

    /// <summary>Whether <paramref name="state"/>, a provider's word for a search, is one in which it holds the search.</summary>
    public static bool IsHeld(string? state) => state is not null && Held.Contains(state, StringComparer.Ordinal);

    /// <summary>
    /// Adds to the context's change set (not saved here) what <paramref name="item"/>'s answer does to its campaign's
    /// searches, where it is an answer of the family: nothing for any other operation.
    /// </summary>
    /// <param name="pluginId">The plugin that answered, as the claim pinned it.</param>
    /// <param name="input">The input the claim composed: a read or a stop names its search's work item there.</param>
    /// <param name="result">What the plugin answered, already held to the operation's schema.</param>
    public async Task AnsweredAsync(
        JasonDbContext db,
        WorkItem item,
        Attempt attempt,
        string pluginId,
        JsonObject? input,
        JsonNode? result,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(attempt);
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);

        if (item.Operation == Search)
        {
            if (result?["items"]?[0] is JsonObject answer)
            {
                await HoldAsync(db, ct).ConfigureAwait(false);
                await MadeAsync(db, item, attempt, pluginId, answer, ct).ConfigureAwait(false);
            }

            return;
        }

        if (item.Operation is not (Status or Stop))
        {
            return;
        }

        // Every search the runtime lets through gets its row when it succeeds, so a read without one names a search this
        // build never saw succeed: said, by the item's id alone, and left.
        if (await RowAsync(db, item, input, ct).ConfigureAwait(false) is not { } row)
        {
            NoRow(_logger, item.PublicId, null);
            return;
        }

        if (item.Operation == Status && result is JsonObject page)
        {
            await HoldAsync(db, ct).ConfigureAwait(false);
            await ReadAsync(db, row, item, attempt, pluginId, page, ct).ConfigureAwait(false);
        }
        else if (item.Operation == Stop && result?["items"]?[0] is JsonObject stopped)
        {
            await StoppedAsync(db, row, item, stopped, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Adds to the change set that ends <paramref name="item"/> failed (not saved here) what its failure does to its
    /// search: a read or a stop that found no search under the reference ends it <c>cancelled</c>, by that item. The search
    /// is gone at the provider, so nothing would read or stop it again, and a row left followed would keep its account's
    /// last route from changing and an archived campaign's stop from ever being settled. Any other failure leaves the
    /// search as it stands.
    /// </summary>
    /// <param name="input">The input the claim composed, which names the search's work item.</param>
    /// <param name="code">The code the item failed with, as the operation's contract declares it.</param>
    public async Task FailedAsync(JasonDbContext db, WorkItem item, JsonObject? input, string code, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(item);

        if (item.Operation is not (Status or Stop) || !string.Equals(code, NotFound, StringComparison.Ordinal))
        {
            return;
        }

        if (await RowAsync(db, item, input, ct).ConfigureAwait(false) is { EndedAs: null } row)
        {
            End(db, row, "cancelled", item);
        }
    }

    /// <summary>
    /// The write transaction the landing reads in, begun here unless the context is in one already: from the reads of who
    /// is known to the save, no other writer — an import above all — runs. Whoever saves the change set commits it.
    /// </summary>
    private static async Task HoldAsync(JasonDbContext db, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null)
        {
            await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Ends a search the dispatcher found stalled: its row, and the line, by the dispatcher, naming no work item that ended
    /// it. The row arrives tracked, with its audience and its search loaded.
    /// </summary>
    public void Stalled(JasonDbContext db, AudienceSearch row) => End(db, row, "stalled", endedBy: null);

    /// <summary>
    /// The search's row, made from its answer: in flight where the provider took the search, ended where it finished
    /// inside the call, whose people then land from the answer itself. A search the claim recorded no audience for has
    /// none: every search the claim lets through carries one.
    /// </summary>
    private async Task MadeAsync(JasonDbContext db, WorkItem search, Attempt attempt, string pluginId, JsonObject answer, CancellationToken ct)
    {
        if (Text(search.Context["audience"]?["id"]) is not { } audienceId
            || await db.Audiences.FirstOrDefaultAsync(audience => audience.CampaignId == search.CampaignId && audience.PublicId == audienceId, ct)
                .ConfigureAwait(false) is not { } held)
        {
            return;
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var row = new AudienceSearch
        {
            CampaignId = search.CampaignId,
            Audience = held,
            SearchWorkItem = search,
            PluginId = pluginId,
            Reference = Text(answer["reference"]),
            Asked = Number(attempt.ContextSnapshot["input"]?["count"]) ?? 0,
            Found = Number(answer["found"]),
            QuietSince = now,
            CreatedAt = now,
        };
        db.AudienceSearches.Add(row);
        if (Text(answer["status"]) != "finished")
        {
            return;
        }

        row.State = "finished";
        await LandAsync(db, row, search, attempt, pluginId, answer["people"] as JsonArray, now, ct).ConfigureAwait(false);
        End(db, row, "finished", search);
    }

    private async Task ReadAsync(JasonDbContext db, AudienceSearch row, WorkItem read, Attempt attempt, string pluginId, JsonObject page, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var wasHeld = IsHeld(row.State);
        row.State = Text(page["state"]);
        row.Cursor = Text(page["cursor"]);
        row.Found = Number(page["found"]);

        // More brings the next read at once only after a page that carried somebody: a provider that says more is readable
        // and gives nobody is read again a cadence later, not back to back. Whether anything is left to read is still the
        // provider's word, and only that ends a search.
        var readable = page["has_more"] is JsonValue more && more.TryGetValue(out bool said) && said;
        row.More = readable && page["people"] is JsonArray { Count: > 0 };

        // An answer, whoever asked: the search is readable, and the dispatcher's next read waits only the cadence.
        row.Failures = 0;
        await LandAsync(db, row, read, attempt, pluginId, page["people"] as JsonArray, now, ct).ConfigureAwait(false);

        // A hold, or a move out of one, starts the quiet afresh: a held search is bound by the long wait of a hold from the
        // moment it was held, and one that moves again by the ordinary bound from the moment it moved.
        if (IsHeld(row.State) != wasHeld)
        {
            row.QuietSince = now;
        }

        if (IsHeld(row.State) && !wasHeld)
        {
            HeldNow(db, row, read);
        }

        if (row.EndedAs is null && !readable && Final.Contains(row.State))
        {
            End(db, row, row.State!, read);
        }
    }

    /// <summary>
    /// The line that a search is held at the provider, keyed by the read that first said so: once per hold, since a read
    /// that says it is still held, in any of the three words, finds it held already.
    /// </summary>
    private void HeldNow(JasonDbContext db, AudienceSearch row, WorkItem read) =>
        journal.Append(
            db,
            Actors.Dispatcher,
            JournalKinds.AudienceSearchHeld,
            campaign: null,
            key: read.PublicId,
            updated: new JsonObject
            {
                ["audience"] = row.Audience!.PublicId,
                ["plugin"] = row.PluginId,
                ["state"] = row.State,
                ["asked"] = row.Asked,
                ["found"] = row.Found,
                ["landed"] = row.Landed,
            },
            workItem: row.SearchWorkItem);

    /// <summary>
    /// A stop that stopped the search, or found it stopped by an earlier stop, or gone from the provider, ends it
    /// <c>cancelled</c>. One that found it ended by itself leaves it to the reads: what it found is still readable, and
    /// the read that finds nobody past its last person ends it as it really ended — except in an archived campaign, whose
    /// searches nobody reads again, where it ends there as the provider ended it.
    /// </summary>
    private async Task StoppedAsync(JasonDbContext db, AudienceSearch row, WorkItem stop, JsonObject answer, CancellationToken ct)
    {
        if (row.EndedAs is not null)
        {
            return;
        }

        var ended = Text(answer["ended_as"]);
        switch (Text(answer["status"]))
        {
            case "cancelled":
            case "already_ended" when ended is null or "cancelled":
                End(db, row, "cancelled", stop);
                break;
            case "already_ended" when Final.Contains(ended)
                && await db.Campaigns.AnyAsync(campaign => campaign.Id == row.CampaignId && campaign.Status == CampaignStatus.Archived, ct).ConfigureAwait(false):
                End(db, row, ended!, stop);
                break;
        }
    }

    /// <summary>
    /// The row of the search a read or a stop names, in the item's own campaign, by the work item the claim composed under
    /// <c>search</c>: the input the attempt ran with, not the item's context as it is now.
    /// </summary>
    private static Task<AudienceSearch?> RowAsync(JasonDbContext db, WorkItem item, JsonObject? input, CancellationToken ct) =>
        Text(input?["search"]?["work_item"]) is { } searched
            ? AudienceSearches.Named(db, item.CampaignId, searched)
                .Include(row => row.Audience)
                .Include(row => row.SearchWorkItem)
                .FirstOrDefaultAsync(ct)
            : Task.FromResult<AudienceSearch?>(null);

    private void End(JasonDbContext db, AudienceSearch row, string endedAs, WorkItem? endedBy)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        row.EndedAs = endedAs;
        row.EndedAt = now;
        journal.Append(
            db,
            Actors.Dispatcher,
            JournalKinds.AudienceSearchEnded,
            campaign: null,
            key: row.SearchWorkItem!.PublicId,
            updated: new JsonObject
            {
                ["audience"] = row.Audience!.PublicId,
                ["plugin"] = row.PluginId,
                ["ended_as"] = endedAs,
                ["state"] = row.State,
                ["asked"] = row.Asked,
                ["found"] = row.Found,
                ["landed"] = row.Landed,
                ["ended_by"] = endedBy?.PublicId,
            },
            workItem: row.SearchWorkItem);
    }

    /// <summary>One page: each person once, matched or made, pinned, and made a member where they may be; one line.</summary>
    private async Task LandAsync(
        JasonDbContext db,
        AudienceSearch row,
        WorkItem read,
        Attempt attempt,
        string pluginId,
        JsonArray? page,
        DateTime now,
        CancellationToken ct)
    {
        var people = Distinct(page);
        if (people.Count == 0)
        {
            return;
        }

        var directory = await Known.ReadAsync(db, row.CampaignId, pluginId, people, ct).ConfigureAwait(false);
        var made = new Dictionary<string, Contact>(StringComparer.Ordinal);
        var landedHere = new HashSet<Contact>();
        var landed = new List<string>();
        var created = new List<string>();
        var alreadyMember = 0;
        var skipped = 0;

        foreach (var person in people)
        {
            var contact = person.Keys.Select(key => made.GetValueOrDefault(key)).FirstOrDefault(earlier => earlier is not null);
            if (contact is null)
            {
                switch (directory.Match(person))
                {
                    case Known.Archived:
                        skipped++;
                        continue;
                    case Known.Found found:
                        contact = found.Contact;
                        break;
                    default:
                        contact = ContactService.NewContact(person.Draft, now);
                        db.Contacts.Add(contact);
                        created.Add(contact.PublicId);
                        break;
                }
            }

            foreach (var key in person.Keys)
            {
                made.TryAdd(key, contact);
            }

            identifiers.PinPerson(db, contact, pluginId, ContactPin, person.ExternalId, read, attempt);

            // A second person of the page who is someone this page already made a member: the campaign holds them now.
            if (landedHere.Contains(contact))
            {
                alreadyMember++;
                continue;
            }

            if (directory.Membership(contact) is { } membership)
            {
                if (membership.State == MembershipState.Excluded)
                {
                    skipped++;
                }
                else
                {
                    alreadyMember++;
                }

                continue;
            }

            var joined = new CampaignContact
            {
                CampaignId = row.CampaignId,
                Contact = contact,
                State = MembershipState.Enrolled,
                AddedAt = now,
                UpdatedAt = now,
            };
            db.CampaignContacts.Add(joined);
            db.AudienceLandings.Add(new AudienceLanding
            {
                Membership = joined,
                Audience = row.Audience,
                SearchWorkItem = row.SearchWorkItem,
                ReadWorkItem = read,
                LandedAt = now,
            });
            landedHere.Add(contact);
            landed.Add(contact.PublicId);
        }

        row.Landed += landed.Count;
        if (landed.Count > 0)
        {
            row.QuietSince = now;
        }

        journal.Append(
            db,
            Actors.Dispatcher,
            JournalKinds.AudiencePeopleLanded,
            campaign: null,
            key: read.PublicId,
            updated: new JsonObject
            {
                ["audience"] = row.Audience!.PublicId,
                ["plugin"] = pluginId,
                ["state"] = row.State,
                ["people"] = people.Count,
                ["landed"] = landed.Count,
                ["created"] = created.Count,
                ["already_member"] = alreadyMember,
                ["skipped"] = skipped,
                ["contact_ids"] = Ids(landed),
                ["created_contact_ids"] = Ids(created),
            },
            workItem: row.SearchWorkItem);
    }

    /// <summary>The contacts this plugin pinned under <c>contact</c> with these identifiers: the pins' own lookup index.</summary>
    internal static IQueryable<ExternalId> Pinned(JasonDbContext db, string pluginId, IReadOnlyCollection<string> ids) =>
        db.ExternalIds.Where(pin => pin.PluginId == pluginId && pin.Kind == ContactPin && pin.ContactId != null && ids.Contains(pin.Value));

    /// <summary>The channels that carry these addresses or these profiles, as the directory stores them: the channels' lookup index.</summary>
    internal static IQueryable<ContactChannel> Reached(JasonDbContext db, IReadOnlyCollection<string> emails, IReadOnlyCollection<string> profiles) =>
        db.ContactChannels.Where(channel => (channel.Channel == ChannelRules.Email && emails.Contains(channel.Value))
            || (channel.Channel == ChannelRules.LinkedIn && profiles.Contains(channel.Value)));

    /// <summary>The people of a page, each once by the provider's identifier, in the page's order.</summary>
    private static List<FoundPerson> Distinct(JsonArray? page)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var people = new List<FoundPerson>();
        foreach (var node in page ?? [])
        {
            if (node is JsonObject person && Text(person["external_id"]) is { Length: > 0 } id && seen.Add(id))
            {
                people.Add(FoundPerson.Read(id, person));
            }
        }

        return people;
    }

    private static JsonArray Ids(List<string> ids) => new([.. ids.Select(id => (JsonNode?)JsonValue.Create(id))]);

    private static string? Text(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue(out string? text) ? text : null;

    private static int? Number(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue(out int number) ? number : null;

    /// <summary>
    /// One person a page held, as a contact would be made of them, with the keys they are matched by: the provider's
    /// identifier, then each address, then each LinkedIn profile, each spelled as the directory stores it.
    /// </summary>
    private sealed record FoundPerson(string ExternalId, ContactDraft Draft, IReadOnlyList<string> Keys)
    {
        public static FoundPerson Read(string id, JsonObject person)
        {
            var ignored = new ValidationErrors();
            var channels = Channels(person["channels"] as JsonArray);
            var company = person["company"] as JsonObject;
            var draft = new ContactDraft(
                ContactValidation.Name(Text(person["first_name"]), "first_name", ignored),
                ContactValidation.Name(Text(person["last_name"]), "last_name", ignored),
                ContactValidation.Text(Text(company?["name"]), "company", ignored),
                ContactValidation.Text(Text(person["title"]), "title", ignored),
                TimeZone: null,
                channels,
                Custom(person, company));
            return new FoundPerson(
                id,
                draft,
                [
                    Known.PinKey(id),
                    .. channels.Where(channel => channel.Channel == ChannelRules.Email).Select(channel => Known.ChannelKey(channel.Channel, channel.Value)),
                    .. channels.Where(channel => channel.Channel == ChannelRules.LinkedIn).Select(channel => Known.ChannelKey(channel.Channel, channel.Value)),
                ]);
        }

        /// <summary>Each channel the directory's rules take, as they store it, once; the first of each channel primary.</summary>
        private static List<ContactChannel> Channels(JsonArray? given)
        {
            var channels = new List<ContactChannel>();
            foreach (var node in given ?? [])
            {
                if (ChannelRules.NormalizeChannelName(Text(node?["channel"]), out _) is not { } name
                    || ChannelRules.NormalizeValue(name, Text(node?["value"]), out _) is not { } value
                    || channels.Exists(channel => channel.Channel == name && channel.Value == value))
                {
                    continue;
                }

                channels.Add(new ContactChannel { Channel = name, Value = value, IsPrimary = !channels.Exists(channel => channel.Channel == name) });
            }

            return channels;
        }

        /// <summary>What the contact's fields have no place for: where the person is, their company's particulars, and the provider's own record.</summary>
        private static JsonObject Custom(JsonObject person, JsonObject? company)
        {
            var custom = new JsonObject();
            Add("location", person["location"]);
            Add("company_domain", company?["domain"]);
            Add("company_size", company?["size"]);
            Add("company_industry", company?["industry"]);
            if (Text(person["raw"]) is { } raw)
            {
                try
                {
                    custom[Contact.ProviderRecordKey] = JsonNode.Parse(raw);
                }
                catch (JsonException)
                {
                    // A record that is not JSON text is the provider's to fix; the person lands without it.
                }
            }

            // The contact's own bound on what it keeps beside its fields: a record that would take the person over it is
            // left out, and the person lands with the rest.
            if (Encoding.UTF8.GetByteCount(custom.ToJsonString(null)) > ContactValidation.MaxCustomBytes)
            {
                custom.Remove(Contact.ProviderRecordKey);
            }

            return custom;

            void Add(string key, JsonNode? node)
            {
                if (Text(node)?.Trim() is { Length: > 0 } value)
                {
                    custom[key] = value;
                }
            }
        }
    }

    /// <summary>
    /// What the directory and the campaign hold of a page's people, read once per page: the contacts their keys find, with
    /// this plugin's pins, and the campaign's memberships of those contacts.
    /// </summary>
    private sealed class Known
    {
        private readonly Dictionary<string, List<Contact>> _byKey;
        private readonly Dictionary<int, CampaignContact> _memberships;

        private Known(Dictionary<string, List<Contact>> byKey, Dictionary<int, CampaignContact> memberships)
        {
            _byKey = byKey;
            _memberships = memberships;
        }

        public abstract record Hit;

        public sealed record Found(Contact Contact) : Hit;

        public sealed record Archived : Hit;

        public sealed record Nobody : Hit;

        public static string PinKey(string id) => "pin\n" + id;

        public static string ChannelKey(string channel, string value) => channel + "\n" + value;

        public static async Task<Known> ReadAsync(JasonDbContext db, int campaignId, string pluginId, List<FoundPerson> people, CancellationToken ct)
        {
            var ids = people.Select(person => person.ExternalId).ToList();
            var emails = Values(people, ChannelRules.Email);
            var profiles = Values(people, ChannelRules.LinkedIn);

            var pinned = await Pinned(db, pluginId, ids)
                .AsNoTracking()
                .Select(pin => new { Key = pin.Value, ContactId = pin.ContactId!.Value })
                .ToListAsync(ct)
                .ConfigureAwait(false);
            var reached = await Reached(db, emails, profiles)
                .AsNoTracking()
                .Select(channel => new { channel.Channel, channel.Value, channel.ContactId })
                .ToListAsync(ct)
                .ConfigureAwait(false);

            var hits = pinned.Select(pin => (Key: PinKey(pin.Key), pin.ContactId))
                .Concat(reached.Select(channel => (Key: ChannelKey(channel.Channel, channel.Value), channel.ContactId)))
                .ToList();
            var found = hits.Select(hit => hit.ContactId).Distinct().ToList();
            var contacts = await db.Contacts
                .Include(contact => contact.ExternalIds.Where(pin => pin.PluginId == pluginId))
                .Where(contact => found.Contains(contact.Id))
                .ToDictionaryAsync(contact => contact.Id, ct)
                .ConfigureAwait(false);
            var memberships = await db.CampaignContacts
                .Where(membership => membership.CampaignId == campaignId && found.Contains(membership.ContactId))
                .ToDictionaryAsync(membership => membership.ContactId, ct)
                .ConfigureAwait(false);

            var byKey = hits
                .GroupBy(hit => hit.Key, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(hit => contacts[hit.ContactId]).Distinct().OrderBy(contact => contact.Id).ToList(),
                    StringComparer.Ordinal);
            return new Known(byKey, memberships);
        }

        /// <summary>
        /// The person's contact by the first of their keys that finds anybody: its oldest live contact, or, where every
        /// contact it finds is archived, a person the directory took out.
        /// </summary>
        public Hit Match(FoundPerson person)
        {
            foreach (var key in person.Keys)
            {
                if (_byKey.TryGetValue(key, out var contacts))
                {
                    return contacts.Find(contact => contact.ArchivedAt is null) is { } live ? new Found(live) : new Archived();
                }
            }

            return new Nobody();
        }

        /// <summary>The campaign's membership of a contact the directory held; none for a contact this page made.</summary>
        public CampaignContact? Membership(Contact contact) =>
            contact.Id != 0 && _memberships.TryGetValue(contact.Id, out var membership) ? membership : null;

        private static List<string> Values(List<FoundPerson> people, string channel) =>
            [.. people.SelectMany(person => person.Draft.Channels).Where(given => given.Channel == channel).Select(given => given.Value).Distinct(StringComparer.Ordinal)];
    }
}
