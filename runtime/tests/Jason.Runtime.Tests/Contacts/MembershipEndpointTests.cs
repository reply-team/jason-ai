using System.Net;
using Jason.Contracts.Api;
using Jason.Contracts.Ids;
using Jason.Runtime.Persistence;

namespace Jason.Runtime.Tests.Contacts;

public class MembershipEndpointTests
{
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
        await fixture.PostOkAsync<AddContactsResult>(
            Operations.CampaignAddContacts,
            new { campaign_id = campaignId, contacts = new[] { new { first_name = "Ada" } } },
            TestContext.Current.CancellationToken);

        var listed = await fixture.PostOkAsync<Page<MembershipItemDto>>(
            Operations.CampaignListContacts,
            new { campaign_id = campaignId },
            TestContext.Current.CancellationToken);

        var member = Assert.Single(listed.Items);
        Assert.Equal("Ada", member.Contact.FirstName);
        Assert.Equal(MembershipState.Enrolled, member.State);
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
