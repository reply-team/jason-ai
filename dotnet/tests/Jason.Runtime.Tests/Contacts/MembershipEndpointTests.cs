using System.Net;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Ids;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Tests.Campaigns;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests.Contacts;

public class MembershipEndpointTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Campaigns get their own operations in a sibling slice; a membership test only needs one to exist.</summary>
    private static string SeedCampaign(RuntimeApiFixture fixture)
    {
        using var db = new JasonDbContext(JasonDbContext.CreateOptions(fixture.Paths.DatabaseFile));
        var campaign = new Campaign
        {
            PublicId = PublicId.New("cmp"),
            Name = "Import",
            ProjectId = DefaultProject.Id,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        db.Campaigns.Add(campaign);
        db.SaveChanges();
        return campaign.PublicId;
    }

    [Fact]
    public async Task An_import_answers_with_a_summary_and_one_result_per_row_in_order()
    {
        await using var fixture = await RuntimeApiFixture.StartAsync(TestContext.Current.CancellationToken);
        var campaignId = SeedCampaign(fixture);

        var result = await fixture.PostOkAsync<AddContactsResult>(
            Operations.CampaignAddContacts,
            new
            {
                campaign_id = campaignId,
                match_by = "email",
                contacts = new object[]
                {
                    new { first_name = "Ada", channels = new[] { new { channel = "email", value = "A@B.co" } } },
                    new { first_name = "Grace", channels = new[] { new { channel = "email", value = "a@b.co" } } },
                    new { first_name = "Alan" },
                },
                reason = "seed list",
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(campaignId, result.CampaignId);
        Assert.Equal(new AddContactsSummary(1, 1, 1), result.Summary);
        Assert.Equal([0, 1, 2], result.Items.Select(item => item.Index));
        Assert.Equal(AddContactsItemStatus.Added, result.Items[0].Status);
        Assert.Equal(AddContactsItemStatus.AlreadyMember, result.Items[1].Status);
        Assert.Equal("no_match_key", result.Items[2].Error!.Code);
    }

    /// <summary>
    /// An import creates contacts through the same door as <c>contact.create</c>: a row whose name holds a control
    /// character is refused by its index, and the rest of the batch lands.
    /// </summary>
    [Fact]
    public async Task An_imported_row_whose_name_holds_a_control_character_is_refused_by_its_index()
    {
        await using var fixture = await RuntimeApiFixture.StartAsync(Ct);
        var campaignId = SeedCampaign(fixture);

        var result = await fixture.PostOkAsync<AddContactsResult>(
            Operations.CampaignAddContacts,
            new
            {
                campaign_id = campaignId,
                contacts = new object[] { new { first_name = "Ada" }, new { first_name = "Grace", last_name = "Hop\u001b]0;per\u0007" } },
            },
            Ct);

        Assert.Equal(AddContactsItemStatus.Added, result.Items[0].Status);
        var refused = result.Items[1];
        Assert.Equal(AddContactsItemStatus.Rejected, refused.Status);
        Assert.Equal("validation_failed", refused.Error!.Code);
        var detail = Assert.Single(refused.Error.Details!);
        Assert.Equal("contacts[1].last_name", detail.Field);
        Assert.Equal("invalid", detail.Code);
    }

    /// <summary>
    /// Matching a row reads at most two contacts, and a query that takes some rows of a set in no order is one EF Core
    /// warns about every time it runs. An import is ordinary work and puts no warning in the log, by either kind of match
    /// key. Read from the file the real sink wrote.
    /// </summary>
    [Fact]
    public async Task An_import_by_either_kind_of_match_key_puts_no_warning_in_the_log()
    {
        await using var fixture = await RuntimeApiFixture.StartAsync(TestContext.Current.CancellationToken);
        var campaignId = SeedCampaign(fixture);
        var imports = new (string MatchBy, object Contact)[]
        {
            ("email", new { first_name = "Ada", channels = new[] { new { channel = "email", value = "ada@example.test" } } }),
            ("custom:crm_id", new { first_name = "Grace", custom = new { crm_id = "G-1" } }),
        };
        foreach (var (matchBy, contact) in imports)
        {
            await fixture.PostOkAsync<AddContactsResult>(
                Operations.CampaignAddContacts,
                new { campaign_id = campaignId, match_by = matchBy, contacts = new[] { contact } },
                TestContext.Current.CancellationToken);
        }

        // Stopping flushes and closes the rolling file sink, so what is on disk now is everything there is.
        await fixture.Runtime.StopAsync();
        var lines = Directory.GetFiles(fixture.Paths.LogsDirectory, "*", SearchOption.AllDirectories)
            .SelectMany(File.ReadAllLines)
            .Select(line => System.Text.Json.Nodes.JsonNode.Parse(line)!)
            .ToList();

        Assert.Contains(lines, line => ((string?)line["RequestPath"]) == Operations.Route(Operations.CampaignAddContacts));
        Assert.DoesNotContain(lines, line => (string?)line["@l"] is "Warning" or "Error" or "Fatal"
            && ((string?)line["SourceContext"])?.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task A_membership_listing_embeds_the_whole_contact()
    {
        await using var fixture = await RuntimeApiFixture.StartAsync(TestContext.Current.CancellationToken);
        var campaignId = SeedCampaign(fixture);
        var ada = await fixture.PostOkAsync<ContactDto>(Operations.ContactCreate, new { first_name = "Ada" }, Ct);
        await fixture.PostOkAsync<AddContactsResult>(
            Operations.CampaignAddContacts,
            new { campaign_id = campaignId, contacts = new[] { new { contact_id = ada.Id } } },
            TestContext.Current.CancellationToken);

        var listed = await fixture.PostOkAsync<Page<MembershipItemDto>>(
            Operations.CampaignListContacts,
            new { campaign_id = campaignId },
            TestContext.Current.CancellationToken);

        var member = Assert.Single(listed.Items);
        Assert.Equal("Ada", member.Contact.FirstName);
        Assert.Equal(MembershipState.Enrolled, member.State);
        Assert.Equal(campaignId, Assert.Single(member.Contact.Campaigns!).CampaignId);
        Assert.Equal(campaignId, Assert.Single(member.Counted!).CampaignId);
        Assert.Equal(JournalKinds.ContactCreated, member.Contact.LastActivity?.Kind);
    }

    [Fact]
    public async Task Several_campaigns_list_a_person_once_with_the_campaigns_that_counted_them()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct, clock: new FixedClock(CampaignWorld.Now));
        var world = await CampaignWorld.WriteAsync(api, Ct);
        string[] both = [world.Spring.PublicId, world.Autumn.PublicId];
        var since = new DateTimeOffset(CampaignWorld.Since, TimeSpan.Zero);
        var until = new DateTimeOffset(CampaignWorld.Until, TimeSpan.Zero);

        // Ada and Hal are in both campaigns: twenty-four memberships, twenty-two people.
        var everyone = await api.PostOkAsync<Page<MembershipItemDto>>(Operations.CampaignListContacts, new { CampaignIds = both, Limit = 100 }, Ct);
        Assert.Equal(22, everyone.Items.Count);
        var hal = everyone.Items.Single(item => item.Contact.FirstName == "Hal");
        Assert.Equal(both.Order(StringComparer.Ordinal), hal.Counted!.Select(counted => counted.CampaignId));
        var unnamed = await api.PostOkAsync<Page<MembershipItemDto>>(Operations.CampaignListContacts, new { Limit = 100 }, Ct);
        Assert.Equal(everyone.Items.Select(item => item.Contact.Id), unnamed.Items.Select(item => item.Contact.Id));

        // Worked on in Spring on the 13th and in Autumn on the 14th: one row, the later first, and the row says the later.
        await CampaignWorld.WorkedOnAsync(api, world.People["Hal"], Ct, (world.Spring, 13), (world.Autumn, 14));
        var worked = await api.PostOkAsync<Page<MembershipItemDto>>(
            Operations.CampaignListContacts, new { CampaignIds = both, Activity = CampaignActivityFilter.WorkedOn, Since = since, Until = until, Limit = 100 }, Ct);
        var halWorked = Assert.Single(worked.Items, item => item.Contact.FirstName == "Hal");
        Assert.Equal([world.Autumn.PublicId, world.Spring.PublicId], halWorked.Counted!.Select(counted => counted.CampaignId));
        var at14 = new DateTimeOffset(2026, 10, 14, 10, 0, 0, TimeSpan.Zero);
        Assert.Equal(new MembershipWhy("activity", null, "researcher", at14, null, null), halWorked.Why);
        Assert.Equal(halWorked.Why, halWorked.Counted![0].Why);
    }

    [Fact]
    public async Task A_waiting_row_says_what_waits()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct, clock: new FixedClock(CampaignWorld.Now));
        var world = await CampaignWorld.WriteAsync(api, Ct);

        var page = await api.PostOkAsync<Page<MembershipItemDto>>(
            Operations.CampaignListContacts, new { CampaignId = world.Spring.PublicId, Summary = CampaignSummaryFilter.Waiting }, Ct);
        var why = page.Items.ToDictionary(item => item.Contact.FirstName!, item => item.Why!);

        Assert.Equal(("approval", "Put one person into the campaign at the provider."), (why["Ada"].Kind, why["Ada"].Text));
        Assert.StartsWith("apr_", why["Ada"].ApprovalId!, StringComparison.Ordinal);
        Assert.Null(why["Ada"].DecisionId);
        foreach (var asked in new[] { "Ben", "Pia" })
        {
            Assert.Equal(("question", "Follow up on LinkedIn?"), (why[asked].Kind, why[asked].Text));
            Assert.StartsWith("dec_", why[asked].DecisionId!, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task A_failed_row_says_why()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct, clock: new FixedClock(CampaignWorld.Now));
        var world = await CampaignWorld.WriteAsync(api, Ct);

        var page = await api.PostOkAsync<Page<MembershipItemDto>>(
            Operations.CampaignListContacts, new { CampaignId = world.Spring.PublicId, Summary = CampaignSummaryFilter.Failed }, Ct);
        var why = page.Items.ToDictionary(item => item.Contact.FirstName!, item => item.Why!);

        Assert.Equal(
            new MembershipWhy("failure", "It did not work.", "provider_error", new DateTimeOffset(2026, 10, 14, 10, 0, 0, TimeSpan.Zero), null, null),
            why["Cleo"]);
        Assert.Equal(
            new MembershipWhy("failure", null, "expired", new DateTimeOffset(2026, 10, 10, 10, 0, 0, TimeSpan.Zero), null, null),
            why["Eve"]);
    }

    [Fact]
    public async Task An_activity_row_says_when_and_what_and_the_rows_come_newest_first()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct, clock: new FixedClock(CampaignWorld.Now));
        var world = await CampaignWorld.WriteAsync(api, Ct);
        var since = new DateTimeOffset(CampaignWorld.Since, TimeSpan.Zero);
        var until = new DateTimeOffset(CampaignWorld.Until, TimeSpan.Zero);

        var replied = await api.PostOkAsync<Page<MembershipItemDto>>(
            Operations.CampaignListContacts, new { CampaignId = world.Spring.PublicId, Activity = CampaignActivityFilter.Replied, Since = since, Until = until }, Ct);
        var contacted = await api.PostOkAsync<Page<MembershipItemDto>>(
            Operations.CampaignListContacts, new { CampaignId = world.Spring.PublicId, Activity = CampaignActivityFilter.Contacted, Since = since, Until = until }, Ct);
        var removed = await api.PostOkAsync<Page<MembershipItemDto>>(
            Operations.CampaignListContacts, new { CampaignId = world.Spring.PublicId, Activity = CampaignActivityFilter.Removed, Since = since, Until = until }, Ct);
        var people = await api.PostOkAsync<Page<MembershipItemDto>>(
            Operations.CampaignListContacts, new { CampaignId = world.Spring.PublicId, Activity = CampaignActivityFilter.People, Since = since, Until = until, Limit = 100 }, Ct);

        // Oli replied on the 14th, Rae on the 12th: the latest to whom it happened first. From the calendar's start, the window
        // that reads back from the page's top stops at the period's start, and Jon's and Sam's earlier first replies follow.
        Assert.Equal(["Oli", "Rae"], replied.Items.Select(item => item.Contact.FirstName));
        var always = await api.PostOkAsync<Page<MembershipItemDto>>(
            Operations.CampaignListContacts, new { CampaignId = world.Spring.PublicId, Activity = CampaignActivityFilter.Replied, Since = DateTimeOffset.MinValue, Until = until }, Ct);
        Assert.Equal(["Oli", "Rae", "Jon", "Sam"], always.Items.Select(item => item.Contact.FirstName));
        var rae = replied.Items.Single(item => item.Contact.FirstName == "Rae");
        Assert.Equal(new MembershipWhy("activity", null, "email", new DateTimeOffset(2026, 10, 12, 8, 0, 0, TimeSpan.Zero), null, null), rae.Why);
        var oli = replied.Items.Single(item => item.Contact.FirstName == "Oli");
        Assert.Equal(MembershipState.Excluded, oli.State);
        var tess = contacted.Items.Single(item => item.Contact.FirstName == "Tess");
        Assert.Equal(new MembershipWhy("activity", null, "message.send", new DateTimeOffset(2026, 10, 12, 10, 0, 0, TimeSpan.Zero), null, null), tess.Why);
        Assert.Equal(
            new MembershipWhy("activity", null, "removed", new DateTimeOffset(2026, 10, 13, 10, 0, 0, TimeSpan.Zero), null, null),
            Assert.Single(removed.Items).Why);

        // Under People a row says the latest thing that happened, its text naming the count, since a reply and an opt-out
        // both say a channel: Uma opted out on the 11th and was replied to on the 13th; Oli was taken out on the 13th and
        // replied on the 14th.
        Assert.Equal(
            new MembershipWhy("activity", "worked_on", "conversation.reply", new DateTimeOffset(2026, 10, 13, 10, 0, 0, TimeSpan.Zero), null, null),
            people.Items.Single(item => item.Contact.FirstName == "Uma").Why);
        Assert.Equal(
            new MembershipWhy("activity", "replied", "email", new DateTimeOffset(2026, 10, 14, 8, 0, 0, TimeSpan.Zero), null, null),
            people.Items.Single(item => item.Contact.FirstName == "Oli").Why);
        Assert.Equal("opted_out", people.Items.Single(item => item.Contact.FirstName == "Kit").Why!.Text);
    }

    [Fact]
    public async Task The_search_finds_by_any_channel_value()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct, clock: new FixedClock(CampaignWorld.Now));
        var world = await CampaignWorld.WriteAsync(api, Ct);
        await using (var scope = api.Resolve<IServiceScopeFactory>().CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<JasonDbContext>();
            var gus = await db.Contacts.SingleAsync(contact => contact.FirstName == "Gus", Ct);
            db.ContactChannels.Add(new ContactChannel { ContactId = gus.Id, Channel = "phone", Value = "+380441234567" });
            await db.SaveChangesAsync(Ct);
        }

        var byEmail = await api.PostOkAsync<Page<MembershipItemDto>>(
            Operations.CampaignListContacts, new { CampaignId = world.Spring.PublicId, Query = "kit@exa" }, Ct);
        var byPhone = await api.PostOkAsync<Page<MembershipItemDto>>(
            Operations.CampaignListContacts, new { CampaignId = world.Spring.PublicId, Query = "+380 44 123" }, Ct);
        var outAndFound = await api.PostOkAsync<Page<MembershipItemDto>>(
            Operations.CampaignListContacts, new { CampaignId = world.Spring.PublicId, Summary = CampaignSummaryFilter.Out, Query = "kit" }, Ct);
        var foundButNotOut = await api.PostOkAsync<Page<MembershipItemDto>>(
            Operations.CampaignListContacts, new { CampaignId = world.Spring.PublicId, Summary = CampaignSummaryFilter.Out, Query = "ada" }, Ct);

        Assert.Equal(["Kit"], byEmail.Items.Select(item => item.Contact.FirstName));
        Assert.Equal(["Gus"], byPhone.Items.Select(item => item.Contact.FirstName));
        Assert.Equal(["Kit"], outAndFound.Items.Select(item => item.Contact.FirstName));
        Assert.Empty(foundButNotOut.Items);
    }

    /// <summary>The search of a campaign's people finds by a custom field's value as <c>contact.list</c> does, never by its key.</summary>
    [Fact]
    public async Task The_search_finds_by_a_custom_fields_value_and_never_by_its_key()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct, clock: new FixedClock(CampaignWorld.Now));
        var world = await CampaignWorld.WriteAsync(api, Ct);
        await using (var scope = api.Resolve<IServiceScopeFactory>().CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<JasonDbContext>();
            var gus = await db.Contacts.SingleAsync(contact => contact.FirstName == "Gus", Ct);
            gus.Custom = new JsonObject { ["industry"] = "Logística", ["account"] = new JsonObject { ["seats"] = 4217 } };
            var oli = await db.Contacts.SingleAsync(contact => contact.FirstName == "Oli", Ct);
            oli.Custom = new JsonObject { ["stage"] = "Pilot" };
            await db.SaveChangesAsync(Ct);
        }

        var since = new DateTimeOffset(CampaignWorld.Since, TimeSpan.Zero);
        var until = new DateTimeOffset(CampaignWorld.Until, TimeSpan.Zero);

        var byValue = await api.PostOkAsync<Page<MembershipItemDto>>(
            Operations.CampaignListContacts, new { CampaignId = world.Spring.PublicId, Query = "LOGISTICA" }, Ct);
        var byNumber = await api.PostOkAsync<Page<MembershipItemDto>>(
            Operations.CampaignListContacts, new { CampaignId = world.Spring.PublicId, Query = "4217" }, Ct);
        var byKey = await api.PostOkAsync<Page<MembershipItemDto>>(
            Operations.CampaignListContacts, new { CampaignId = world.Spring.PublicId, Query = "industry" }, Ct);
        var bySyntax = await api.PostOkAsync<Page<MembershipItemDto>>(
            Operations.CampaignListContacts, new { CampaignId = world.Spring.PublicId, Query = "\":" }, Ct);
        var repliedAndFound = await api.PostOkAsync<Page<MembershipItemDto>>(
            Operations.CampaignListContacts,
            new { CampaignId = world.Spring.PublicId, Activity = CampaignActivityFilter.Replied, Since = since, Until = until, Query = "pilot" },
            Ct);

        Assert.Equal(["Gus"], byValue.Items.Select(item => item.Contact.FirstName));
        Assert.Equal(["Gus"], byNumber.Items.Select(item => item.Contact.FirstName));
        Assert.Empty(byKey.Items);
        Assert.Empty(bySyntax.Items);
        Assert.Equal(["Oli"], repliedAndFound.Items.Select(item => item.Contact.FirstName));
    }

    /// <summary>A listing asks for one thing at a time, by one count and its own period, and is refused by the field at fault.</summary>
    public static TheoryData<string, string[]> RefusedListings => new()
    {
        { """{"summary":"out","activity":"replied","since":"2026-10-09T00:00:00Z","until":"2026-10-16T00:00:00Z"}""", ["activity:invalid"] },
        { """{"summary":"new","state":"excluded"}""", ["state:invalid"] },
        { """{"since":"2026-10-09T00:00:00Z"}""", ["since:invalid"] },
        { """{"activity":"added"}""", ["since:required", "until:required"] },
        { $$"""{"query":"{{new string('a', 201)}}"}""", ["query:too_long"] },
        { """{"campaign_ids":["cmp_01K7QN00000000000000000001"]}""", ["campaign_ids:invalid"] },
        // A moment no calendar holds, in a cursor nobody was given.
        { """{"activity":"replied","since":"2026-10-09T00:00:00Z","until":"2026-10-16T00:00:00Z","cursor":"OTk5OTk5OTk5OTk5OTk5OTl8Y250X3g"}""", ["cursor:invalid"] },
    };

    [Theory]
    [MemberData(nameof(RefusedListings))]
    public async Task A_listing_asked_the_wrong_way_is_refused_by_the_field_at_fault(string asked, string[] faults)
    {
        await using var fixture = await RuntimeApiFixture.StartAsync(Ct);
        var body = JsonNode.Parse(asked)!.AsObject();
        body["campaign_id"] = SeedCampaign(fixture);

        var error = await fixture.PostErrorAsync(Operations.CampaignListContacts, body, HttpStatusCode.BadRequest, Ct);

        Assert.Equal("validation_failed", error.Code);
        Assert.Equal(faults, error.Details!.Select(detail => $"{detail.Field}:{detail.Code}"));
    }

    [Fact]
    public async Task Removing_a_contact_answers_with_its_own_summary()
    {
        await using var fixture = await RuntimeApiFixture.StartAsync(TestContext.Current.CancellationToken);
        var campaignId = SeedCampaign(fixture);
        var added = await fixture.PostOkAsync<AddContactsResult>(
            Operations.CampaignAddContacts,
            new { campaign_id = campaignId, contacts = new[] { new { first_name = "Ada" } } },
            TestContext.Current.CancellationToken);

        var removed = await fixture.PostOkAsync<RemoveContactsResult>(
            Operations.CampaignRemoveContacts,
            new { campaign_id = campaignId, contact_ids = new[] { added.Items[0].ContactId }, reason = "asked to stop" },
            TestContext.Current.CancellationToken);

        Assert.Equal(new RemoveContactsSummary(1, 0, 0, 0), removed.Summary);
    }

    [Fact]
    public async Task An_unknown_campaign_is_a_not_found_envelope()
    {
        await using var fixture = await RuntimeApiFixture.StartAsync(TestContext.Current.CancellationToken);

        var error = await fixture.PostErrorAsync(
            Operations.CampaignAddContacts,
            new { campaign_id = "cmp_missing", contacts = new[] { new { first_name = "Ada" } } },
            HttpStatusCode.NotFound,
            TestContext.Current.CancellationToken);

        Assert.Equal("campaign_not_found", error.Code);
    }
}
