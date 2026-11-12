using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Ids;
using Jason.Contracts.Operations;
using Jason.Contracts.Plugins;
using Jason.Runtime.Audiences;
using Jason.Runtime.Contacts;
using Jason.Runtime.Domain;
using Jason.Runtime.Execution;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Plugins;
using Jason.Runtime.Plugins.Invocation;
using Jason.Runtime.Tests.Plugins;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Audiences;

/// <summary>
/// The people a search found become the campaign's, page by page, in the change set that ends the answer that carried
/// them: a new person a contact with their channels, the provider's identifier pinned and a membership; a known person a
/// member, never a second contact; a person removed from the campaign, or archived, left out. Each page that carried
/// anybody is one line, the search's row moves with every answer, and a search ends once.
/// </summary>
public sealed class SearchLandingTests
{
    private static readonly DateTime Noon = new(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// A search the provider took is followed from a row of its own, and its first page lands two people the directory
    /// never held: each a contact with what the provider said of them, the provider's identifier pinned by the read that
    /// learned it, and a member of the campaign that came from this search. One line says so; the row moves to where the
    /// next read starts.
    /// </summary>
    [Fact]
    public async Task A_page_makes_new_people_contacts_pinned_and_members_and_says_so_once()
    {
        using var scene = await Scene.AcceptedAsync();
        await using (var db = scene.Open())
        {
            var made = await db.AudienceSearches.AsNoTracking().SingleAsync(Ct);
            Assert.Equal(
                (scene.Search.Id, scene.Audience.Id, TestPlugins.FakeProviderId, "srch-1", (string?)null, (string?)null, 25, (int?)null, 0, Noon, (string?)null),
                (made.SearchWorkItemId, made.AudienceId, made.PluginId, made.Reference, made.Cursor, made.State, made.Asked, made.Found, made.Landed, made.QuietSince, made.EndedAs));
            Assert.Empty(await db.Journal.AsNoTracking().Where(line => line.Kind.StartsWith("audience_")).ToListAsync(Ct));
        }

        scene.Clock.Advance(TimeSpan.FromMinutes(5));
        var read = await scene.ReadAsync(Page("in_progress", found: 2, cursor: "after-2", hasMore: false, Lena, Omar));

        await using var after = scene.Open();
        var lena = await ContactAsync(after, "lena.novak@northwind-freight.example");
        Assert.Equal(("Lena", "Novak", "Head of Operations", "Northwind Freight"), (lena.FirstName, lena.LastName, lena.Title, lena.Company));
        Assert.Equal(
            """{"location":"Porto, Portugal","company_domain":"northwind-freight.example","company_size":"51-200","company_industry":"Logistics","provider_record":{"id":"p-1","seniority":"head"}}""",
            lena.Custom.ToJsonString());
        var omar = await after.Contacts.AsNoTracking().Include(c => c.Channels).Include(c => c.ExternalIds).SingleAsync(c => c.FirstName == "Omar", Ct);
        Assert.Equal([("linkedin", "https://www.linkedin.com/in/omar-example", true)], omar.Channels.Select(c => (c.Channel, c.Value, c.IsPrimary)));
        Assert.Equal("{}", omar.Custom.ToJsonString());

        Assert.Equal(
            [(lena.Id, "p-1", read.Attempt), (omar.Id, "p-2", read.Attempt)],
            (await after.ExternalIds.AsNoTracking().Where(pin => pin.Kind == "contact").OrderBy(pin => pin.Id).ToListAsync(Ct))
                .Select(pin => (pin.ContactId!.Value, pin.Value, pin.RecordedByAttemptId)));
        var members = await after.CampaignContacts.AsNoTracking().Include(m => m.Landing).OrderBy(m => m.Id).ToListAsync(Ct);
        Assert.Equal([lena.Id, omar.Id], members.Select(member => member.ContactId));
        Assert.All(members, member => Assert.Equal(
            (MembershipState.Enrolled, Noon.AddMinutes(5), scene.Audience.Id, scene.Search.Id, read.Item.Id),
            (member.State, member.AddedAt, member.Landing!.AudienceId, member.Landing.SearchWorkItemId, member.Landing.ReadWorkItemId)));

        var line = Assert.Single(await LinesAsync(after, JournalKinds.AudiencePeopleLanded));
        Assert.Equal((ActorType.System, "dispatcher", read.Item.PublicId, scene.Search.PublicId), (line.ActorType, line.ActorId, line.Key, line.WorkItemId));
        Assert.Equal(
            $$"""{"audience":"{{scene.Audience.PublicId}}","plugin":"{{TestPlugins.FakeProviderId}}","state":"in_progress","people":2,"landed":2,"created":2,"already_member":0,"skipped":0,"contact_ids":["{{lena.PublicId}}","{{omar.PublicId}}"],"created_contact_ids":["{{lena.PublicId}}","{{omar.PublicId}}"]}""",
            line.New!.ToJsonString());

        var row = await after.AudienceSearches.AsNoTracking().SingleAsync(Ct);
        Assert.Equal(("after-2", "in_progress", (int?)2, 2, false, Noon.AddMinutes(5), (string?)null), (row.Cursor, row.State, row.Found, row.Landed, row.More, row.QuietSince, row.EndedAs));
        Assert.Empty(await LinesAsync(after, JournalKinds.AudienceSearchEnded));
    }

    /// <summary>
    /// A person the directory holds is found by the provider's identifier pinned to them, by an address, or by their
    /// LinkedIn profile: they become a member of the campaign and nobody new is made. The pin
    /// is added where the person had none.
    /// </summary>
    [Theory]
    [InlineData("by the pin")]
    [InlineData("by the address")]
    [InlineData("by the profile")]
    public async Task A_known_person_becomes_a_member_and_never_a_second_contact(string known)
    {
        using var scene = await Scene.AcceptedAsync();
        var person = known == "by the profile" ? Omar : Lena;
        int held;
        await using (var db = scene.Open())
        {
            var contact = Known(known == "by the pin" ? null : person["channels"]![0]!["value"]!.GetValue<string>(), known == "by the profile" ? "linkedin" : "email");
            if (known == "by the pin")
            {
                contact.ExternalIds.Add(Pin("p-1"));
            }

            db.Contacts.Add(contact);
            await db.SaveChangesAsync(Ct);
            held = contact.Id;
        }

        await scene.ReadAsync(Page("in_progress", found: 1, cursor: "after-1", hasMore: false, person));

        await using var after = scene.Open();
        Assert.Equal([held], await after.Contacts.AsNoTracking().Select(c => c.Id).ToListAsync(Ct));
        Assert.Equal(held, (await after.CampaignContacts.AsNoTracking().Include(m => m.Landing).SingleAsync(Ct)).Landing!.Membership!.ContactId);
        Assert.Equal(person["external_id"]!.GetValue<string>(), (await after.ExternalIds.AsNoTracking().SingleAsync(Ct)).Value);
        var line = Assert.Single(await LinesAsync(after, JournalKinds.AudiencePeopleLanded));
        Assert.Equal((1, 1, 0), ((int)line.New!["people"]!, (int)line.New["landed"]!, (int)line.New["created"]!));
    }

    /// <summary>
    /// The landing reads who is known inside the transaction that saves it, so an import into the same campaign at that
    /// moment waits for it rather than falling between the read and the save. A person new to the directory, in the page
    /// and in the import, is one contact and one member, never two; a person the directory knows and the campaign does not
    /// yet hold, in both, is one member, and the read is recorded rather than stopped on the campaign's unique membership.
    /// </summary>
    [Theory]
    [InlineData("new to the directory")]
    [InlineData("known, not yet a member")]
    public async Task An_import_at_the_moment_of_a_landing_neither_doubles_a_person_nor_stops_the_read(string person)
    {
        using var scene = await Scene.AcceptedAsync();
        const string Address = "lena.novak@northwind-freight.example";
        string? known = null;
        if (person == "known, not yet a member")
        {
            await using var db = scene.Open();
            var contact = Known(Address, "email");
            db.Contacts.Add(contact);
            await db.SaveChangesAsync(Ct);
            known = contact.PublicId;
        }

        await scene.ReadAsync(
            Page("in_progress", found: 1, cursor: "after-1", hasMore: false, Lena),
            meanwhile: async () =>
            {
                await using var db = scene.Open();
                var item = known is null
                    ? new AddContactsItem(null, "Lena", "Novak", null, null, null, [new ChannelInput("email", Address, null, null, null)], null)
                    : new AddContactsItem(known, null, null, null, null, null, null, null);
                await new MembershipService(db, new JournalWriter(scene.Clock), scene.Clock, TestCanceller.New(scene.Clock))
                    .AddAsync(new AddContactsRequest(scene.Campaign.PublicId, "email", [item], new ActorRef(ActorType.Human, "ada"), null), Ct);
            });

        await using var after = scene.Open();
        var holding = await after.ContactChannels.AsNoTracking().Where(channel => channel.Value == Address).Select(channel => channel.ContactId).ToListAsync(Ct);
        var contactId = Assert.Single(holding);
        Assert.Equal(contactId, (await after.CampaignContacts.AsNoTracking().SingleAsync(Ct)).ContactId);
    }

    /// <summary>
    /// A person is known by any key they carry, the provider's identifier first, then each address, then each LinkedIn
    /// profile: an address the directory never saw does not make a second contact of a person whose profile it holds, and
    /// the person found is not changed by what the page says. A second person of the page who turns out to be the same
    /// contact is counted once as landed and once as already a member, so the page's counts add up to the people on it.
    /// </summary>
    [Fact]
    public async Task A_person_is_known_by_any_key_they_carry_and_one_contact_lands_once_per_page()
    {
        using var scene = await Scene.AcceptedAsync();
        int held;
        await using (var db = scene.Open())
        {
            var known = Known("old.address@harbor-labs.example", "email");
            known.Channels.Add(new ContactChannel { Channel = "linkedin", Value = "https://www.linkedin.com/in/omar-example" });
            db.Contacts.Add(known);
            await db.SaveChangesAsync(Ct);
            held = known.Id;
        }

        var both = Person("p-5", "Omar", "Haddad", null, ("email", "omar@quayside.example"), ("linkedin", "https://www.linkedin.com/in/omar-example"));
        var again = Person("p-6", "O.", "Haddad", null, ("email", "omar@quayside.example"));
        await scene.ReadAsync(Page("in_progress", found: 2, cursor: "after-2", hasMore: false, both, again));

        await using var after = scene.Open();
        var contact = await after.Contacts.AsNoTracking().Include(c => c.Channels).SingleAsync(Ct);
        Assert.Equal(held, contact.Id);
        Assert.Equal(
            ["https://www.linkedin.com/in/omar-example", "old.address@harbor-labs.example"],
            contact.Channels.Select(channel => channel.Value).Order(StringComparer.Ordinal));
        var pin = await after.ExternalIds.AsNoTracking().SingleAsync(Ct);
        Assert.Equal(("p-5", "p-6"), (pin.Value, pin.DivergedValue));
        var line = Assert.Single(await LinesAsync(after, JournalKinds.AudiencePeopleLanded));
        Assert.Equal(
            (2, 1, 0, 1, 0),
            ((int)line.New!["people"]!, (int)line.New["landed"]!, (int)line.New["created"]!, (int)line.New["already_member"]!, (int)line.New["skipped"]!));
    }

    /// <summary>
    /// The provider's record is kept beside the person only while the contact stays inside its own bound on what it keeps
    /// beside its fields: a record that would take it over is left out, and the person lands with the rest.
    /// </summary>
    [Fact]
    public async Task A_record_that_would_take_the_contact_over_its_bound_is_left_out_and_the_person_still_lands()
    {
        using var scene = await Scene.AcceptedAsync();
        var oversized = Person("p-7", "Lena", "Novak", null, ("email", "lena.novak@northwind-freight.example"));
        oversized["location"] = "Porto, Portugal";
        oversized["raw"] = "\"" + new string('\u0436', 12_000) + "\"";

        await scene.ReadAsync(Page("in_progress", found: 1, cursor: "after-1", hasMore: false, oversized));

        await using var after = scene.Open();
        var contact = await after.Contacts.AsNoTracking().SingleAsync(Ct);
        Assert.Equal("""{"location":"Porto, Portugal"}""", contact.Custom.ToJsonString());
        Assert.Single(await after.AudienceLandings.AsNoTracking().ToListAsync(Ct));
    }

    /// <summary>
    /// A page read twice — a read repeated after its answer was lost, which the operation allows — makes nobody twice:
    /// the people the first landing made are found by the identifiers it pinned, the campaign already holds them, and the
    /// second line says so.
    /// </summary>
    [Fact]
    public async Task A_page_read_again_makes_nobody_twice()
    {
        using var scene = await Scene.AcceptedAsync();
        var page = Page("in_progress", found: 2, cursor: "after-2", hasMore: false, Lena, Omar);

        await scene.ReadAsync(page);
        await scene.ReadAsync(page);

        await using var after = scene.Open();
        Assert.Equal(2, await after.Contacts.AsNoTracking().CountAsync(Ct));
        Assert.Equal(2, await after.AudienceLandings.AsNoTracking().CountAsync(Ct));
        Assert.Equal(2, (await after.AudienceSearches.AsNoTracking().SingleAsync(Ct)).Landed);
        var lines = await LinesAsync(after, JournalKinds.AudiencePeopleLanded);
        Assert.Equal([(2, 0), (0, 2)], lines.Select(line => ((int)line.New!["landed"]!, (int)line.New["already_member"]!)));
    }

    /// <summary>
    /// The landing commits with the answer or not at all: a read whose attempt another writer ended meanwhile — its lease
    /// taken back — is refused at the save, and nobody it carried becomes a contact, a member or a line.
    /// </summary>
    [Fact]
    public async Task A_read_whose_attempt_was_taken_back_lands_nothing()
    {
        using var scene = await Scene.AcceptedAsync();

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => scene.ReadAsync(Page("in_progress", found: 1, cursor: "after-1", hasMore: false, Lena), takenBack: true));

        await using var after = scene.Open();
        Assert.Empty(await after.Contacts.AsNoTracking().ToListAsync(Ct));
        Assert.Empty(await after.AudienceLandings.AsNoTracking().ToListAsync(Ct));
        Assert.Empty(await LinesAsync(after, JournalKinds.AudiencePeopleLanded));
        Assert.Null((await after.AudienceSearches.AsNoTracking().SingleAsync(Ct)).Cursor);
    }

