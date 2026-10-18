using System.Text.RegularExpressions;
using Jason.Runtime.Configuration;

namespace Jason.App.Tests.Skills;

public partial class InteractiveSkillTests
{
    /// <summary>How every place that brings a session to a stop points at the one rule for it.</summary>
    private const string StopPointer = "ask first, as \"Starting and stopping it\" says";

    /// <summary>What a stop does, in the one sentence the operating skill says it in.</summary>
    private const string WhatAStopDoes =
        "What a stop does: new work stops being claimed, what is in flight gets up to ten seconds to finish, nothing is "
        + "cancelled, and a provider operation already under way may still act, ending at its own lease.";

    private const string StopRule = "**Ask before any stop you run yourself.**";

    /// <summary>
    /// A stop a session runs itself — <c>jason runtime stop</c>, or the restart that begins with one — is asked first, in
    /// words, saying what a stop does, whatever brought the session to it. The rule is said once, where starting and
    /// stopping are taught, and every place that brings a session to a stop points at it rather than saying it again.
    /// Written into two of those places and nowhere else, it did not reach a restart the person asked for: a session took
    /// the request for the yes, judged a runtime with nothing in flight safe to restart, and went to run it without a word.
    /// </summary>
    [Fact]
    public void The_operating_skill_says_once_that_a_stop_it_runs_is_asked_first()
    {
        var text = File.ReadAllText(SkillPack.Find("operating-the-installation").File).Replace("\r\n", "\n", StringComparison.Ordinal);
        var skill = Flattened("operating-the-installation");

        Assert.True(
            Occurrences(skill, StopRule) == 1,
            $"The operating skill opens the rule for a stop it runs itself {Occurrences(skill, StopRule)} time(s); it says it once.");
        Assert.Equal(
            StopRule + " Before you run `jason runtime stop` or `jason runtime restart`, whatever brought you to it, ask the person in words, say what a stop does, and run it only on their yes. "
            + "That holds when the restart was their own request, and when the runtime looks idle: work can be claimed between the status you read and the stop. "
            + WhatAStopDoes,
            ParagraphAt(text, StopRule));

        // Where starting and stopping are taught: the restart is named a stop and pointed at the rule, which follows it
        // before the foreground form.
        var section = Whitespace().Replace(SectionOf(text, "## Starting and stopping it"), " ");
        var restart = section.IndexOf("`jason runtime restart` is the two in order, so it is a stop as well: the next paragraph holds for it.", StringComparison.Ordinal);
        var rule = section.IndexOf(StopRule, StringComparison.Ordinal);
        var run = section.IndexOf("`jason runtime run` keeps the runtime", StringComparison.Ordinal);
        Assert.True(
            restart >= 0 && rule > restart && run > rule,
            $"In \"Starting and stopping it\" the restart sentence is at offset {restart}, the rule at {rule} and the foreground form at {run}; they belong in that order.");

        // Every other place points at it, and none says it again in its own words.
        Assert.DoesNotContain("ask before you run", skill, StringComparison.OrdinalIgnoreCase);
        Assert.True(
            Occurrences(skill, StopPointer) == 3,
            $"The operating skill points at the rule {Occurrences(skill, StopPointer)} time(s); the 0.1.0 task, session 0 and a package manager's upgrade each point at it once.");
        foreach (var (context, place) in new[]
        {
            (ParagraphAt(text, "If `enable` or `disable` answers `ERROR: Access is denied.`, follow its message first"), "the 0.1.0 task"),
            (BulletAt(text, "- **`failed`, session 0**"), "session 0"),
            (BlockAt(text, "**Installed by a package manager.**"), "a package manager's upgrade"),
        })
        {
            Assert.True(
                context.Contains(StopPointer, StringComparison.Ordinal),
                $"Where the operating skill brings a session to a stop for {place}, it does not point at the rule: {context}");
        }
    }

