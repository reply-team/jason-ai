using System.Globalization;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Ids;
using Jason.Runtime.Campaigns;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.WorkItems;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Contacts;

/// <summary>
/// The contact directory: people, the channels that reach them, and whatever else is known about them.
/// Nothing here is campaign-scoped — one contact belongs to as many campaigns as the user puts it in, which
/// is why archiving is a flag and never a delete.
/// </summary>
public sealed class ContactService(JasonDbContext db, JournalWriter journal, TimeProvider clock, WorkItemCanceller canceller)
{
    public async Task<ContactDto> CreateAsync(ContactCreateRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var actor = Actors.Resolve(request.Actor);
        var draft = ContactValidation.Validate(request);
        var contact = NewContact(draft, clock.GetUtcNow().UtcDateTime);

        db.Contacts.Add(contact);
        journal.Append(db, actor, JournalKinds.ContactCreated, campaign: null, key: contact.PublicId, reason: request.Reason, contact: contact);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return ContactMapper.ToDto(contact);
    }

    public async Task<ContactDto> GetAsync(ContactGetRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var contact = await LoadAsync(db, request.ContactId, cancellationToken).ConfigureAwait(false);
        return (await WithCampaignsAndLatestLineAsync(db, [contact], cancellationToken).ConfigureAwait(false))[0];
    }

    public async Task<Page<ContactDto>> ListAsync(ContactListRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = new ValidationErrors();
        string? channel = null;
        string? value = null;
        if (!string.IsNullOrWhiteSpace(request.Channel))
        {
            channel = ChannelValidation.Name(request.Channel, "channel", errors);
        }

        if (!string.IsNullOrWhiteSpace(request.Value))
        {
            if (string.IsNullOrWhiteSpace(request.Channel))
            {
                errors.Add("value", "invalid", "value narrows a channel; send channel as well.");
            }
            else if (channel is not null)
            {
                value = ChannelValidation.Value(channel, request.Value, "value", errors);
            }
        }

        // A count lists people by where they stand, a channel by an address: one request asks one of the two.
        if (request.Summary is not null && (!string.IsNullOrWhiteSpace(request.Channel) || !string.IsNullOrWhiteSpace(request.Value)))
        {
            errors.Add("summary", "invalid", "summary lists people by where they stand; send it without channel and value.");
        }

        var text = string.IsNullOrWhiteSpace(request.Query) ? null : request.Query.Trim();
        if (text is { Length: > ContactSearch.MaxLength })
        {
            errors.Add("query", "too_long", string.Create(CultureInfo.InvariantCulture, $"query must be at most {ContactSearch.MaxLength} characters."));
        }

        errors.ThrowIfAny();
        var limit = Paging.ResolveLimit(request.Limit);
        var after = Paging.DecodeCursor(request.Cursor);

        var query = db.Contacts.AsQueryable();
        var campaign = string.IsNullOrWhiteSpace(request.CampaignId)
            ? null
            : await CampaignService.LoadAsync(db, request.CampaignId, cancellationToken).ConfigureAwait(false);
        if (request.Summary is { } summary)
        {
            // The count's own definition decides who is in, removed people too under removed, in the one campaign named or
            // in any campaign campaign.summary counts when none is.
            List<int> counting = campaign is not null
                ? [campaign.Id]
                : [.. (await CampaignNumbers.ResolveAsync(db, null, cancellationToken).ConfigureAwait(false)).Select(shown => shown.Id)];
            query = Counted(db, query, counting, summary);
        }
        else if (campaign is not null)
        {
            query = query.Where(contact => db.CampaignContacts.Any(member =>
                member.ContactId == contact.Id && member.CampaignId == campaign.Id && member.State != MembershipState.Excluded));
        }

        if (text is not null)
        {
            query = ContactSearch.Apply(query, text);
        }

        if (request.IncludeArchived != true)
        {
            query = query.Where(contact => contact.ArchivedAt == null);
        }

        if (channel is not null)
        {
            query = value is null
                ? query.Where(contact => contact.Channels.Any(entry => entry.Channel == channel))
                : query.Where(contact => contact.Channels.Any(entry => entry.Channel == channel && entry.Value == value));
        }

        if (request.Order == ContactListOrder.Activity)
        {
            var rows = await ContactActivity.Ordered(ContactActivity.After(ContactActivity.Rows(query, db), after))
                .Take(limit + 1)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            var page = Paging.ToPage(rows, limit, ContactActivity.CursorKey, row => row.PublicId);
            var ids = page.Items.ToList();
            var people = await WithChannelsAndPins(db.Contacts).Where(contact => ids.Contains(contact.PublicId)).ToListAsync(cancellationToken).ConfigureAwait(false);
            var inOrder = ids.Select(id => people.Single(contact => contact.PublicId == id)).ToList();
            return new Page<ContactDto>(await WithCampaignsAndLatestLineAsync(db, inOrder, cancellationToken).ConfigureAwait(false), page.NextCursor);
        }

        if (after is not null)
        {
            query = ContactActivity.IsActivityCursor(after)
                ? throw ContactActivity.InvalidCursor()
                : query.Where(contact => string.Compare(contact.PublicId, after) > 0);
        }

        var fetched = await WithChannelsAndPins(query).OrderBy(contact => contact.PublicId).Take(limit + 1).ToListAsync(cancellationToken).ConfigureAwait(false);
        var byId = Paging.ToPage(fetched, limit, contact => contact.PublicId, contact => contact);
        return new Page<ContactDto>(await WithCampaignsAndLatestLineAsync(db, byId.Items, cancellationToken).ConfigureAwait(false), byId.NextCursor);
    }