    /// <summary>
    /// A known person whose pin at the plugin names somebody else at the provider keeps that pin: the identifier the read
    /// returned is recorded beside it, and the person still lands.
    /// </summary>
    [Fact]
    public async Task An_identifier_that_differs_from_the_pin_is_recorded_beside_it_and_the_person_still_lands()
    {
        using var scene = await Scene.AcceptedAsync();
        await using (var db = scene.Open())
        {
            var contact = Known("lena.novak@northwind-freight.example", "email");
            contact.ExternalIds.Add(Pin("p-old"));
            db.Contacts.Add(contact);
            await db.SaveChangesAsync(Ct);
        }

        var read = await scene.ReadAsync(Page("in_progress", found: 1, cursor: "after-1", hasMore: false, Lena));

        await using var after = scene.Open();
        var pin = await after.ExternalIds.AsNoTracking().SingleAsync(Ct);
        Assert.Equal(("p-old", "p-1", read.Attempt), (pin.Value, pin.DivergedValue, pin.DivergedByAttemptId));
        var diverged = Assert.Single(await LinesAsync(after, JournalKinds.ExternalIdDiverged));
        Assert.Equal((pin.ContactId, read.Item.PublicId), (diverged.ContactId, diverged.WorkItemId));
        Assert.Single(await after.AudienceLandings.AsNoTracking().ToListAsync(Ct));
    }

