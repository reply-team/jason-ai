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

    /// <summary>
    /// The one line a person reads first, rendered by the operation's own template from the composed input — the
    /// document the plugin would receive — with the values it singles out beside it.
    /// </summary>
    [Fact]
    public void The_preview_carries_the_summary_its_operation_declares_and_the_values_it_highlights()
    {
        var campaign = WorkItemFactory.NewCampaign("Autumn outreach");

        var preview = ApprovalPreview.Of(Enroll, Composed(campaign, "Autumn outreach"), campaign, NewContact(), "email");

        Assert.Equal(
            "Enroll Alex Rivera (alex@example.test) in campaign Autumn outreach at the provider: start at step, first touch next_open_window, "
            + "refuse if they already take part. If the campaign is live there, its messages start going to this person on that timing.",
            (string?)preview["summary"]);
        Assert.Equal(
            """{"/contacts/0/channels/0/value":"alex@example.test","/campaign/name":"Autumn outreach","/args/start":{"position":"step","step":3},"/args/first_touch":"next_open_window","/args/collision":"refuse"}""",
            preview["highlights"]!.ToJsonString());
    }

    /// <summary>
    /// The summary is read from the subject's own input and from nothing composed again: a campaign renamed since
    /// the claim does not change what the person approving was shown about the work that would run.
    /// </summary>
    [Fact]
    public void The_summary_is_rendered_from_the_subject_and_not_from_the_rows_beside_it()
    {
        var campaign = WorkItemFactory.NewCampaign("Renamed since");

        var preview = ApprovalPreview.Of(Enroll, Composed(campaign, "Autumn outreach"), campaign, NewContact(), "email");

        Assert.Contains("in campaign Autumn outreach at the provider", (string?)preview["summary"], StringComparison.Ordinal);
        Assert.Equal("Renamed since", (string?)preview["campaign"]!["name"]);
    }

    /// <summary>
    /// The person in the summary is the person in the document that would run, not whoever the contact row says now:
    /// a contact renamed, or reached at another address, since the claim must not change what the approver read about
    /// the work. The preview's own <c>contact</c> line is the row's, and it may differ.
    /// </summary>
    [Fact]
    public void The_summary_names_the_person_in_the_subject_and_not_the_contact_row_beside_it()
    {
        var campaign = WorkItemFactory.NewCampaign("Autumn outreach");
        var elsewhere = new Contact { PublicId = "ctc_01K52JR0000000000000000009", FirstName = "Someone", LastName = "Else" };
        elsewhere.Channels.Add(new ContactChannel { Channel = "email", Value = "else@example.test", IsPrimary = true });

        var preview = ApprovalPreview.Of(Enroll, Composed(campaign, "Autumn outreach"), campaign, elsewhere, "email");

        Assert.StartsWith("Enroll Alex Rivera (alex@example.test) in campaign", (string?)preview["summary"], StringComparison.Ordinal);
        Assert.Equal("alex@example.test", (string?)preview["highlights"]!["/contacts/0/channels/0/value"]);
        Assert.Equal("Someone Else", (string?)preview["contact"]!["name"]);
    }

    /// <summary>An operation that declares no preview is shown as it always was: no summary, and no highlights.</summary>
    [Fact]
    public void An_operation_that_declares_no_preview_has_no_summary()
    {
        var campaign = WorkItemFactory.NewCampaign();
        var read = OperationCatalog.Find("campaign.get")!;
        Assert.Null(read.Preview);

        var preview = ApprovalPreview.Of(read, Subject(campaign), campaign, contact: null, channel: null);

        Assert.False(preview.ContainsKey("summary"));
        Assert.False(preview.ContainsKey("highlights"));
    }

    /// <summary>The subject of an enrollment whose composed input names a person, a campaign and the three choices.</summary>
    private static ApprovalSubject Composed(Campaign campaign, string campaignName) =>
        ApprovalSubject.Of(
            new ProviderOpPlan(
                TestPlugins.Loaded("provider", ["campaign.enroll"]),
                "snp_one",
                "rts_one",
                RouteScope.CampaignDefault,
                new JsonObject { ["account"] = "one" },
                "sha256:account",
                Enroll,
                new JsonObject
                {
                    ["args"] = new JsonObject
                    {
                        ["channel"] = "email",
                        ["collision"] = "refuse",
                        ["start"] = new JsonObject { ["position"] = "step", ["step"] = 3 },
                        ["first_touch"] = "next_open_window",
                    },
                    ["contacts"] = new JsonArray(new JsonObject
                    {
                        ["id"] = "ctc_01K52JR0000000000000000001",
                        ["first_name"] = "Alex",
                        ["last_name"] = "Rivera",
                        ["channels"] = new JsonArray(new JsonObject { ["channel"] = "email", ["value"] = "alex@example.test", ["primary"] = true }),
                        ["external_ids"] = new JsonObject(),
                    }),
                    ["campaign"] = new JsonObject { ["id"] = campaign.PublicId, ["name"] = campaignName, ["status"] = "active" },
                }),
            WorkItemFactory.NewProviderOp(campaign, "campaign.enroll"));

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