    /// <summary>
    /// The people among these whom one Summary count counts in at least one of the campaigns, read once for all of them
    /// rather than per person: apart so that its plan can be read.
    /// </summary>
    internal static IQueryable<Contact> Counted(JasonDbContext db, IQueryable<Contact> contacts, IReadOnlyCollection<int> campaigns, CampaignSummaryFilter summary)
    {
        var counted = CampaignPeople.Matching(db, campaigns, summary).Select(membership => membership.ContactId);
        return contacts.Where(contact => counted.Contains(contact.Id));
    }

    private static IQueryable<Contact> WithChannelsAndPins(IQueryable<Contact> contacts) =>
        contacts.Include(contact => contact.Channels).Include(contact => contact.ExternalIds);

    /// <summary>
    /// The people as the reads of people answer them: each with the campaigns they are on, in any state, where they stand
    /// in each, and the newest line about them. One query for the memberships, one for where they stand and one for the
    /// lines of the whole page, never one per person.
    /// </summary>
    internal static async Task<IReadOnlyList<ContactDto>> WithCampaignsAndLatestLineAsync(JasonDbContext db, IReadOnlyList<Contact> contacts, CancellationToken ct)
    {
        var keys = contacts.Select(contact => contact.Id).ToList();
        var memberships = await db.CampaignContacts.AsNoTracking()
            .Where(member => keys.Contains(member.ContactId))
            .OrderBy(member => member.AddedAt)
            .ThenBy(member => member.Id)
            .Select(member => new { member.ContactId, member.CampaignId, member.Campaign!.PublicId, member.Campaign.Name, member.State, member.AddedAt, member.UpdatedAt })
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var standing = memberships.Count == 0
            ? new Dictionary<(int ContactId, int CampaignId), IReadOnlyList<CampaignSummaryFilter>>()
            : await CampaignPeople.StandingAsync(db, [.. memberships.Select(member => member.CampaignId).Distinct()], keys, ct).ConfigureAwait(false);
        var newest = await db.Journal.AsNoTracking()
            .Where(entry => entry.ContactId != null && keys.Contains(entry.ContactId.Value))
            .GroupBy(entry => entry.ContactId)
            .Select(lines => lines.Max(entry => entry.PublicId))
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var lines = await db.Journal.AsNoTracking().Where(entry => newest.Contains(entry.PublicId)).ToListAsync(ct).ConfigureAwait(false);
        var latest = (await JournalEntries.ToDtosAsync(db, lines, ct).ConfigureAwait(false))
            .Zip(lines)
            .ToDictionary(pair => pair.Second.ContactId!.Value, pair => pair.First);

        return contacts.Select(contact => ContactMapper.ToDto(contact) with
        {
            Campaigns = [.. memberships
                .Where(member => member.ContactId == contact.Id)
                .Select(member => new ContactCampaignDto(member.PublicId, member.Name, member.State, ContactMapper.ToOffset(member.AddedAt), ContactMapper.ToOffset(member.UpdatedAt),
                    standing.GetValueOrDefault((member.ContactId, member.CampaignId), [])))],
            LastActivity = latest.GetValueOrDefault(contact.Id),
        }).ToList();
    }

    public async Task<ContactDto> UpdateAsync(ContactUpdateRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var actor = Actors.Resolve(request.Actor);
        var contact = await LoadAsync(db, request.ContactId, cancellationToken).ConfigureAwait(false);
        if (contact.ArchivedAt is not null)
        {
            throw DomainErrors.ContactArchived(contact.PublicId);
        }

        // Everything is validated before anything is applied, so a payload with two problems reports both and
        // leaves the contact exactly as it was.
        var errors = new ValidationErrors();
        var firstName = request.FirstName.IsSet ? ContactValidation.Name(request.FirstName.Value, "first_name", errors) : contact.FirstName;
        var lastName = request.LastName.IsSet ? ContactValidation.Name(request.LastName.Value, "last_name", errors) : contact.LastName;
        var company = request.Company.IsSet ? ContactValidation.Text(request.Company.Value, "company", errors) : contact.Company;
        var title = request.Title.IsSet ? ContactValidation.Text(request.Title.Value, "title", errors) : contact.Title;
        var timeZone = request.TimeZone.IsSet ? TimeZoneRules.Normalize(request.TimeZone.Value, "time_zone", errors) : contact.TimeZone;
        var channels = request.Channels.IsSet ? ContactValidation.ValidateChannels(request.Channels.Value, string.Empty, errors) : null;
        var custom = request.Custom.IsSet ? ContactValidation.ValidateCustom(request.Custom.Value, "custom", errors) : null;
        errors.ThrowIfAny();

        var changed = new List<string>();
        Apply(contact.FirstName, firstName, "first_name", value => contact.FirstName = value, changed);
        Apply(contact.LastName, lastName, "last_name", value => contact.LastName = value, changed);
        Apply(contact.Company, company, "company", value => contact.Company = value, changed);
        Apply(contact.Title, title, "title", value => contact.Title = value, changed);
        Apply(contact.TimeZone, timeZone, "time_zone", value => contact.TimeZone = value, changed);

        if (channels is not null && ReplaceChannels(contact, channels))
        {
            changed.Add("channels");
        }

        if (custom is not null && !JsonNode.DeepEquals(custom, contact.Custom))
        {
            contact.Custom = custom;
            changed.Add("custom");
        }

        if (changed.Count == 0)
        {
            return ContactMapper.ToDto(contact);
        }

        contact.UpdatedAt = clock.GetUtcNow().UtcDateTime;

        // The chronicle names the fields that moved and never their values: a role reading the history learns
        // that the title changed, and reads the contact itself if it needs to know to what.
        journal.Append(
            db,
            actor,
            JournalKinds.ContactUpdated,
            campaign: null,
            key: contact.PublicId,
            updated: new JsonArray([.. changed.Select(field => (JsonNode?)JsonValue.Create(field))]),
            reason: request.Reason,
            contact: contact);

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ContactMapper.ToDto(contact);
    }