    /// <summary>
    /// A page that carried nobody lands nobody and says nothing; the row still takes what the provider said — where the
    /// next read starts, its state, how many it found — and the quiet goes on. An answer, even an empty one, ends a run of
    /// reads that failed. An empty page that says more is readable brings no read at once: the next waits the cadence, so
    /// a provider that answers so is not read back to back; and it ends no search, whatever state it names, since what is
    /// still readable is still to land.
    /// </summary>
    [Fact]
    public async Task An_empty_page_says_nothing_and_moves_the_row()
    {
        using var scene = await Scene.AcceptedAsync();
        scene.Clock.Advance(TimeSpan.FromMinutes(5));
        await using (var db = scene.Open())
        {
            await db.AudienceSearches.ExecuteUpdateAsync(set => set.SetProperty(row => row.Failures, 2), Ct);
        }

        await scene.ReadAsync(Page("in_progress", found: 0, cursor: "after-0", hasMore: true));

        await using var after = scene.Open();
        Assert.Empty(await after.Journal.AsNoTracking().Where(line => line.Kind.StartsWith("audience_")).ToListAsync(Ct));
        Assert.Empty(await after.Contacts.AsNoTracking().ToListAsync(Ct));
        var row = await after.AudienceSearches.AsNoTracking().SingleAsync(Ct);
        Assert.Equal(("after-0", "in_progress", (int?)0, 0, Noon, (string?)null, 0, false), (row.Cursor, row.State, row.Found, row.Landed, row.QuietSince, row.EndedAs, row.Failures, row.More));

        await scene.ReadAsync(Page("finished", found: 3, cursor: "after-0", hasMore: true));

        await using var finished = scene.Open();
        Assert.Empty(await finished.Journal.AsNoTracking().Where(line => line.Kind == JournalKinds.AudienceSearchEnded).ToListAsync(Ct));
        var open = await finished.AudienceSearches.AsNoTracking().SingleAsync(Ct);
        Assert.Equal(("finished", (string?)null, false), (open.State, open.EndedAs, open.More));
    }

