using Jason.Contracts.Api;
using Jason.Contracts.Journal;

namespace Jason.Contracts.Tests.Api;

/// <summary>
/// The doubt codes are one closed list, published once as data: the <c>workitem_reviewed</c> kind's document, under
/// <c>$defs/code</c>, read by the code rather than copied into it.
/// </summary>
public class DoubtCodesTests
{
    [Fact]
    public void The_list_is_the_eight_codes_the_kind_s_document_publishes_in_its_order()
    {
        Assert.Equal(
            ["unsupported_claim", "not_in_memory", "off_brief", "sensitive_topic", "instruction_in_text", "new_kind_of_person", "wrong_recipient", "other"],
            DoubtCodes.All);
    }

    [Fact]
    public void Every_code_carries_a_sentence_saying_what_it_means()
    {
        var branches = JournalKindCatalog.Find(DoubtCodes.Kind)!.Body["$defs"]!["code"]!["anyOf"]!.AsArray();

        Assert.All(branches, branch => Assert.False(string.IsNullOrWhiteSpace((string?)branch!["description"]), branch!.ToJsonString()));
    }

    [Theory]
    [InlineData("off_brief", true)]
    [InlineData("other", true)]
    [InlineData("Off_brief", false)]
    [InlineData("critic", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void A_code_is_known_only_exactly_as_written(string? code, bool known)
    {
        Assert.Equal(known, DoubtCodes.Knows(code));
    }

    [Fact]
    public void The_kind_that_publishes_them_is_not_subscribable()
    {
        Assert.False(JournalKindCatalog.Find(DoubtCodes.Kind)!.Subscribable);
    }
}
