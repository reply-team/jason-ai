namespace Jason.App.Tests.Skills;

public partial class InteractiveSkillTests
{
    [Fact]
    public void The_operating_skill_says_what_a_session_cannot_do_and_what_is_not_released_yet()
    {
        var skill = Flattened("operating-the-installation");

        // On Windows the two verbs that change a logon registration are refused outright from a
        // medium-integrity process. A skill that did not name the elevated prompt would have a session
        // report the Task Scheduler's "Access is denied" as a broken installation, and a person would go
        // looking for a fault that is not there.
        Assert.Contains("elevated prompt", skill, StringComparison.Ordinal);

        // And naming it is not enough: the session has to know the limit is its own. Without this sentence
        // the obvious next move is to try to elevate - relaunching, guessing at a runas, retrying the same
        // refused command - instead of handing the person the line and asking what it said.
        Assert.Contains("you cannot open one", skill, StringComparison.Ordinal);

        // The other half of the same fact. A session that believed every logon verb was out of reach
        // would refuse to answer "is Jason set to start at logon?", which it can always answer.
        Assert.Contains("needs no elevation", skill, StringComparison.Ordinal);

        // The install one-liners resolve to the latest release and there is none. A skill that printed them
        // without this would have a person paste a line that answers 404 and conclude the product does not
        // install - when building from source is what works today.
        Assert.Contains("404 until the first release", skill, StringComparison.Ordinal);

        // The runtime volunteers that a newer version exists, so every session on this installation learns
        // it. Without a bound, every one of them says so, and a person who declined once is told again.
        Assert.Contains("at most once", skill, StringComparison.Ordinal);

        // Mentioning is not the same as derailing. This is what keeps a noticed version a sentence at the
        // end rather than a detour out of the task the person actually came with.
        Assert.Contains("never interrupt", skill, StringComparison.OrdinalIgnoreCase);

        // The one command in this skill that changes the machine. It stops the runtime and replaces the
        // running executable, so a session that ran it helpfully would take an installation down - and its
        // work in flight with it - for a decision that was never asked of it.
        Assert.Contains(
            "never run `jason update apply` unless the person asked for it",
            skill,
            StringComparison.Ordinal);
    }
}
