using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Operations;
using Jason.Runtime.Approvals;
using Jason.Runtime.Persistence;
using Jason.Runtime.Routing;
using Jason.Runtime.Tests.Plugins;

namespace Jason.Runtime.Tests.Approvals;

/// <summary>
/// What a person is actually approving, written down as a document so that "the same thing" and "something else"
/// are decidable rather than argued about. The claim compares hashes before it runs anything, so what belongs in
/// the subject is everything a change to which would make the decision a different decision.
/// </summary>
public class ApprovalSubjectTests
{
    private static readonly Campaign Outreach = WorkItemFactory.NewCampaign();

    /// <summary>
    /// One item for the whole class, because the item is part of the subject: a second item with the same
    /// arguments is a second decision, and a test that built one per call would be comparing two of them.
    /// </summary>
    private static readonly WorkItem Parked = WorkItemFactory.NewProviderOp(Outreach, "campaign.enroll");

    [Fact]
    public void The_same_subject_written_two_ways_has_one_hash_and_a_changed_argument_has_another()
    {
        var subject = Subject(Input("alex@example.test"));

        // The same document with its keys written in another order is the same document.
        var again = Subject(new JsonObject
        {
            ["channel"] = "email",
            ["campaign"] = new JsonObject { ["id"] = "sq-1" },
            ["contact"] = new JsonObject { ["email"] = "alex@example.test" },
        });

        Assert.Equal(subject.Hash, again.Hash);
        Assert.StartsWith("sha256:", subject.Hash, StringComparison.Ordinal);
        Assert.NotEqual(subject.Hash, Subject(Input("sam@example.test")).Hash);
    }

    [Fact]
    public void A_different_account_is_a_different_decision_and_a_different_build_of_the_same_package_is_not()
    {
        var subject = Subject(Input("alex@example.test"));

        Assert.NotEqual(subject.Hash, Subject(Input("alex@example.test"), bindingIdentity: "sha256:other").Hash);

        // The plugin's version and the package digest are provenance: they are recorded on the attempt that runs,
        // and they are deliberately not part of what was approved. Hashing them would re-park every pending
        // decision the moment an operator reloaded a package, for a change to nobody's data.
        Assert.DoesNotContain("1.0.0", subject.Document.ToJsonString(), StringComparison.Ordinal);
        Assert.DoesNotContain("digest", subject.Document.ToJsonString(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_subject_names_the_operation_the_item_the_input_the_plugin_and_the_account()
    {
        var subject = Subject(Input("alex@example.test"));

        Assert.Equal(
            ["binding_identity", "input", "operation", "operation_version", "plugin", "work_item"],
            subject.Document.Select(member => member.Key).Order(StringComparer.Ordinal));
        Assert.Equal("campaign.enroll", (string?)subject.Document["operation"]);
        Assert.Equal(1, (int?)subject.Document["operation_version"]);
        Assert.Equal("provider", (string?)subject.Document["plugin"]);
        Assert.StartsWith("wi_", (string?)subject.Document["work_item"], StringComparison.Ordinal);
        Assert.Equal("alex@example.test", (string?)subject.Document["input"]!["contact"]!["email"]);
    }

    private static ApprovalSubject Subject(JsonObject input, string? bindingIdentity = "sha256:account") =>
        ApprovalSubject.Of(
            new ProviderOpPlan(
                TestPlugins.Loaded("provider", ["campaign.enroll"]),
                "snp_one",
                "rts_one",
                RouteScope.CampaignDefault,
                bindingIdentity is null ? null : new JsonObject { ["account"] = "one" },
                bindingIdentity,
                OperationCatalog.Find("campaign.enroll")!,
                input),
            Parked);

    private static JsonObject Input(string email) => new()
    {
        ["campaign"] = new JsonObject { ["id"] = "sq-1" },
        ["contact"] = new JsonObject { ["email"] = email },
        ["channel"] = "email",
    };
}
