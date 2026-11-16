namespace Jason.App.Tests.Skills;

/// <summary>
/// The chat of Jason Desktop reads the same session skills a person's own session reads, and three of their acts are
/// the person's own there: starting or resuming a campaign, and saying who the messages are written as and how they
/// sound. Each skill that tells a session to do one of them carries the chat's exception beside it, in the form
/// <c>approvals-and-questions</c> uses for send modes, and keeps its own instruction for a person's own session.
/// </summary>
public partial class InteractiveSkillTests
{
    /// <summary>
    /// Without it, the quick start's "then you start the campaign" sends the chat's AI to start what only the person
    /// may, or to tell them it started when nothing did; and its "never ask for a preference" leaves a chat whose
    /// person has named nobody to write as silent about the one thing that holds every message.
    /// </summary>
    [Fact]
    public void The_quick_start_leaves_starting_the_campaign_to_the_person_in_the_desktop_chat()
    {
        var skill = Flattened("quick-start");
        Assert.Contains("In the chat of Jason Desktop you cannot start the campaign: starting it is the person's own act. There, once the plan is agreed and its work is queued, say once that it is ready and that they press Start at the top of the campaign's chat, and never say it has started until `jason campaign get` answers the status `active`. Elsewhere you start it on their go, as above.", skill, StringComparison.Ordinal);
        Assert.Contains("Then you start the campaign:", skill, StringComparison.Ordinal);
        Assert.Contains("In the chat of Jason Desktop a missing name is said all the same, once: that nobody is named to write as, and that they set it in Settings under \"Your name and tone\"; a missing tone is said once too, as theirs to add there.", skill, StringComparison.Ordinal);
    }

    /// <summary>
    /// Without them, the managed path starts and resumes campaigns from the chat, and asks there for the two
    /// preferences the chat can neither take nor set, so the roles that write fail later with nobody named. A
    /// person's own session still starts the campaign from the work block.
    /// </summary>
    [Fact]
    public void The_managed_path_leaves_starting_and_the_name_and_tone_to_the_person_in_the_desktop_chat()
    {
        var skill = Flattened("managed-campaign-work");
        Assert.Contains("In the chat of Jason Desktop you cannot start a campaign or resume a paused one: either is the person's own act. There, once the plan is agreed and its work is created, say once that it is ready and that they press Start at the top of the campaign's chat, or Resume for a paused one, and never say it has started until `jason campaign get` answers the status `active`. In a person's own session you start it on their go, as above, and resume it the same way.", skill, StringComparison.Ordinal);
        Assert.Contains("In the chat of Jason Desktop you cannot ask for those two preferences or set them. Where the sender has no value, tell the person once that it is set in Settings under \"Your name and tone\", go on with what writes to nobody, and create nothing that writes to anybody until it has one; a missing tone is said once, as theirs to add there, and holds nothing. A person's own session asks for them and sets them as `planner-in-session` says.", skill, StringComparison.Ordinal);

        var printed = SkillPack.PrintedCommands(SkillPack.Find("managed-campaign-work").File).ToList();
        var create = printed.FindIndex(line => line.StartsWith("jason campaign create ", StringComparison.Ordinal));
        var start = printed.IndexOf("jason campaign start cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD");
        Assert.True(
            create >= 0 && start > create,
            $"managed-campaign-work prints the campaign's create at {create} and its start at {start}: the work block a "
            + "person's own session follows still ends by starting the campaign.");
    }

    /// <summary>
    /// Without it, the planner's opening asks the chat's person for their name and tone and then runs a set the chat
    /// refuses; a person's own session still asks and sets.
    /// </summary>
    [Fact]
    public void The_planner_points_the_desktop_chat_to_settings_for_the_name_and_tone()
    {
        var skill = Flattened("planner-in-session");
        Assert.Contains("In the chat of Jason Desktop you neither ask for these two nor set them: the person sets them there in Settings, under \"Your name and tone\". Where the sender has no value, say so once and where to click, and go on with the parts of the plan that write to nobody; until it has one, nothing that writes to anybody is agreed or created. A tone with no value is said once, as one they may add, and holds nothing back. In a person's own session, ask for them and set them as above.", skill, StringComparison.Ordinal);
        Assert.Contains("set it under their own name as the actor:", skill, StringComparison.Ordinal);
    }
}