    /// <summary>
    /// A person the campaign took out stays out, as an import leaves them out, and a person archived in the directory is
    /// never made again: both are skipped. A person the campaign already holds stays as they are, with no row saying the
    /// search brought them. A person the page lists twice lands once.
    /// </summary>
    [Fact]
    public async Task People_removed_or_archived_are_skipped_members_stay_as_they_are_and_a_repeat_lands_once()
    {
        using var scene = await Scene.AcceptedAsync();
        await using (var db = scene.Open())
        {
            var removed = Known("lena.novak@northwind-freight.example", "email");
            var archived = Known("https://www.linkedin.com/in/omar-example", "linkedin");
            archived.ArchivedAt = Noon;
            var member = Known("ines.ruiz@harbor-labs.example", "email");
            db.Contacts.AddRange(removed, archived, member);
            db.CampaignContacts.AddRange(
                new CampaignContact { CampaignId = scene.Campaign.Id, Contact = removed, State = MembershipState.Excluded, AddedAt = Noon, UpdatedAt = Noon },
                new CampaignContact { CampaignId = scene.Campaign.Id, Contact = member, State = MembershipState.Paused, AddedAt = Noon, UpdatedAt = Noon });
            await db.SaveChangesAsync(Ct);
        }

        await scene.ReadAsync(Page("in_progress", found: 5, cursor: "after-5", hasMore: false, Lena, Omar, Ines, Noor, Noor));

        await using var after = scene.Open();
        var landed = Assert.Single(await after.AudienceLandings.AsNoTracking().Include(l => l.Membership).ThenInclude(m => m!.Contact).ToListAsync(Ct));
        Assert.Equal("Noor", landed.Membership!.Contact!.FirstName);
        Assert.Equal(
            [MembershipState.Excluded, MembershipState.Paused, MembershipState.Enrolled],
            await after.CampaignContacts.AsNoTracking().OrderBy(m => m.Id).Select(m => m.State).ToListAsync(Ct));
        Assert.Equal(4, await after.Contacts.AsNoTracking().CountAsync(Ct));
        var line = Assert.Single(await LinesAsync(after, JournalKinds.AudiencePeopleLanded));
        Assert.Equal(
            (4, 1, 1, 1, 2),
            ((int)line.New!["people"]!, (int)line.New["landed"]!, (int)line.New["created"]!, (int)line.New["already_member"]!, (int)line.New["skipped"]!));
        Assert.Equal(1, (await after.AudienceSearches.AsNoTracking().SingleAsync(Ct)).Landed);
    }

