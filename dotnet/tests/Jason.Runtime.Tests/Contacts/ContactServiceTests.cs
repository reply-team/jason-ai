using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Ids;
using Jason.Contracts.Json;
using Jason.Runtime.Contacts;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Tests.Journal;
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

    private static ContactCreateRequest Person(string first, string? last, string? company, string? title, params ChannelInput[] channels) =>
        new(first, last, company, title, null, channels, null, null, null);

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

        Assert.Null(contact.Campaigns);
        Assert.Null(contact.LastActivity);
        var read = await service.GetAsync(new ContactGetRequest(contact.Id), TestContext.Current.CancellationToken);
        Assert.Empty(read.Campaigns!);
        Assert.Equal(JournalKinds.ContactCreated, read.LastActivity!.Kind);
        Assert.Equal(contact.Id, read.LastActivity.ContactId);

        var entry = Assert.Single(await db.Journal.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(JournalKinds.ContactCreated, entry.Kind);
        Assert.Null(entry.CampaignId);
        Assert.Equal(contact.Id, entry.Key);
        Assert.Equal(db.Contacts.Single(c => c.PublicId == contact.Id).Id, entry.ContactId);
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

    /// <summary>Two spellings of one LinkedIn profile are one value, so a contact carrying both carries the same pair twice.</summary>
    [Fact]
    [Critical(Family.F1, "one key per profile in a contact")]
    public async Task Two_spellings_of_one_profile_inside_one_contact_are_the_same_pair_twice()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var service = NewService(db, new FixedClock(Noon));

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            service.CreateAsync(
                Create(channels: [Channel("linkedin", "https://www.linkedin.com/in/marta-example"), Channel("linkedin", "uk.linkedin.com/in/Marta-Example//?trk=feed")]),
                TestContext.Current.CancellationToken));

        var detail = Assert.Single(ex.Details!);
        Assert.Equal(("channels[1]", "duplicate"), (detail.Field, detail.Code));
    }

    [Fact]
    [Critical(Family.S1, "one primary per channel")]
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

    /// <summary>
    /// A listing narrows by one channel's normalized value, or by any part of what a person is known by — a name, a
    /// company, a title, any channel value — whatever its case and accents, a phone however it is spaced, and with the
    /// characters a pattern would read as wildcards taken as written; a pasted LinkedIn link by its profile in any spelling,
    /// a name outside ASCII included, and never a profile whose name only starts the same. Both narrow together.
    /// </summary>
    [Theory]
    [InlineData("Email", " FOO@bar.COM ", null, false, "Foo")]
    [InlineData("email", null, null, false, "Ada Lovelace|Foo|Other")]
    [InlineData(null, null, "kavali.ex", false, "Ada Lovelace")]
    [InlineData(null, null, "ADA@KAVALI", false, "Ada Lovelace")]
    [InlineData(null, null, "+48 601 234 567", false, "Ada Lovelace")]
    [InlineData(null, null, "601-234-567", false, "Ada Lovelace")]
    [InlineData(null, null, "in/ada-love", false, "Ada Lovelace")]
    [InlineData(null, null, "https://www.linkedin.com/in/ada-lovelace/", false, "Ada Lovelace")]
    [InlineData(null, null, "linkedin.com/in/Ada-Lovelace?trk=public_profile", false, "Ada Lovelace")]
    [InlineData(null, null, "https://www.linkedin.com/in/ada/", false, "Link")]
    [InlineData(null, null, "uk.linkedin.com/in/J%C3%96RN-Sample/", false, "Jörn Sample")]
    [InlineData(null, null, "https://www.linkedin.com/in/JÖRN-SAMPLE?trk=feed", false, "Jörn Sample")]
    [InlineData(null, null, "love", false, "Ada Lovelace")]
    [InlineData(null, null, "ada lovelace", false, "Ada Lovelace")]
    [InlineData(null, null, "andina", false, "Álvaro Núñez")]
    [InlineData(null, null, "director of", false, "Álvaro Núñez")]
    [InlineData(null, null, "álv", false, "Álvaro Núñez")]
    [InlineData(null, null, "łuk", false, "Łukasz Zieliński")]
    [InlineData(null, null, "alvaro", false, "Álvaro Núñez")]
    [InlineData(null, null, "nunez", false, "Álvaro Núñez")]
    [InlineData(null, null, "lukasz zielinski", false, "Łukasz Zieliński")]
    [InlineData(null, null, "r&d", false, "Łukasz Zieliński")]
    [InlineData(null, null, "a_b", false, "")]
    [InlineData(null, null, "%", false, "")]
    [InlineData(null, null, "\\", false, "")]
    [InlineData(null, null, "grace", false, "")]
    [InlineData(null, null, "grace", true, "Grace Hopper")]
    [InlineData("email", "ada@kavali.example", "love", false, "Ada Lovelace")]
    [InlineData("email", "foo@bar.com", "love", false, "")]
    public async Task A_listing_finds_contacts_by_a_channel_value_or_by_any_text_they_hold(string? channel, string? value, string? query, bool archived, string expected)
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var service = NewService(db, new FixedClock(Noon));
        var ct = TestContext.Current.CancellationToken;
        await service.CreateAsync(Person("Foo", null, null, null, Channel("email", "foo@bar.com")), ct);
        await service.CreateAsync(Person("Other", null, null, null, Channel("email", "other@bar.com")), ct);
        await service.CreateAsync(Person("Link", null, null, null, Channel("linkedin", "https://www.linkedin.com/in/ada")), ct);
        await service.CreateAsync(Person("Jörn", "Sample", null, null, Channel("linkedin", "https://www.linkedin.com/in/J%C3%B6rn-Sample")), ct);
        await service.CreateAsync(
            Person("Ada", "Lovelace", "Kavali", "CTO", Channel("email", "ada@kavali.example"), Channel("phone", "+48601234567"), Channel("linkedin", "https://www.linkedin.com/in/ada-lovelace")),
            ct);
        await service.CreateAsync(Person("Álvaro", "Núñez", "Andina Tech", "Director of Sales"), ct);
        await service.CreateAsync(Person("Łukasz", "Zieliński", "Vireo", "Head of R&D"), ct);
        await service.CreateAsync(Person("Axel", "Bloom", "axb", null), ct);
        var grace = await service.CreateAsync(Person("Grace", "Hopper", null, null, Channel("email", "grace@navy.example")), ct);
        await service.ArchiveAsync(new ContactArchiveRequest(grace.Id, null, "left the navy"), ct);

        var page = await service.ListAsync(new ContactListRequest(channel, value, archived ? true : null, null, null, query), ct);

        var names = page.Items.Select(c => string.Join(' ', new[] { c.FirstName, c.LastName }.Where(n => n is not null))).Order(StringComparer.Ordinal);
        Assert.Equal(expected, string.Join('|', names));
    }

    /// <summary>
    /// A listing's text also finds a person by the values of their custom fields: a text or a number at any depth, inside
    /// an object or a list, folded as the other terms are whatever escaping the stored JSON uses, with wildcards taken as
    /// written. Never by a key, never by the JSON's own punctuation, never by a flag or a null, and never by a text that
    /// runs from one value into the next.
    /// </summary>
    [Theory]
    [InlineData("retail", "Rita")]
    [InlineData("RETA", "Rita")]
    [InlineData("platinum", "Rita")]
    [InlineData("nordics", "Rita")]
    [InlineData("4217", "Rita")]
    [InlineData("21", "Rita")]
    [InlineData("industry", "")]
    [InlineData("tier", "")]
    [InlineData("\":", "")]
    [InlineData("{", "")]
    [InlineData("[", "")]
    [InlineData(",", "")]
    [InlineData("true", "")]
    [InlineData("null", "")]
    [InlineData("retail\u001fplatinum", "")]
    [InlineData("50%", "Rita")]
    [InlineData("%_off", "Rita")]
    [InlineData("_off", "Rita")]
    [InlineData("x off", "Wim")]
    [InlineData("munchen", "Mona|Sven")]
    [InlineData("MÜNCH", "Mona|Sven")]
    [InlineData("cafe", "Mona|Sven")]
    [InlineData("göteborg", "Mona")]
    public async Task A_listing_finds_contacts_by_the_values_of_their_custom_fields_and_never_by_a_key(string query, string expected)
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var service = NewService(db, new FixedClock(Noon));
        var ct = TestContext.Current.CancellationToken;
        await service.CreateAsync(Custom("Rita", new JsonObject
        {
            ["industry"] = "retail",
            ["account"] = new JsonObject { ["tier"] = "Platinum", ["regions"] = new JsonArray("Nordics") },
            ["seats"] = 4217,
            ["active"] = true,
            ["churned"] = null,
            ["code"] = "50%_off",
        }), ct);
        await service.CreateAsync(Custom("Wim", new JsonObject { ["code"] = "50x off" }), ct);
        // Written by the service, the stored JSON escapes every letter outside ASCII; written by hand, it need not.
        await service.CreateAsync(Custom("Mona", new JsonObject { ["city"] = "München", ["venue"] = "Café", ["hq"] = "Göteborg" }), ct);
        var sven = await service.CreateAsync(Custom("Sven", null), ct);
        await db.Database.ExecuteSqlAsync($"UPDATE contacts SET custom_json = {"{\"city\":\"München\",\"venue\":\"Café\"}"} WHERE public_id = {sven.Id}", ct);
        Assert.Equal(1, await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS value FROM contacts WHERE custom_json LIKE '%\\u00fc%'").SingleAsync(ct));

        var page = await service.ListAsync(new ContactListRequest(null, null, null, null, null, query), ct);

        Assert.Equal(expected, string.Join('|', page.Items.Select(c => c.FirstName).Order(StringComparer.Ordinal)));
    }

    private static ContactCreateRequest Custom(string first, JsonObject? custom) => new(first, null, null, null, null, null, custom, null, null);

    [Theory]
    [InlineData(null, "foo@bar.com", 0, "value")]
    [InlineData(null, null, 201, "query")]
    public async Task A_listing_filter_it_cannot_use_is_refused_on_its_field(string? channel, string? value, int queryLength, string field)
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var service = NewService(db, new FixedClock(Noon));
        var query = queryLength == 0 ? null : new string('x', queryLength);

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            service.ListAsync(new ContactListRequest(channel, value, null, null, null, query), TestContext.Current.CancellationToken));

        Assert.Equal(field, Assert.Single(ex.Details!).Field);
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

        // A blank query is no query.
        var blank = await service.ListAsync(new ContactListRequest(null, null, null, null, null, "   "), TestContext.Current.CancellationToken);
        Assert.Equal(kept.Id, Assert.Single(blank.Items).Id);

        var all = await service.ListAsync(new ContactListRequest(null, null, true, null, null), TestContext.Current.CancellationToken);
        Assert.Equal(2, all.Items.Count);
    }

    [Theory]
    [InlineData(null, null, "a b c d e")]
    [InlineData("activity", null, "d b a c e")]
    [InlineData("activity", "autumn", "b a")]
    public async Task A_listing_walks_its_pages_in_the_order_asked_for(string? order, string? campaign, string expected)
    {
        var ct = TestContext.Current.CancellationToken;
        using var database = new TestDatabase();
        using var db = database.Open();
        var clock = new FixedClock(Noon);
        var service = NewService(db, clock);
        var writer = new JournalWriter(clock);

        // a, b and d are created through the service; c and e arrive as an import does, with no line of their own.
        var letters = new Dictionary<string, string>(StringComparer.Ordinal);
        var people = new Dictionary<string, Contact>(StringComparer.Ordinal);
        foreach (var letter in (string[])["a", "b", "c", "d", "e"])
        {
            if (letter is "c" or "e")
            {
                var imported = new Contact { PublicId = PublicId.New("cnt"), FirstName = letter, CreatedAt = Noon.UtcDateTime, UpdatedAt = Noon.UtcDateTime };
                db.Contacts.Add(imported);
                await db.SaveChangesAsync(ct);
            }
            else
            {
                await service.CreateAsync(Create(firstName: letter), ct);
            }

            var stored = db.Contacts.Single(c => c.FirstName == letter);
            letters[stored.PublicId] = letter;
            people[letter] = stored;
        }

        var autumn = new Campaign { PublicId = "cmp_AUTUMN", Name = "Autumn", ProjectId = DefaultProject.Id, CreatedAt = Noon.UtcDateTime, UpdatedAt = Noon.UtcDateTime };
        db.Campaigns.Add(autumn);
        db.CampaignContacts.AddRange(
            new CampaignContact { Campaign = autumn, Contact = people["a"], AddedAt = Noon.UtcDateTime, UpdatedAt = Noon.UtcDateTime },
            new CampaignContact { Campaign = autumn, Contact = people["b"], AddedAt = Noon.UtcDateTime, UpdatedAt = Noon.UtcDateTime },
            new CampaignContact { Campaign = autumn, Contact = people["d"], State = MembershipState.Excluded, AddedAt = Noon.UtcDateTime, UpdatedAt = Noon.UtcDateTime.AddHours(1) });
        foreach (var letter in (string[])["a", "b", "d"])
        {
            clock.Advance(TimeSpan.FromHours(1));
            writer.AppendDocumented(db, Actors.Runtime, JournalKinds.ContactUpdated, campaign: null, key: people[letter].PublicId, contact: people[letter]);
            await db.SaveChangesAsync(ct);
        }

        var walked = new List<ContactDto>();
        string? cursor = null;
        string? firstCursor = null;
        do
        {
            var request = new ContactListRequest(null, null, null, 2, cursor, CampaignId: campaign is null ? null : "cmp_AUTUMN", Order: order is null ? null : ContactListOrder.Activity);
            var page = await service.ListAsync(request, ct);
            walked.AddRange(page.Items);
            cursor = page.NextCursor;
            firstCursor ??= cursor;
        }
        while (cursor is not null);

        Assert.Equal(expected, string.Join(' ', walked.Select(c => letters[c.Id])));
        foreach (var contact in walked)
        {
            var letter = letters[contact.Id];
            Assert.Equal(letter is "c" or "e" ? null : JournalKinds.ContactUpdated, contact.LastActivity?.Kind);
            var memberships = string.Join(',', contact.Campaigns!.Select(m => $"{m.CampaignName}:{m.State}"));
            Assert.Equal(letter switch { "a" or "b" => "Autumn:Enrolled", "d" => "Autumn:Excluded", _ => string.Empty }, memberships);
        }

        if (order is not null && firstCursor is not null)
        {
            var other = await Assert.ThrowsAsync<ValidationException>(() => service.ListAsync(new ContactListRequest(null, null, null, 2, firstCursor), ct));
            Assert.Equal("cursor", Assert.Single(other.Details!).Field);
        }
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
                Optional<IReadOnlyList<ChannelInput>?>.Of([Channel("linkedin", "https://www.linkedin.com/in/Ada-Example/")]),
                Optional<JsonObject?>.Of(new JsonObject { ["crm_id"] = "2" }),
                null,
                "cleanup"),
            TestContext.Current.CancellationToken);

        Assert.Equal("Ada", updated.FirstName);
        Assert.Null(updated.Title);
        Assert.Equal("https://www.linkedin.com/in/ada-example", Assert.Single(updated.Channels).Value);
        Assert.Equal("2", (string?)updated.Custom["crm_id"]);
        Assert.Equal(Noon.AddHours(1), updated.UpdatedAt);

        var entry = Assert.Single(await db.Journal.Where(e => e.Kind == JournalKinds.ContactUpdated).ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(created.Id, entry.Key);
        Assert.Equal(db.Contacts.Single(c => c.PublicId == created.Id).Id, entry.ContactId);
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
    [Critical(Family.X1, "the unique index during a replace")]
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
        Assert.Equal(db.Contacts.Single(c => c.PublicId == created.Id).Id, entry.ContactId);
    }
}
