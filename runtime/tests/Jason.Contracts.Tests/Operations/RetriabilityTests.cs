using Jason.Contracts.Operations;
using Jason.Contracts.Plugins;

namespace Jason.Contracts.Tests.OperationContracts;

/// <summary>
/// Whether a failed attempt of an operation is worth another one. Three rules by four classes, every cell
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
    [InlineData(RepeatAfterAmbiguous.Safe, FailureClass.Ambiguous, true)]

    // after_recovery_read: repeating is allowed because the contract obliges the plugin to read first.
    [InlineData(RepeatAfterAmbiguous.AfterRecoveryRead, FailureClass.Transient, true)]
    [InlineData(RepeatAfterAmbiguous.AfterRecoveryRead, FailureClass.Permanent, false)]
    [InlineData(RepeatAfterAmbiguous.AfterRecoveryRead, FailureClass.Validation, false)]
    [InlineData(RepeatAfterAmbiguous.AfterRecoveryRead, FailureClass.Ambiguous, true)]

    // never: the item ends for a person to look at rather than being sent at somebody twice.
    [InlineData(RepeatAfterAmbiguous.Never, FailureClass.Transient, true)]
    [InlineData(RepeatAfterAmbiguous.Never, FailureClass.Permanent, false)]
    [InlineData(RepeatAfterAmbiguous.Never, FailureClass.Validation, false)]
    [InlineData(RepeatAfterAmbiguous.Never, FailureClass.Ambiguous, false)]
    public void The_rule_is_the_operation_s_own(RepeatAfterAmbiguous rule, FailureClass failureClass, bool expected) =>
        Assert.Equal(expected, OutcomeContract.Retriable(failureClass, ContractWith(rule)));

    /// <summary>
    /// Only an ambiguous failure reads the operation at all: the other three classes mean the same thing
    /// whatever was being attempted, which is why they are not a contract's to decide.
    /// </summary>
    [Theory]
    [InlineData(FailureClass.Transient)]
    [InlineData(FailureClass.Permanent)]
    [InlineData(FailureClass.Validation)]
    public void Three_of_the_four_classes_answer_the_same_under_every_rule(FailureClass failureClass) =>
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
    /// The contract under test is built here rather than published: the three shipped operations keep the rules
    /// their documents state, and <c>never</c> — which none of them uses — still has something exercising it.
    /// </summary>
    private static OperationContract ContractWith(RepeatAfterAmbiguous rule) =>
        OperationCatalog.Find("campaign.get")! with { RepeatAfterAmbiguous = rule };
}