    /// <summary>
    /// A provider that finishes a search inside its call answers every person it found: they land from the search's own
    /// result, which is the page, and the search ends at once, said once, by the search itself.
    /// </summary>
    [Fact]
    public async Task A_search_that_finished_inside_its_call_lands_from_its_result_and_ends()
    {
        using var scene = await Scene.SearchedAsync(Finished(Lena, Omar));

        await using var after = scene.Open();
        var row = await after.AudienceSearches.AsNoTracking().SingleAsync(Ct);
        Assert.Equal(((string?)null, "finished", (int?)2, 2, "finished", (DateTime?)Noon), (row.Reference, row.State, row.Found, row.Landed, row.EndedAs, row.EndedAt));
        Assert.All(await after.AudienceLandings.AsNoTracking().ToListAsync(Ct), landing => Assert.Equal(scene.Search.Id, landing.ReadWorkItemId));
        Assert.Equal(scene.Search.PublicId, Assert.Single(await LinesAsync(after, JournalKinds.AudiencePeopleLanded)).Key);
        var ended = Assert.Single(await LinesAsync(after, JournalKinds.AudienceSearchEnded));
        Assert.Equal((scene.Search.PublicId, scene.Search.PublicId), (ended.Key, ended.WorkItemId));
        Assert.Equal(
            $$"""{"audience":"{{scene.Audience.PublicId}}","plugin":"{{TestPlugins.FakeProviderId}}","ended_as":"finished","state":"finished","asked":25,"found":2,"landed":2,"ended_by":"{{scene.Search.PublicId}}"}""",
            ended.New!.ToJsonString());
    }

    /// <summary>
    /// A search the provider holds — paused, out of credits, at the plan's limit — is said at once: the first read that
    /// says so writes one line, with the search, its audience, the state and the counts, and starts the quiet from there,
    /// so the long bound of a held search counts from the hold. Reads that say it is still held, in the same word or
    /// another, say nothing more; a read that says it moves again starts the quiet afresh, and a hold after it is said again.
    /// </summary>
    [Fact]
    public async Task A_held_search_is_said_once_per_hold_and_its_quiet_starts_at_each_change()
    {
        using var scene = await Scene.AcceptedAsync();
        scene.Clock.Advance(TimeSpan.FromMinutes(5));
        var held = await scene.ReadAsync(Page("paused_for_credits", found: 3, cursor: "after-0", hasMore: false));
        scene.Clock.Advance(TimeSpan.FromMinutes(5));
        await scene.ReadAsync(Page("paused", found: 3, cursor: "after-0", hasMore: false));
        await using (var db = scene.Open())
        {
            var line = Assert.Single(await LinesAsync(db, JournalKinds.AudienceSearchHeld));
            Assert.Equal((held.Item.PublicId, scene.Search.PublicId), (line.Key, line.WorkItemId));
            Assert.Equal(
                $$"""{"audience":"{{scene.Audience.PublicId}}","plugin":"fake-provider","state":"paused_for_credits","asked":25,"found":3,"landed":0}""",
                line.New!.ToJsonString());
            Assert.Equal(Noon.AddMinutes(5), (await db.AudienceSearches.AsNoTracking().SingleAsync(Ct)).QuietSince);
        }

        scene.Clock.Advance(TimeSpan.FromMinutes(5));
        await scene.ReadAsync(Page("in_progress", found: 3, cursor: "after-0", hasMore: false));
        await using (var db = scene.Open())
        {
            Assert.Equal(Noon.AddMinutes(15), (await db.AudienceSearches.AsNoTracking().SingleAsync(Ct)).QuietSince);
        }

        scene.Clock.Advance(TimeSpan.FromMinutes(5));
        await scene.ReadAsync(Page("paused_for_limit", found: 3, cursor: "after-0", hasMore: false));

        await using var after = scene.Open();
        Assert.Equal(["paused_for_credits", "paused_for_limit"], (await LinesAsync(after, JournalKinds.AudienceSearchHeld)).Select(line => (string?)line.New!["state"]));
    }

    /// <summary>
    /// A search ends on the read that says it is final with nothing more readable — not on one that is final with more to
    /// read — and a read after that still lands what it carries, but says no second ending.
    /// </summary>
    [Fact]
    public async Task A_search_ends_on_its_last_page_once()
    {
        using var scene = await Scene.AcceptedAsync();
        await scene.ReadAsync(Page("exhausted", found: 2, cursor: "after-1", hasMore: true, Lena));
        await using (var db = scene.Open())
        {
            Assert.Equal(((string?)null, true), await db.AudienceSearches.AsNoTracking().Select(s => ValueTuple.Create(s.EndedAs, s.More)).SingleAsync(Ct));
        }

        var last = await scene.ReadAsync(Page("exhausted", found: 2, cursor: "after-2", hasMore: false, Omar));
        await scene.ReadAsync(Page("exhausted", found: 3, cursor: "after-3", hasMore: false, Noor));

        await using var after = scene.Open();
        var row = await after.AudienceSearches.AsNoTracking().SingleAsync(Ct);
        Assert.Equal(("exhausted", 3, "after-3"), (row.EndedAs, row.Landed, row.Cursor));
        var ended = Assert.Single(await LinesAsync(after, JournalKinds.AudienceSearchEnded));
        Assert.Equal(("exhausted", "exhausted", 2, last.Item.PublicId), ((string?)ended.New!["ended_as"], (string?)ended.New["state"], (int)ended.New["landed"]!, (string?)ended.New["ended_by"]));
    }

