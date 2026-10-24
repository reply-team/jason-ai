using Jason.Contracts.Events;
using Jason.Contracts.Notifications;
using Jason.Contracts.Operations;
using Jason.Contracts.Plugins;
using Jason.Runtime.Tests;

namespace Jason.Contracts.Tests.OperationContracts;

/// <summary>
/// Whether a failed attempt of an operation is worth another one. Three rules by five classes, every cell
/// written down: the table is the specification, and the only thing that varies between the rows is the
/// operation's own <c>repeat_after_ambiguous</c>, because that is the only thing an operation gets a say in.
/// </summary>
public class RetriabilityTests
{
    [Theory]

    // safe: repeating changes nothing and costs nothing.
    [InlineData(RepeatAfterAmbiguous.Safe, FailureClass.Transient, true)]
    [InlineData(RepeatAfterAmbiguous.Safe, FailureClass.Permanent, false)]
    [InlineData(RepeatAfterAmbiguous.Safe, FailureClass.Validation, false)]
    [InlineData(RepeatAfterAmbiguous.Safe, FailureClass.Blocked, false)]
    // Critical: ambiguous repeats under Safe, never under Never.
    [InlineData(RepeatAfterAmbiguous.Safe, FailureClass.Ambiguous, true, Traits = ["Category", "Critical", "CriticalFamily", nameof(Family.W5)])]

    // after_recovery_read: repeating is allowed because the contract obliges the plugin to read first.
    [InlineData(RepeatAfterAmbiguous.AfterRecoveryRead, FailureClass.Transient, true)]
    [InlineData(RepeatAfterAmbiguous.AfterRecoveryRead, FailureClass.Permanent, false)]
    [InlineData(RepeatAfterAmbiguous.AfterRecoveryRead, FailureClass.Validation, false)]
    [InlineData(RepeatAfterAmbiguous.AfterRecoveryRead, FailureClass.Blocked, false)]
    [InlineData(RepeatAfterAmbiguous.AfterRecoveryRead, FailureClass.Ambiguous, true)]

    // never: the item ends for a person to look at rather than being sent at somebody twice.
    [InlineData(RepeatAfterAmbiguous.Never, FailureClass.Transient, true)]
    [InlineData(RepeatAfterAmbiguous.Never, FailureClass.Permanent, false)]
    [InlineData(RepeatAfterAmbiguous.Never, FailureClass.Validation, false)]
    [InlineData(RepeatAfterAmbiguous.Never, FailureClass.Blocked, false)]
    // Critical: ambiguous repeats under Safe, never under Never.
    [InlineData(RepeatAfterAmbiguous.Never, FailureClass.Ambiguous, false, Traits = ["Category", "Critical", "CriticalFamily", nameof(Family.W5)])]
    public void The_rule_is_the_operation_s_own(RepeatAfterAmbiguous rule, FailureClass failureClass, bool expected) =>
        Assert.Equal(expected, OutcomeContract.Retriable(failureClass, ContractWith(rule)));

    /// <summary>
    /// Only an ambiguous failure reads the operation at all: the other four classes mean the same thing
    /// whatever was being attempted, which is why they are not a contract's to decide. A block is never repeated: the
    /// provider did nothing, and asking it again before the block clears would meet the same block.
    /// </summary>
    [Theory]
    [InlineData(FailureClass.Transient)]
    [InlineData(FailureClass.Permanent)]
    [InlineData(FailureClass.Validation)]
    [InlineData(FailureClass.Blocked)]
    public void Four_of_the_five_classes_answer_the_same_under_every_rule(FailureClass failureClass) =>
        Assert.Single(Enum.GetValues<RepeatAfterAmbiguous>()
            .Select(rule => OutcomeContract.Retriable(failureClass, ContractWith(rule)))
            .Distinct());

    /// <summary>
    /// Every published operation is measured by this same function, so a document whose rule changes changes
    /// what the runtime does without anyone editing code.
    /// </summary>
    [Fact]
    public void The_published_operations_are_read_through_the_same_rule() =>
        Assert.All(OperationCatalog.All, contract => Assert.Equal(
            contract.RepeatAfterAmbiguous is RepeatAfterAmbiguous.Safe or RepeatAfterAmbiguous.AfterRecoveryRead,
            OutcomeContract.Retriable(FailureClass.Ambiguous, contract)));

    /// <summary>
    /// A reply in a thread is the published operation that says <c>never</c>: no read tells this work item's lost send
    /// from another one to the same person, so an ambiguous end stops it for a person, while a failure that says
    /// nothing was sent is still worth another attempt.
    /// </summary>
    [Fact]
    [Critical(Family.W5, "conversation.reply is never repeated blind")]
    public void The_published_reply_ends_for_a_person_after_an_ambiguous_end()
    {
        var reply = OperationCatalog.Find("conversation.reply");

        Assert.NotNull(reply);
        Assert.Equal(RepeatAfterAmbiguous.Never, reply.RepeatAfterAmbiguous);
        Assert.False(OutcomeContract.Retriable(FailureClass.Ambiguous, reply));
        Assert.True(OutcomeContract.Retriable(FailureClass.Transient, reply));
    }

    /// <summary>
    /// The events and delivery families declare no block, and their own readers refuse a blocked answer before a class
    /// is read; should one reach their rule all the same, it is final there too.
    /// </summary>
    [Fact]
    public void A_block_is_final_in_the_families_that_never_declare_one()
    {
        Assert.False(EventsContract.Retriable(FailureClass.Blocked));
        Assert.False(DeliveryContract.Retriable(FailureClass.Blocked));
    }

    /// <summary>
    /// The contract under test is built here rather than published, so every rule has its row whichever documents use
    /// it, read from one operation that differs only in the rule.
    /// </summary>
    private static OperationContract ContractWith(RepeatAfterAmbiguous rule) =>
        OperationCatalog.Find("campaign.get")! with { RepeatAfterAmbiguous = rule };
}
