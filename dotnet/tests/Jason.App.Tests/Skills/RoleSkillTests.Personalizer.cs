namespace Jason.App.Tests.Skills;

public partial class RoleSkillTests
{
    [Fact]
    public void The_personalizer_is_told_that_nothing_here_stores_a_draft_about_a_person()
    {
        var skill = Flattened("personalizer");

        // The role most likely to write about a person into the person's own record, which is their data and
        // is read by everything else that touches them.
        Assert.Contains("There is no per-contact draft store in this build", skill, StringComparison.Ordinal);
        Assert.Contains("is not a place to park drafts about them", skill, StringComparison.Ordinal);

        CarriesTheLaunchContract("personalizer");
    }

    /// <summary>
    /// The paragraph that said no operation sends outreach went stale when <c>message.send</c> arrived. The personalizer is
    /// told which operations write a message a person receives and that a person approves each with a preview before it
    /// is sent, and that a template is the copywriter's shape where its own is final text for one person.
    /// </summary>
    [Fact]
    public void The_personalizer_is_told_which_operations_write_a_message_a_person_receives()
    {
        var skill = Flattened("personalizer");

        Assert.DoesNotContain("No operation this build publishes sends outreach", skill, StringComparison.Ordinal);
        Assert.DoesNotContain("Nothing here composes outreach and sends it.", skill, StringComparison.Ordinal);
        Assert.Contains(
            "**The operations that write a message a person outside Jason receives are `message.send`, an email or a LinkedIn message to one person outside any campaign; `social_invitation.send`, a LinkedIn connection request with its note; and `conversation.reply`, an answer inside the thread the person wrote in. Each one waits for a person, who approves it with a preview, unless that person has chosen that sends to this kind of contact go automatically.**",
            skill,
            StringComparison.Ordinal);
        Assert.Contains("Each is its own act, one message to one person, and the claim decides which when it runs the item: the choice is the person's and never a role's.", skill, StringComparison.Ordinal);
        Assert.Contains("What you write is this work item's result; completing it sends nothing.", skill, StringComparison.Ordinal);
        Assert.Contains(
            "whatever goes out after that is the provider's doing, on its schedule, and nobody approves those messages one by one.",
            skill,
            StringComparison.Ordinal);
        Assert.Contains(
            "`campaign.activate` is what starts a campaign sending, and a person approves it with a preview that names how many people its first step goes to.",
            skill,
            StringComparison.Ordinal);
        Assert.Contains("A template is the copywriter's shape; yours is final text for one person.", skill, StringComparison.Ordinal);
    }

    /// <summary>
    /// A first message built on something that happened to the person reads as written for them; one padded around a
    /// title reads as pretending to know them. The personalizer is told that the length follows what this runtime holds
    /// about the person, where that comes from, and that a length the person set is theirs to keep.
    /// </summary>
    [Fact]
    public void The_personalizer_makes_a_first_message_as_long_as_what_is_known_about_the_person()
    {
        var skill = Flattened("personalizer");

        Assert.Contains("**A first message is as long as what is known about the person.**", skill, StringComparison.Ordinal);
        Assert.Contains(
            "A concrete fact is one this runtime holds about them or their case — what a researcher established on an item about them, their record, the campaign's context — and something that happened or was said, not a title or a company's name alone.",
            skill,
            StringComparison.Ordinal);
        Assert.Contains("With one, write a pointed message built on it, and not a long one.", skill, StringComparison.Ordinal);
        Assert.Contains(
            "With little known, write a very short message that does not pretend to know them: no interest, familiarity or praise that nothing here supports.",
            skill,
            StringComparison.Ordinal);
        Assert.Contains("Where the brief or the person's memory says how long it should be, theirs holds.", skill, StringComparison.Ordinal);
    }
}
