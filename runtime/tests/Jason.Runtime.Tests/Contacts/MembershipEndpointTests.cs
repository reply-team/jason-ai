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