    public async Task<ContactDto> ArchiveAsync(ContactArchiveRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var actor = Actors.Resolve(request.Actor);
        var contact = await LoadAsync(db, request.ContactId, cancellationToken).ConfigureAwait(false);
        if (contact.ArchivedAt is not null)
        {
            return ContactMapper.ToDto(contact);
        }

        var now = clock.GetUtcNow().UtcDateTime;
        contact.ArchivedAt = now;
        contact.UpdatedAt = now;
        journal.Append(db, actor, JournalKinds.ContactArchived, campaign: null, key: contact.PublicId, reason: request.Reason, contact: contact);

        // Archiving somebody means nothing may reach out to them any more, so work still open about them stops
        // wherever it lives rather than running one last time.
        await canceller.CancelOpenForContactAsync(db, contact.Id, actor, "contact archived", cancellationToken).ConfigureAwait(false);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        canceller.StopCancelled();

        return ContactMapper.ToDto(contact);
    }

    /// <summary>The contact with its channels and its pins, or the 404 the caller is owed. Archived contacts are still readable.</summary>
    public static async Task<Contact> LoadAsync(JasonDbContext db, string? publicId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);

        if (string.IsNullOrWhiteSpace(publicId))
        {
            throw DomainErrors.Required("contact_id");
        }

        var id = publicId.Trim();
        return await db.Contacts
            .Include(contact => contact.Channels)
            .Include(contact => contact.ExternalIds)
            .SingleOrDefaultAsync(contact => contact.PublicId == id, cancellationToken).ConfigureAwait(false)
            ?? throw DomainErrors.ContactNotFound(id);
    }

    internal static Contact NewContact(ContactDraft draft, DateTime now)
    {
        var contact = new Contact
        {
            PublicId = PublicId.New("cnt"),
            FirstName = draft.FirstName,
            LastName = draft.LastName,
            Company = draft.Company,
            Title = draft.Title,
            TimeZone = draft.TimeZone,
            Custom = draft.Custom,
            CreatedAt = now,
            UpdatedAt = now,
        };

        foreach (var channel in draft.Channels)
        {
            contact.Channels.Add(channel);
        }

        return contact;
    }

    private static void Apply(string? current, string? wanted, string field, Action<string?> assign, List<string> changed)
    {
        if (string.Equals(current, wanted, StringComparison.Ordinal))
        {
            return;
        }

        assign(wanted);
        changed.Add(field);
    }

    /// <summary>The caller states the whole desired list, so the old rows go and the new ones arrive together.</summary>
    private bool ReplaceChannels(Contact contact, List<ContactChannel> desired)
    {
        if (SameChannels(contact.Channels, desired))
        {
            return false;
        }

        foreach (var existing in contact.Channels.ToList())
        {
            db.ContactChannels.Remove(existing);
        }

        contact.Channels.Clear();
        foreach (var channel in desired)
        {
            contact.Channels.Add(channel);
        }

        return true;
    }

    private static bool SameChannels(IReadOnlyList<ContactChannel> current, IReadOnlyList<ContactChannel> wanted)
    {
        if (current.Count != wanted.Count)
        {
            return false;
        }

        return Ordered(current).SequenceEqual(Ordered(wanted));

        static IEnumerable<(string Channel, string Value, string? Label, bool IsPrimary, string? Data)> Ordered(IReadOnlyList<ContactChannel> channels) =>
            channels
                .Select(channel => (channel.Channel, channel.Value, channel.Label, channel.IsPrimary, Data: channel.Data?.ToJsonString(null)))
                .OrderBy(channel => channel.Channel, StringComparer.Ordinal)
                .ThenBy(channel => channel.Value, StringComparer.Ordinal);
    }
}
