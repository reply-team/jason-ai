namespace Jason.App.Tests.Skills;

public partial class RoleSkillTests
{
    [Fact]
    public void The_copywriter_is_told_that_nothing_here_stores_a_message()
    {
        var skill = Flattened("copywriter");

        // Writing is not sending, and there is nowhere to put a draft that anything reads back. A copywriter
        // that believed otherwise would write into its note and report the work done, and the campaign would
        // have messaging nobody can find.
        Assert.Contains("There is no message store and no template store in this build", skill, StringComparison.Ordinal);
        Assert.Contains("Nothing is sent by being written", skill, StringComparison.Ordinal);

        CarriesTheLaunchContract("copywriter");
    }

    /// <summary>
    /// The paragraph that said no operation sends outreach went stale when <c>message.send</c> arrived. The copywriter is told
    /// which operations write a message a person receives, that a person approves each with a preview before it is sent, and
    /// what stays the provider's own doing on its schedule, so it does not write as though its copy could never reach anybody.
    /// </summary>
    [Fact]
    public void The_copywriter_is_told_which_operations_write_a_message_a_person_receives()
    {
        var skill = Flattened("copywriter");

        Assert.DoesNotContain("No operation this build publishes sends outreach", skill, StringComparison.Ordinal);
        Assert.DoesNotContain("Nothing here composes outreach and sends it.", skill, StringComparison.Ordinal);
        Assert.Contains(
            "**The operations that write a message a person outside Jason receives are `message.send`, an email or a LinkedIn message to one person outside any campaign; `social_invitation.send`, a LinkedIn connection request with its note; and `conversation.reply`, an answer inside the thread the person wrote in. A person approves each one with a preview before it is sent.**",
            skill,
            StringComparison.Ordinal);
        Assert.Contains("Each is its own decision, one message to one person, and the dispatcher runs one only after a person has approved it.", skill, StringComparison.Ordinal);
        Assert.Contains(
            "whatever goes out after that is the provider's doing, on its schedule, and nobody approves those messages one by one.",
            skill,
            StringComparison.Ordinal);
        Assert.Contains("Your copy is this work item's result; completing it sends nothing.", skill, StringComparison.Ordinal);
        Assert.Contains(
            "`campaign.activate` is what starts a campaign sending, and a person approves it with a preview that names how many people its first step goes to.",
            skill,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// One message for a whole audience may be a template in Jason's own standard. The skill points at the standard's page
    /// rather than restating a rule of it, because that page is the standard's only statement, and says that a template is
    /// for an email only, since a LinkedIn message is refused.
    /// </summary>
    [Fact]
    public void The_copywriter_may_answer_with_a_template_where_the_brief_asks_for_one_message_per_audience()
    {
        var skill = Flattened("copywriter");

        Assert.Contains(
            "Where the brief asks for one message per audience rather than one text per person, you may answer with a template in Jason's own standard, only the constructs and names `message.send` lists, and a person reads it with one rendered example before anything is sent.",
            skill,
            StringComparison.Ordinal);
        Assert.Contains(
            "A template is for an email only: a LinkedIn message written as a template is refused, so write one as final text.",
            skill,
            StringComparison.Ordinal);
        Assert.Contains("`docs/contracts/README.md`, in the section \"The template standard\"", skill, StringComparison.Ordinal);
        Assert.Contains("There is no message store and no template store in this build.", skill, StringComparison.Ordinal);
        Assert.Contains("A template is the text of one message, written once for an audience", skill, StringComparison.Ordinal);
        Assert.True(
            skill.IndexOf("## When one message serves a whole audience", StringComparison.Ordinal)
                is var section and >= 0
            && section < skill.IndexOf("## Write as the sender, in the company's tone", StringComparison.Ordinal),
            "The template section comes before the section both copy roles share word for word.");

        CarriesTheLaunchContract("copywriter");
    }

    /// <summary>
    /// The standard is strict and the template is refused whole at the work item's creation when one tag is wrong, so the
    /// role is told where the page is and what to do without it: the document is published with the project's source and no
    /// verb prints it, so a machine without a copy gets final text and the answer says so, never a template written from
    /// guesswork.
    /// </summary>
    [Fact]
    public void The_copywriter_writes_final_text_and_says_so_when_it_holds_no_copy_of_the_standard()
    {
        var skill = Flattened("copywriter");

        Assert.Contains(
            "That page is published with the project's source and no verb prints it: on a machine with no copy of it, write final text for each person and say so in your answer.",
            skill,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A template serves an audience as one <c>message.send</c> item per person, each carrying it, and <c>compose</c> makes
    /// the subject a template too. The role answers with both together as its result and says who carries them into each
    /// person's item, each parked for its own approval; a body template beside a subject written as final text would be
    /// refused when the item is created, and nobody would be told to copy the result at all.
    /// </summary>
    [Fact]
    public void The_copywriter_answers_with_a_subject_template_and_a_body_template_together_for_whoever_raises_the_sends()
    {
        var skill = Flattened("copywriter");

        Assert.Contains(
            "Answer with a subject template and a body template together, as this work item's result: whoever raises the sends copies them into each person's `message.send` item, and each of those parks for its own approval.",
            skill,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The person's one rendered example cannot show the sending account's names or a custom field, which only the provider
    /// fills in, and the sending account is the provider's, not the <c>sender.identity</c> preference the next section has the
    /// role write by. Told neither, the role would read `sender.name` as the sender it signs as and write the signature twice.
    /// </summary>
    [Fact]
    public void The_copywriter_is_told_what_the_example_cannot_show_and_that_sender_names_are_the_providers()
    {
        var skill = Flattened("copywriter");

        Assert.Contains(
            "The example cannot show `sender.*` or `contact.custom.*`, which only the provider fills in, and `sender.*` is the provider account's identity, not the `sender.identity` preference you write by below.",
            skill,
            StringComparison.Ordinal);
    }

    /// <summary>The text changed and no host has read it in this state, so the role stays a draft in its file and in the catalog.</summary>
    [Fact]
    public void The_copywriter_stays_a_draft_until_a_host_reads_it()
    {
        var file = SkillPack.Find("copywriter").File;
        Assert.Equal("draft", SkillFrontMatter.Read(file).Metadata.GetValueOrDefault("status"));
        Assert.Contains("**Status: draft.** No agent host has read this text in the state it ships in.", Flattened("copywriter"), StringComparison.Ordinal);

        var catalog = Whitespace().Replace(File.ReadAllText(SkillPack.Catalog()), " ");
        Assert.Contains("| `copywriter` | draft | — | — | — |", catalog, StringComparison.Ordinal);
    }
}