    /// <summary>
    /// A stop that stopped the search, or found it stopped by an earlier one or gone from the provider, ends it cancelled.
    /// One that found it ended by itself leaves it to the reads: what it found is still to be read, and the last read ends
    /// it as it really ended. In an archived campaign no read comes, so such a stop ends it there as it ended. A search
    /// already ended stays as it ended.
    /// </summary>
    [Theory]
    [InlineData("cancelled", null, false, "cancelled")]
    [InlineData("already_ended", "cancelled", false, "cancelled")]
    [InlineData("already_ended", null, false, "cancelled")]
    [InlineData("already_ended", "finished", false, null)]
    [InlineData("already_ended", "exhausted", false, null)]
    [InlineData("already_ended", "finished", true, "finished")]
    [InlineData("already_ended", "exhausted", true, "exhausted")]
    public async Task A_stop_ends_a_search_cancelled_unless_it_ended_by_itself(string status, string? endedAs, bool archived, string? ends)
    {
        using var scene = await Scene.AcceptedAsync();
        if (archived)
        {
            await using var db = scene.Open();
            await db.Campaigns.ExecuteUpdateAsync(set => set.SetProperty(campaign => campaign.Status, CampaignStatus.Archived), Ct);
        }

        var stop = await scene.StopAsync(status, endedAs);
        await using (var db = scene.Open())
        {
            Assert.Equal(ends, (await db.AudienceSearches.AsNoTracking().SingleAsync(Ct)).EndedAs);
        }

        var again = await scene.StopAsync("already_ended", "cancelled");

        await using var after = scene.Open();
        Assert.Equal(ends ?? "cancelled", (await after.AudienceSearches.AsNoTracking().SingleAsync(Ct)).EndedAs);
        var lines = await LinesAsync(after, JournalKinds.AudienceSearchEnded);
        Assert.Equal([ends is null ? again.Item.PublicId : stop.Item.PublicId], lines.Select(line => (string?)line.New!["ended_by"]));
    }

    private static readonly JsonObject Lena = Person("p-1", "Lena", "Novak", "Head of Operations", ("email", "lena.novak@northwind-freight.example"));

    private static readonly JsonObject Omar = Person("p-2", "Omar", "Haddad", null, ("linkedin", "https://www.linkedin.com/in/omar-example/"));

    private static readonly JsonObject Ines = Person("p-3", "Ines", "Ruiz", "Founder", ("email", "ines.ruiz@harbor-labs.example"));

    private static readonly JsonObject Noor = Person("p-4", "Noor", "Aziz", "COO", ("email", "noor.aziz@quayside.example"));

    private static JsonObject Person(string id, string first, string last, string? title, params (string Channel, string Value)[] channels)
    {
        var lena = id == "p-1";
        return new JsonObject
        {
            ["external_id"] = id,
            ["first_name"] = first,
            ["last_name"] = last,
            ["title"] = title,
            ["company"] = lena
                ? new JsonObject { ["name"] = "Northwind Freight", ["domain"] = "northwind-freight.example", ["size"] = "51-200", ["industry"] = "Logistics" }
                : new JsonObject { ["name"] = null, ["domain"] = null, ["size"] = null, ["industry"] = null },
            ["location"] = lena ? "Porto, Portugal" : null,
            ["channels"] = new JsonArray([.. channels.Select(channel => (JsonNode)new JsonObject { ["channel"] = channel.Channel, ["value"] = channel.Value })]),
            ["raw"] = lena ? """{"id":"p-1","seniority":"head"}""" : null,
        };
    }

    private static JsonObject Page(string state, int found, string cursor, bool hasMore, params JsonObject[] people) => new()
    {
        ["state"] = state,
        ["found"] = found,
        ["people"] = new JsonArray([.. people.Select(person => (JsonNode)person.DeepClone())]),
        ["cursor"] = cursor,
        ["has_more"] = hasMore,
        ["vendor"] = new JsonObject { ["status"] = state },
    };

    private static JsonObject Finished(params JsonObject[] people) => new()
    {
        ["items"] = new JsonArray(new JsonObject
        {
            ["status"] = "finished",
            ["reference"] = null,
            ["found"] = people.Length,
            ["people"] = new JsonArray([.. people.Select(person => (JsonNode)person.DeepClone())]),
        }),
    };

    private static Contact Known(string? value, string channel)
    {
        var contact = new Contact { PublicId = PublicId.New("cnt"), FirstName = "Known", CreatedAt = Noon, UpdatedAt = Noon };
        if (value is not null)
        {
            contact.Channels.Add(new ContactChannel { Channel = channel, Value = channel == "linkedin" ? "https://www.linkedin.com/in/omar-example" : value });
        }

        return contact;
    }

    private static ExternalId Pin(string value) => new()
    {
        PluginId = TestPlugins.FakeProviderId,
        Kind = "contact",
        Value = value,
        RecordedAt = Noon,
        RecordedByAttemptId = "att_01K5B7Q2WE5X3M9T0YH4C6RDN0",
    };

    private static Task<Contact> ContactAsync(JasonDbContext db, string email) =>
        db.Contacts.AsNoTracking().Include(c => c.Channels).Include(c => c.ExternalIds)
            .SingleAsync(c => c.Channels.Any(channel => channel.Channel == "email" && channel.Value == email), Ct);

