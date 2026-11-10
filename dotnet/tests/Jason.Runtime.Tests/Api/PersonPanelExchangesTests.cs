using System.Net;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Runtime.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests.Api;

/// <summary>
/// What the person panel reads and writes, recorded from this runtime: one person in two campaigns, the reads the panel
/// opens with, and each act the panel makes — the same requests the CLI's verbs send. Every test starts from the same
/// seed and records the same opening read first, so the scrubber numbers the ids alike in every file and the panel's
/// screen tests can open the person once and then make any one act.
/// </summary>
public class PersonPanelExchangesTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly DateTimeOffset Nine = new(2026, 10, 16, 9, 0, 0, TimeSpan.Zero);
    private static readonly object Ada = new { type = "human", id = "ada" };

    private static string Id(string prefix, int n) => $"{prefix}_01K7QP{n:D20}";

    private static readonly string Ingrid = Id("cnt", 201);
    private static readonly string Nordic = Id("cmp", 201);
    private static readonly string Webinar = Id("cmp", 202);
    private static readonly string FollowUps = Id("cmp", 203);
    private const string Email = "ingrid@fjordline.example";
    private const string LinkedIn = "https://www.linkedin.com/in/ingrid-solberg-example";

    /// <summary>The reads the panel opens with: the person, whether each of their addresses is on Jason's do-not-contact register, the researcher's findings, and the campaigns a move may go to.</summary>
    [Fact]
    public async Task The_panel_reads()
    {
        await using var api = await StartAsync();

        Assert.Empty((await api.RecordAsync<Page<SuppressionDto>>(Operations.SuppressionList, "person-email-clear", new { channel = "email", value = Email }, Ct)).Items);
        Assert.Empty((await api.RecordAsync<Page<SuppressionDto>>(Operations.SuppressionList, "person-linkedin-clear", new { channel = "linkedin", value = LinkedIn }, Ct)).Items);

        var findings = await api.RecordAsync<Page<WorkItemSummaryDto>>(
            Operations.WorkItemList,
            "researcher-findings",
            new { contact_id = Ingrid, kind = "ai_role", role = "researcher", status = new[] { "succeeded" }, order = "finished", limit = 20 },
            Ct);
        var finding = Assert.Single(findings.Items);
        var whole = await api.RecordAsync<WorkItemDto>(Operations.WorkItemGet, "researcher-finding", new { work_item_id = finding.Id }, Ct);
        Assert.Equal("Fjordline opened an office in Bergen in September.", (string?)whole.Result!["summary"]);

        var targets = await api.RecordAsync<Page<CampaignSummaryDto>>(Operations.CampaignList, "move-targets", new { limit = 10 }, Ct);
        Assert.Equal(3, targets.Items.Count);
    }

    /// <summary>
    /// The register read for the addresses of the people the People screen's other recordings hold, none of them on it: a
    /// request names a channel and a value and nothing else, so the read stands beside those recordings as it is.
    /// </summary>
    [Fact]
    public async Task The_register_read_for_the_people_screen_s_people()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct, clock: new FixedClock(Nine));

        (string Case, string Channel, string Value)[] reads =
        [
            ("mateo-email-clear", "email", "mateo@soluna.example"),
            ("mateo-linkedin-clear", "linkedin", "https://www.linkedin.com/in/mateo-ruiz-example"),
            ("mateo-phone-clear", "phone", "+525512345678"),
            ("aguilar-email-clear", "email", "aguilar@andina.example"),
        ];
        foreach (var (@case, channel, value) in reads)
        {
            Assert.Empty((await api.RecordAsync<Page<SuppressionDto>>(Operations.SuppressionList, @case, new { channel, value }, Ct)).Items);
        }
    }

    /// <summary>Saving the record with a reason, a value the runtime refuses, and the provider's own update queued as work.</summary>
    [Fact]
    public async Task Save_and_the_provider_update()
    {
        await using var api = await StartAsync();

        var saved = await api.RecordAsync<ContactDto>(Operations.ContactUpdate, "person-saved", new { contact_id = Ingrid, title = "VP Revenue", reason = "Promoted in October.", actor = Ada }, Ct);
        Assert.Equal("VP Revenue", saved.Title);
        // The channels are replaced whole, each as the panel sends it: the ones the person had, and the one added.
        var channels = await api.RecordAsync<ContactDto>(
            Operations.ContactUpdate,
            "person-channels",
            new
            {
                contact_id = Ingrid,
                channels = new object[]
                {
                    new { channel = "email", value = Email, label = (string?)null, primary = true, data = (object?)null },
                    new { channel = "linkedin", value = LinkedIn, label = (string?)null, primary = false, data = (object?)null },
                    new { channel = "phone", value = "+4791234567", label = (string?)null, primary = false, data = (object?)null },
                },
                reason = "Gave a phone number.",
                actor = Ada,
            },
            Ct);
        Assert.Equal(3, channels.Channels.Count);
        Assert.Equal("validation_failed", (await api.RecordErrorAsync(Operations.ContactUpdate, "person-refused", new { contact_id = Ingrid, time_zone = "Europe/Atlantis", reason = "Moved.", actor = Ada }, HttpStatusCode.BadRequest, Ct)).Code);

        // The provider's contact.update takes the arguments its document declares and nothing else; the runtime composes the rest at the claim.
        var queued = await api.RecordAsync<WorkItemDto>(
            Operations.WorkItemCreate,
            "provider-contact-update",
            new
            {
                campaign_id = Nordic,
                kind = "provider_op",
                operation = "contact.update",
                contact_id = Ingrid,
                context = new { input = new { channel = "email", fields = new { title = "VP Revenue" } } },
                reason = "Promoted in October.",
                actor = Ada,
            },
            Ct);
        Assert.Equal(WorkItemKind.ProviderOp, queued.Kind);
    }

    /// <summary>An address put on Jason's do-not-contact register, and taken off again with the reason that requires.</summary>
    [Fact]
    public async Task Do_not_contact_on_and_off()
    {
        await using var api = await StartAsync();

        var on = await api.RecordAsync<SuppressionDto>(Operations.SuppressionAdd, "person-email", new { channel = "email", value = Email, reason = "Asked not to be emailed.", actor = Ada }, Ct);
        Assert.Equal(Email, on.Value);
        var off = await api.RecordAsync<SuppressionRemovedDto>(Operations.SuppressionRemove, "person-email", new { channel = "email", value = Email, reason = "Asked to hear from us again.", actor = Ada }, Ct);
        Assert.True(off.Removed);
    }

    /// <summary>The person excluded from a campaign, and the person as they read afterwards.</summary>
    [Fact]
    public async Task Exclude_from_a_campaign()
    {
        await using var api = await StartAsync();

        var removed = await api.RecordAsync<RemoveContactsResult>(Operations.CampaignRemoveContacts, "exclude-one", new { campaign_id = Nordic, contact_ids = new[] { Ingrid }, reason = "Not a fit for the pilot.", actor = Ada }, Ct);
        Assert.Equal(1, removed.Summary.Removed);
        var after = await api.RecordAsync<ContactDto>(Operations.ContactGet, "person-after-exclude", new { contact_id = Ingrid }, Ct);
        Assert.Contains(after.Campaigns!, c => c.CampaignId == Nordic && c.State == MembershipState.Excluded);
    }

    /// <summary>A move in its two steps, in order: into the target, then out of the campaign the person was in.</summary>
    [Fact]
    public async Task Move_to_another_campaign()
    {
        await using var api = await StartAsync();

        var into = await api.RecordAsync<AddContactsResult>(Operations.CampaignAddContacts, "move-in", new { campaign_id = FollowUps, contacts = new[] { new { contact_id = Ingrid } }, reason = "Moved to the follow-ups.", actor = Ada }, Ct);
        Assert.Equal(1, into.Summary.Added);
        var outOf = await api.RecordAsync<RemoveContactsResult>(Operations.CampaignRemoveContacts, "move-out", new { campaign_id = Nordic, contact_ids = new[] { Ingrid }, reason = "Moved to the follow-ups.", actor = Ada }, Ct);
        Assert.Equal(1, outOf.Summary.Removed);
        var after = await api.RecordAsync<ContactDto>(Operations.ContactGet, "person-after-move", new { contact_id = Ingrid }, Ct);
        Assert.Contains(after.Campaigns!, c => c.CampaignId == FollowUps && c.State == MembershipState.Enrolled);
    }

    /// <summary>
    /// A move whose second step is refused: the campaign the person was in is archived elsewhere after the panel opened,
    /// so the add succeeds and the remove does not, and the person is in both campaigns.
    /// </summary>
    [Fact]
    public async Task Move_whose_second_step_is_refused()
    {
        await using var api = await StartAsync();
        await api.PostOkAsync<CampaignDto>(Operations.CampaignArchive, new { campaign_id = Nordic, actor = Ada }, Ct);

        // The first step is the same request as a move that completes, and is answered the same way.
        var into = await api.RecordAsync<AddContactsResult>(Operations.CampaignAddContacts, "move-in", new { campaign_id = FollowUps, contacts = new[] { new { contact_id = Ingrid } }, reason = "Moved to the follow-ups.", actor = Ada }, Ct);
        Assert.Equal(1, into.Summary.Added);
        Assert.Equal("campaign_archived", (await api.RecordErrorAsync(Operations.CampaignRemoveContacts, "move-out-archived", new { campaign_id = Nordic, contact_ids = new[] { Ingrid }, reason = "Moved to the follow-ups.", actor = Ada }, HttpStatusCode.Conflict, Ct)).Code);
        var after = await api.RecordAsync<ContactDto>(Operations.ContactGet, "person-after-half-move", new { contact_id = Ingrid }, Ct);
        Assert.Contains(after.Campaigns!, c => c.CampaignId == Nordic && c.State == MembershipState.Enrolled);
        Assert.Contains(after.Campaigns!, c => c.CampaignId == FollowUps && c.State == MembershipState.Enrolled);
    }

    /// <summary>The researcher asked about the person, with the person's question and without one.</summary>
    [Fact]
    public async Task Research_asked()
    {
        await using var api = await StartAsync();

        var asked = await api.RecordAsync<WorkItemDto>(
            Operations.WorkItemCreate,
            "researcher-asked",
            new { campaign_id = Nordic, kind = "ai_role", role = "researcher", contact_id = Ingrid, context = new { question = "Who owns the Bergen office?" }, actor = Ada },
            Ct);
        Assert.Equal("researcher", asked.Role);
        var plain = await api.RecordAsync<WorkItemDto>(
            Operations.WorkItemCreate,
            "researcher-asked-plain",
            new { campaign_id = Nordic, kind = "ai_role", role = "researcher", contact_id = Ingrid, actor = Ada },
            Ct);
        Assert.Empty(plain.Context);
    }

    /// <summary>The runtime with the seed in place and the panel's first read recorded, which numbers the ids.</summary>
    private static async Task<RuntimeApiFixture> StartAsync()
    {
        var api = await RuntimeApiFixture.StartAsync(Ct, clock: new FixedClock(Nine));
        await SeedAsync(api);
        var person = await api.RecordAsync<ContactDto>(Operations.ContactGet, "person-panel", new { contact_id = Ingrid }, Ct);
        Assert.Equal([Webinar, Nordic], person.Campaigns!.Select(c => c.CampaignId));
        return api;
    }

    /// <summary>
    /// Ingrid, enrolled in the Nordic pilot and excluded from the spring webinar, with one finished research about her;
    /// and the follow-ups campaign she is not in. Ids and moments are fixed so every file reads the same on every machine.
    /// </summary>
    private static async Task SeedAsync(RuntimeApiFixture api)
    {
        var day = new DateTime(2026, 10, 16, 0, 0, 0, DateTimeKind.Utc);
        using var scope = api.Resolve<IServiceScopeFactory>().CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JasonDbContext>();

        Campaign Campaign(string id, string name, int daysAgo)
        {
            var campaign = WorkItemFactory.NewCampaign(name, CampaignStatus.Active, day.AddDays(-daysAgo));
            campaign.PublicId = id;
            db.Campaigns.Add(campaign);
            return campaign;
        }

        var nordic = Campaign(Nordic, "Nordic pilot", 20);
        var webinar = Campaign(Webinar, "Spring webinar", 40);
        Campaign(FollowUps, "Q4 follow-ups", 5);

        var ingrid = new Contact
        {
            PublicId = Ingrid,
            FirstName = "Ingrid",
            LastName = "Solberg",
            Company = "Fjordline",
            Title = "Head of Revenue",
            TimeZone = "Europe/Oslo",
            CreatedAt = day.AddDays(-30),
            UpdatedAt = day.AddDays(-30),
        };
        ingrid.Channels.Add(new ContactChannel { Channel = "email", Value = Email, IsPrimary = true });
        ingrid.Channels.Add(new ContactChannel { Channel = "linkedin", Value = LinkedIn, IsPrimary = false });
        db.Contacts.Add(ingrid);

        db.CampaignContacts.AddRange(
            new CampaignContact { Campaign = webinar, Contact = ingrid, State = MembershipState.Excluded, AddedAt = day.AddDays(-35), UpdatedAt = day.AddDays(-25) },
            new CampaignContact { Campaign = nordic, Contact = ingrid, AddedAt = day.AddDays(-14), UpdatedAt = day.AddDays(-14) });

        var research = WorkItemFactory.NewAiRole(nordic, "researcher", day.AddDays(-2));
        research.PublicId = Id("wi", 201);
        research.Contact = ingrid;
        research.Status = WorkItemStatus.Succeeded;
        research.FinishedAt = day.AddDays(-2).AddHours(1);
        research.Result = new JsonObject
        {
            ["summary"] = "Fjordline opened an office in Bergen in September.",
            ["notes"] = "<b>Hiring</b> two account executives; the careers page says so.",
        };
        db.WorkItems.Add(research);
        var ran = WorkItemFactory.NewAttempt(research, 1, AttemptStatus.Succeeded, day.AddDays(-2));
        ran.PublicId = Id("att", 201);
        db.Attempts.Add(ran);
        await db.SaveChangesAsync(Ct);
    }
}
