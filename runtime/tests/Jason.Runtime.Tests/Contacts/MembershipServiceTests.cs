using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Ids;
using Jason.Runtime.Contacts;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Contacts;

public class MembershipServiceTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);

    private static MembershipService NewService(JasonDbContext db, TimeProvider clock) => new(db, new JournalWriter(clock), clock);

    private static ContactService NewContacts(JasonDbContext db, TimeProvider clock) => new(db, new JournalWriter(clock), clock);

    private static Campaign NewCampaign(JasonDbContext db, DateTime? archivedAt = null)
    {
        var campaign = new Campaign
        {
            PublicId = PublicId.New("cmp"),
            Name = "Import",
            Status = archivedAt is null ? CampaignStatus.Draft : CampaignStatus.Archived,
            CreatedAt = Noon.UtcDateTime,
            UpdatedAt = Noon.UtcDateTime,
            ArchivedAt = archivedAt,
        };
        db.Campaigns.Add(campaign);
        db.SaveChanges();
        return campaign;
    }

    private static ChannelInput Email(string value) => new("email", value, null, null, null);

    private static AddContactsItem Payload(string? firstName = null, IReadOnlyList<ChannelInput>? channels = null, JsonObject? custom = null) =>
        new(null, firstName, null, null, null, null, channels, custom);

    private static AddContactsItem ById(string contactId) => new(contactId, null, null, null, null, null, null, null);

    private static AddContactsRequest Add(Campaign campaign, string? matchBy, params AddContactsItem[] items) =>
        new(campaign.PublicId, matchBy, items, null, "an import");

    [Fact]
    public async Task A_payload_without_match_by_creates_a_contact_and_enrols_it()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var campaign = NewCampaign(db);
        var service = NewService(db, new FixedClock(Noon));

        var result = await service.AddAsync(Add(campaign, null, Payload("Ada", [Email("a@b.co")])), TestContext.Current.CancellationToken);

        Assert.Equal(campaign.PublicId, result.CampaignId);
        Assert.Equal(new AddContactsSummary(1, 0, 0), result.Summary);
        var item = Assert.Single(result.Items);
        Assert.Equal(0, item.Index);
        Assert.Equal(AddContactsItemStatus.Added, item.Status);
        Assert.True(item.ContactCreated);
        Assert.Equal(MembershipState.Enrolled, item.State);
        Assert.StartsWith("cnt_", item.ContactId!, StringComparison.Ordinal);
        Assert.Single(await db.Contacts.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Single(await db.CampaignContacts.ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task The_same_contact_id_twice_in_one_batch_reports_the_second_as_already_a_member()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var clock = new FixedClock(Noon);
        var campaign = NewCampaign(db);
        var existing = await NewContacts(db, clock).CreateAsync(
            new ContactCreateRequest("Ada", null, null, null, null, null, null, null, null),
            TestContext.Current.CancellationToken);
        var service = NewService(db, clock);

        var result = await service.AddAsync(Add(campaign, null, ById(existing.Id), ById(existing.Id)), TestContext.Current.CancellationToken);

        Assert.Equal(new AddContactsSummary(1, 1, 0), result.Summary);
        Assert.Equal(AddContactsItemStatus.Added, result.Items[0].Status);
        Assert.False(result.Items[0].ContactCreated);
        Assert.Equal(AddContactsItemStatus.AlreadyMember, result.Items[1].Status);
        Assert.Equal(MembershipState.Enrolled, result.Items[1].State);
        Assert.Single(await db.CampaignContacts.ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_contact_id_item_that_also_carries_payload_fields_is_rejected()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var clock = new FixedClock(Noon);
        var campaign = NewCampaign(db);
        var existing = await NewContacts(db, clock).CreateAsync(
            new ContactCreateRequest("Ada", null, null, null, null, null, null, null, null),
            TestContext.Current.CancellationToken);
        var service = NewService(db, clock);

        var item = new AddContactsItem(existing.Id, "Grace", null, null, null, null, null, null);
        var result = await service.AddAsync(Add(campaign, null, item), TestContext.Current.CancellationToken);

        var rejected = Assert.Single(result.Items);
        Assert.Equal(AddContactsItemStatus.Rejected, rejected.Status);
        Assert.Equal("validation_failed", rejected.Error!.Code);
        var detail = Assert.Single(rejected.Error.Details!);
        Assert.Equal("contacts[0].contact_id", detail.Field);
        Assert.Equal("not_allowed", detail.Code);
        Assert.Empty(await db.CampaignContacts.ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_match_by_hit_adds_the_matched_contact_and_never_patches_it()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var clock = new FixedClock(Noon);
        var campaign = NewCampaign(db);
        var existing = await NewContacts(db, clock).CreateAsync(
            new ContactCreateRequest("Ada", null, "Analytical Engines", null, null, [Email("a@b.co")], null, null, null),
            TestContext.Current.CancellationToken);
        var service = NewService(db, clock);

        var result = await service.AddAsync(Add(campaign, "email", Payload("Grace", [Email(" A@B.CO ")])), TestContext.Current.CancellationToken);

        var item = Assert.Single(result.Items);
        Assert.Equal(AddContactsItemStatus.Added, item.Status);
        Assert.False(item.ContactCreated);
        Assert.Equal(existing.Id, item.ContactId);

        var stored = Assert.Single(await db.Contacts.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal("Ada", stored.FirstName);
        Assert.Equal("Analytical Engines", stored.Company);
    }

    [Fact]
    public async Task Two_contacts_sharing_the_key_are_an_ambiguous_match_the_caller_has_to_resolve()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var clock = new FixedClock(Noon);
        var campaign = NewCampaign(db);
        var contacts = NewContacts(db, clock);
        await contacts.CreateAsync(new ContactCreateRequest("Ada", null, null, null, null, [Email("a@b.co")], null, null, null), TestContext.Current.CancellationToken);
        await contacts.CreateAsync(new ContactCreateRequest("Grace", null, null, null, null, [Email("a@b.co")], null, null, null), TestContext.Current.CancellationToken);
        var service = NewService(db, clock);

        var result = await service.AddAsync(Add(campaign, "email", Payload(channels: [Email("a@b.co")])), TestContext.Current.CancellationToken);

        var item = Assert.Single(result.Items);
        Assert.Equal(AddContactsItemStatus.Rejected, item.Status);
        Assert.Equal("ambiguous_match", item.Error!.Code);
        Assert.Empty(await db.CampaignContacts.ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task An_item_that_carries_no_match_key_is_rejected_rather_than_silently_created()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var campaign = NewCampaign(db);
        var service = NewService(db, new FixedClock(Noon));

        var result = await service.AddAsync(Add(campaign, "email", Payload("Ada")), TestContext.Current.CancellationToken);

        var item = Assert.Single(result.Items);
        Assert.Equal(AddContactsItemStatus.Rejected, item.Status);
        Assert.Equal("no_match_key", item.Error!.Code);
        Assert.Empty(await db.Contacts.ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Two_rows_with_the_same_new_key_create_one_contact()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var campaign = NewCampaign(db);
        var service = NewService(db, new FixedClock(Noon));

        var result = await service.AddAsync(
            Add(campaign, "email", Payload("Ada", [Email("a@b.co")]), Payload("Ada again", [Email("A@b.co")])),
            TestContext.Current.CancellationToken);

        Assert.Equal(new AddContactsSummary(1, 1, 0), result.Summary);
        Assert.True(result.Items[0].ContactCreated);
        Assert.Equal(AddContactsItemStatus.AlreadyMember, result.Items[1].Status);
        Assert.Equal(result.Items[0].ContactId, result.Items[1].ContactId);
        Assert.Single(await db.Contacts.ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_custom_field_can_be_the_match_key()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var clock = new FixedClock(Noon);
        var campaign = NewCampaign(db);
        var existing = await NewContacts(db, clock).CreateAsync(
            new ContactCreateRequest("Ada", null, null, null, null, null, new JsonObject { ["crm_id"] = "A-1" }, null, null),
            TestContext.Current.CancellationToken);
        var service = NewService(db, clock);

        var result = await service.AddAsync(
            Add(campaign, "custom:crm_id", Payload("Ada", custom: new JsonObject { ["crm_id"] = "A-1" }), Payload("New", custom: new JsonObject { ["crm_id"] = "A-2" })),
            TestContext.Current.CancellationToken);

        Assert.Equal(new AddContactsSummary(2, 0, 0), result.Summary);
        Assert.Equal(existing.Id, result.Items[0].ContactId);
        Assert.False(result.Items[0].ContactCreated);
        Assert.True(result.Items[1].ContactCreated);
    }

    [Fact]
    public async Task An_invalid_row_is_rejected_by_index_while_the_rest_of_the_batch_lands()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var campaign = NewCampaign(db);
        var service = NewService(db, new FixedClock(Noon));

        var result = await service.AddAsync(
            Add(campaign, null, Payload("Bad", [Email("not-an-address")]), Payload("Good", [Email("good@example.com")])),
            TestContext.Current.CancellationToken);

        Assert.Equal(new AddContactsSummary(1, 0, 1), result.Summary);
        Assert.Equal("validation_failed", result.Items[0].Error!.Code);
        Assert.Equal("contacts[0].channels[0].value", result.Items[0].Error!.Details![0].Field);
        Assert.Equal(AddContactsItemStatus.Added, result.Items[1].Status);
        Assert.Single(await db.Contacts.ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task An_archived_or_unknown_contact_referenced_by_id_is_rejected()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var clock = new FixedClock(Noon);
        var campaign = NewCampaign(db);
        var contacts = NewContacts(db, clock);
        var gone = await contacts.CreateAsync(new ContactCreateRequest("Ada", null, null, null, null, null, null, null, null), TestContext.Current.CancellationToken);
        await contacts.ArchiveAsync(new ContactArchiveRequest(gone.Id, null, "left"), TestContext.Current.CancellationToken);
        var service = NewService(db, clock);

        var result = await service.AddAsync(Add(campaign, null, ById(gone.Id), ById("cnt_missing")), TestContext.Current.CancellationToken);

        Assert.Equal(new AddContactsSummary(0, 0, 2), result.Summary);
        Assert.Equal("contact_archived", result.Items[0].Error!.Code);
        Assert.Equal("contact_not_found", result.Items[1].Error!.Code);
    }

    [Fact]
    public async Task An_import_never_re_enrols_an_excluded_member_but_an_explicit_reference_does()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var clock = new FixedClock(Noon);
        var campaign = NewCampaign(db);
        var existing = await NewContacts(db, clock).CreateAsync(
            new ContactCreateRequest("Ada", null, null, null, null, [Email("a@b.co")], null, null, null),
            TestContext.Current.CancellationToken);
        var service = NewService(db, clock);
        await service.AddAsync(Add(campaign, null, ById(existing.Id)), TestContext.Current.CancellationToken);
        await service.RemoveAsync(new RemoveContactsRequest(campaign.PublicId, [existing.Id], null, "asked to stop"), TestContext.Current.CancellationToken);

        var reimport = await service.AddAsync(Add(campaign, "email", Payload(channels: [Email("a@b.co")])), TestContext.Current.CancellationToken);
        Assert.Equal(AddContactsItemStatus.AlreadyMember, reimport.Items[0].Status);
        Assert.Equal(MembershipState.Excluded, reimport.Items[0].State);

        var deliberate = await service.AddAsync(Add(campaign, null, ById(existing.Id)), TestContext.Current.CancellationToken);
        Assert.Equal(AddContactsItemStatus.Added, deliberate.Items[0].Status);
        Assert.Equal(MembershipState.Enrolled, deliberate.Items[0].State);
    }

    [Fact]
    public async Task A_batch_past_the_limit_and_an_archived_campaign_fail_the_whole_call()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var campaign = NewCampaign(db);
        var archived = NewCampaign(db, Noon.UtcDateTime);
        var service = NewService(db, new FixedClock(Noon));

        var tooBig = await Assert.ThrowsAsync<InvalidRequestException>(() =>
            service.AddAsync(Add(campaign, null, [.. Enumerable.Range(0, MembershipService.MaxBatch + 1).Select(_ => Payload("x"))]), TestContext.Current.CancellationToken));
        Assert.Equal("batch_too_large", tooBig.Code);

        var closed = await Assert.ThrowsAsync<ConflictException>(() =>
            service.AddAsync(Add(archived, null, Payload("x")), TestContext.Current.CancellationToken));
        Assert.Equal("campaign_archived", closed.Code);

        var unknown = await Assert.ThrowsAsync<NotFoundException>(() =>
            service.AddAsync(new AddContactsRequest("cmp_missing", null, [Payload("x")], null, null), TestContext.Current.CancellationToken));
        Assert.Equal("campaign_not_found", unknown.Code);
    }

    [Fact]
    public async Task One_contacts_added_entry_records_the_whole_batch()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var campaign = NewCampaign(db);
        var service = NewService(db, new FixedClock(Noon));

        var result = await service.AddAsync(
            Add(campaign, null, Payload("Ada", [Email("a@b.co")]), Payload("Bad", [Email("nope")])),
            TestContext.Current.CancellationToken);

        var entry = Assert.Single(await db.Journal.Where(e => e.Kind == JournalKinds.ContactsAdded).ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(campaign.Id, entry.CampaignId);
        Assert.Equal("an import", entry.Reason);
        var written = entry.New!.AsObject();
        Assert.Equal(1, (int?)written["added"]);
        Assert.Equal(0, (int?)written["already_member"]);
        Assert.Equal(1, (int?)written["rejected"]);
        Assert.Equal([result.Items[0].ContactId], written["contact_ids"]!.AsArray().Select(n => (string?)n));
        Assert.Equal([result.Items[0].ContactId], written["created_contact_ids"]!.AsArray().Select(n => (string?)n));
    }

    [Fact]
    public async Task Removing_excludes_the_membership_and_names_every_other_outcome()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var clock = new FixedClock(Noon);
        var campaign = NewCampaign(db);
        var contacts = NewContacts(db, clock);
        var member = await contacts.CreateAsync(new ContactCreateRequest("Ada", null, null, null, null, null, null, null, null), TestContext.Current.CancellationToken);
        var stranger = await contacts.CreateAsync(new ContactCreateRequest("Grace", null, null, null, null, null, null, null, null), TestContext.Current.CancellationToken);
        var service = NewService(db, clock);
        await service.AddAsync(Add(campaign, null, ById(member.Id)), TestContext.Current.CancellationToken);

        var result = await service.RemoveAsync(
            new RemoveContactsRequest(campaign.PublicId, [member.Id, member.Id, stranger.Id, "cnt_missing"], null, "asked to stop"),
            TestContext.Current.CancellationToken);

        Assert.Equal(new RemoveContactsSummary(1, 1, 1, 1), result.Summary);
        Assert.Equal(RemoveContactsItemStatus.Removed, result.Items[0].Status);
        Assert.Equal(RemoveContactsItemStatus.AlreadyExcluded, result.Items[1].Status);
        Assert.Equal(RemoveContactsItemStatus.NotMember, result.Items[2].Status);
        Assert.Equal(RemoveContactsItemStatus.Rejected, result.Items[3].Status);
        Assert.Equal("contact_not_found", result.Items[3].Error!.Code);

        var entry = Assert.Single(await db.Journal.Where(e => e.Kind == JournalKinds.ContactsRemoved).ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal("asked to stop", entry.Reason);
        Assert.Equal([member.Id], entry.New!.AsObject()["contact_ids"]!.AsArray().Select(n => (string?)n));
    }

    [Fact]
    public async Task A_listing_hides_excluded_members_unless_they_are_asked_for_and_never_leaves_its_campaign()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var clock = new FixedClock(Noon);
        var ours = NewCampaign(db);
        var theirs = NewCampaign(db);
        var contacts = NewContacts(db, clock);
        var kept = await contacts.CreateAsync(new ContactCreateRequest("Ada", null, null, null, null, null, null, null, null), TestContext.Current.CancellationToken);
        var dropped = await contacts.CreateAsync(new ContactCreateRequest("Grace", null, null, null, null, null, null, null, null), TestContext.Current.CancellationToken);
        var elsewhere = await contacts.CreateAsync(new ContactCreateRequest("Alan", null, null, null, null, null, null, null, null), TestContext.Current.CancellationToken);
        var service = NewService(db, clock);
        await service.AddAsync(Add(ours, null, ById(kept.Id), ById(dropped.Id)), TestContext.Current.CancellationToken);
        await service.AddAsync(Add(theirs, null, ById(elsewhere.Id)), TestContext.Current.CancellationToken);
        await service.RemoveAsync(new RemoveContactsRequest(ours.PublicId, [dropped.Id], null, "asked to stop"), TestContext.Current.CancellationToken);

        var visible = await service.ListAsync(new ListContactsRequest(ours.PublicId, null, null, null), TestContext.Current.CancellationToken);
        var item = Assert.Single(visible.Items);
        Assert.Equal(kept.Id, item.Contact.Id);
        Assert.Equal(MembershipState.Enrolled, item.State);
        Assert.Equal(Noon, item.AddedAt);

        var excluded = await service.ListAsync(new ListContactsRequest(ours.PublicId, MembershipState.Excluded, null, null), TestContext.Current.CancellationToken);
        Assert.Equal(dropped.Id, Assert.Single(excluded.Items).Contact.Id);

        var other = await service.ListAsync(new ListContactsRequest(theirs.PublicId, null, null, null), TestContext.Current.CancellationToken);
        Assert.Equal(elsewhere.Id, Assert.Single(other.Items).Contact.Id);
    }

    [Fact]
    public async Task A_membership_listing_walks_its_pages()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var campaign = NewCampaign(db);
        var service = NewService(db, new FixedClock(Noon));
        var added = await service.AddAsync(
            Add(campaign, null, [.. Enumerable.Range(0, 5).Select(index => Payload("c" + index))]),
            TestContext.Current.CancellationToken);

        var seen = new List<string>();
        string? cursor = null;
        do
        {
            var page = await service.ListAsync(new ListContactsRequest(campaign.PublicId, null, 2, cursor), TestContext.Current.CancellationToken);
            seen.AddRange(page.Items.Select(member => member.Contact.Id));
            cursor = page.NextCursor;
        }
        while (cursor is not null);

        Assert.Equal(added.Items.Select(item => item.ContactId!).Order(StringComparer.Ordinal), seen);
    }
}
