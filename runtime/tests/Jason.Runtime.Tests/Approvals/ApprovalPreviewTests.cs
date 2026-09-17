using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Operations;
using Jason.Runtime.Approvals;
using Jason.Runtime.Persistence;
using Jason.Runtime.Routing;
using Jason.Runtime.Tests.Plugins;

namespace Jason.Runtime.Tests.Approvals;

/// <summary>
/// The preview is what makes a decision a decision rather than a guess. It is assembled where the claim already
/// holds everything it needs — the contract, the campaign, the person, the account — and stored, so that what a
/// person reads is what the claim saw rather than a second assembly of it later against rows that have moved on.
/// </summary>
public class ApprovalPreviewTests
{
    private static readonly OperationContract Enroll = OperationCatalog.Find("campaign.enroll")!;

    [Fact]
    public void The_preview_names_the_effect_the_person_and_the_account_and_repeats_the_contracts_own_words()
    {
        var campaign = WorkItemFactory.NewCampaign("Autumn outreach");
        var contact = NewContact();

        var preview = ApprovalPreview.Of(Enroll, Subject(campaign), campaign, contact, "email");

        Assert.Equal(Enroll.Intent, (string?)preview["intent"]);
        Assert.Equal(campaign.PublicId, (string?)preview["campaign"]!["id"]);
        Assert.Equal("Autumn outreach", (string?)preview["campaign"]!["name"]);
        Assert.Equal(contact.PublicId, (string?)preview["contact"]!["id"]);
        Assert.Equal("Alex Rivera", (string?)preview["contact"]!["name"]);
        Assert.Equal("email", (string?)preview["contact"]!["channel"]);
        Assert.Equal("alex@example.test", (string?)preview["contact"]!["value"]);
        Assert.Equal("provider", (string?)preview["plugin"]);
        Assert.Equal("sha256:account", (string?)preview["binding_identity"]);
        Assert.Equal("campaign.enroll", (string?)preview["subject"]!["operation"]);
    }

    /// <summary>
    /// The dangerous reading and the condition beside it, both, and never one resolved into the other. A person
    /// deciding needs to know that this operation is `act` and `irreversible` as published, and the detail is
    /// what tells them when it is less than that.
    /// </summary>
    [Fact]
    public void The_properties_carry_the_published_value_and_the_condition_that_qualifies_it()
    {
        var campaign = WorkItemFactory.NewCampaign();

        var preview = ApprovalPreview.Of(Enroll, Subject(campaign), campaign, NewContact(), "email");

        Assert.Equal("act", (string?)preview["reach"]!["value"]);
        Assert.True((bool?)preview["reach"]!["conditional"]);
        Assert.Equal(Enroll.Reach.Detail, (string?)preview["reach"]!["detail"]);
        Assert.Equal("irreversible", (string?)preview["reversibility"]!["value"]);
        Assert.Equal("metered", (string?)preview["cost"]!["value"]);
    }

    /// <summary>An operation that acts on nobody says so, rather than saying nothing about somebody.</summary>
    [Fact]
    public void Work_that_names_no_person_has_no_person_in_its_preview()
    {
        var campaign = WorkItemFactory.NewCampaign();

        var preview = ApprovalPreview.Of(Enroll, Subject(campaign), campaign, contact: null, channel: null);

        Assert.Null(preview["contact"]);
    }

    private static ApprovalSubject Subject(Campaign campaign) =>
        ApprovalSubject.Of(
            new ProviderOpPlan(
                TestPlugins.Loaded("provider", ["campaign.enroll"]),
                "snp_one",
                "rts_one",
                RouteScope.CampaignDefault,
                new JsonObject { ["account"] = "one" },
                "sha256:account",
                Enroll,
                new JsonObject { ["campaign"] = new JsonObject { ["id"] = "sq-1" } }),
            WorkItemFactory.NewProviderOp(campaign, "campaign.enroll"));

    private static Contact NewContact()
    {
        var contact = new Contact
        {
            PublicId = "ctc_01K52JR0000000000000000001",
            FirstName = "Alex",
            LastName = "Rivera",
        };

        contact.Channels.Add(new ContactChannel { Channel = "email", Value = "alex@example.test", IsPrimary = true });
        return contact;
    }
}