    /// <summary>
    /// What a stop does is one sentence, the same in both places the skill says it: in the rule, and where a person may type
    /// the stop themselves around a package manager's upgrade. Two wordings are how one place came to tell a person less
    /// than the other. Each clause is what the runtime does: the dispatcher stops claiming and drains its handlers for
    /// <c>Dispatcher:DrainSeconds</c>, kills nothing it gives up on, and leaves a provider operation to end at its lease —
    /// so the sentence's ten seconds is held to the setting's default.
    /// </summary>
    [Fact]
    public void The_operating_skill_says_what_a_stop_does_in_one_sentence()
    {
        var text = File.ReadAllText(SkillPack.Find("operating-the-installation").File).Replace("\r\n", "\n", StringComparison.Ordinal);
        var skill = Flattened("operating-the-installation");

        Assert.True(
            Occurrences(skill, WhatAStopDoes) == 2,
            $"The operating skill says what a stop does in its one sentence {Occurrences(skill, WhatAStopDoes)} time(s); it says it in the rule and around a package manager's upgrade.");
        Assert.Contains(WhatAStopDoes, ParagraphAt(text, StopRule), StringComparison.Ordinal);
        Assert.Contains(WhatAStopDoes, BlockAt(text, "**Installed by a package manager.**"), StringComparison.Ordinal);

        foreach (var other in new[]
        {
            "nothing new is claimed from then on",
            "finishes or ends at its own lease",
            "does not take back a send already under way",
            "it stops the runtime claiming new work",
        })
        {
            Assert.DoesNotContain(other, skill, StringComparison.Ordinal);
        }

        var drain = new DispatcherOptions().DrainSeconds;
        Assert.True(drain == 10, $"The skill says a stop gives what is in flight up to ten seconds, and Dispatcher:DrainSeconds defaults to {drain}.");
    }

    /// <summary>
    /// The provider's own web app is no step of any Jason flow. A person opens it by a link for the four acts only they can
    /// do on their own account — connecting a LinkedIn account, a mailbox by single sign-on, signing in, the first API
    /// key — and a session never sends them there for the work. Said where the skill hands a ready installation on to the
    /// real work, because that is where a session would otherwise walk the person through the provider's screens. The
    /// paragraph is held whole, so a fifth use added to it is as red as one taken away.
    /// </summary>
    [Fact]
    public void The_operating_skill_sends_a_person_to_the_provider_s_web_app_for_four_things_only()
    {
        var text = File.ReadAllText(SkillPack.Find("operating-the-installation").File).Replace("\r\n", "\n", StringComparison.Ordinal);
        const string Opening = "The provider's own web app is no step of any Jason flow.";

        Assert.Equal(
            Opening + " A person opens it by a link for four things only — connecting a LinkedIn account, connecting a mailbox by single sign-on, signing in, and creating the first API key — and each is theirs to do: say what it is for, and wait until they say it is done. "
            + "Never send them there for the work itself.",
            ParagraphAt(text, Opening));

        var skill = Flattened("operating-the-installation");
        var ready = skill.IndexOf("When `jason status` says `ready`, ask the person once what comes next", StringComparison.Ordinal);
        var app = skill.IndexOf(Opening, StringComparison.Ordinal);
        var off = skill.IndexOf("## Taking it off again", StringComparison.Ordinal);
        Assert.True(
            ready >= 0 && app > ready && off > app,
            $"The paragraph on the provider's web app is at offset {app}; it belongs after the one on a ready installation (offset {ready}) and before \"Taking it off again\" (offset {off}).");
    }

    /// <summary>
    /// An update now redeploys the skills an installation recorded, as its last step, so the skill says what that step
    /// does and what it leaves: a failed redeploy leaves the update standing and names its own repair, and an update a
    /// build before 0.12.0 applied has no such step and needs one <c>jason skills update</c> after it. Said where the
    /// skill tells what <c>jason status</c> reports after an update, so a session that meets skills from another release
    /// knows which of the two cases it is in.
    /// </summary>
    [Fact]
    public void The_operating_skill_says_what_an_update_does_for_the_skills()
    {
        var text = File.ReadAllText(SkillPack.Find("operating-the-installation").File).Replace("\r\n", "\n", StringComparison.Ordinal);
        var paragraph = ParagraphAt(text, "After an update, `jason status` says whether the role skills still belong to this build");

        Assert.Contains(
            "From 0.12.0 on, `jason update apply` redeploys the skills this installation recorded as its last step, once the update is complete; if that step's line says it could not, the update still stands and the fix is the line it names.",
            paragraph,
            StringComparison.Ordinal);
        Assert.Contains(
            "An update applied by a build before 0.12.0 has no such step, so it needs one `jason skills update` after it.",
            paragraph,
            StringComparison.Ordinal);
    }