    /// <summary>
    /// A read or a stop that finds no search under the reference ends it <c>cancelled</c>, with the line any ending writes,
    /// by that item, in a campaign that runs as in an archived one: the search is gone at the provider, so nothing would
    /// ever read or stop it again. No other failure ends a search: one the provider may answer later leaves it to be read
    /// again, and one a person has to fix leaves it followed.
    /// </summary>
    [Theory]
    [InlineData("audience.search_status", "search_not_found", FailureClass.Permanent, false, "cancelled")]
    [InlineData("audience.search_cancel", "search_not_found", FailureClass.Permanent, false, "cancelled")]
    [InlineData("audience.search_cancel", "search_not_found", FailureClass.Permanent, true, "cancelled")]
    [InlineData("audience.search_status", "provider_unavailable", FailureClass.Transient, false, null)]
    [InlineData("audience.search_status", "unauthorized", FailureClass.Permanent, false, null)]
    [InlineData("audience.search_cancel", "unauthorized", FailureClass.Permanent, true, null)]
    public async Task A_read_or_a_stop_that_finds_the_search_gone_ends_it_cancelled(string operation, string code, FailureClass failure, bool archived, string? ends)
    {
        using var scene = await Scene.AcceptedAsync();
        if (archived)
        {
            await using var db = scene.Open();
            await db.Campaigns.ExecuteUpdateAsync(set => set.SetProperty(campaign => campaign.Status, CampaignStatus.Archived), Ct);
        }

        var failed = await scene.FailAsync(operation, new OutcomeError(failure, code, "the provider said no"));

        await using var after = scene.Open();
        Assert.Equal(ends, (await after.AudienceSearches.AsNoTracking().SingleAsync(Ct)).EndedAs);
        var lines = await LinesAsync(after, JournalKinds.AudienceSearchEnded);
        Assert.Equal(ends is null ? [] : [failed.Item.PublicId], lines.Select(line => (string?)line.New!["ended_by"]));
        Assert.Equal(ends is null ? [] : ["cancelled"], lines.Select(line => (string?)line.New!["ended_as"]));
    }

    private static Task<List<JournalEntry>> LinesAsync(JasonDbContext db, string kind) =>
        db.Journal.AsNoTracking().Where(line => line.Kind == kind).OrderBy(line => line.Id).ToListAsync(Ct);

    /// <summary>A campaign, its audience 1, and a search for twenty-five people under it, answered.</summary>
    private sealed class Scene : IDisposable
    {
        private readonly TestDatabase _database = new();

        private Scene()
        {
            Clock = new FixedClock(new DateTimeOffset(Noon));
            var journal = new JournalWriter(Clock);
            Recorder = new ProviderOutcomeRecorder(
                new AttemptOutcomes(journal, Clock, TestOptions.Settings(o => o.RetryDelaySeconds = 0)),
                new ExternalIdStore(journal, Clock),
                new SentFacts(journal),
                new SearchLanding(journal, new ExternalIdStore(journal, Clock), Clock),
                TestOptions.PluginSettings(_ => { }));
        }

        public FixedClock Clock { get; }

        public ProviderOutcomeRecorder Recorder { get; }

        public Campaign Campaign { get; private set; } = null!;

        public Audience Audience { get; private set; } = null!;

        public WorkItem Search { get; private set; } = null!;

        public JasonDbContext Open() => _database.Open();

        public void Dispose() => _database.Dispose();

        public static Task<Scene> AcceptedAsync() => SearchedAsync(new JsonObject
        {
            ["items"] = new JsonArray(new JsonObject { ["status"] = "accepted", ["reference"] = "srch-1", ["found"] = null, ["people"] = new JsonArray() }),
        });

        public static async Task<Scene> SearchedAsync(JsonObject answer)
        {
            var scene = new Scene();
            await using var db = scene.Open();
            scene.Campaign = WorkItemFactory.NewCampaign(now: Noon);
            scene.Audience = new Audience
            {
                PublicId = PublicId.New("aud"),
                Campaign = scene.Campaign,
                Number = 1,
                Definition = new JsonObject { ["industries"] = new JsonArray("Logistics") },
                DefinitionHash = "sha256:aa",
                CreatedAt = Noon,
            };
            db.Campaigns.Add(scene.Campaign);
            db.Audiences.Add(scene.Audience);
            await db.SaveChangesAsync(Ct);

            scene.Search = WorkItemFactory.NewProviderOp(scene.Campaign, "audience.search", Noon);
            scene.Search.Context = new JsonObject
            {
                ["input"] = new JsonObject { ["definition"] = scene.Audience.Definition.DeepClone(), ["count"] = 25 },
                ["audience"] = CampaignAudiences.Carried(scene.Audience, scene.Audience.Definition),
            };
            var external = (string?)answer["items"]![0]!["reference"] is { } reference ? new JsonObject { ["search"] = reference } : null;
            await scene.AnswerAsync(db, scene.Search, answer, external);
            return scene;
        }

        /// <summary>A read of the search, made and answered with <paramref name="page"/>.</summary>
        public Task<(WorkItem Item, string Attempt)> ReadAsync(JsonObject page, bool takenBack = false, Func<Task>? meanwhile = null) =>
            RunAsync("audience.search_status", new JsonObject { ["search"] = Search.PublicId, ["limit"] = 100 }, page, takenBack, meanwhile);

        /// <summary>A stop of the search, made and answered <paramref name="status"/>.</summary>
        public Task<(WorkItem Item, string Attempt)> StopAsync(string status, string? endedAs) => RunAsync(
            "audience.search_cancel",
            new JsonObject { ["search"] = Search.PublicId },
            new JsonObject { ["items"] = new JsonArray(new JsonObject { ["status"] = status, ["ended_as"] = endedAs }) });

