using Jason.Contracts.Api;
using Jason.Runtime.Tests;

namespace Jason.Contracts.Tests.Api;

// Jason.Contracts.Operations, the namespace of the canonical operation contracts, shadows the operation-name class
// from inside this namespace, so that one name is bound here explicitly.
using Operations = Jason.Contracts.Api.Operations;

/// <summary>
/// What a handler's <c>api</c> grant can open: every operation of the Runtime API is a read, in exactly one write family,
/// or never grantable, so an operation added to the API is red here until somebody has said which.
/// </summary>
public class OperationFamiliesTests
{
    private static IReadOnlyList<string> Published { get; } =
        [.. typeof(Operations).GetFields().Where(field => field.IsLiteral).Select(field => (string)field.GetRawConstantValue()!)];

    [Fact]
    [Critical(Family.R2, "every operation is classified for the door")]
    public void Every_operation_is_a_read_a_write_family_or_never_and_only_one_of_them()
    {
        Assert.NotEmpty(Published);
        Assert.Equal(Published.Order(StringComparer.Ordinal), OperationFamilies.Catalog.Order(StringComparer.Ordinal));
        Assert.All(Published, operation =>
        {
            var places = (OperationFamilies.IsRead(operation) ? 1 : 0)
                + OperationFamilies.Writes.Count(family => family.Value.Contains(operation, StringComparer.Ordinal))
                + (OperationFamilies.Never.Contains(operation) ? 1 : 0);
            Assert.True(places == 1, $"'{operation}' is classified {places} times; it must be a read, in one write family, or never.");
        });
    }

    [Theory]
    [InlineData("campaign.get")]
    [InlineData("campaign.list")]
    [InlineData("campaign.summary")]
    [InlineData("campaign.activity_by_period")]
    [InlineData("campaign.list_contacts")]
    [InlineData("workitem.count")]
    [InlineData("journal.list")]
    [InlineData("approval.list_standing")]
    [InlineData("route.resolve")]
    [InlineData("events.sources")]
    public void A_read_is_known_by_its_verb(string operation)
    {
        Assert.True(OperationFamilies.IsRead(operation));
        Assert.Equal(OperationFamilies.Read, OperationFamilies.FamilyOf(operation));
    }

    /// <summary>
    /// The installation's and the UI's own doors read nothing a handler needs, and a conversation read spends a provider
    /// call, so none of them is a read a grant opens.
    /// </summary>
    [Theory]
    [InlineData("system.info")]
    [InlineData("ui.tokens")]
    [InlineData("contact.threads")]
    [InlineData("contact.thread")]
    [InlineData("journal.kinds.extra")]
    public void Not_every_question_is_a_read_a_grant_opens(string operation)
    {
        Assert.False(OperationFamilies.IsRead(operation));
        Assert.Null(OperationFamilies.FamilyOf(operation));
    }

    [Fact]
    public void A_future_read_by_the_verb_rule_is_a_read()
    {
        Assert.True(OperationFamilies.IsRead("journal.kinds"));
    }

    /// <summary>A person's own decisions, the installation's settings and an executor's fenced verbs are never a plugin's.</summary>
    [Fact]
    [Critical(Family.R2, "a person's doors stay closed to a plugin")]
    public void A_door_never_opens_these()
    {
        string[] closed =
        [
            "approval.approve", "approval.reject", "approval.set_standing", "approval.unset_standing", "block.clear",
            "decision.answer", "preference.set", "preference.unset", "workitem.resume", "workitem.complete",
            "workitem.heartbeat", "plugin.reload", "route.set", "system.shutdown", "ui.open",
        ];
        var everything = OperationFamilies.OperationsOf(OperationFamilies.All);

        Assert.All(closed, operation =>
        {
            Assert.Contains(operation, OperationFamilies.Never);
            Assert.Null(OperationFamilies.FamilyOf(operation));
            Assert.DoesNotContain(operation, everything);
        });
    }

    [Fact]
    public void The_write_families_are_named_by_their_noun()
    {
        Assert.Equal(
            ["read", "campaign", "contact", "workitem", "suppression", "report", "journal.append"],
            OperationFamilies.All);
        Assert.Equal(["journal.append"], OperationFamilies.Writes["journal.append"]);
        Assert.Equal(["report.submit"], OperationFamilies.Writes["report"]);
        Assert.Equal(["workitem.create", "workitem.update", "workitem.cancel"], OperationFamilies.Writes["workitem"]);
        Assert.Equal(["suppression.add", "suppression.remove"], OperationFamilies.Writes["suppression"]);
        Assert.Equal(["contact.create", "contact.update", "contact.archive"], OperationFamilies.Writes["contact"]);
        Assert.Equal(
            ["campaign.create", "campaign.update", "campaign.start", "campaign.pause", "campaign.archive", "campaign.update_context", "campaign.add_contacts", "campaign.remove_contacts"],
            OperationFamilies.Writes["campaign"]);
    }

    [Fact]
    public void Families_resolve_to_the_union_of_their_operations()
    {
        var operations = OperationFamilies.OperationsOf(["read", "suppression"]);

        Assert.Contains("campaign.get", operations);
        Assert.Contains("suppression.add", operations);
        Assert.DoesNotContain("campaign.create", operations);
        Assert.Empty(OperationFamilies.OperationsOf([]));
        Assert.True(OperationFamilies.IsFamily("journal.append"));
        Assert.False(OperationFamilies.IsFamily("journal"));
        Assert.Throws<ArgumentException>(() => OperationFamilies.OperationsOf(["everything"]));
    }
}
