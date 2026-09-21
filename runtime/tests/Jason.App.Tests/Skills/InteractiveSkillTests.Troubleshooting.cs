namespace Jason.App.Tests.Skills;

public partial class InteractiveSkillTests
{
    [Fact]
    public void The_troubleshooting_skill_is_honest_about_what_this_version_does_not_have()
    {
        var skill = Flattened("troubleshooting-jason");

        // Every other page a person has read this year offers a command that packages up the evidence. This
        // build has none, and a session that implied one would send somebody hunting for a verb that does
        // not exist - then report the tool as broken when they could not find it.
        Assert.Contains("no command that collects a diagnostic bundle", skill, StringComparison.Ordinal);

        // So the honest answer names where the files actually are. Without the path, "look at the logs" is
        // advice nobody can follow, and the next sentence - that nothing reads them for you - is what stops
        // a session promising to search them.
        Assert.Contains("~/.jason/logs/", skill, StringComparison.Ordinal);

        // A failed item ends; there is no verb that reopens one. A session that offered to "retry it" would
        // be promising something the API cannot do, and the person would wait for work that never runs.
        Assert.Contains("a failed item is finished", skill, StringComparison.OrdinalIgnoreCase);

        // And the thing to do instead, said as a rule rather than as a refusal: the corrected item is new
        // work with its own id, which is also why the old one's history stays readable.
        Assert.Contains("work still wanted is new work", skill, StringComparison.Ordinal);

        // The runtime already retried whatever deserved retrying. Typing the same thing again by hand is
        // how a real message reaches a real person twice, and it is the single most tempting wrong move
        // when somebody is standing over the session asking why nothing has happened.
        Assert.Contains("do not retry blindly", skill, StringComparison.OrdinalIgnoreCase);

        // The one outcome class that is neither success nor failure has to be named by its code, because
        // that is the word the work item prints and the word a person will paste back.
        Assert.Contains("ambiguous", skill, StringComparison.Ordinal);

        // And what the code means, spelled out. Read as "it failed", an ambiguous attempt gets repeated and
        // the provider acts twice; read as "it worked", the effect is recorded as done when it may not be.
        Assert.Contains("nobody can say whether the provider acted", skill, StringComparison.Ordinal);
    }
}