        /// <summary>A read or a stop of the search, made, and failed by the plugin with <paramref name="error"/>.</summary>
        public Task<(WorkItem Item, string Attempt)> FailAsync(string operation, OutcomeError error) => RunAsync(
            operation,
            operation == "audience.search_status" ? new JsonObject { ["search"] = Search.PublicId, ["limit"] = 100 } : new JsonObject { ["search"] = Search.PublicId },
            new InvocationOutcome.Failed(error, new OutcomeDiagnostics(12, 2, 0, 1)));

        private Task<(WorkItem Item, string Attempt)> RunAsync(string operation, JsonObject args, JsonObject answer, bool takenBack = false, Func<Task>? meanwhile = null) =>
            RunAsync(operation, args, new InvocationOutcome.Succeeded(answer, null, new OutcomeDiagnostics(12, 2, 0, 1)), takenBack, meanwhile);

        private async Task<(WorkItem Item, string Attempt)> RunAsync(string operation, JsonObject args, InvocationOutcome outcome, bool takenBack = false, Func<Task>? meanwhile = null)
        {
            await using var db = Open();
            var campaign = await db.Campaigns.SingleAsync(c => c.Id == Campaign.Id, Ct);
            var item = WorkItemFactory.NewProviderOp(campaign, operation, Clock.GetUtcNow().UtcDateTime);
            item.Context = new JsonObject { ["input"] = args };
            var attempt = await AnswerAsync(db, item, outcome, takenBack, meanwhile);
            return (item, attempt);
        }

        private Task<string> AnswerAsync(JasonDbContext db, WorkItem item, JsonObject answer, JsonObject? externalIds) =>
            AnswerAsync(db, item, new InvocationOutcome.Succeeded(answer, externalIds, new OutcomeDiagnostics(12, 2, 0, 1)));

        private async Task<string> AnswerAsync(JasonDbContext db, WorkItem item, InvocationOutcome outcome, bool takenBack = false, Func<Task>? meanwhile = null)
        {
            var now = Clock.GetUtcNow().UtcDateTime;
            item.Status = WorkItemStatus.Processing;
            var attempt = WorkItemFactory.NewAttempt(item, 1, AttemptStatus.Running, now);
            attempt.Provenance = new AttemptProvenanceDto(
                TestPlugins.FakeProviderId, "1.0.0", "sha256:0", PluginProtocol.CurrentVersion, PluginProtocol.OperationContractVersion,
                item.Operation, 1, PluginProtocol.SnapshotIdPrefix + "_01K5B7Q2WE5X3M9T0YH4C6RDNC", "rts_01K5B7Q2WE5X3M9T0YH4C6RDND", RouteScope.GlobalDefault, "sha256:1",
                CorrelationId: attempt.PublicId);
            if (item.Id == 0)
            {
                db.WorkItems.Add(item);
            }

            db.Attempts.Add(attempt);
            await db.SaveChangesAsync(Ct);

            var input = item.Operation == "audience.search"
                ? null
                : new JsonObject
                {
                    ["args"] = item.Context["input"]!.DeepClone(),
                    ["search"] = new JsonObject { ["work_item"] = Search.PublicId, ["reference"] = "srch-1", ["cursor"] = null },
                };
            var result = new PluginInvocationResult(
                outcome,
                new InvocationProvenance(
                    TestPlugins.FakeProviderId,
                    "1.0.0",
                    "sha256:0",
                    PluginProtocol.CurrentVersion,
                    PluginProtocol.OperationContractVersion,
                    PluginProtocol.InvocationIdPrefix + "_01K5B7Q2WE5X3M9T0YH4C6RDNA",
                    attempt.PublicId,
                    PluginProtocol.SnapshotIdPrefix + "_01K5B7Q2WE5X3M9T0YH4C6RDNC"),
                Launch: null);
            if (takenBack)
            {
                // The lease enforcer's move, by another writer once the answer arrived and before it is recorded: an answer
                // that lands people holds the database from its reads to its save, so no writer falls between those.
                await using var enforcer = Open();
                await enforcer.WorkItems.Where(row => row.Id == item.Id).ExecuteUpdateAsync(set => set.SetProperty(row => row.Status, WorkItemStatus.Created), Ct);
            }

            await Recorder.RecordAsync(db, item, attempt, OperationCatalog.Find(item.Operation!)!, TestPlugins.FakeProviderId, false, result, Ct, input);

            // Another writer between the answer's reads and its save, on a connection and a thread of its own: it is given a
            // moment to land first, and where the answer's reads hold the database it waits for the save instead.
            var writer = meanwhile is null ? null : Task.Run(meanwhile, Ct);
            if (writer is not null)
            {
                await Task.WhenAny(writer, Task.Delay(TimeSpan.FromSeconds(2), Ct));
            }

            await db.SaveChangesAsync(Ct);

            // As the attempt's handler does: a transaction the recording opened is committed with the save.
            if (db.Database.CurrentTransaction is { } open)
            {
                await open.CommitAsync(Ct);
            }

            if (writer is not null)
            {
                await writer;
            }

            Assert.Equal(outcome is InvocationOutcome.Succeeded, item.Status == WorkItemStatus.Succeeded);
            return attempt.PublicId;
        }
    }
}
