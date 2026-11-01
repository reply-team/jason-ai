using Jason.Contracts.Api;
using Jason.Contracts.Plugins;
using Jason.Runtime.Execution;
using Jason.Runtime.Persistence;
using Jason.Runtime.Routing;
using Jason.Runtime.WorkItems;

namespace Jason.Runtime.Tests.Routing;

/// <summary>
/// A repeat after a lost answer is the same request or it is not made: the work item's key settles only the request it
/// went with, and that request is the whole input the claim composed, not only the arguments the planner wrote.
/// </summary>
public partial class ProviderOpPreflightTests
{
    /// <summary>
    /// The claim of the attempt that lost its answer named the input it composed, and a repeat is made only with an input
    /// of that name. Another address on the consumed channel, another field of the person, a pin of the person or of the
    /// campaign added since, a campaign renamed: the provider would be asked something else under a key that says it is
    /// the same, and answer with the earlier send, so the repeat is refused, ambiguous and final, with the code an edited
    /// argument gets. A campaign paused and resumed in between asks nothing else of the provider, and an argument edited
    /// since is still refused for its own reason.
    /// </summary>
    [Theory]
    [InlineData("nothing", null)]
    [InlineData("the campaign paused", null)]
    [InlineData("another address", "another input than the one composed for it now")]
    [InlineData("another first name", "another input than the one composed for it now")]
    [InlineData("a pin of the person", "another input than the one composed for it now")]
    [InlineData("a pin of the campaign", "another input than the one composed for it now")]
    [InlineData("the campaign renamed", "another input than the one composed for it now")]
    [InlineData("another subject", "other arguments than the work item carries now")]
    public void A_send_repeated_after_a_lost_answer_goes_only_with_the_input_that_attempt_composed(string since, string? refusal)
    {
        var scenario = Scenario.Sending("snd-1");
        scenario.Plugin = scenario.Plugin with { Manifest = scenario.Plugin.Manifest with { KeyedOperations = [Send] } };
        scenario.ContactPins = Pins("contact", "p-88421");
        scenario.CampaignPins = Pins("campaign", "c-7714");
        scenario.Unsettled = true;
        scenario.UnsettledClaim = since switch
        {
            "nothing" => facts => facts,
            "the campaign paused" => facts => facts with { Campaign = Copy(facts.Campaign, status: CampaignStatus.Paused) },
            "another address" => facts => facts with { Contact = Copy(facts.Contact!, address: "ada@old.example.test") },
            "another first name" => facts => facts with { Contact = Copy(facts.Contact!, firstName: "Adeline") },
            "a pin of the person" => facts => facts with { ContactPins = PreflightFacts.NoPins },
            "a pin of the campaign" => facts => facts with { CampaignPins = PreflightFacts.NoPins },
            "the campaign renamed" => facts => facts with { Campaign = Copy(facts.Campaign, name: "Spring flow") },
            "another subject" => facts => facts,
            _ => throw new ArgumentOutOfRangeException(nameof(since), since, null),
        };
        if (since == "another subject")
        {
            scenario.UnsettledArguments = new() { [WorkItemService.InputKey] = scenario.Arguments!.DeepClone() };
            scenario.UnsettledArguments[WorkItemService.InputKey]!["message"]!["subject"] = "Hello again";
        }

        var verdict = scenario.Check();

        Assert.Equal(refusal is null ? AttemptErrors.ApprovalRequired : AttemptErrors.ProviderAnswerLost, verdict.Code);
        if (refusal is not null)
        {
            Assert.True(verdict.Refused);
            Assert.Equal(FailureClass.Ambiguous, verdict.Class);
            Assert.Null(verdict.Plan);
            Assert.Contains(refusal, verdict.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("ada@", verdict.Message, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// An attempt claimed before attempts named their input kept no digest, so nothing says what it was composed from
    /// beyond its arguments: its repeat is compared on the arguments alone, as it was when it lost its answer, and the
    /// person's address the claim composes now is not held against it.
    /// </summary>
    [Theory]
    [InlineData(false, null)]
    [InlineData(true, AttemptErrors.ProviderAnswerLost)]
    public void A_repeat_of_an_attempt_that_kept_no_digest_is_compared_on_its_arguments_alone(bool edited, string? expected)
    {
        var scenario = Scenario.Sending("snd-1");
        scenario.Plugin = scenario.Plugin with { Manifest = scenario.Plugin.Manifest with { KeyedOperations = [Send] } };
        scenario.Contact = Scenario.Person(("email", "ada@another.example.test"));
        scenario.Unsettled = true;
        if (edited)
        {
            scenario.UnsettledArguments = new() { [WorkItemService.InputKey] = scenario.Arguments!.DeepClone() };
            scenario.UnsettledArguments[WorkItemService.InputKey]!["message"]!["subject"] = "Hello again";
        }

        var verdict = scenario.Check();

        Assert.Equal(expected ?? AttemptErrors.ApprovalRequired, verdict.Code);
    }

    /// <summary>
    /// An ambiguous answer may still name what the provider calls the person or the campaign, and the attempt that lost its
    /// answer pins it: the input its claim composed did not carry that pin, and the repeat's does. A pin that attempt
    /// learned from its own answer is not a change of the request, so the repeat the key settles is made; a pin recorded
    /// since by any other attempt is, and the repeat is refused.
    /// </summary>
    [Theory]
    [InlineData("contact", true, null)]
    [InlineData("campaign", true, null)]
    [InlineData("contact", false, AttemptErrors.ProviderAnswerLost)]
    [InlineData("campaign", false, AttemptErrors.ProviderAnswerLost)]
    public void A_pin_the_lost_attempt_learned_from_its_own_answer_is_not_a_change_of_the_request(string entity, bool byThatAttempt, string? expected)
    {
        var scenario = Scenario.Sending("snd-1");
        scenario.Plugin = scenario.Plugin with { Manifest = scenario.Plugin.Manifest with { KeyedOperations = [Send] } };
        scenario.ContactPins = Pins("contact", "p-88421");
        scenario.CampaignPins = Pins("campaign", "c-7714");
        scenario.Unsettled = true;
        scenario.UnsettledClaim = entity == "contact"
            ? facts => facts with { ContactPins = PreflightFacts.NoPins }
            : facts => facts with { CampaignPins = PreflightFacts.NoPins };
        var kinds = byThatAttempt ? new HashSet<string>(StringComparer.Ordinal) { entity } : [];
        scenario.UnsettledLearned = entity == "contact" ? new PinsLearned(kinds, new HashSet<string>()) : new PinsLearned(new HashSet<string>(), kinds);

        var verdict = scenario.Check();

        Assert.Equal(expected ?? AttemptErrors.ApprovalRequired, verdict.Code);
    }

    private static Dictionary<string, string> Pins(string kind, string value) =>
        new(StringComparer.Ordinal) { [kind] = value };

    /// <summary>The same person, as the claim of an earlier attempt read them.</summary>
    private static Contact Copy(Contact contact, string? address = null, string? firstName = null)
    {
        var then = new Contact { PublicId = contact.PublicId, FirstName = firstName ?? contact.FirstName, LastName = contact.LastName };
        foreach (var channel in contact.Channels)
        {
            then.Channels.Add(new ContactChannel { Channel = channel.Channel, Value = address ?? channel.Value, IsPrimary = channel.IsPrimary });
        }

        return then;
    }

    /// <summary>The same campaign, as the claim of an earlier attempt read it.</summary>
    private static Campaign Copy(Campaign campaign, string? name = null, CampaignStatus? status = null) => new()
    {
        PublicId = campaign.PublicId,
        Name = name ?? campaign.Name,
        ProjectId = campaign.ProjectId,
        Status = status ?? campaign.Status,
        CreatedAt = campaign.CreatedAt,
        UpdatedAt = campaign.UpdatedAt,
    };
}
