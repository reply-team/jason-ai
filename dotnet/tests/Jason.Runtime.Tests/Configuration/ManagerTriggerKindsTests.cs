using Jason.Contracts.Configuration;
using Jason.Contracts.Journal;
using Jason.Runtime.Journal;

namespace Jason.Runtime.Tests.Configuration;

/// <summary>
/// The kinds a manager trigger may name: the kinds the runtime reserves for its own journal lines, read from their
/// documents. The list lives in the contracts because the command-line client needs it and cannot reference the
/// runtime; it is the catalog's, so a kind the runtime starts writing arrives here with its document.
/// </summary>
public class ManagerTriggerKindsTests
{
    [Fact]
    public void The_kinds_are_exactly_the_kinds_the_catalog_publishes()
    {
        Assert.Equal(JournalKindCatalog.Kinds, ManagerTriggerKinds.All.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void The_kinds_are_exactly_the_chronicle_kinds_the_runtime_writes_itself()
    {
        Assert.Equal(
            JournalKinds.Reserved.Order(StringComparer.Ordinal),
            ManagerTriggerKinds.All.Order(StringComparer.Ordinal));
    }

    /// <summary>Held to the validator that refuses anything else, not only to the list it reads: a kind it accepts is a kind that is here.</summary>
    [Fact]
    public void Every_kind_the_validator_accepts_is_one_of_them_and_a_word_it_refuses_is_not()
    {
        var validator = new Jason.Runtime.Configuration.ManagerOptionsValidator();

        Assert.All(
            ManagerTriggerKinds.All,
            kind => Assert.True(validator.Validate(null, new Jason.Runtime.Configuration.ManagerOptions { Triggers = [kind] }).Succeeded, kind));
        Assert.False(validator.Validate(null, new Jason.Runtime.Configuration.ManagerOptions { Triggers = ["not_a_kind"] }).Succeeded);
        Assert.DoesNotContain("not_a_kind", ManagerTriggerKinds.All);
    }
}
