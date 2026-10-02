namespace Jason.App.Tests.Skills;

public partial class InteractiveSkillTests
{
    [Fact]
    public void The_operating_skill_says_what_a_session_cannot_do_and_what_is_not_released_yet()
    {
        var skill = Flattened("operating-the-installation");

        // Notifications: the toast needs nothing; a plugin sends a pointer to somebody else's server, so it is the
        // person's to ask for; a session reads the check rather than guessing; and it never sends a test
        // notification with the operating system's own tools or edits the registry.
        Assert.Contains("Naming a notification plugin in `settings.json` sends a pointer to a third party's server. Do it only when the person asks, with the topic they choose.", skill, StringComparison.Ordinal);
        Assert.Contains("Do not send a test notification through the operating system's own tools", skill, StringComparison.Ordinal);
        Assert.Contains("`jason status`'s `notifications` check", skill, StringComparison.Ordinal);
        Assert.Contains("Do not disturb is invisible to Jason", skill, StringComparison.Ordinal);

        // On Windows the verbs that change a logon registration run from an ordinary prompt on every account now,
        // so a session runs them itself. A skill that still sent people to elevate would have a session hand a
        // standard-account user a command they cannot run, for something the session could have done.
        Assert.Contains("Run `enable` and `disable` yourself", skill, StringComparison.Ordinal);
        Assert.DoesNotContain("Standard Windows accounts are unsupported", skill, StringComparison.Ordinal);
        Assert.DoesNotContain("S4U", skill, StringComparison.Ordinal);

        // The one case left for an elevated prompt, worded on the refusal having happened - it was never
        // measured - and the session's own limit inside it: without this sentence the obvious next move is to
        // try to elevate, relaunching, guessing at a runas, retrying the same refused command, instead of
        // handing the person the line and asking what it said.
        Assert.Contains("If `enable` or `disable` answers `ERROR: Access is denied.`", skill, StringComparison.Ordinal);
        Assert.DoesNotContain("One case is refused", skill, StringComparison.Ordinal);
        Assert.Contains("you cannot open one", skill, StringComparison.Ordinal);

        // One task per account, and the old shared one taken away only where it is the person's own. And uninstall
        // gets the order it needs - the elevated disable, then uninstall again - rather than enable's.
        Assert.Contains("Each account has its own task", skill, StringComparison.Ordinal);
        Assert.Contains("never touch anybody else's", skill, StringComparison.Ordinal);
        Assert.Contains("only where it is the person's own 0.1.0 task, the one that runs the runtime", skill, StringComparison.Ordinal);
        Assert.DoesNotContain("take the person's own away", skill, StringComparison.Ordinal);
        Assert.Contains("then `jason uninstall` again", skill, StringComparison.Ordinal);
        Assert.Contains("the machine refused to register a logon task for this account", skill, StringComparison.Ordinal);

        // Enable again only where enable was refused. After a refused disable the elevated removal was the whole
        // job, and a session that then ran enable would register the very thing the person asked to have removed.
        Assert.Contains("Run `jason runtime autostart enable` yourself afterwards only if it was `enable` that was refused", skill, StringComparison.Ordinal);
        Assert.Contains("after a refused `disable`, the removal was the whole job", skill, StringComparison.Ordinal);
        Assert.DoesNotContain("ask what it said, and then run `jason runtime autostart enable` yourself", skill, StringComparison.Ordinal);

        // And the elevated remedy is an administrator's act on their own account. On a standard account an elevated
        // prompt runs as the administrator, whose disable would take the administrator's tasks and not this person's.
        Assert.Contains("On an administrator's own account", skill, StringComparison.Ordinal);
        Assert.Contains("On a standard account an elevated prompt runs as somebody else", skill, StringComparison.Ordinal);
        Assert.Contains("ask an administrator to delete the task by the name the message gives", skill, StringComparison.Ordinal);
        Assert.Contains("never run `jason` under another account", skill, StringComparison.Ordinal);
        Assert.Contains("or by this version, run from an elevated prompt", skill, StringComparison.Ordinal);

        // And how the registration is checked, and by whom it is touched. In a gated reading a session enabled it and
        // read it back with status, then guessed the task's name, listed the machine's tasks to find it, and ran it by
        // hand "to prove the registered command works" - which started a runtime now, when the person had asked for
        // one at the next logon. status is the check; the operating system's own tools are not the session's to use on
        // the registration; and a runtime now is a different verb, offered rather than done.
        Assert.Contains(
            "**`status` is how you check it.** It reads back what the operating system holds for this account.",
            skill,
            StringComparison.Ordinal);
        Assert.Contains(
            "Leave the registration to `enable`, `disable` and `status`: do not run, change or delete it with the " +
            "operating system's own tools (`schtasks` or the Task Scheduler, `launchctl`, `systemctl`), not even to test it.",
            skill,
            StringComparison.Ordinal);
        Assert.Contains("Running it by hand starts a runtime now, which is not what the person asked for.", skill, StringComparison.Ordinal);
        Assert.Contains(
            "If they want the runtime running now as well, that is `jason runtime start`: say so, or ask.",
            skill,
            StringComparison.Ordinal);

        // After an upgrade from 0.1.0 the runtime that 0.1.0's task started may still hold the data directory. The
        // sentence is keyed to evidence a session can have, and the stop waits for the person's word, because a stop
        // ends work in flight.
        Assert.Contains("Only when `jason status` says the runtime runs in session 0, or the person tells you 0.1.0 had autostart enabled", skill, StringComparison.Ordinal);
        Assert.Contains("ask before you run `jason runtime stop`: it stops work in flight", skill, StringComparison.Ordinal);
        Assert.Contains("If `enable` or `disable` answers `ERROR: Access is denied.`, follow its message first, as below.", skill, StringComparison.Ordinal);
        Assert.Contains("Then `jason runtime start` starts the runtime this version runs, or the person signs in again with autostart enabled.", skill, StringComparison.Ordinal);
        var stopAt = skill.IndexOf("`jason runtime stop` once ends it", StringComparison.Ordinal);
        Assert.True(
            skill.IndexOf("After `enable` or `disable` once", StringComparison.Ordinal) is var enableAt and >= 0
                && stopAt > enableAt
                && skill.IndexOf("Then `jason runtime start` starts the runtime", StringComparison.Ordinal) > stopAt,
            "the skill does not name the acts in order");

        // And the one cost, said when the registration is made rather than discovered at the next sign-out.
        Assert.Contains("ends when they sign out", skill, StringComparison.Ordinal);

        // And what signing out does to work in flight, as the work-execution contract says it: not "taken up by
        // the next runtime", which a provider operation that may already have acted never is. It waits for a
        // person, and a session that promised otherwise would tell someone their send would simply resume.
        Assert.DoesNotContain("Work that was in flight is taken up by the next runtime", skill, StringComparison.Ordinal);
        Assert.Contains("a provider operation that may already have acted waits for a person", skill, StringComparison.Ordinal);

        // The install one-liners resolve to the latest release. A skill that printed them without this would
        // have a person paste a line, get a 404 and conclude the product does not install - when a repository
        // with no published release simply has no latest, and building from source is the way in.
        //
        // Stated as the rule rather than as today's state, so that it does not turn false the day a release
        // exists. The sentence this replaces asserted a present fact - "no release has been published yet" -
        // and this assertion was what kept it alive: the fifth site of that shape in this repository, and the
        // one the plan for this increment did not name.
        Assert.Contains("If they answer 404", skill, StringComparison.Ordinal);
        Assert.Contains("build from source", skill, StringComparison.Ordinal);
        Assert.DoesNotContain("no release has been published yet", skill, StringComparison.Ordinal);

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

        // A session asked for one plugin by name in both gated runs, and both times typed `plugin get`, which
        // does not exist. The skill names the whole plugin vocabulary rather than leaving the gap to a guess.
        Assert.Contains("there is no `jason plugin get`", skill, StringComparison.Ordinal);
    }
}
