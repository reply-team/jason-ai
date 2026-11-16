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
    /// may, or to tell them it started when nothing did.
    /// </summary>
    [Fact]
    public void The_quick_start_leaves_starting_the_campaign_to_the_person_in_the_desktop_chat()
    {
        var skill = Flattened("quick-start");
        Assert.Contains("In the chat of Jason Desktop you cannot start the campaign: starting it is the person's own act. There, once the plan is agreed and its work is queued, say that it is ready and that they open the agent from Agents in the sidebar and press Start, and never say it has started until `jason campaign get` answers that it is active. Elsewhere you start it on their go, as above.", skill, StringComparison.Ordinal);
        Assert.Contains("Then you start the campaign:", skill, StringComparison.Ordinal);
    }

    /// <summary>
    /// Without them, the managed path starts and resumes campaigns from the chat, and asks there for the two
    /// preferences the chat can neither take nor set, so the roles that write fail later with nobody named.
    /// </summary>
    [Fact]
    public void The_managed_path_leaves_starting_and_the_name_and_tone_to_the_person_in_the_desktop_chat()
    {
        var skill = Flattened("managed-campaign-work");
        Assert.Contains("In the chat of Jason Desktop you cannot start a campaign or resume a paused one: either is the person's own act. There, once the plan is agreed and its work is created, say that it is ready and that they open the agent from Agents in the sidebar and press Start, or Resume for a paused one, and never say it has started until `jason campaign get` answers that it is active. In a person's own session you start it on their go, as above, and resume it the same way.", skill, StringComparison.Ordinal);
        Assert.Contains("In the chat of Jason Desktop you cannot ask for those two preferences or set them. Where either has no value, tell the person once which it is and that it is set in Settings under \"Your name and tone\"; go on with what writes to nobody, and create nothing that writes to anybody until both have one. A person's own session asks for them and sets them as `planner-in-session` says.", skill, StringComparison.Ordinal);
    }

    /// <summary>
    /// Without it, the planner's opening asks the chat's person for their name and tone and then runs a set the chat
    /// refuses; a person's own session still asks and sets.
    /// </summary>
    [Fact]
    public void The_planner_points_the_desktop_chat_to_settings_for_the_name_and_tone()
    {
        var skill = Flattened("planner-in-session");
        Assert.Contains("In the chat of Jason Desktop you neither ask for these two nor set them: the person sets them there in Settings, under \"Your name and tone\". Where one has no value, say once which and where to click, and go on with the parts of the plan that write to nobody; until both have a value, nothing that writes to anybody is agreed or created. In a person's own session, ask for them and set them as above.", skill, StringComparison.Ordinal);
        Assert.Contains("set it under their own name as the actor:", skill, StringComparison.Ordinal);
    }
}
