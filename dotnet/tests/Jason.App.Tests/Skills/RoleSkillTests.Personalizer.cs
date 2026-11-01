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
            "**The operations that write a message a person outside Jason receives are `message.send`, an email or a LinkedIn message to one person outside any campaign; `social_invitation.send`, a LinkedIn connection request with its note; and `conversation.reply`, an answer inside the thread the person wrote in. A person approves each one with a preview before it is sent.**",
            skill,
            StringComparison.Ordinal);
        Assert.Contains("Each is its own decision, one message to one person, and the dispatcher runs one only after a person has approved it.", skill, StringComparison.Ordinal);
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
}
