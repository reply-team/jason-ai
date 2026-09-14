using System.Globalization;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Contacts;

/// <summary>
/// Who is in a campaign. Adding is an import: every row reports its own outcome and a bad row never fails the
/// batch, because a thousand-line file with one broken address must not cost the caller the other 999.
/// Removal is an exclusion rather than a deletion, so a re-import cannot quietly undo it.
/// </summary>
public sealed class MembershipService(JasonDbContext db, JournalWriter journal, TimeProvider clock)
{
    public const int MaxBatch = 1000;

    public async Task<AddContactsResult> AddAsync(AddContactsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var actor = Actors.Resolve(request.Actor);
        var campaign = await LoadCampaignAsync(db, request.CampaignId, cancellationToken).ConfigureAwait(false);
        if (campaign.ArchivedAt is not null)
        {
            throw DomainErrors.CampaignArchived(campaign.PublicId);
        }

        var items = request.Contacts ?? throw DomainErrors.Required("contacts");
        if (items.Count > MaxBatch)
        {
            throw DomainErrors.BatchTooLarge(MaxBatch);
        }

        var errors = new ValidationErrors();
        var matchKey = MatchKey.Parse(request.MatchBy, errors);
        errors.ThrowIfAny();

        var now = clock.GetUtcNow().UtcDateTime;
        var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            var memberships = await db.CampaignContacts
                .Where(membership => membership.CampaignId == campaign.Id)
                .ToDictionaryAsync(membership => membership.ContactId, cancellationToken)
                .ConfigureAwait(false);

            // A file that names the same person twice must add them once. Contacts created earlier in this
            // batch are not in the database yet, so the batch has to remember them itself.
            var createdInThisBatch = new Dictionary<string, Contact>(StringComparer.Ordinal);
            var results = new List<AddContactsItemResult>(items.Count);
            var addedIds = new List<string>();
            var createdIds = new List<string>();
            var added = 0;
            var alreadyMember = 0;
            var rejected = 0;

            for (var index = 0; index < items.Count; index++)
            {
                var item = items[index];
                AddContactsItemResult outcome;
                try
                {
                    outcome = item is null
                        ? throw new ValidationException([new ErrorDetail(ItemField(index), "invalid", "a contacts item must be an object.")])
                        : item.ContactId is not null
                            ? await AddByIdAsync(campaign, item, index, memberships, now, cancellationToken).ConfigureAwait(false)
                            : await AddByPayloadAsync(campaign, item, index, matchKey, memberships, createdInThisBatch, now, cancellationToken).ConfigureAwait(false);
                }
                catch (DomainException ex)
                {
                    outcome = new AddContactsItemResult(index, AddContactsItemStatus.Rejected, item?.ContactId, false, null, ErrorOf(ex));
                }

                switch (outcome.Status)
                {
                    case AddContactsItemStatus.Added:
                        added++;
                        addedIds.Add(outcome.ContactId!);
                        if (outcome.ContactCreated)
                        {
                            createdIds.Add(outcome.ContactId!);
                        }

                        break;
                    case AddContactsItemStatus.AlreadyMember:
                        alreadyMember++;
                        break;
                    default:
                        rejected++;
                        break;
                }

                results.Add(outcome);
            }

            // One entry for the whole import: a 500-row file is one thing that happened, not 500.
            journal.Append(
                db,
                actor,
                JournalKinds.ContactsAdded,
                campaign,
                updated: new JsonObject
                {
                    ["added"] = added,
                    ["already_member"] = alreadyMember,
                    ["rejected"] = rejected,
                    ["contact_ids"] = Ids(addedIds),
                    ["created_contact_ids"] = Ids(createdIds),
                },
                reason: request.Reason);

            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            return new AddContactsResult(campaign.PublicId, new AddContactsSummary(added, alreadyMember, rejected), results);
        }
    }

    public async Task<RemoveContactsResult> RemoveAsync(RemoveContactsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var actor = Actors.Resolve(request.Actor);
        var campaign = await LoadCampaignAsync(db, request.CampaignId, cancellationToken).ConfigureAwait(false);
        if (campaign.ArchivedAt is not null)
        {
            throw DomainErrors.CampaignArchived(campaign.PublicId);
        }

        var ids = request.ContactIds ?? throw DomainErrors.Required("contact_ids");
        if (ids.Count > MaxBatch)
        {
            throw DomainErrors.BatchTooLarge(MaxBatch);
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            var wanted = ids.Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id.Trim()).Distinct(StringComparer.Ordinal).ToList();
            var contacts = await db.Contacts
                .Where(contact => wanted.Contains(contact.PublicId))
                .ToDictionaryAsync(contact => contact.PublicId, cancellationToken)
                .ConfigureAwait(false);
            var memberships = await db.CampaignContacts
                .Where(membership => membership.CampaignId == campaign.Id)
                .ToDictionaryAsync(membership => membership.ContactId, cancellationToken)
                .ConfigureAwait(false);

            var results = new List<RemoveContactsItemResult>(ids.Count);
            var removedIds = new List<string>();
            var removed = 0;
            var notMember = 0;
            var alreadyExcluded = 0;
            var rejected = 0;

            for (var index = 0; index < ids.Count; index++)
            {
                var id = ids[index]?.Trim();
                if (string.IsNullOrEmpty(id))
                {
                    rejected++;
                    results.Add(new RemoveContactsItemResult(index, RemoveContactsItemStatus.Rejected, null, ErrorOf(DomainErrors.Required(string.Create(CultureInfo.InvariantCulture, $"contact_ids[{index}]")))));
                    continue;
                }

                if (!contacts.TryGetValue(id, out var contact))
                {
                    rejected++;
                    results.Add(new RemoveContactsItemResult(index, RemoveContactsItemStatus.Rejected, id, ErrorOf(DomainErrors.ContactNotFound(id))));
                    continue;
                }

                if (!memberships.TryGetValue(contact.Id, out var membership))
                {
                    notMember++;
                    results.Add(new RemoveContactsItemResult(index, RemoveContactsItemStatus.NotMember, contact.PublicId, null));
                    continue;
                }

                if (membership.State == MembershipState.Excluded)
                {
                    alreadyExcluded++;
                    results.Add(new RemoveContactsItemResult(index, RemoveContactsItemStatus.AlreadyExcluded, contact.PublicId, null));
                    continue;
                }

                membership.State = MembershipState.Excluded;
                membership.UpdatedAt = now;
                removed++;
                removedIds.Add(contact.PublicId);
                results.Add(new RemoveContactsItemResult(index, RemoveContactsItemStatus.Removed, contact.PublicId, null));
            }

            journal.Append(
                db,
                actor,
                JournalKinds.ContactsRemoved,
                campaign,
                updated: new JsonObject
                {
                    ["removed"] = removed,
                    ["not_member"] = notMember,
                    ["already_excluded"] = alreadyExcluded,
                    ["rejected"] = rejected,
                    ["contact_ids"] = Ids(removedIds),
                },
                reason: request.Reason);

            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            return new RemoveContactsResult(campaign.PublicId, new RemoveContactsSummary(removed, notMember, alreadyExcluded, rejected), results);
        }
    }

    public async Task<Page<MembershipItemDto>> ListAsync(ListContactsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var campaign = await LoadCampaignAsync(db, request.CampaignId, cancellationToken).ConfigureAwait(false);
        var limit = Paging.ResolveLimit(request.Limit);
        var after = Paging.DecodeCursor(request.Cursor);

        var query = db.CampaignContacts
            .Include(membership => membership.Contact!)
            .ThenInclude(contact => contact.Channels)
            .Where(membership => membership.CampaignId == campaign.Id);

        // Excluded members are the answer to "who did we take out", never to "who is in this campaign".
        query = request.State is { } state
            ? query.Where(membership => membership.State == state)
            : query.Where(membership => membership.State != MembershipState.Excluded);

        if (after is not null)
        {
            query = query.Where(membership => string.Compare(membership.Contact!.PublicId, after) > 0);
        }

        var fetched = await query
            .OrderBy(membership => membership.Contact!.PublicId)
            .Take(limit + 1)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Paging.ToPage(
            fetched,
            limit,
            membership => membership.Contact!.PublicId,
            membership => new MembershipItemDto(
                ContactMapper.ToDto(membership.Contact!),
                membership.State,
                ContactMapper.ToOffset(membership.AddedAt),
                ContactMapper.ToOffset(membership.UpdatedAt)));
    }

    /// <summary>The campaign this operation is about, or the error the caller is owed.</summary>
    private static async Task<Campaign> LoadCampaignAsync(JasonDbContext db, string? publicId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(publicId))
        {
            throw DomainErrors.Required("campaign_id");
        }

        var id = publicId.Trim();
        return await db.Campaigns.SingleOrDefaultAsync(campaign => campaign.PublicId == id, cancellationToken).ConfigureAwait(false)
            ?? throw DomainErrors.CampaignNotFound(id);
    }

    private async Task<AddContactsItemResult> AddByIdAsync(
        Campaign campaign,
        AddContactsItem item,
        int index,
        Dictionary<int, CampaignContact> memberships,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (HasPayload(item))
        {
            throw new ValidationException([new ErrorDetail(
                ItemField(index, "contact_id"),
                "not_allowed",
                "an item that names contact_id carries no other contact fields; change a contact through contact.update.")]);
        }

        var contact = await ContactService.LoadAsync(db, item.ContactId, cancellationToken).ConfigureAwait(false);
        if (contact.ArchivedAt is not null)
        {
            throw DomainErrors.ContactArchived(contact.PublicId);
        }

        return Enrol(campaign, contact, index, memberships, now, contactCreated: false, explicitReference: true);
    }

    private async Task<AddContactsItemResult> AddByPayloadAsync(
        Campaign campaign,
        AddContactsItem item,
        int index,
        MatchKey? matchKey,
        Dictionary<int, CampaignContact> memberships,
        Dictionary<string, Contact> createdInThisBatch,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var draft = ContactValidation.Validate(item, ItemField(index) + ".");
        if (matchKey is null)
        {
            return Enrol(campaign, NewContact(draft, now), index, memberships, now, contactCreated: true, explicitReference: false);
        }

        var keyValue = matchKey.ValueOf(draft)
            ?? throw new InvalidRequestException("no_match_key", "the item carries no value for the requested match_by key; dedup was asked for, so it is not created blind.");

        if (createdInThisBatch.TryGetValue(keyValue, out var earlier))
        {
            return new AddContactsItemResult(index, AddContactsItemStatus.AlreadyMember, earlier.PublicId, false, MembershipState.Enrolled, null);
        }

        var matches = await FindMatchesAsync(matchKey, keyValue, cancellationToken).ConfigureAwait(false);
        if (matches.Count > 1)
        {
            throw new ConflictException("ambiguous_match", "more than one contact matches the match_by key; resolve the duplicates and import again.");
        }

        if (matches.Count == 0)
        {
            var created = NewContact(draft, now);
            createdInThisBatch[keyValue] = created;
            return Enrol(campaign, created, index, memberships, now, contactCreated: true, explicitReference: false);
        }

        // The match decides identity and nothing else: the payload never patches the contact it found, or an
        // import would silently overwrite work done through contact.update.
        return Enrol(campaign, matches[0], index, memberships, now, contactCreated: false, explicitReference: false);
    }

    private Contact NewContact(ContactDraft draft, DateTime now)
    {
        var contact = ContactService.NewContact(draft, now);
        db.Contacts.Add(contact);
        return contact;
    }

    private AddContactsItemResult Enrol(
        Campaign campaign,
        Contact contact,
        int index,
        Dictionary<int, CampaignContact> memberships,
        DateTime now,
        bool contactCreated,
        bool explicitReference)
    {
        if (contact.Id != 0 && memberships.TryGetValue(contact.Id, out var existing))
        {
            // Naming a contact by id is a deliberate act and re-enrols it; a match found during an import
            // never does, or a bulk re-import would quietly undo every removal the user made.
            if (existing.State == MembershipState.Excluded && explicitReference)
            {
                existing.State = MembershipState.Enrolled;
                existing.UpdatedAt = now;
                return new AddContactsItemResult(index, AddContactsItemStatus.Added, contact.PublicId, false, MembershipState.Enrolled, null);
            }

            return new AddContactsItemResult(index, AddContactsItemStatus.AlreadyMember, contact.PublicId, false, existing.State, null);
        }

        var membership = new CampaignContact
        {
            CampaignId = campaign.Id,
            Contact = contact,
            State = MembershipState.Enrolled,
            AddedAt = now,
            UpdatedAt = now,
        };

        db.CampaignContacts.Add(membership);
        if (contact.Id != 0)
        {
            memberships[contact.Id] = membership;
        }

        return new AddContactsItemResult(index, AddContactsItemStatus.Added, contact.PublicId, contactCreated, MembershipState.Enrolled, null);
    }

    /// <summary>At most two matches are fetched: one is a hit, two is enough to know the key is ambiguous.</summary>
    private async Task<List<Contact>> FindMatchesAsync(MatchKey key, string value, CancellationToken cancellationToken)
    {
        if (key is MatchKey.ByChannel byChannel)
        {
            var channel = byChannel.Channel;
            var ids = await db.ContactChannels
                .Where(entry => entry.Channel == channel && entry.Value == value && entry.Contact!.ArchivedAt == null)
                .Select(entry => entry.ContactId)
                .Distinct()
                .Take(2)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return await db.Contacts
                .Include(contact => contact.Channels)
                .Where(contact => ids.Contains(contact.Id))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        // The field name is validated to letters, digits and underscores, and both it and the value travel as
        // parameters, so the JSON path cannot carry anything but a field name.
        var path = "$." + ((MatchKey.ByCustomField)key).Field;
        return await db.Contacts
            .FromSqlInterpolated($"SELECT * FROM contacts WHERE archived_at IS NULL AND CAST(json_extract(custom_json, {path}) AS TEXT) = {value}")
            .Include(contact => contact.Channels)
            .Take(2)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static bool HasPayload(AddContactsItem item) =>
        item.FirstName is not null
        || item.LastName is not null
        || item.Company is not null
        || item.Title is not null
        || item.TimeZone is not null
        || item.Channels is not null
        || item.Custom is not null;

    /// <summary>Per-item problems are addressed by row so the caller can find the line in the file it sent.</summary>
    private static string ItemField(int index, string? name = null) =>
        name is null
            ? string.Create(CultureInfo.InvariantCulture, $"contacts[{index}]")
            : string.Create(CultureInfo.InvariantCulture, $"contacts[{index}].{name}");

    private static ErrorBody ErrorOf(DomainException ex) => new(ex.Code, ex.Message, ex.Retryable, ex.Details);

    private static JsonArray Ids(List<string> ids) => new([.. ids.Select(id => (JsonNode?)JsonValue.Create(id))]);
}
