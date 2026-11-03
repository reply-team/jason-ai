using Jason.Contracts.Ids;
using Jason.Contracts.Notifications;

namespace Jason.Contracts.Tests.Notifications;

/// <summary>
/// The <c>jason:</c> links a Windows toast carries, and the one parser the executable reads them back with. The toast
/// builds its links from the same rows the parser reads, so a link the toast writes is always one the parser takes.
/// </summary>
public sealed class AttentionLinksTests
{
    /// <summary>Made up, of the shape <see cref="PublicId.New"/> gives.</summary>
    private const string Approval = "apr_01JBZ3X7Q9K2M4N6P8R0S2T4V6";

    private const string Question = "dec_01JBZ3X7Q9K2M4N6P8R0S2T4V6";

    [Theory]
    [InlineData("jason://attention")]
    [InlineData("jason://attention/")]
    [InlineData("JASON://Attention")]
    [InlineData("Jason://ATTENTION/")]
    public void The_bare_link_is_the_list(string uri) =>
        Assert.Equal(new AttentionTarget(AttentionAct.List, null, null), AttentionLinks.Parse(uri));

    [Fact]
    public void The_list_link_is_written_as_the_parser_reads_it() =>
        Assert.Equal(new AttentionTarget(AttentionAct.List, null, null), AttentionLinks.Parse(AttentionLinks.List));

    [Fact]
    public void An_approval_link_opens_that_approval()
    {
        Assert.Equal("jason://attention/approvals/" + Approval, AttentionLinks.Item(Approval));
        Assert.Equal(new AttentionTarget(AttentionAct.Open, AttentionKind.Approval, Approval), AttentionLinks.Parse(AttentionLinks.Item(Approval)!));
    }

    [Fact]
    public void A_question_link_opens_that_question()
    {
        Assert.Equal("jason://attention/questions/" + Question, AttentionLinks.Item(Question));
        Assert.Equal(new AttentionTarget(AttentionAct.Open, AttentionKind.Question, Question), AttentionLinks.Parse(AttentionLinks.Item(Question)!));
    }

    [Fact]
    public void A_fresh_id_of_either_kind_makes_a_link_the_parser_takes()
    {
        foreach (var id in new[] { PublicId.New(AttentionLinks.ApprovalPrefix), PublicId.New(AttentionLinks.QuestionPrefix) })
        {
            Assert.Equal(id, AttentionLinks.Parse(AttentionLinks.Item(id)!)?.Id);
        }
    }

    [Fact]
    public void An_approval_s_prompt_link_copies_its_prompt()
    {
        Assert.Equal("jason://attention/approvals/" + Approval + "/prompt", AttentionLinks.Prompt(Approval));
        Assert.Equal(new AttentionTarget(AttentionAct.Prompt, AttentionKind.Approval, Approval), AttentionLinks.Parse(AttentionLinks.Prompt(Approval)!));
    }

    [Fact]
    public void A_question_s_prompt_link_copies_its_prompt()
    {
        Assert.Equal("jason://attention/questions/" + Question + "/prompt", AttentionLinks.Prompt(Question));
        Assert.Equal(new AttentionTarget(AttentionAct.Prompt, AttentionKind.Question, Question), AttentionLinks.Parse(AttentionLinks.Prompt(Question)!));
    }

    [Fact]
    public void One_trailing_slash_after_a_prompt_link_is_taken() =>
        Assert.Equal(AttentionAct.Prompt, AttentionLinks.Parse($"jason://attention/questions/{Question}/prompt/")?.Act);

    [Theory]
    [InlineData(null)]
    [InlineData("cmp_01JBZ3X7Q9K2M4N6P8R0S2T4V6")]
    [InlineData("apr_01JBZ3X7Q9K2M4N6P8R0S2T4V")]
    public void Nothing_but_an_approval_or_a_question_id_gets_a_prompt_link(string? id) =>
        Assert.Null(AttentionLinks.Prompt(id));

    [Fact]
    public void One_trailing_slash_after_an_item_is_taken() =>
        Assert.Equal(Approval, AttentionLinks.Parse($"jason://attention/approvals/{Approval}/")?.Id);

    [Theory]
    [InlineData(null)]
    [InlineData("cmp_01JBZ3X7Q9K2M4N6P8R0S2T4V6")]
    [InlineData("apr_01jbz3x7q9k2m4n6p8r0s2t4v6")]
    [InlineData("apr_01JBZ3X7Q9K2M4N6P8R0S2T4V")]
    [InlineData("apr_01JBZ3X7Q9K2M4N6P8R0S2T4V6/x")]
    [InlineData("")]
    public void Nothing_but_an_approval_or_a_question_id_gets_a_link(string? id) =>
        Assert.Null(AttentionLinks.Item(id));

    /// <summary>Every link the table does not hold is refused, and nothing in one is decoded or guessed at.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("jason:")]
    [InlineData("jason:attention")]
    [InlineData("jason:/attention")]
    [InlineData("http://attention")]
    [InlineData("jasonx://attention")]
    [InlineData("jason://")]
    [InlineData("jason://other")]
    [InlineData("jason://attentions")]
    [InlineData("jason://attention//")]
    [InlineData("jason://attention/approvals")]
    [InlineData("jason://attention/approvals/")]
    [InlineData("jason://attention/APPROVALS/" + Approval)]
    [InlineData("jason://attention/campaigns/cmp_01JBZ3X7Q9K2M4N6P8R0S2T4V6")]
    [InlineData("jason://attention/approvals/" + Question)]
    [InlineData("jason://attention/questions/" + Approval)]
    [InlineData("jason://attention/approvals/apr_01jbz3x7q9k2m4n6p8r0s2t4v6")]
    [InlineData("jason://attention/approvals/apr_01JBZ3X7Q9K2M4N6P8R0S2T4V")]
    [InlineData("jason://attention/approvals/" + Approval + "//")]
    [InlineData("jason://attention/approvals/" + Approval + "/approve")]
    [InlineData("jason://attention/approvals/" + Approval + "/x/y")]
    [InlineData("jason://attention/approvals/" + Approval + "/PROMPT")]
    [InlineData("jason://attention/approvals/" + Approval + "/prompt/x")]
    [InlineData("jason://attention/approvals/" + Approval + "/prompt//")]
    [InlineData("jason://attention/questions/" + Approval + "/prompt")]
    [InlineData("jason://attention/approvals/prompt")]
    [InlineData("jason://attention/approvals//" + Approval)]
    [InlineData("jason://attention/approvals/apr%5F01JBZ3X7Q9K2M4N6P8R0S2T4V6")]
    [InlineData("jason://attention/approvals/" + Approval + "?x=1")]
    [InlineData("jason://attention/approvals/" + Approval + "#top")]
    [InlineData("jason://attention?x=1")]
    [InlineData("jason://attention/approvals/" + Approval + " ")]
    [InlineData(" jason://attention")]
    [InlineData("jason://attention/approvals/" + Approval + "\" --data-dir \"x")]
    [InlineData("jason://user@attention")]
    [InlineData("jason://attention:80")]
    [InlineData("jason://attention\\approvals\\" + Approval)]
    public void A_link_the_table_does_not_hold_is_refused(string uri) =>
        Assert.Null(AttentionLinks.Parse(uri));
}