    private static int Occurrences(string text, string value)
    {
        var count = 0;
        for (var at = text.IndexOf(value, StringComparison.Ordinal); at >= 0; at = text.IndexOf(value, at + value.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    /// <summary>The paragraph that holds <paramref name="words"/>, from the blank line before it to the one after, flattened.</summary>
    private static string ParagraphAt(string text, string words)
    {
        var at = Locate(text, words);
        var start = text.LastIndexOf("\n\n", at, StringComparison.Ordinal) is var before and >= 0 ? before + 2 : 0;
        var end = text.IndexOf("\n\n", at, StringComparison.Ordinal);
        return Whitespace().Replace(text[start..(end > at ? end : text.Length)], " ").Trim();
    }

    /// <summary>One list entry, from its marker to the next entry or the end of the list, flattened.</summary>
    private static string BulletAt(string text, string marker)
    {
        var at = Locate(text, "\n" + marker);
        var next = text.IndexOf("\n- ", at + 1, StringComparison.Ordinal);
        var blank = text.IndexOf("\n\n", at + 1, StringComparison.Ordinal);
        var end = new[] { next, blank, text.Length }.Where(index => index > at).Min();
        return Whitespace().Replace(text[at..end], " ").Trim();
    }

    /// <summary>From a bold opening to the next bold opening at the start of a line, flattened: one topic with its lines.</summary>
    private static string BlockAt(string text, string opening)
    {
        var at = Locate(text, opening);
        var end = text.IndexOf("\n**", at + 1, StringComparison.Ordinal);
        return Whitespace().Replace(text[at..(end > at ? end : text.Length)], " ").Trim();
    }

    /// <summary>Where the text says <paramref name="words"/>, wherever its lines happen to break.</summary>
    private static int Locate(string text, string words)
    {
        var found = Regex.Match(text, Regex.Escape(words).Replace("\\ ", "\\s+", StringComparison.Ordinal));
        Assert.True(found.Success, $"The text no longer says: {words}");
        return found.Index;
    }

    /// <summary>One section, from its heading to the next heading of the same level.</summary>
    private static string SectionOf(string text, string heading)
    {
        var at = text.IndexOf("\n" + heading + "\n", StringComparison.Ordinal);
        Assert.True(at >= 0, $"The text has no section \"{heading}\".");
        var end = text.IndexOf("\n## ", at + 1, StringComparison.Ordinal);
        return text[at..(end > at ? end : text.Length)];
    }

    /// <summary>
    /// When the pointer is sent: a person waited for, and a protective action that failed. A skill that named the first
    /// alone would have a session tell a person that a notification about failed work cannot have come from Jason.
    /// </summary>
    [Fact]
    public void The_operating_skill_says_a_failed_protective_action_is_told_as_well()
    {
        var skill = Flattened("operating-the-installation");

        Assert.Contains(
            "When Jason starts waiting for a person — an approval parked at a claim, or a question a role raised — or when a protective action fails, it tells them once, with a pointer: how many, and where to look.",
            skill,
            StringComparison.Ordinal);
        Assert.DoesNotContain("it tells them once, with a pointer: how many wait, and where to look.", skill, StringComparison.Ordinal);
    }

    /// <summary>
    /// A ready installation is followed by one question, asked once: the wiring check — named for what it is, a synthetic
    /// walk that is not the real work — or the real work at once. A skip is the person's answer and the walk is not offered
    /// again, and the real work begins with an invitation to tell about the first campaign in their own words, which
    /// becomes the campaign's context. A session that took the walk for the job, or offered it at every turn, would keep
    /// the person from the work they came for.
    /// </summary>
    [Fact]
    public void The_operating_skill_asks_once_after_ready_and_honours_a_skip()
    {
        var skill = Flattened("operating-the-installation");

        Assert.Contains(
            "When `jason status` says `ready`, ask the person once what comes next: the wiring check, `docs/golden-path.md`, in the same session, or their real work at once.",
            skill,
            StringComparison.Ordinal);
        Assert.Contains(
            "Name the wiring check for what it is: a synthetic walk to meet the system, which connects their provider and proves it with their own address, and is not the real work.",
            skill,
            StringComparison.Ordinal);
        Assert.Contains("A skip is honoured: do not offer the walk again.", skill, StringComparison.Ordinal);

        // A skip connects no provider, and `ready` needs none, so the session that skipped meets `no_route` later with
        // nowhere to turn unless this paragraph says where a provider is connected: the wiring check's own steps, without
        // its enrolment.
        Assert.Contains(
            "A skip leaves no provider connected: when the work first needs one, the Reply CLI section and steps 3 to 5 of `docs/golden-path.md` connect it, without the walk's enrolment.",
            skill,
            StringComparison.Ordinal);
        Assert.Contains(
            "The real work, at once or after the walk, begins with an invitation to tell you about their first campaign in their own words — typed, a link, a file or a voice note, or one of a few examples you offer to start from.",
            skill,
            StringComparison.Ordinal);
        Assert.Contains(
            "What they tell you becomes the campaign's context when the campaign is created (`managed-campaign-work`), and the work is planned with the `campaign-planning` skill and worked by Jason's roles.",
            skill,
            StringComparison.Ordinal);
        Assert.DoesNotContain("one word from the person starts it", skill, StringComparison.Ordinal);
    }

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
        // sentence is keyed to evidence a session can have, and the stop waits for the person's word — by the one rule
        // for a stop a session runs itself, pointed at rather than said again, so the two can never drift apart.
        // "It stops work in flight" promised the opposite of what a stop does.
        Assert.Contains("Only when `jason status` says the runtime runs in session 0, or the person tells you 0.1.0 had autostart enabled", skill, StringComparison.Ordinal);
        Assert.Contains(
            "After `enable` or `disable` once, `jason runtime stop` once ends it; " + StopPointer + ".",
            skill,
            StringComparison.Ordinal);
        Assert.DoesNotContain("it stops work in flight", skill, StringComparison.Ordinal);
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

        // The install one-liners go through the product's address and resolve to the latest release. The public
        // repository has one, so a 404 from them is a fault on the way to it, said as it stands - not a
        // repository with nothing released, and never a reason to build: the runtime's source is not published.
        // The sentence this replaces sent a session to build from source, which no reader can do.
        Assert.Contains("curl -fsSL https://jasonharness.ai/releases/latest/download/install.sh | sh", skill, StringComparison.Ordinal);
        Assert.Contains("irm https://jasonharness.ai/releases/latest/download/install.ps1 | iex", skill, StringComparison.Ordinal);
        Assert.Contains("If either answers 404, the release could not be reached", skill, StringComparison.Ordinal);
        Assert.Contains("there is nothing to build instead — the runtime's source is not published", skill, StringComparison.Ordinal);
        Assert.DoesNotContain("has no published release", skill, StringComparison.Ordinal);
        Assert.DoesNotContain("the way in is to build from source", skill, StringComparison.Ordinal);
        Assert.DoesNotContain("no release has been published yet", skill, StringComparison.Ordinal);

        // And installing is the install page's, which a session may follow itself when the person asks it to,
        // asking before every act the page marks. The sentence this replaces called it a person's job at a
        // terminal, which the README's own prompt for an agent contradicts. The page is named by the address the
        // prompt gives, because the skill is read in a harness on a machine that has no checkout to open it in.
        Assert.Contains(
            "Installing Jason is `docs/INSTALL.md` (<https://raw.githubusercontent.com/reply-team/jason-ai/main/docs/INSTALL.md>), followed in order,",
            skill,
            StringComparison.Ordinal);
        Assert.Contains("ask them before every act it marks \"Ask first\"", skill, StringComparison.Ordinal);
        Assert.DoesNotContain("rather than something to perform on their behalf", skill, StringComparison.Ordinal);

        // Followed in order means its teaching section too: a host that installed from an earlier text listed its steps
        // and left that one out, and an installation with no role skills launches every role untaught.
        Assert.Contains("its §4 included: that section teaches the installation its role skills", skill, StringComparison.Ordinal);

        // A session that installs takes the routes the page gives an agent, in its order, never pipes a download into a
        // shell, and hands the one-liners to the person where neither route is open to it.
        Assert.Contains(
            "a package where the page names one for this platform and its package manager is already there, otherwise the steps it gives by hand",
            skill,
            StringComparison.Ordinal);
        Assert.Contains("Never pipe a download into a shell", skill, StringComparison.Ordinal);
        Assert.Contains("The two lines are the person's: where neither route is open to you, hand them the lines to type.", skill, StringComparison.Ordinal);

        // After an update the role skills can be another release's. The skill says what `jason status` says about it and
        // that the repair is the line it prints — no more, because the repair's own words are the check's to give. Only
        // what a default install took fails: a deployment pinned with --ref passes, so the sentence names the default.
        Assert.Contains(
            "role skills a default install took at another release's tag fail its `role_skills` check, which names both tags, and its repair is the line it prints",
            skill,
            StringComparison.Ordinal);

        // An installation a package manager owns is updated through the manager, and `jason update apply` refuses it. The
        // update check names the manager's one command, so the skill is where a session learns to type it between the
        // runtime's stop and its start.
        Assert.Contains(
            "Where Homebrew or winget installed Jason, `jason update check` names the manager's own upgrade, and `jason update apply` refuses the installation.",
            skill,
            StringComparison.Ordinal);

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
