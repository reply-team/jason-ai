using System.Globalization;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Runtime.Campaigns;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.WorkItems;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Contacts;

/// <summary>
/// Who is in a campaign. Adding is an import: every row reports its own outcome and a bad row never fails the
/// batch, because a thousand-line file with one broken address must not cost the caller the other 999.
/// Removal is an exclusion rather than a deletion, so a re-import cannot quietly undo it.
/// </summary>
public sealed class MembershipService(JasonDbContext db, JournalWriter journal, TimeProvider clock, WorkItemCanceller canceller)
{
    public const int MaxBatch = 1000;

    public async Task<AddContactsResult> AddAsync(AddContactsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var actor = Actors.Resolve(request.Actor);
        var campaign = await CampaignService.LoadAsync(db, request.CampaignId, cancellationToken).ConfigureAwait(false);
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
        var campaign = await CampaignService.LoadAsync(db, request.CampaignId, cancellationToken).ConfigureAwait(false);
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
            var excludedContactIds = new List<int>();
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
                excludedContactIds.Add(contact.Id);
                results.Add(new RemoveContactsItemResult(index, RemoveContactsItemStatus.Removed, contact.PublicId, null));
            }

            // Removing somebody is "stop touching this person here": their open work in this campaign would
            // contradict that if it were left to run.
            foreach (var contactId in excludedContactIds)
            {
                await canceller.CancelOpenAsync(db, campaign.Id, contactId, actor, "contact removed from campaign", cancellationToken).ConfigureAwait(false);
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
            canceller.StopCancelled();

            return new RemoveContactsResult(campaign.PublicId, new RemoveContactsSummary(removed, notMember, alreadyExcluded, rejected), results);
        }
    }

    public async Task<Page<MembershipItemDto>> ListAsync(ListContactsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var text = ValidateListing(request, out var period);
        List<(int Id, string PublicId)> campaigns = request.CampaignId is not null
            ? [await OneAsync(request.CampaignId, cancellationToken).ConfigureAwait(false)]
            : await CampaignNumbers.ResolveAsync(db, request.CampaignIds, cancellationToken).ConfigureAwait(false);
        var ids = campaigns.Select(campaign => campaign.Id).ToList();
        var publicIds = campaigns.ToDictionary(campaign => campaign.Id, campaign => campaign.PublicId);
        var order = campaigns.Select((campaign, index) => (campaign.Id, index)).ToDictionary(pair => pair.Id, pair => pair.index);
        var limit = Paging.ResolveLimit(request.Limit);

        // A summary count decides who is listed by its own definition — the counts' — so it takes no state. Under an activity
        // count the moments decide who is listed, and the page's people's memberships are few: one is counted exactly when
        // the count's why says what happened in it, removed since or not.
        IQueryable<CampaignContact> memberships;
        if (request.Summary is { } summary)
        {
            memberships = CampaignPeople.Matching(db, ids, summary);
        }
        else if (request.Activity is not null)
        {
            memberships = db.CampaignContacts.Where(membership => ids.Contains(membership.CampaignId));
        }
        else
        {
            // Excluded members are the answer to "who did we take out", never to "who is in this campaign".
            memberships = db.CampaignContacts.Where(membership => ids.Contains(membership.CampaignId));
            memberships = request.State is { } state
                ? memberships.Where(membership => membership.State == state)
                : memberships.Where(membership => membership.State != MembershipState.Excluded);
        }

        var (people, next) = request.Activity is { } happened
            ? await ByMomentAsync(ids, happened, period, text, request.Cursor, limit, cancellationToken).ConfigureAwait(false)
            : await ByIdAsync(memberships, text, request.Cursor, limit, cancellationToken).ConfigureAwait(false);

        var loaded = await db.Contacts.AsNoTracking()
            .Include(contact => contact.Channels)
            .Include(contact => contact.ExternalIds)
            .Where(contact => people.Contains(contact.Id))
            .ToDictionaryAsync(contact => contact.Id, cancellationToken)
            .ConfigureAwait(false);
        var page = people.Select(id => loaded[id]).ToList();
        var contacts = await ContactService.WithCampaignsAndLatestLineAsync(db, page, cancellationToken).ConfigureAwait(false);

        var counted = await memberships
            .Where(membership => people.Contains(membership.ContactId))
            .Select(membership => new { membership.ContactId, membership.CampaignId, membership.State, membership.AddedAt, membership.UpdatedAt })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var pairs = counted.Select(membership => (membership.ContactId, membership.CampaignId)).ToList();
        var why = request switch
        {
            { Summary: { } count } => await CampaignWhy.ForSummaryAsync(db, pairs, page, count, cancellationToken).ConfigureAwait(false),
            { Activity: { } count } => await CampaignWhy.ForActivityAsync(db, pairs, page, count, period.Since, period.Until, cancellationToken).ConfigureAwait(false),
            _ => null,
        };
        if (request.Activity is not null)
        {
            counted = [.. counted.Where(membership => why!.ContainsKey((membership.ContactId, membership.CampaignId)))];
        }

        var items = new List<MembershipItemDto>(page.Count);
        foreach (var (contact, dto) in page.Zip(contacts))
        {
            var mine = counted
                .Where(membership => membership.ContactId == contact.Id)
                .Select(membership => (Membership: membership, Why: why?.GetValueOrDefault((contact.Id, membership.CampaignId))))
                .OrderByDescending(entry => request.Activity is null ? null : entry.Why?.At)
                .ThenBy(entry => order[entry.Membership.CampaignId])
                .ToList();
            if (mine.Count == 0)
            {
                continue;
            }

            var lead = mine[0];
            items.Add(new MembershipItemDto(
                dto,
                lead.Membership.State,
                ContactMapper.ToOffset(lead.Membership.AddedAt),
                ContactMapper.ToOffset(lead.Membership.UpdatedAt),
                lead.Why,
                [.. mine.Select(entry => new CountedIn(publicIds[entry.Membership.CampaignId], entry.Why))]));
        }

        return new Page<MembershipItemDto>(items, next);
    }

    private async Task<(int Id, string PublicId)> OneAsync(string campaignId, CancellationToken cancellationToken)
    {
        var campaign = await CampaignService.LoadAsync(db, campaignId, cancellationToken).ConfigureAwait(false);
        return (campaign.Id, campaign.PublicId);
    }

    /// <summary>A page of the people these memberships name, once each, by their public id.</summary>
    private async Task<(List<int> People, string? Next)> ByIdAsync(
        IQueryable<CampaignContact> memberships, string? text, string? cursor, int limit, CancellationToken cancellationToken)
    {
        var fetched = await PageById(db, memberships, text, Paging.DecodeCursor(cursor), limit + 1)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var kept = fetched.Take(limit).ToList();
        return ([.. kept.Select(person => person.Id)], fetched.Count > limit ? Paging.EncodeCursor(kept[^1].PublicId) : null);
    }

    /// <summary>
    /// The query behind <see cref="ByIdAsync"/>'s page, apart so that its plan can be read. It starts from the memberships, so
    /// it grows with the campaigns asked: started from the contacts, SQLite walks every contact of the installation.
    /// </summary>
    internal static IQueryable<PersonById> PageById(
        JasonDbContext db, IQueryable<CampaignContact> memberships, string? text, string? after, int take)
    {
        var people = memberships.Select(membership => new { membership.ContactId, membership.Contact!.PublicId }).Distinct();
        if (text is not null)
        {
            var found = ContactSearch.Apply(db.Contacts, text);
            people = people.Where(person => found.Any(contact => contact.Id == person.ContactId));
        }

        if (after is not null)
        {
            people = people.Where(person => string.Compare(person.PublicId, after) > 0);
        }

        return people
            .OrderBy(person => person.PublicId)
            .Select(person => new PersonById(person.ContactId, person.PublicId))
            .Take(take);
    }

    internal sealed record PersonById(int Id, string PublicId);

    /// <summary>
    /// A page of the people one activity count counts, once each, the latest to whom it happened first; the cursor carries
    /// the moment and the public id of the last one shown.
    /// </summary>
    private async Task<(List<int> People, string? Next)> ByMomentAsync(
        List<int> campaigns,
        CampaignActivityFilter count,
        (DateTime Since, DateTime Until) period,
        string? text,
        string? cursor,
        int limit,
        CancellationToken cancellationToken)
    {
        // A person's place is the latest moment it happened to them, so a page needs only what happened since the earliest
        // moment it shows: the window reads back from the page's top, growing until it holds a page or reaches the period's
        // start. Whoever's latest moment falls inside it is found with that moment; nobody outside it could rank higher.
        var after = MomentCursor(cursor);
        var top = after is { } last ? DateTimeOffset.FromUnixTimeMilliseconds(last.Ms).UtcDateTime : period.Until;
        var span = TimeSpan.FromTicks(Math.Max(TimeSpan.FromHours(1).Ticks, (period.Until - period.Since).Ticks / 32));
        List<PersonMoment> fetched;
        while (true)
        {
            // Compared as spans: near the calendar's start, top - span would fall before it.
            var from = top - period.Since <= span ? period.Since : top - span;
            fetched = await PageByMoment(db, campaigns, count, from, period.Until, text, after, limit + 1)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            if (fetched.Count > limit || from == period.Since)
            {
                break;
            }

            span *= 4;
        }

        var kept = fetched.Take(limit).ToList();
        var next = fetched.Count > limit
            ? Paging.EncodeCursor(string.Create(CultureInfo.InvariantCulture, $"{kept[^1].Ms}|{kept[^1].PublicId}"))
            : null;
        return ([.. kept.Select(row => row.ContactId)], next);
    }

    /// <summary>The query behind one pass of <see cref="ByMomentAsync"/>'s window, apart so that its plan can be read.</summary>
    internal static IQueryable<PersonMoment> PageByMoment(
        JasonDbContext db,
        List<int> campaigns,
        CampaignActivityFilter count,
        DateTime from,
        DateTime until,
        string? text,
        (long Ms, string PublicId)? after,
        int take)
    {
        var rows = CampaignPeople.ByMoment(db, campaigns, count, from, until);
        if (text is not null)
        {
            var found = ContactSearch.Apply(db.Contacts, text);
            rows = rows.Where(row => found.Any(contact => contact.Id == row.ContactId));
        }

        if (after is { } a)
        {
            rows = rows.Where(row => row.Ms < a.Ms || (row.Ms == a.Ms && string.Compare(row.PublicId, a.PublicId) > 0));
        }

        return rows.OrderByDescending(row => row.Ms).ThenBy(row => row.PublicId).Take(take);
    }

    private static readonly long MinMs = DateTimeOffset.MinValue.ToUnixTimeMilliseconds();

    private static readonly long MaxMs = DateTimeOffset.MaxValue.ToUnixTimeMilliseconds();

    private static (long Ms, string PublicId)? MomentCursor(string? cursor)
    {
        if (Paging.DecodeCursor(cursor) is not { } decoded)
        {
            return null;
        }

        var bar = decoded.IndexOf('|', StringComparison.Ordinal);
        if (bar <= 0
            || !long.TryParse(decoded.AsSpan(0, bar), NumberStyles.Integer, CultureInfo.InvariantCulture, out var ms)
            || ms < MinMs || ms > MaxMs)
        {
            throw new ValidationException([new ErrorDetail("cursor", "invalid", "cursor must be the next_cursor returned by an earlier page.")]);
        }

        return (ms, decoded[(bar + 1)..]);
    }

    /// <summary>One filter at most; an activity's period, and a period only with an activity; no state beside a filter.</summary>
    private static string? ValidateListing(ListContactsRequest request, out (DateTime Since, DateTime Until) period)
    {
        period = default;
        var errors = new ValidationErrors();
        if (request.CampaignId is not null && request.CampaignIds is not null)
        {
            errors.Add("campaign_ids", "invalid", "Name the campaigns in campaign_ids or one in campaign_id, not both.");
        }

        if (request.Summary is not null && request.Activity is not null)
        {
            errors.Add("activity", "invalid", "Ask for one count: summary or activity, not both.");
        }

        if ((request.Summary is not null || request.Activity is not null) && request.State is not null)
        {
            errors.Add("state", "invalid", "A count decides who is listed; send state without summary or activity.");
        }

        if (request.Activity is null && (request.Since is not null || request.Until is not null))
        {
            errors.Add(request.Since is not null ? "since" : "until", "invalid", "since and until bound an activity; send activity as well.");
        }

        var text = string.IsNullOrWhiteSpace(request.Query) ? null : request.Query.Trim();
        if (text is { Length: > ContactSearch.MaxLength })
        {
            errors.Add("query", "too_long", string.Create(CultureInfo.InvariantCulture, $"query must be at most {ContactSearch.MaxLength} characters."));
        }

        errors.ThrowIfAny();
        if (request.Activity is not null && request.Summary is null)
        {
            period = CampaignNumbers.Period(request.Since, request.Until);
        }

        return text;
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

    /// <summary>
    /// At most two matches are fetched: one is a hit, two is enough to know the key is ambiguous. Which two is decided by
    /// the contact's own key, so the answer to the same rows is the same answer, and EF Core has no unordered set to warn
    /// about taking a part of.
    /// </summary>
    private async Task<List<Contact>> FindMatchesAsync(MatchKey key, string value, CancellationToken cancellationToken)
    {
        if (key is MatchKey.ByChannel byChannel)
        {
            var channel = byChannel.Channel;
            var ids = await db.ContactChannels
                .Where(entry => entry.Channel == channel && entry.Value == value && entry.Contact!.ArchivedAt == null)
                .Select(entry => entry.ContactId)
                .Distinct()
                .OrderBy(id => id)
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
            .OrderBy(contact => contact.Id)
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
