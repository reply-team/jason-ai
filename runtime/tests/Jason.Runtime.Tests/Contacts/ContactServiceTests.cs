using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Json;
using Jason.Runtime.Contacts;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Contacts;

public class ContactServiceTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);

    private static ContactService NewService(JasonDbContext db, TimeProvider clock) => new(db, new JournalWriter(clock), clock, TestCanceller.New(clock));

    private static ContactCreateRequest Create(
        string? firstName = null,
        IReadOnlyList<ChannelInput>? channels = null,
        JsonObject? custom = null,
        string? timeZone = null,
        string? title = null) =>
        new(firstName, null, null, title, timeZone, channels, custom, null, null);

    private static ChannelInput Channel(string channel, string value, string? label = null, bool? primary = null) =>
        new(channel, value, label, primary, null);

    [Fact]
    public async Task A_contact_needs_no_channel_at_all_and_is_journaled_globally()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var service = NewService(db, new FixedClock(Noon));

        var contact = await service.CreateAsync(Create(firstName: "  Ada  "), TestContext.Current.CancellationToken);

        Assert.StartsWith("cnt_", contact.Id, StringComparison.Ordinal);
        Assert.Equal("Ada", contact.FirstName);
        Assert.Empty(contact.Channels);
        Assert.Empty(contact.Custom);
        Assert.Equal(Noon, contact.CreatedAt);
        Assert.Equal(Noon, contact.UpdatedAt);
        Assert.Null(contact.ArchivedAt);

        var entry = Assert.Single(await db.Journal.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(JournalKinds.ContactCreated, entry.Kind);
        Assert.Null(entry.CampaignId);
        Assert.Equal(contact.Id, entry.Key);
    }

    [Fact]
    public async Task Two_addresses_on_one_channel_are_both_kept_and_the_primary_comes_first()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var service = NewService(db, new FixedClock(Noon));

        var contact = await service.CreateAsync(
            Create(channels: [Channel("email", "work@Example.com", "work"), Channel("email", "HOME@example.com", "home", primary: true)]),
            TestContext.Current.CancellationToken);

        Assert.Collection(
            contact.Channels,
            first =>
            {
                Assert.Equal("home@example.com", first.Value);
                Assert.True(first.Primary);
                Assert.Equal("home", first.Label);
            },
            second =>
            {
                Assert.Equal("work@example.com", second.Value);
                Assert.False(second.Primary);
            });
    }

    [Fact]
    public async Task The_same_pair_twice_inside_one_contact_is_refused()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var service = NewService(db, new FixedClock(Noon));

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            service.CreateAsync(Create(channels: [Channel("email", "a@b.co"), Channel("email", " A@B.CO ")]), TestContext.Current.CancellationToken));

        var detail = Assert.Single(ex.Details!);
        Assert.Equal("channels[1]", detail.Field);
        Assert.Equal("duplicate", detail.Code);
    }

    [Fact]
    public async Task A_second_primary_on_the_same_channel_is_refused()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var service = NewService(db, new FixedClock(Noon));

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            service.CreateAsync(
                Create(channels: [Channel("email", "a@b.co", primary: true), Channel("email", "c@d.co", primary: true)]),
                TestContext.Current.CancellationToken));

        var detail = Assert.Single(ex.Details!);
        Assert.Equal("channels[1].primary", detail.Field);
        Assert.Equal("duplicate", detail.Code);
    }

    [Fact]
    public async Task Every_problem_in_one_payload_is_reported_at_once()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var service = NewService(db, new FixedClock(Noon));

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            service.CreateAsync(
                Create(firstName: new string('n', 201), timeZone: "Pacific Standard Time", channels: [Channel("email", "nope")]),
                TestContext.Current.CancellationToken));

        Assert.Equal(
            ["channels[0].value", "first_name", "time_zone"],
            ex.Details!.Select(d => d.Field).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task A_custom_object_past_the_limit_is_refused()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var service = NewService(db, new FixedClock(Noon));

        var custom = new JsonObject { ["notes"] = new string('x', ContactValidation.MaxCustomBytes) };

        var ex = await Assert.ThrowsAsync<ValidationException>(() => service.CreateAsync(Create(custom: custom), TestContext.Current.CancellationToken));

        var detail = Assert.Single(ex.Details!);
        Assert.Equal("custom", detail.Field);
        Assert.Equal("too_long", detail.Code);
    }

    [Fact]
    public async Task An_unknown_or_missing_contact_id_is_answered_precisely()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var service = NewService(db, new FixedClock(Noon));

        var missing = await Assert.ThrowsAsync<ValidationException>(() => service.GetAsync(new ContactGetRequest("  "), TestContext.Current.CancellationToken));
        Assert.Equal("contact_id", Assert.Single(missing.Details!).Field);

        var unknown = await Assert.ThrowsAsync<NotFoundException>(() => service.GetAsync(new ContactGetRequest("cnt_nope"), TestContext.Current.CancellationToken));
        Assert.Equal("contact_not_found", unknown.Code);
    }

    [Fact]
    public async Task A_listing_finds_a_contact_by_the_normalized_value_of_a_channel()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var service = NewService(db, new FixedClock(Noon));
        var wanted = await service.CreateAsync(Create(channels: [Channel("email", "foo@bar.com")]), TestContext.Current.CancellationToken);
        await service.CreateAsync(Create(channels: [Channel("email", "other@bar.com")]), TestContext.Current.CancellationToken);
        await service.CreateAsync(Create(channels: [Channel("linkedin", "https://example.com/in/x")]), TestContext.Current.CancellationToken);

        var byValue = await service.ListAsync(new ContactListRequest("Email", " FOO@bar.COM ", null, null, null), TestContext.Current.CancellationToken);
        Assert.Equal(wanted.Id, Assert.Single(byValue.Items).Id);

        var byChannel = await service.ListAsync(new ContactListRequest("email", null, null, null, null), TestContext.Current.CancellationToken);
        Assert.Equal(2, byChannel.Items.Count);
    }

    [Fact]
    public async Task A_value_filter_without_a_channel_is_a_usage_error()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var service = NewService(db, new FixedClock(Noon));

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            service.ListAsync(new ContactListRequest(null, "foo@bar.com", null, null, null), TestContext.Current.CancellationToken));

        Assert.Equal("value", Assert.Single(ex.Details!).Field);
    }

    [Fact]
    public async Task Archived_contacts_stay_out_of_a_listing_unless_they_are_asked_for()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var service = NewService(db, new FixedClock(Noon));
        var kept = await service.CreateAsync(Create(firstName: "kept"), TestContext.Current.CancellationToken);
        var gone = await service.CreateAsync(Create(firstName: "gone"), TestContext.Current.CancellationToken);
        await service.ArchiveAsync(new ContactArchiveRequest(gone.Id, null, "left the company"), TestContext.Current.CancellationToken);

        var visible = await service.ListAsync(new ContactListRequest(null, null, null, null, null), TestContext.Current.CancellationToken);
        Assert.Equal(kept.Id, Assert.Single(visible.Items).Id);

        var all = await service.ListAsync(new ContactListRequest(null, null, true, null, null), TestContext.Current.CancellationToken);
        Assert.Equal(2, all.Items.Count);
    }

    [Fact]
    public async Task A_listing_walks_its_pages_in_creation_order()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var service = NewService(db, new FixedClock(Noon));
        var created = new List<string>();
        for (var i = 0; i < 5; i++)
        {
            created.Add((await service.CreateAsync(Create(firstName: "c" + i), TestContext.Current.CancellationToken)).Id);
        }

        var seen = new List<string>();
        string? cursor = null;
        do
        {
            var page = await service.ListAsync(new ContactListRequest(null, null, null, 2, cursor), TestContext.Current.CancellationToken);
            seen.AddRange(page.Items.Select(c => c.Id));
            cursor = page.NextCursor;
        }
        while (cursor is not null);

        Assert.Equal(created.Order(StringComparer.Ordinal), seen);
    }

    [Fact]
    public async Task An_update_applies_only_the_fields_it_carries_and_journals_their_names()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var clock = new FixedClock(Noon);
        var service = NewService(db, clock);
        var created = await service.CreateAsync(
            Create(firstName: "Ada", title: "CTO", channels: [Channel("email", "a@b.co", "work", primary: true)], custom: new JsonObject { ["crm_id"] = "1" }),
            TestContext.Current.CancellationToken);

        clock.Advance(TimeSpan.FromHours(1));
        var updated = await service.UpdateAsync(
            new ContactUpdateRequest(
                created.Id,
                Optional<string?>.Absent,
                Optional<string?>.Absent,
                Optional<string?>.Absent,
                Optional<string?>.Of(null),
                Optional<string?>.Absent,
                Optional<IReadOnlyList<ChannelInput>?>.Of([Channel("linkedin", "https://example.com/in/ada")]),
                Optional<JsonObject?>.Of(new JsonObject { ["crm_id"] = "2" }),
                null,
                "cleanup"),
            TestContext.Current.CancellationToken);

        Assert.Equal("Ada", updated.FirstName);
        Assert.Null(updated.Title);
        Assert.Equal("https://example.com/in/ada", Assert.Single(updated.Channels).Value);
        Assert.Equal("2", (string?)updated.Custom["crm_id"]);
        Assert.Equal(Noon.AddHours(1), updated.UpdatedAt);

        var entry = Assert.Single(await db.Journal.Where(e => e.Kind == JournalKinds.ContactUpdated).ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(created.Id, entry.Key);
        Assert.Equal("cleanup", entry.Reason);
        Assert.Equal(["title", "channels", "custom"], entry.New!.AsArray().Select(n => (string?)n));
    }

    [Fact]
    public async Task An_update_that_changes_nothing_writes_no_journal_entry()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var service = NewService(db, new FixedClock(Noon));
        var created = await service.CreateAsync(Create(firstName: "Ada", channels: [Channel("email", "a@b.co")]), TestContext.Current.CancellationToken);

        var updated = await service.UpdateAsync(
            new ContactUpdateRequest(
                created.Id,
                Optional<string?>.Of("Ada"),
                Optional<string?>.Absent,
                Optional<string?>.Absent,
                Optional<string?>.Absent,
                Optional<string?>.Absent,
                Optional<IReadOnlyList<ChannelInput>?>.Of([Channel("email", "A@b.co")]),
                Optional<JsonObject?>.Absent,
                null,
                null),
            TestContext.Current.CancellationToken);

        Assert.Equal("Ada", updated.FirstName);
        Assert.Empty(await db.Journal.Where(e => e.Kind == JournalKinds.ContactUpdated).ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Replacing_the_channel_list_survives_a_value_that_stays_while_the_primary_moves()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var service = NewService(db, new FixedClock(Noon));
        var created = await service.CreateAsync(
            Create(channels: [Channel("email", "a@b.co", "old", primary: true), Channel("email", "c@d.co")]),
            TestContext.Current.CancellationToken);

        var updated = await service.UpdateAsync(
            new ContactUpdateRequest(
                created.Id,
                Optional<string?>.Absent,
                Optional<string?>.Absent,
                Optional<string?>.Absent,
                Optional<string?>.Absent,
                Optional<string?>.Absent,
                Optional<IReadOnlyList<ChannelInput>?>.Of([Channel("email", "c@d.co", "new", primary: true), Channel("email", "e@f.co")]),
                Optional<JsonObject?>.Absent,
                null,
                null),
            TestContext.Current.CancellationToken);

        Assert.Collection(
            updated.Channels,
            first =>
            {
                Assert.Equal("c@d.co", first.Value);
                Assert.True(first.Primary);
                Assert.Equal("new", first.Label);
            },
            second => Assert.Equal("e@f.co", second.Value));
        Assert.Equal(2, await db.ContactChannels.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task An_update_on_an_archived_contact_is_a_conflict()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var service = NewService(db, new FixedClock(Noon));
        var created = await service.CreateAsync(Create(firstName: "Ada"), TestContext.Current.CancellationToken);
        await service.ArchiveAsync(new ContactArchiveRequest(created.Id, null, "gone"), TestContext.Current.CancellationToken);

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            service.UpdateAsync(
                new ContactUpdateRequest(
                    created.Id,
                    Optional<string?>.Of("Grace"),
                    Optional<string?>.Absent,
                    Optional<string?>.Absent,
                    Optional<string?>.Absent,
                    Optional<string?>.Absent,
                    Optional<IReadOnlyList<ChannelInput>?>.Absent,
                    Optional<JsonObject?>.Absent,
                    null,
                    null),
                TestContext.Current.CancellationToken));

        Assert.Equal("contact_archived", ex.Code);
    }

    [Fact]
    public async Task Archiving_is_idempotent_and_journaled_once()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var clock = new FixedClock(Noon);
        var service = NewService(db, clock);
        var created = await service.CreateAsync(Create(firstName: "Ada"), TestContext.Current.CancellationToken);

        var first = await service.ArchiveAsync(new ContactArchiveRequest(created.Id, null, "left"), TestContext.Current.CancellationToken);
        clock.Advance(TimeSpan.FromHours(2));
        var second = await service.ArchiveAsync(new ContactArchiveRequest(created.Id, null, "left again"), TestContext.Current.CancellationToken);

        Assert.Equal(Noon, first.ArchivedAt);
        Assert.Equal(first.ArchivedAt, second.ArchivedAt);
        var entry = Assert.Single(await db.Journal.Where(e => e.Kind == JournalKinds.ContactArchived).ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal("left", entry.Reason);
    }
}
