using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Ids;
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
        journal.Append(db, actor, JournalKinds.ContactCreated, campaign: null, key: contact.PublicId, reason: request.Reason);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return ContactMapper.ToDto(contact);
    }

    public async Task<ContactDto> GetAsync(ContactGetRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ContactMapper.ToDto(await LoadAsync(db, request.ContactId, cancellationToken).ConfigureAwait(false));
    }

    public async Task<Page<ContactDto>> ListAsync(ContactListRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = new ValidationErrors();
        string? channel = null;
        string? value = null;
        if (!string.IsNullOrWhiteSpace(request.Channel))
        {
            channel = ChannelRules.NormalizeChannelName(request.Channel, "channel", errors);
        }

        if (!string.IsNullOrWhiteSpace(request.Value))
        {
            if (string.IsNullOrWhiteSpace(request.Channel))
            {
                errors.Add("value", "invalid", "value narrows a channel; send channel as well.");
            }
            else if (channel is not null)
            {
                value = ChannelRules.NormalizeValue(channel, request.Value, "value", errors);
            }
        }

        errors.ThrowIfAny();
        var limit = Paging.ResolveLimit(request.Limit);
        var after = Paging.DecodeCursor(request.Cursor);

        var query = db.Contacts.Include(contact => contact.Channels).AsQueryable();
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

        if (after is not null)
        {
            query = query.Where(contact => string.Compare(contact.PublicId, after) > 0);
        }

        var fetched = await query.OrderBy(contact => contact.PublicId).Take(limit + 1).ToListAsync(cancellationToken).ConfigureAwait(false);
        return Paging.ToPage(fetched, limit, contact => contact.PublicId, ContactMapper.ToDto);
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
        var firstName = request.FirstName.IsSet ? ContactValidation.Text(request.FirstName.Value, "first_name", errors) : contact.FirstName;
        var lastName = request.LastName.IsSet ? ContactValidation.Text(request.LastName.Value, "last_name", errors) : contact.LastName;
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
            reason: request.Reason);

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
        journal.Append(db, actor, JournalKinds.ContactArchived, campaign: null, key: contact.PublicId, reason: request.Reason);

        // Archiving somebody means nothing may reach out to them any more, so work still open about them stops
        // wherever it lives rather than running one last time.
        await canceller.CancelOpenForContactAsync(db, contact.Id, actor, "contact archived", cancellationToken).ConfigureAwait(false);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return ContactMapper.ToDto(contact);
    }

    /// <summary>The contact with its channels, or the 404 the caller is owed. Archived contacts are still readable.</summary>
    public static async Task<Contact> LoadAsync(JasonDbContext db, string? publicId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);

        if (string.IsNullOrWhiteSpace(publicId))
        {
            throw DomainErrors.Required("contact_id");
        }

        var id = publicId.Trim();
        return await db.Contacts.Include(contact => contact.Channels).SingleOrDefaultAsync(contact => contact.PublicId == id, cancellationToken).ConfigureAwait(false)
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
