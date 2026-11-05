using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Jason.Cli.Doctor;
using Jason.Cli.Tests.Status;
using Jason.Contracts.Api;
using Jason.Contracts.Json;
using Jason.Runtime.Tests;

namespace Jason.Cli.Tests.Documentation;

/// <summary>
/// Sentences the published pages had wrong, held to what the code now does. Each was found in review, each was
/// read by somebody following the pages, and none of them was a command a guard could type.
/// </summary>
/// <remarks>
/// Most of these are retractions, which is the one place a negative scan is the right shape: a specific sentence
/// that was false is kept from coming back, and a positive assertion beside it carries what is true instead.
/// </remarks>
public partial class PageClaimsTests
{
    /// <summary>
    /// An agent that installs Jason repairs required checks only. The README prompt's step 4 once ran every check's
    /// repair, optional ones included: the autostart repair registers a runtime to start at logon and the harness repair
    /// writes into the agent's own configuration — both the person's to allow. The steps are the install page's now,
    /// and so is the rule, in its section for an agent.
    /// </summary>
    [Fact]
    public void An_agent_that_installs_it_repairs_required_checks_only()
    {
        var agent = Flat(Agent());

        Assert.Contains("For each required check that is not ok, run the repair it prints.", agent, StringComparison.Ordinal);
        Assert.Contains("Optional checks are reported, not repaired", agent, StringComparison.Ordinal);
        Assert.DoesNotContain("For each check that is not ok", agent, StringComparison.Ordinal);
        Assert.DoesNotContain("For each check that is not ok", Prompt(), StringComparison.Ordinal);
    }

    /// <summary>
    /// And names where the executable unpacks its native libraries on each platform: under the temporary directory
    /// on Windows, under <c>~/.net</c> on macOS and Linux — which is what CI's published executables report.
    /// </summary>
    [Fact]
    public void The_pages_name_where_the_executable_unpacks_on_each_platform()
    {
        Assert.Contains("~/.net", Agent(), StringComparison.Ordinal);
        Assert.Contains("under\n`~/.net` on macOS and Linux", Page("docs", "INSTALL.md"), StringComparison.Ordinal);

        var release = Page("docs", "release-and-update.md");
        Assert.Contains("`~/.net/jason/<hash>/`", release, StringComparison.Ordinal);
        Assert.DoesNotContain("/tmp/.net/jason", release, StringComparison.Ordinal);
    }

    /// <summary>
    /// The pages say what an executable another program holds open meets: the update's and the rollback's moves, and the
    /// Windows installer's, wait for it, and the wait they name is the uninstall's own rule's.
    /// </summary>
    [Fact]
    public void The_pages_say_how_long_a_held_executable_is_waited_for()
    {
        var wait = Jason.Cli.Uninstall.InstallationRemovers.HeldFiles.WaitInWords;

        Assert.Contains(
            $"On Windows an executable another program holds open — a scanner reading the one just run or just downloaded — is tried again for {wait} first; one still held then is named from `~`, with how long it was tried, and that refusal is retryable",
            Flat(Page("docs", "release-and-update.md")),
            StringComparison.Ordinal);

        var install = Flat(Page("docs", "INSTALL.md"));
        Assert.Contains($"On Windows, `install.ps1` waits up to {wait} for a file another program holds open", install, StringComparison.Ordinal);

        // A file held while the archive unpacks is said by the directory it unpacks into, "or a file in it": the script
        // cannot know which file it was, so "says which file" is no longer what it does.
        Assert.Contains("before it gives up and names the file, or the directory it was unpacking into.", install, StringComparison.Ordinal);
        Assert.DoesNotContain("before it gives up and says which file.", install, StringComparison.Ordinal);
        Assert.Contains(
            "if the new `jason.exe` cannot move in, it puts the one you had back — or, when it cannot, says where it is — so an install that fails never leaves you without the version you started with.",
            install,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A release teaches itself with git, over the network, at its published tag: "a shell, and nothing else" was
    /// true of installing it and not of teaching it.
    /// </summary>
    [Fact]
    public void The_install_page_says_what_teaching_a_release_needs()
    {
        var install = Page("docs", "INSTALL.md");

        Assert.Contains("with `git`, over the\nnetwork, at the tag it was published as", install, StringComparison.Ordinal);
        Assert.Contains("asks for a\ntag that has not been published yet, and refuses", install, StringComparison.Ordinal);
    }

    /// <summary>
    /// A first dry run fetches the skills into Jason's staging directory, so it has something to show, and deploys
    /// nothing. §4 says so — that it fetches, which is a write into the data directory — and that the install line is
    /// what deploys them, printing its one root first.
    /// </summary>
    /// <remarks>
    /// It said the dry run fetched nothing and exited 1 on a first install, which was true while the dry run refused;
    /// the sentence is kept from coming back.
    /// </remarks>
    [Fact]
    public void The_install_page_says_a_first_dry_run_fetches_into_staging_and_deploys_nothing()
    {
        var teach = Flat(Section(Page("docs", "INSTALL.md"), "## 4. "));

        Assert.Contains(
            "On a first install the dry run fetches the skills first, with `git`, into Jason's staging directory inside the data directory, so that there is something to show; it deploys nothing.",
            teach,
            StringComparison.Ordinal);
        Assert.Contains(
            "The line below deploys them: it prints its one root before it writes a skill there, and writes nowhere else.",
            teach,
            StringComparison.Ordinal);
        Assert.DoesNotContain("the dry run says so and exits 1", teach, StringComparison.Ordinal);
        Assert.DoesNotContain("Nothing is written, and the one target root is printed", teach, StringComparison.Ordinal);
    }

    /// <summary>
    /// §4 says what moves a deployment its record pins, in both roots — the role skills' line and an agent's pack's,
    /// each with the source it came from — which are the lines the two checks print. And it says another tag rather
    /// than an older one, because the check compares by equality: a record newer than the runtime fails as well.
    /// </summary>
    [Fact]
    public void The_install_page_names_the_line_that_moves_a_pinned_deployment_in_either_root()
    {
        var teach = Flat(Section(Page("docs", "INSTALL.md"), "## 4. "));

        Assert.Contains(
            "`jason skills install --roles-only` for the role skills, and `jason skills install --pack <pack>` for a pack in an agent's root, each with the `--source` it came from when that was not this repository.",
            teach,
            StringComparison.Ordinal);
        Assert.DoesNotContain("at the older release's tag", teach, StringComparison.Ordinal);
    }

    /// <summary>
    /// And §4 says that the apply itself redeploys the skills, and what an update applied by an older build still needs:
    /// "after an update, run status" alone told every updater to repair by hand what the apply now does.
    /// </summary>
    [Fact]
    public void The_install_page_says_the_apply_redeploys_the_skills_and_what_an_older_build_s_update_needs()
    {
        var teach = Flat(Section(Page("docs", "INSTALL.md"), "## 4. "));

        Assert.Contains(
            "`jason update apply` ends by running `jason skills update` at the new release's tag, and then places the release's UI as `jason ui install` does; an update applied by a build before 0.12.0 needs one `jason skills update` after it, and one applied by a build older than the UI step places no UI, so `jason ui install` follows it.",
            teach,
            StringComparison.Ordinal);
        Assert.Contains(
            "A package manager's upgrade does not run `jason update apply`, so `jason skills update` and `jason ui install` follow it.",
            teach,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The README is the page a person on an older build reads before typing that build's own <c>jason update apply</c>,
    /// so it says what that update still needs, as the two pages behind it do.
    /// </summary>
    [Fact]
    public void The_readme_says_what_an_update_applied_by_an_older_build_needs()
    {
        var readme = Flat(Page("docs", "handbook.md"));

        Assert.Contains(
            "an update applied by a build before 0.12.0 needs one `jason skills update` after it",
            readme,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Nine names have no API operation behind them — <c>status</c>, <c>uninstall</c>, <c>logs</c> and <c>doctor</c>,
    /// the <c>skills</c> and <c>update</c> nouns, <c>ui install</c> and <c>plugin install</c>, verbs of nouns whose
    /// other verbs are operations, and <c>session open</c> — and no page says two, the four it said before the two verbs
    /// that read the installation's files arrived, the six it said before the UI was placed by a command, the seven it said
    /// before a plugin package was, or the eight it said before a person's own agent host was started by one.
    /// </summary>
    [Theory]
    [InlineData("docs/handbook.md", "It is one of nine names in this CLI with no API operation behind it — `status`, `uninstall`, `logs` and `doctor`, the `skills` and `update` nouns, `ui install` and `plugin install`, which change this installation's files as those two do, and `session open`")]
    [InlineData("docs/INSTALL.md", "It is one of nine names in this CLI with no API operation behind it — `status`, `uninstall`, `logs` and `doctor`, the `skills` and `update` nouns, `ui install` and `plugin install`, which change this installation's files as those two do, and `session open`")]
    [InlineData("CLAUDE.md", "The verb map is one-to-one with API operations with **nine declared exceptions**")]
    public void No_page_counts_fewer_names_without_an_operation_than_the_code_declares(string page, string said)
    {
        var text = Flat(Page(page.Split('/')));

        Assert.DoesNotContain("two commands", text, StringComparison.Ordinal);
        Assert.DoesNotContain("two declared exceptions", text, StringComparison.Ordinal);
        Assert.DoesNotContain("the only command here", text, StringComparison.Ordinal);
        Assert.DoesNotContain("one of four names", text, StringComparison.Ordinal);
        Assert.DoesNotContain("four declared exceptions", text, StringComparison.Ordinal);
        Assert.DoesNotContain("one of six names", text, StringComparison.Ordinal);
        Assert.DoesNotContain("six declared exceptions", text, StringComparison.Ordinal);
        Assert.DoesNotContain("one of seven names", text, StringComparison.Ordinal);
        Assert.DoesNotContain("seven declared exceptions", text, StringComparison.Ordinal);
        Assert.DoesNotContain("one of eight names", text, StringComparison.Ordinal);
        Assert.DoesNotContain("eight declared exceptions", text, StringComparison.Ordinal);
        Assert.Contains(said, text, StringComparison.Ordinal);
    }

    /// <summary>
    /// The agent <c>jason session open</c> starts runs in the person's own host, where the CLI's approve and answer verbs
    /// are as reachable as anywhere else. So no page or help states as a fact that it never approves, rejects or answers:
    /// each says what Jason's brief asks and whose settings decide what the agent may run.
    /// </summary>
    [Theory]
    [InlineData("CLAUDE.md", "Jason's brief asks the agent to propose only and not to approve, reject or answer; what it may run is the host's own permission settings' to decide")]
    [InlineData("docs/ui.md", "Jason's brief asks the agent to propose only and not to approve, reject or answer; what it may run is your host's own permission settings' to decide.")]
    [InlineData("dotnet/src/Jason.Cli/Session/SessionOpener.cs", "Jason's brief asks the agent to propose only and not to approve, reject or answer; what it may run is the host's own permission settings' to decide.")]
    [InlineData("dotnet/src/Jason.Cli/Link/LinkHandler.cs", "Jason's brief asks the agent to propose only and not to approve, reject or answer; what it may run is the host's own permission settings' to decide.")]
    [InlineData("ui/src/screens/attention/session-link.tsx", "Jason's brief asks the agent to propose only and not to approve, reject or answer; what it may run is your host's own permission settings' to decide.")]
    [InlineData("dotnet/src/Jason.Cli/CliApp.cs", "Jason's brief asks the agent to propose only and not to approve, reject or answer; what it may run is the host's own permission settings' to decide.")]
    public void No_page_says_the_agent_a_session_starts_cannot_decide(string page, string said)
    {
        var text = Flat(Regex.Replace(Page(page.Split('/')), @"(?m)^\s*(///|\*)\s?", ""));

        Assert.DoesNotContain("proposes and never decides", text, StringComparison.Ordinal);
        Assert.DoesNotContain("reads and proposes and decides nothing", text, StringComparison.Ordinal);
        Assert.DoesNotContain("the person still decides", text, StringComparison.Ordinal);
        Assert.DoesNotContain("approving, rejecting and answering stay with you", text, StringComparison.Ordinal);
        Assert.DoesNotContain("the agent it starts proposes.", text, StringComparison.Ordinal);
        Assert.Contains(said, text, StringComparison.Ordinal);
    }

    /// <summary>The help says what the brief asks of the agent, never what the agent cannot do.</summary>
    [Fact]
    public void The_session_help_says_what_the_brief_asks_and_who_decides_what_runs()
    {
        var help = Jason.Cli.Session.SessionCommands.OpenDescription;

        Assert.DoesNotContain("it never approves, rejects or answers", help, StringComparison.Ordinal);
        Assert.Contains("Jason's brief asks the agent to propose only and not to approve, reject or answer; what it may run is your host's own permission settings' to decide.", help, StringComparison.Ordinal);
    }

    /// <summary>
    /// The register of accepted decisions records what a diagnostic bundle is, in the row beside the notification
    /// one, word for word as it was accepted: the row is what a later change to the bundle has to be measured against.
    /// </summary>
    [Fact]
    public void The_decision_register_records_what_a_diagnostic_bundle_is()
    {
        var architecture = Page("docs", "architecture.md");
        var start = architecture.IndexOf("\n## 18. Accepted decision register", StringComparison.Ordinal);
        var end = architecture.IndexOf("\n## 19. ", StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "docs/architecture.md no longer has §18 followed by §19.");

        var rows = architecture[start..end].Split('\n');
        var notification = Array.FindIndex(rows, row => row.StartsWith("| DEC-NOTIFY-001 |", StringComparison.Ordinal));
        Assert.True(notification >= 0, "§18 no longer has the notification row.");
        Assert.Equal(DiagnosticBundleDecision, rows[notification + 1]);
    }

    /// <summary>The register's row for the diagnostic bundle, as it was accepted.</summary>
    private const string DiagnosticBundleDecision =
        "| DEC-DIAG-001 | A diagnostic bundle is shareable by construction. `jason doctor` composes it from the installation's files and the runtime's own metadata: "
        + "versions, the readiness report, configuration as shape with literal values only from one published allowlist, a bounded log slice under the logging invariant, "
        + "plugin status and routes — which plugin performs which operation, here and per campaign, never a binding's account or its identity — "
        + "and journal and work-item metadata without values, with the home directory and the account's names rewritten; "
        + "journal values travel only on an explicit flag with a printed warning; it reaches no network and is never uploaded; it is listed before it is written. "
        + "`jason logs` reads the installation's files and asks the runtime nothing; `jason doctor` asks the runtime only through its API and never opens the database; "
        + "they are the fifth and sixth declared verb-map exceptions. |";

    /// <summary>
    /// The routes and the subscriptions travel, and the pages that say what a bundle holds say so: the working notes name
    /// them among what the runtime is asked, and the README says what a route and a subscribed kind are written as and
    /// that a route's account and a chain's envelope never are.
    /// </summary>
    [Fact]
    public void The_pages_say_a_bundle_carries_the_routes_and_never_a_route_s_account()
    {
        var notes = Flat(Page("CLAUDE.md"));
        var readme = Flat(Page("docs", "handbook.md"));

        Assert.Contains(
            "of the runtime it asks only what the runtime says about itself, its roles, profiles, routes and subscriptions, and the journal and work-item pages it counts",
            notes,
            StringComparison.Ordinal);
        Assert.Contains(
            "which plugin performs which operation, here and per campaign, by plugin id and operation name — never which account a route binds",
            readme,
            StringComparison.Ordinal);
        Assert.Contains(
            "which plugins each subscribed journal kind is handed to, where its cursor stands and how its newest chains ended, by status and code — never the envelope a chain carried",
            readme,
            StringComparison.Ordinal);
        Assert.Contains(
            "the database, plugin packages, a route's account and its identity, the envelopes subscription chains carried, the names of execution profiles, or anything read from the folder Jason keeps what it learns about your business in — `~/Jason` unless `Harness:Root` names another — whose folders and files it never lists or opens. Only `--include-journal` adds",
            readme,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// What <c>--include-journal</c> adds, said on each page in the composer's own text: the one the warning and the
    /// never-carried list quote, so a page cannot promise a bundle carries less than the flag puts in it. The list the
    /// pages said before named six things and left out most of what the journal's entries hold.
    /// </summary>
    [Theory]
    [InlineData("docs/handbook.md")]
    [InlineData("CLAUDE.md")]
    public void The_pages_say_what_the_journal_flag_adds_in_the_composers_own_words(string page)
    {
        var text = Flat(Page(page.Split('/')));
        var expected = Flat(DoctorComposer.JournalCarries);

        Assert.True(
            text.Contains(expected, StringComparison.Ordinal),
            Drift(text, expected) is { } phrase
                ? $"{page} does not say \"{phrase}\" where the composer's text says it, after the phrases before it."
                : $"{page} says every phrase of the composer's text in its order, with something else between them.");
        Assert.DoesNotContain("contact fields", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// The first phrase of <paramref name="expected"/>, read as phrases between its commas, that <paramref name="text"/>
    /// does not say after the phrases before it — missing, or out of order — or null where it says every one in order.
    /// </summary>
    private static string? Drift(string text, string expected)
    {
        var at = 0;
        foreach (var phrase in expected.Split(", ", StringSplitOptions.None))
        {
            var found = text.IndexOf(phrase, at, StringComparison.Ordinal);
            if (found < 0)
            {
                return phrase;
            }

            at = found + phrase.Length;
        }

        return null;
    }

    /// <summary>
    /// Skills go by receipt; the registration, the PATH entry and the executable go by rule. "It removes only what a
    /// receipt names" was contradicted by the verb's own list of what it removes.
    /// </summary>
    [Theory]
    [InlineData("README.md")]
    [InlineData("docs/handbook.md")]
    [InlineData("docs/INSTALL.md")]
    [InlineData("docs/release-and-update.md")]
    [InlineData("CLAUDE.md")]
    [InlineData("skills/runtime/operating-the-installation/SKILL.md")]
    [InlineData("dotnet/src/Jason.Cli/Uninstall/UninstallCommand.cs")]
    public void No_page_says_the_uninstall_removes_only_what_a_receipt_names(string page)
    {
        var text = Page(page.Split('/'));

        Assert.DoesNotContain("removes only what a receipt names", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("removes only what a receipt names", text.Replace("<b>", string.Empty, StringComparison.Ordinal).Replace("</b>", string.Empty, StringComparison.Ordinal), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("outside the paths the receipts name", text, StringComparison.Ordinal);
        Assert.DoesNotContain("anything outside the paths it names", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// What a live edit may and may not do to a running runtime, and where a refused start is heard, is one sentence
    /// and is said in the same words on every page that says it: the project instructions and the three documents
    /// that carry settings. One of them kept the older wording, which said where a refused start is heard on stderr
    /// alone and left out what <c>jason runtime start</c> answers.
    /// </summary>
    [Theory]
    [InlineData("CLAUDE.md")]
    [InlineData("docs/work-execution.md")]
    [InlineData("docs/release-and-update.md")]
    [InlineData("docs/notifications.md")]
    public void Every_page_that_says_what_a_live_edit_does_says_it_in_the_same_words(string page) =>
        Assert.Contains(LiveEditInvariant, Flat(Page(page.Split('/'))), StringComparison.Ordinal);

    /// <summary>The live-edit sentence, as every page that states it has it.</summary>
    private const string LiveEditInvariant =
        "A live edit never stops the runtime: a refused edit keeps the last good settings and is logged by path and type, "
        + "and a start with a malformed or mistyped file still fails, saying where — on stderr under `jason runtime run`, "
        + "and after \"It said:\" in what `jason runtime start` answers: the line and byte of a malformed file, "
        + "the setting's path and expected type for a mistyped one.";

    /// <summary>The sixth sentence a release would have made false, in both places it was written.</summary>
    [Theory]
    [InlineData("skills/README.md")]
    [InlineData("dotnet/src/Jason.Cli/Skills/SkillsSource.cs")]
    public void No_page_says_git_is_today_the_only_way_skills_can_work(string page) =>
        Assert.DoesNotContain("the only way it can work at all", Page(page.Split('/')), StringComparison.Ordinal);

    /// <summary>
    /// The operating-the-installation skill says what the verb's exit code now means: 1 when it did not get there, with
    /// every step listed either way — not "understood and refused", which predated the report written after a failure.
    /// </summary>
    [Fact]
    public void The_operating_skill_states_the_uninstall_exit_code_as_it_is()
    {
        var skill = Page("skills", "runtime", "operating-the-installation", "SKILL.md");

        Assert.DoesNotContain("1 when it understood and refused", skill, StringComparison.Ordinal);
        Assert.Contains("stopped part-way", skill, StringComparison.Ordinal);
        Assert.Contains("every step that did happen is listed either way", skill, StringComparison.Ordinal);
    }

    /// <summary>
    /// The PATH entry goes only where its directory holds nothing but Jason, on every platform. The pages said so
    /// of Windows alone — which is what the code did: on macOS and Linux the marker alone made the block Jason's,
    /// and the installer's default directory there is the one other tools share.
    /// </summary>
    [Theory]
    [InlineData("docs/handbook.md", "only where the installer wrote it and its directory holds nothing but Jason")]
    [InlineData("docs/INSTALL.md", "is one rule on both platforms")]
    [InlineData("docs/release-and-update.md", "only where the installer wrote it and its directory holds nothing but Jason")]
    [InlineData("dotnet/src/Jason.Cli/Uninstall/UninstallCommand.cs", "only where the installer wrote it and its directory holds nothing but Jason")]
    public void The_pages_hold_every_platform_to_the_directory_rule(string page, string said)
    {
        var text = Flat(Page(page.Split('/')));

        Assert.Contains(said, text, StringComparison.Ordinal);
        Assert.DoesNotContain("on Windows, only where the directory holds nothing but Jason", text, StringComparison.Ordinal);
        Assert.DoesNotContain("only an entry for a directory that holds nothing but Jason", text, StringComparison.Ordinal);
        Assert.DoesNotContain("is a rule of its own on each platform", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Beside every install command a person or an agent is handed, a sentence says that Jason is released under the MIT
    /// licence, linked as each place can link it: within the tree on the pages a reader reads in it, at the release's own
    /// tag in the release's notes, and at <c>main</c> in the prompt, which is pasted outside the tree and installs the
    /// latest release. The pages a person reads point at what Jason sends off their machine beside it. Nothing there
    /// asks anyone to accept anything.
    /// </summary>
    [Theory]
    [InlineData("README.md", "Jason is released under the MIT licence: [LICENSE](LICENSE). What it sends off your machine, and what stays your responsibility when it reaches people, is in [docs/responsible-use.md](docs/responsible-use.md).")]
    [InlineData("docs/handbook.md", "Jason is released under the MIT licence: [LICENSE](../LICENSE). What it sends off your machine, and what stays your responsibility when it reaches people, is in [docs/responsible-use.md](responsible-use.md).")]
    [InlineData("docs/INSTALL.md", "Jason is released under the MIT licence: [LICENSE](../LICENSE). What it sends off your machine, and what stays your responsibility when it reaches people, is in [responsible-use.md](responsible-use.md).")]
    [InlineData(".github/release-notes-template.md", "Jason is released under the MIT licence: <https://github.com/reply-team/jason-ai/blob/v{{version}}/LICENSE>.")]
    public void Every_page_with_the_install_line_says_the_licence_after_it(string page, string sentence)
    {
        var text = Page(page.Split('/'));

        var oneLiner = text.IndexOf("install.ps1 | iex", StringComparison.Ordinal);
        Assert.True(oneLiner > 0, $"{page} has no one-liner.");
        var said = Flat(text).IndexOf(sentence, StringComparison.Ordinal);
        Assert.True(said > Flat(text[..oneLiner]).Length, $"{page} does not say, after its one-liners: {sentence}");
    }

    /// <summary>
    /// Jason is MIT, the runtime included, and the Jason Runtime Licence is retired: no page a person reads names it,
    /// links its file, or asks anyone to accept anything before installing.
    /// </summary>
    [Theory]
    [InlineData("README.md")]
    [InlineData("docs/handbook.md")]
    [InlineData("docs/INSTALL.md")]
    [InlineData("docs/philosophy.md")]
    [InlineData("docs/release-and-update.md")]
    [InlineData("docs/responsible-use.md")]
    [InlineData("TRADEMARKS.md")]
    [InlineData(".github/release-notes-template.md")]
    public void No_page_names_the_retired_licence_or_asks_for_acceptance(string page)
    {
        var text = Flat(Page(page.Split('/')));

        Assert.DoesNotContain("Runtime Licence", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("LICENSE-RUNTIME", text, StringComparison.Ordinal);
        Assert.DoesNotContain("you accept", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("accepts the same licence", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// The licence section of the README and of the handbook each says once that the repository is released under MIT
    /// with its history, so a licence file in an older tree is superseded, and names the marks beside it. The handbook's
    /// also opens by saying that MIT covers the runtime and the executable each release builds.
    /// </summary>
    [Theory]
    [InlineData("README.md", "\"Jason\", \"Jason AI\", \"Reply\" and \"Reply.io\" are trademarks of ReplyApp Inc.")]
    [InlineData("docs/handbook.md", "The names \"Jason\", \"Jason AI\", \"Reply\" and \"Reply.io\", and their logos, are trademarks of ReplyApp Inc.")]
    public void Each_page_s_licence_section_releases_the_repository_and_its_history_under_mit(string page, string marks)
    {
        var section = Flat(Section(Page(page.Split('/')), "## License\n"));

        Assert.True(
            section.Contains("ReplyApp Inc. releases this repository, its history included, under the MIT licence from 0.24.0, and the licence files in older trees are superseded.", StringComparison.Ordinal),
            $"{page}'s License section does not release the repository and its history under MIT in the repository's words.");
        Assert.True(section.Contains(marks, StringComparison.Ordinal), $"{page}'s License section does not name the marks: {marks}");
        if (page == "docs/handbook.md")
        {
            Assert.True(
                section.Contains("Everything in this repository is MIT ([LICENSE](../LICENSE)), the runtime included, and so is the executable each release builds from it", StringComparison.Ordinal),
                $"{page}'s License section does not open by saying MIT covers the runtime and the executable.");
        }
    }

    /// <summary>
    /// The update check, the install scripts and <c>jason update</c> go to the product's own address, which redirects
    /// them to the public repository's releases. The pages that say what leaves a machine say where those requests go
    /// and who operates the address, and that the skills still come from the repository directly.
    /// </summary>
    [Theory]
    [InlineData("docs/handbook.md", "The check, the install scripts, `jason update` and `jason ui install` go to `get.jasonharness.ai`, an address Reply.io operates, which redirects them to this repository's releases; the skills come from this repository directly.")]
    [InlineData("docs/INSTALL.md", "That request, and the downloads the install scripts, `jason update apply` and `jason ui install` make, go to `get.jasonharness.ai`, an address Reply.io operates, which redirects them to this repository's releases on GitHub.")]
    [InlineData("docs/release-and-update.md", "**Where it goes.** The default feed is on `get.jasonharness.ai`, an address Reply.io operates, which redirects every request under `/releases/` to the same path in the public repository's releases on GitHub.")]
    [InlineData("docs/release-and-update.md", "An installation built before 0.7.0 asks GitHub directly; both answer with the same file.")]
    [InlineData("docs/release-and-update.md", "An installation from 0.7.0 to 0.23.x asks the earlier address, `jasonharness.ai`, which keeps redirecting the same paths; if it ever stops, `jason update apply --feed https://get.jasonharness.ai/releases/latest/download/manifest.json`, or a reinstall with the current one-liner, moves it, and the executable that arrives asks `get.jasonharness.ai` from then on.")]
    [InlineData("docs/release-and-update.md", "`Update:FeedUrl` set to that address moves only that runtime's own check: `jason update`, `jason ui install` and `jason plugin install` read no settings and ask the address they were built with unless `--feed` names another.")]
    public void The_pages_say_where_a_release_request_goes_and_who_operates_the_address(string page, string said) =>
        Assert.Contains(said, Flat(Unquoted(Page(page.Split('/')))), StringComparison.Ordinal);

    /// <summary>
    /// What the address keeps of those requests, and for how long, is <c>docs/responsible-use.md</c>'s to say. The pages
    /// that say where the requests go point there, right after saying it, rather than repeat it.
    /// </summary>
    [Theory]
    [InlineData("docs/handbook.md", "the skills come from this repository directly. What that address keeps, and for how long, is in [docs/responsible-use.md](responsible-use.md#update-check-and-downloads).")]
    [InlineData("docs/INSTALL.md", "which redirects them to this repository's releases on GitHub. What that address keeps, and for how long, is in [responsible-use.md](responsible-use.md#update-check-and-downloads).")]
    public void The_pages_point_at_what_the_address_keeps_where_they_say_where_requests_go(string page, string said) =>
        Assert.Contains(said, Flat(Unquoted(Page(page.Split('/')))), StringComparison.Ordinal);

    /// <summary>
    /// The names are trademarks, and the licence grants no right to use them, so README says so where it names the
    /// licence, and points at the note that says what anyone may do with them. It no longer has a section saying
    /// "Jason AI" is used descriptively, which beside that note is not so.
    /// </summary>
    [Fact]
    public void The_readme_names_the_marks_beside_the_licences()
    {
        var text = Flat(Page("docs", "handbook.md"));
        var licences = text.IndexOf("## License", StringComparison.Ordinal);
        Assert.True(licences >= 0, "README has no License section.");

        Assert.Contains(
            "The names \"Jason\", \"Jason AI\", \"Reply\" and \"Reply.io\", and their logos, are trademarks of ReplyApp Inc. The licence grants no right to use them; [TRADEMARKS.md](../TRADEMARKS.md) says what you may do with them without asking.",
            text[licences..],
            StringComparison.Ordinal);
        Assert.DoesNotContain("## Naming", text, StringComparison.Ordinal);
        Assert.DoesNotContain("used descriptively", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_install_prompt_says_the_licence_at_main_and_asks_for_no_acceptance()
    {
        var prompt = Flat(Prompt());

        Assert.Contains("Jason is released under the MIT licence: <https://github.com/reply-team/jason-ai/blob/main/LICENSE>.", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("accept", prompt, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The pages that say what an install leaves say it keeps the two notice files beside the executable, under the names
    /// it keeps them as; that a run over the installed version writes one that is missing; and that a person who places
    /// the executable by hand keeps them too. The uninstall's pages say the files go with it, by name.
    /// </summary>
    [Theory]
    [InlineData("docs/handbook.md", "with its licence and the third-party notices beside it, as `jason-LICENSE` and `jason-THIRD-PARTY-NOTICES`")]
    [InlineData("docs/INSTALL.md", "with its licence and the third-party notices beside it, as `jason-LICENSE` and `jason-THIRD-PARTY-NOTICES`")]
    [InlineData("docs/handbook.md", "every archive carries the licence and the third-party notices, and an install keeps both beside the executable as `jason-LICENSE` and `jason-THIRD-PARTY-NOTICES`")]
    [InlineData("docs/INSTALL.md", "unless a licence file is missing beside the executable")]
    [InlineData(".github/release-notes-template.md", "unless a licence file is missing beside the executable")]
    [InlineData(".github/release-notes-template.md", "If you then put the executable in place by hand, keep `LICENSE` and `THIRD-PARTY-NOTICES` beside it, named `jason-LICENSE` and `jason-THIRD-PARTY-NOTICES`.")]
    [InlineData("docs/INSTALL.md", "nothing but the executable, its two notice files (`jason-LICENSE`, `jason-THIRD-PARTY-NOTICES`) and the `jason.previous.exe` an upgrade can leave beside it")]
    [InlineData("docs/INSTALL.md", "on macOS and Linux it removes those two files by name from a directory other tools share")]
    [InlineData("docs/INSTALL.md", "with `LICENSE` and `THIRD-PARTY-NOTICES` beside it, named `jason-LICENSE` and `jason-THIRD-PARTY-NOTICES`")]
    [InlineData("docs/handbook.md", "holds nothing but Jason — the executable, its two notice files and what an upgrade left beside it")]
    [InlineData("docs/release-and-update.md", "holds nothing but Jason — the executable, its two notice files and what an upgrade left beside it")]
    [InlineData("docs/release-and-update.md", "beside the executable only the executable and its two notice files")]
    public void The_pages_say_where_the_notice_files_are_kept_and_that_they_go(string page, string said) =>
        Assert.Contains(said, Flat(Page(page.Split('/'))), StringComparison.Ordinal);

    /// <summary>
    /// Every archive carries <c>THIRD-PARTY-NOTICES</c> beside the executable and <c>LICENSE</c>, and each page that says
    /// what an archive holds says so: a page that listed the other two "and nothing else" would tell a reader the
    /// notices were never given to them.
    /// </summary>
    [Theory]
    [InlineData("docs/release-and-update.md", "**An archive holds the executable, `LICENSE` and `THIRD-PARTY-NOTICES`, and nothing else.**")]
    [InlineData("docs/release-and-update.md", "| `jason-win-x64.zip` | the Windows executable, `LICENSE` (the MIT licence) and `THIRD-PARTY-NOTICES` |")]
    [InlineData("docs/release-and-update.md", "| `jason-linux-x64.tar.gz` | the Linux executable, `LICENSE` (the MIT licence) and `THIRD-PARTY-NOTICES` |")]
    [InlineData("docs/release-and-update.md", "| `jason-osx-arm64.tar.gz` | the macOS (Apple silicon) executable, `LICENSE` (the MIT licence) and `THIRD-PARTY-NOTICES` |")]
    [InlineData(".github/release-notes-template.md", "Each archive holds the executable, `LICENSE` (the MIT licence, which covers the whole repository, the runtime included) and `THIRD-PARTY-NOTICES`, nothing else.")]
    public void The_pages_say_every_archive_carries_the_notices(string page, string said)
    {
        var text = Flat(Page(page.Split('/')));

        Assert.Contains(said, text, StringComparison.Ordinal);
        Assert.DoesNotContain("holds the executable and `LICENSE`", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// The pages that list what a release publishes, or say how a plugin is installed, name each plugin's package a release
    /// publishes, Reply's and the journal forwarder's, by what it holds and how it is installed: the files of
    /// <c>plugins/&lt;id&gt;</c> at the tag, placed by <c>jason plugin install</c> once they match <c>checksums.txt</c>. None of them says plugins are installed from the
    /// repository any more, or that the package is unpacked by hand; the package is never called an archive, which on those
    /// pages is a platform's.
    /// </summary>
    [Theory]
    [InlineData("docs/release-and-update.md", "| `jason-plugin-reply.zip` | the Reply plugin's package: the files of `plugins/reply` at the tag, at the archive's root |")]
    [InlineData("docs/release-and-update.md", "| `jason-plugin-journal-forwarder.zip` | the journal forwarder's package: the files of `plugins/journal-forwarder` at the tag, at the archive's root |")]
    [InlineData("docs/release-and-update.md", "| `checksums.txt` | one `sha256sum` line per archive, and one for each package |")]
    [InlineData("docs/release-and-update.md", "Installing it is `jason plugin install reply`, which unpacks it into `~/.jason/plugins/reply` once it matches its line — `docs/golden-path.md` step 3 runs it — and what is unpacked has the content digest of the package at the tag (DEC-INSTALL-002).")]
    [InlineData("docs/release-and-update.md", "listed in `checksums.txt`, and named in the manifest's `plugins` (§3) rather than its artifacts, which are platforms.")]
    [InlineData("docs/release-and-update.md", "Nor does an added root field every reader since 0.1.0 skips, as `plugins` is:")]
    [InlineData(".github/release-notes-template.md", "| `jason-plugin-reply.zip` | the Reply plugin's package: the files of `plugins/reply` at `v{{version}}`, which `jason plugin install reply` places in `~/.jason/plugins/reply` |")]
    [InlineData(".github/release-notes-template.md", "| `jason-plugin-journal-forwarder.zip` | the journal forwarder's package: the files of `plugins/journal-forwarder` at `v{{version}}`, which `jason plugin install journal-forwarder` places in `~/.jason/plugins/journal-forwarder` |")]
    [InlineData(".github/release-notes-template.md", "| `checksums.txt` | the SHA-256 of each archive and of each package, in `sha256sum` format |")]
    [InlineData(".github/release-notes-template.md", "| `jason-ui.zip` | the browser UI's package: `ui/` built at `v{{version}}`, which `jason ui install` places as the default UI |")]
    [InlineData(".github/release-notes-template.md", "The Reply plugin's package is installed by `jason plugin install reply`, which places it in `~/.jason/plugins/reply` once it matches its line in `checksums.txt` and the manifest's content digest; step 3 of `docs/golden-path.md` runs it.")]
    [InlineData("docs/plugins.md", "`jason plugin install <id>` installs a package the release you run publishes: it downloads the file the manifest names, from that release's own directory, checks it against the release's `checksums.txt` and, unpacked, against that digest, and moves it in whole as `~/.jason/plugins/<id>`, or places nothing and exits 1 saying why.")]
    [InlineData("docs/release-and-update.md", "The Reply plugin is published beside the archives instead, from 0.11.0 on, as a package of its own:")]
    [InlineData("docs/handbook.md", "signs in with it, places the package from the release with `jason plugin install reply`, grants it that one program")]
    [InlineData("docs/release-and-update.md", "The journal forwarder is published the same way from 0.30.0 on, as `jason-plugin-journal-forwarder.zip`, the files of `plugins/journal-forwarder` at the tag, and `jason plugin install journal-forwarder` places it in `~/.jason/plugins/journal-forwarder`;")]
    [InlineData(".github/release-notes-template.md", "The journal forwarder's package is installed the same way, by `jason plugin install journal-forwarder`, into `~/.jason/plugins/journal-forwarder`;")]
    [InlineData("docs/plugins.md", "the journal forwarder from 0.30.0 on as `jason-plugin-journal-forwarder.zip`, each with its files at the archive's root,")]
    public void The_pages_say_how_the_plugin_s_package_is_published_and_installed(string page, string said)
    {
        var text = Flat(Page(page.Split('/')));

        Assert.Contains(said, text, StringComparison.Ordinal);
        Assert.DoesNotContain("installed from the repository", text, StringComparison.Ordinal);
        Assert.DoesNotContain("prints the lines", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Wherever a page walks the role skills in, or says what follows an older applier, the UI follows: the install
    /// guide's own lines are held above, and these are the walk-throughs beside it.
    /// </summary>
    [Theory]
    [InlineData("docs/handbook.md", "`jason ui install` places this release's browser UI, the page `jason ui open` opens; a runtime works without it.")]
    [InlineData("skills/runtime/operating-the-installation/SKILL.md", "An update applied by a build before 0.12.0 has no such step, so it needs one `jason skills update` after it. After the skills it places the release's UI, and an update applied by a build older than that step places none, so `jason ui install` follows it; `jason status` names it until it has.")]
    public void The_walk_throughs_say_when_jason_ui_install_follows(string page, string said) =>
        Assert.Contains(said, Flat(Page(page.Split('/'))), StringComparison.Ordinal);

    /// <summary>
    /// A build from source leaves debugging symbols beside the executable, and §3 says to copy the executable alone.
    /// A directory that holds the <c>.pdb</c> files as well is not one the uninstall takes for Jason's own, so a
    /// reader who copied the whole publish folder would keep both its PATH entry and the directory. The golden path
    /// builds from source too, and said to put the executable "somewhere on your PATH".
    /// </summary>
    [Theory]
    [InlineData("docs/INSTALL.md", "Copy that file alone: the `.pdb` files beside it are debugging symbols")]
    [InlineData("docs/golden-path.md", "installed from a release as [`docs/INSTALL.md`](INSTALL.md) §2 describes")]
    public void The_pages_say_to_copy_the_executable_alone(string page, string said)
    {
        var text = Flat(Page(page.Split('/')));

        Assert.Contains(said, text, StringComparison.Ordinal);
        Assert.DoesNotContain("somewhere on your `PATH`", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// The prompt needs no connector, and a session started with the account's connectors offers tools the install
    /// never asked for. That is said to the person who starts the session, above the prompt, so the prompt itself
    /// names none.
    /// </summary>
    [Fact]
    public void The_readme_says_to_start_the_install_session_without_connectors_above_the_prompt()
    {
        var readme = Page("docs", "handbook.md");
        var fence = readme.IndexOf("```text\nInstall Jason on this machine", StringComparison.Ordinal);
        var sentence = readme.IndexOf("Start the agent session for the install without external connectors: the prompt below needs none.", StringComparison.Ordinal);

        Assert.True(fence > 0, "the prompt's fence is where the page has it");
        Assert.InRange(sentence, 0, fence);

        // Said before the line that hands over the prompt, so "Paste this … :" is followed by the prompt itself.
        var paste = readme.IndexOf("Paste this into an agent", StringComparison.Ordinal);
        Assert.InRange(sentence, 0, paste);
        Assert.Contains("On Claude Code that is `--strict-mcp-config`.", Flat(readme[..fence]), StringComparison.Ordinal);
        Assert.Contains("Connectors in your agent's SDR work are yours to use.", Flat(readme[..fence]), StringComparison.Ordinal);
    }

    /// <summary>
    /// What auto mode does at the install depends on whether anything can ask the person. A session that can ask asks
    /// before the download runs, and with the person's yes the install goes on; a session nothing can ask — one run
    /// headless — refuses the download and says so. So the page no longer says auto mode stops the install at its first
    /// step, which is true only of the second. It says to expect either, that both are right, what the person does about
    /// each, and that a mode asking before every command is the other way, with one agent's spelling of it as the example.
    /// </summary>
    [Fact]
    public void The_readme_says_what_auto_mode_does_at_the_install_above_the_prompt()
    {
        var readme = Page("docs", "handbook.md");
        var fence = readme.IndexOf("```text\nInstall Jason on this machine", StringComparison.Ordinal);
        Assert.True(fence > 0, "the prompt's fence is where the page has it");
        var above = Flat(readme[..fence]);

        Assert.Contains(
            "If your agent's default mode is auto, expect one of two things at the install: it asks you before the download runs, or, where nothing can ask you, it refuses and says so. Both are right. Say yes, or type the one-liner above yourself; never let it work around a refusal. A mode that asks before every command is the other way: on Claude Code, `--permission-mode manual` (`default` before 2.1.200), or shift-tab in an open session; in any other agent, the mode that asks before it runs a command.",
            above,
            StringComparison.Ordinal);
        Assert.DoesNotContain("not auto mode, which refuses to run a downloaded installer", Flat(readme), StringComparison.Ordinal);
    }

    /// <summary>
    /// The prompt, byte for byte, is held here by its digest, so a change to it is a deliberate one. Hosts have read this
    /// text from a local copy before the release, with the install page beside it rather than at its address; the
    /// reading of record is still a person's own, pasting it from the published page into a fresh agent session after the
    /// release that publishes it. Until then the digest stands for a deliberate change, not for a reading.
    /// </summary>
    [Theory]
    [InlineData("README.md")]
    [InlineData("docs/handbook.md")]
    public void The_install_prompt_is_the_text_the_hosts_read_and_names_no_connector(string page)
    {
        var prompt = Fenced(Page(page.Split('/')), "```text\nInstall Jason on this machine");
        Assert.DoesNotContain("connector", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(PromptDigest, "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(prompt))));
    }

    /// <summary>
    /// The install page's section for an agent, byte for byte, is held here by its digest, as the prompt is: the rules an
    /// agent follows moved out of the prompt and into it, and a change to them is a deliberate one. The facts that pin
    /// its sentences say why each matters; this says none of the rest changed by accident. A host rehearsed an earlier
    /// revision of the section once, from a staged copy, before the release; the reading of record is a person's own
    /// walk after it. Until that reading, the digest stands for a deliberate change.
    /// </summary>
    [Fact]
    public void The_agent_section_is_the_text_the_hosts_read() =>
        Assert.Equal(AgentSectionDigest, "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Agent()))));

    /// <summary>
    /// The install path is a release, so nothing sends an agent to build anything: the prompt says nothing of a build,
    /// and the install page's section for an agent says to build nothing and to report a 404 from a one-liner as it is.
    /// </summary>
    [Fact]
    public void The_install_prompt_builds_nothing()
    {
        var prompt = Fenced(Page("README.md"), "```text\nInstall Jason on this machine");
        var agent = Flat(Agent());

        Assert.DoesNotContain("from source", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(".NET SDK", prompt, StringComparison.Ordinal);
        Assert.Contains("build nothing", agent, StringComparison.Ordinal);
        Assert.Contains("stop and report exactly that", agent, StringComparison.Ordinal);
    }

    /// <summary>
    /// A refusal — a harness that will not run a command, a package manager that will not install — is reported once and
    /// never worked around, and the section says what working around is, so the rule cannot be read as forbidding the
    /// next route. It no longer speaks of the one-liner alone: an agent's routes are a package and the steps by hand,
    /// and the one-liner is the person's.
    /// </summary>
    [Fact]
    public void The_agent_section_reports_a_refusal_once_and_never_works_around_it()
    {
        var agent = Flat(Agent());

        Assert.Contains(
            "A refusal — by your harness, or by a package manager — is reported once, as it stands, and never worked around. Working around it is a retry, the same command rewrapped, or the refused code run by another means, such as a downloaded script run another way. Taking the next route in the order above is not working around it.",
            agent,
            StringComparison.Ordinal);
        Assert.DoesNotContain("If your harness refuses to run the one-liner as code from outside", agent, StringComparison.Ordinal);
    }

    /// <summary>
    /// An agent installs by the first route that fits: a package, where the page names one for the platform and its
    /// package manager is already there — installing a package manager to reach it would be installing something else —
    /// then the steps by hand. It never pipes a download into a shell, and the install scripts are the person's, handed
    /// to them where even the steps by hand are refused. The order is what is held, as well as the words.
    /// </summary>
    [Fact]
    public void The_agent_section_takes_a_package_then_the_route_by_hand()
    {
        var agent = Flat(Agent());

        var package = agent.IndexOf("Where §2 names a package for this platform and its package manager is already on this machine, use it (*With a package manager*).", StringComparison.Ordinal);
        var byHand = agent.IndexOf("say so once and take §2 *By hand*: download the archive and `checksums.txt` from the release, check the digest and stop on a mismatch, unpack, keep the two notice files beside the executable, and run `jason --version` by its full path.", StringComparison.Ordinal);
        var person = agent.IndexOf("The install scripts are the person's: where your harness refuses even the route by hand, say so and give the person the one-liner to type themselves.", StringComparison.Ordinal);

        Assert.True(package >= 0, "The section for an agent does not take a package first, where its package manager is already on this machine.");
        Assert.True(byHand > package, "The section for an agent does not take §2 by hand after the package.");
        Assert.True(person > byHand, "The section for an agent does not hand the person the one-liner after the route by hand.");
    }

    /// <summary>
    /// The section for an agent sends it to §2's package subsection by name, so that subsection is there: a route named
    /// and not written would leave an agent nowhere to go.
    /// </summary>
    [Fact]
    public void The_agent_sections_package_route_names_a_subsection_that_is_there()
    {
        Assert.Contains("(*With a package manager*)", Flat(Agent()), StringComparison.Ordinal);
        Assert.Contains("\n### With a package manager\n", Section(Page("docs", "INSTALL.md"), "## 2. "), StringComparison.Ordinal);
    }

    /// <summary>
    /// §2's package subsection sits after the one-liners and before the route by hand: the order a person reads the routes
    /// in, and the order an agent takes them.
    /// </summary>
    [Fact]
    public void With_a_package_manager_comes_before_by_hand_and_after_the_one_liners()
    {
        var install = Section(Page("docs", "INSTALL.md"), "## 2. ");
        var oneLiner = install.IndexOf("irm https://get.jasonharness.ai/releases/latest/download/install.ps1 | iex", StringComparison.Ordinal);
        var package = install.IndexOf("\n### With a package manager\n", StringComparison.Ordinal);
        var byHand = install.IndexOf("\n### By hand\n", StringComparison.Ordinal);

        Assert.True(oneLiner >= 0 && package > oneLiner && byHand > package, "§2 does not give the one-liners, then the package subsection, then the route by hand.");
    }

    /// <summary>
    /// The package subsection names the tap, and only a package that is there: the winget package is not published yet,
    /// so no line installs it.
    /// </summary>
    [Fact]
    public void The_package_subsection_names_the_tap_and_no_winget_install_yet()
    {
        var package = Flat(Subsection(Section(Page("docs", "INSTALL.md"), "## 2. "), "### With a package manager"));

        Assert.Contains("On macOS, with Homebrew: `brew install reply-team/tap/jason`.", package, StringComparison.Ordinal);
        Assert.DoesNotContain("winget install", package, StringComparison.Ordinal);
    }

    /// <summary>
    /// No build for an Intel Mac is published, and Homebrew says so in words that never name the architecture: it
    /// stops at the formula, before any dependency is read. So the page says it where the macOS route is given, with
    /// what a person on such a machine can do today.
    /// </summary>
    [Fact]
    public void The_package_subsection_says_an_Intel_Mac_has_no_build()
    {
        var package = Flat(Subsection(Section(Page("docs", "INSTALL.md"), "## 2. "), "### With a package manager"));

        Assert.Contains(
            "Every macOS route on this page — the one-liner, Homebrew and the archive by hand — is for Apple silicon only: no build for an Intel Mac is published, so on an Intel Mac Homebrew answers \"formula requires at least a URL\", `install.sh` names the platforms it has, and the way to Jason today is another machine: Linux x64, Windows x64 or an Apple silicon Mac.",
            package,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// And it says who owns such an installation, in the words <c>jason update apply</c>'s refusal uses, and how the
    /// manager's upgrade is typed, with the reason: Homebrew removes the keg a running runtime starts its plugin hosts from.
    /// </summary>
    [Fact]
    public void The_package_subsection_says_how_the_managers_upgrade_is_typed_and_why()
    {
        var package = Flat(Subsection(Section(Page("docs", "INSTALL.md"), "## 2. "), "### With a package manager"));

        Assert.Contains("would replace a file the package manager owns", package, StringComparison.Ordinal);
        Assert.Contains(
            "That command is typed between `jason runtime stop` and `jason runtime start`, because Homebrew's upgrade removes the previous version's keg by default, the path a running runtime starts its plugin hosts from.",
            package,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The operating skill teaches a manager's upgrade in one paragraph: the runtime stopped, the manager's upgrade, the
    /// runtime started, the stop asked first by the skill's one rule for it and said what it does even where the person
    /// types it, and why on each platform. <c>jason update check</c>'s one command leaves
    /// the sequence to the skill, so the skill is held to saying it.
    /// </summary>
    [Fact]
    public void The_operating_skill_types_a_managers_upgrade_between_the_stop_and_the_start()
    {
        var skill = Page("skills", "runtime", "operating-the-installation", "SKILL.md");
        var start = skill.IndexOf("**Installed by a package manager.**", StringComparison.Ordinal);
        Assert.True(start >= 0, "the operating skill has no paragraph on an installation a package manager made.");
        var end = skill.IndexOf("\n**", start + 1, StringComparison.Ordinal);
        var paragraph = Flat(skill[start..(end > start ? end : skill.Length)]);

        var stop = paragraph.IndexOf("jason runtime stop", StringComparison.Ordinal);
        var upgrade = paragraph.IndexOf("winget upgrade Reply.Jason", StringComparison.Ordinal);
        var started = paragraph.IndexOf("jason runtime start", upgrade + 1, StringComparison.Ordinal);
        Assert.True(stop >= 0 && upgrade > stop && started > upgrade, "the paragraph does not give the stop, the manager's upgrade and the start in that order.");
        Assert.Contains("`brew upgrade jason`", paragraph, StringComparison.Ordinal);
        Assert.Contains(
            "If you run the stop, ask first, as \"Starting and stopping it\" says; if the person types the lines, tell them what it does all the same. "
            + "What a stop does: new work stops being claimed, what is in flight gets up to ten seconds to finish, nothing is cancelled, and a provider operation already under way may still act, ending at its own lease.",
            paragraph,
            StringComparison.Ordinal);
        Assert.Contains("On Windows winget cannot replace the executable while the runtime runs: it answers Access is denied and keeps the old version.", paragraph, StringComparison.Ordinal);
        Assert.Contains("HOMEBREW_NO_INSTALL_CLEANUP", paragraph, StringComparison.Ordinal);
        Assert.Contains("`runtime_version`", paragraph, StringComparison.Ordinal);
    }

    /// <summary>
    /// A package manager that is there and refuses — or fails, or that the agent's harness will not run — is the same
    /// case for the person as one that is not there: the agent says so once and takes the steps by hand, which is the
    /// next route rather than a way around the refusal.
    /// </summary>
    [Fact]
    public void The_agent_section_falls_through_to_by_hand_when_the_package_manager_refuses()
    {
        var agent = Flat(Agent());

        Assert.Contains("Where it is not, where it fails, or where it or your harness refuses it, say so once and take §2 *By hand*", agent, StringComparison.Ordinal);
        Assert.Contains("That is the next route, not a way around the refusal.", agent, StringComparison.Ordinal);
        Assert.DoesNotContain("no other way of running the same code", agent, StringComparison.Ordinal);
    }

    [Fact]
    [Critical(Family.D1, "what an agent is taught to run")]
    public void The_agent_section_never_pipes_a_download_into_a_shell() =>
        Assert.Contains("Never pipe a download into a shell.", Flat(Agent()), StringComparison.Ordinal);

    /// <summary>
    /// The route by hand is the last an agent takes. Where even that is refused, the install is the person's to type, and
    /// the agent hands them the line rather than finding another way to run it.
    /// </summary>
    [Fact]
    public void The_agent_section_gives_the_person_the_one_liner_where_even_the_route_by_hand_is_refused() =>
        Assert.Contains(
            "The install scripts are the person's: where your harness refuses even the route by hand, say so and give the person the one-liner to type themselves.",
            Flat(Agent()),
            StringComparison.Ordinal);

    /// <summary>
    /// The PATH entry an agent would make by hand is the person's to allow, and an installation <c>jason</c> cannot be
    /// called by name from is not ready. So a no makes it the person's own step: the agent finishes the rest and hands
    /// over the exact line <c>jason status</c> prints, with the command that proves it — never "not ready" alone.
    /// </summary>
    [Fact]
    public void The_agent_section_hands_over_a_declined_path_entry_with_the_line_and_the_proof() =>
        Assert.Contains(
            "With a no to the PATH entry, it is the person's own step: finish everything else, then hand them the exact line `jason status` prints for `path`, and say that `jason status` again proves it. Never end on \"not ready\" alone: name the step that remains.",
            Flat(Agent()),
            StringComparison.Ordinal);

    /// <summary>
    /// A session run headless has a question tool that answers nothing: the person is not there. That is nobody to ask,
    /// and an act that needs their word is left undone.
    /// </summary>
    [Fact]
    public void The_agent_section_says_an_unanswered_question_is_nobody_to_ask() =>
        Assert.Contains(
            "With nobody to ask, leave it undone and say so. A question tool that comes back with no answer is nobody to ask.",
            Flat(Agent()),
            StringComparison.Ordinal);

    /// <summary>
    /// A ready installation is not the end of what the person came for: the agent says it can work, and where it will keep
    /// the person's files, as the scripts say it to a person who ran them alone, and asks once what comes next — the wiring check, named for what it is, a synthetic walk that is not the real work, or the real work at
    /// once. A skip is the person's answer and is not asked again, and the real work begins with an invitation to tell
    /// about the first campaign in their own words. An agent that took the walk for the job, or offered it at every turn,
    /// would keep the person from the work they came for.
    /// </summary>
    [Fact]
    public void The_agent_section_asks_once_after_ready_between_the_walk_and_the_real_work()
    {
        var agent = Flat(Agent());

        Assert.Contains(
            "When `ready` is true, tell the person Jason is installed and can work and where it will keep their files — the two lines §2 shows, with the folder the `harness_root` check of `jason status` names, which is the one in force — and ask them once what comes next: [the wiring check](golden-path.md) first, in this session, or their real work at once.",
            agent,
            StringComparison.Ordinal);
        Assert.Contains(
            "Name the wiring check for what it is: a synthetic walk to meet the system, which connects their provider and proves it with their own address, and is not the real work.",
            agent,
            StringComparison.Ordinal);
        Assert.Contains("A skip is honoured: do not offer the walk again.", agent, StringComparison.Ordinal);
        Assert.Contains(
            "The real work, at once or after the walk, begins with an invitation to tell you about their first campaign in their own words — typed, a link, a file or a voice note, or one of a few examples you offer to start from — and goes on as a campaign planned with the `campaign-planning` skill and worked by Jason's roles.",
            agent,
            StringComparison.Ordinal);
        Assert.DoesNotContain("one word from them runs", agent, StringComparison.Ordinal);
    }

    /// <summary>The two sentences of the README that may name TypeScript, as the README says them.</summary>
    private static readonly string[] TypeScriptSentences =
    [
        "The browser UI is TypeScript.",
        "TypeScript would need a build step on every machine that edits the plugin, and the runtime would still load only the JavaScript it produces, so we do not recommend it.",
    ];

    /// <summary>
    /// The README says what a plugin author meets today: a plugin is plain JavaScript, run by the engine the runtime
    /// embeds, and a <c>.ts</c> entry is refused at load. It never says TypeScript comes first, nor that plugins or the
    /// changes a person or an agent makes are written in TypeScript, which would send an agent to write a package the
    /// runtime refuses. Two sentences may name TypeScript, each pinned by its text: the one that says the browser UI is
    /// TypeScript, and the one that says why it is not recommended for a plugin. Any other sentence that names it fails.
    /// </summary>
    [Fact]
    public void The_readme_says_plugins_are_plain_javascript_and_never_typescript()
    {
        var page = Page("README.md");
        var readme = Flat(Unquoted(page)).Replace("**", string.Empty, StringComparison.Ordinal);

        Assert.Contains("Plugins are plain JavaScript.", readme, StringComparison.Ordinal);
        Assert.Contains("write plain JavaScript. Plugins and other changes go in JavaScript against the published contracts.", readme, StringComparison.Ordinal);
        Assert.Contains("\n### Plugins are JavaScript\n", page, StringComparison.Ordinal);
        Assert.DoesNotContain("TypeScript first", readme, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("#typescript-first", readme, StringComparison.Ordinal);

        var named = Regex.Split(readme, @"(?<=[.!?])\s+")
            .Where(sentence => sentence.Contains("TypeScript", StringComparison.OrdinalIgnoreCase))
            .Where(sentence => !TypeScriptSentences.Contains(sentence, StringComparer.Ordinal))
            .ToList();
        Assert.True(named.Count == 0, "The README names TypeScript in a sentence other than the two it may: " + string.Join(" / ", named));
    }

    /// <summary>
    /// Every count the README states is the tree's own, wherever the page states it: the facts table, the Maturity row and
    /// "In numbers". The SDR operations and their families are what the sdr-operations skill's own source holds, one
    /// fragment per family. When a count moves, the page changes, never this row.
    /// </summary>
    [Fact]
    public void Every_count_the_readme_states_is_the_tree_s_own()
    {
        var root = Root();
        var readme = Flat(Unquoted(Page("README.md"))).Replace("**", string.Empty, StringComparison.Ordinal);

        string[] Files(string folder, string pattern) =>
            Directory.GetFiles(Path.Combine(root, folder), pattern, SearchOption.AllDirectories)
                .Where(file => !Path.GetRelativePath(root, file).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(part => part is "bin" or "obj"))
                .ToArray();
        int Skills(params string[] folder) =>
            Directory.GetDirectories(Path.Combine([root, .. folder])).Count(directory => File.Exists(Path.Combine(directory, "SKILL.md")));
        var skills = Files("skills", "SKILL.md");
        int Status(string status) => skills.Count(file => Regex.IsMatch(File.ReadAllText(file), $@"(?m)^  status: {status}\r?$"));
        var families = Directory.GetFiles(Path.Combine(root, "skills", "business", "sdr-operations", "operations"), "*.yaml")
            .Where(file => Regex.IsMatch(Path.GetFileName(file), @"^\d\d-"))
            .ToArray();
        var operations = families.Sum(file => Regex.Matches(File.ReadAllText(file), @"(?m)^  - name: ").Count);

        (string Pattern, int[] Tree)[] counts =
        [
            (@"(\d+) guides", [Directory.GetFiles(Path.Combine(root, "docs"), "*.md").Length]),
            (@"(\d+) operation contracts", [Directory.GetFiles(Path.Combine(root, "docs", "contracts", "operations"), "*.json").Length]),
            (@"(\d+) (?:test|contract) fixtures", [Files(Path.Combine("docs", "contracts", "fixtures"), "*").Length]),
            (@"(\d+) API fixtures", [Files(Path.Combine("docs", "api", "fixtures"), "*").Length]),
            (@"(\d+) C# test files", [Files(Path.Combine("dotnet", "tests"), "*.cs").Length]),
            (@"(\d+) business skills, (\d+) runtime skills and (\d+) role skills", [Skills("skills", "business"), Skills("skills", "runtime"), Skills("skills", "runtime", "roles")]),
            (@"(\d+) of the (\d+) skills are `verified`", [Status("verified"), skills.Length]),
            (@"the other (\d+) are still `draft`", [Status("draft")]),
            (@"(\d+) SDR operations", [operations]),
            (@"(\d+) SDR operations in (\d+) families", [operations, families.Length]),
        ];

        var wrong = new List<string>();
        foreach (var (pattern, tree) in counts)
        {
            var said = Regex.Matches(readme, pattern);
            if (said.Count == 0)
            {
                wrong.Add($"the README no longer states /{pattern}/");
            }

            wrong.AddRange(said
                .Where(match => !match.Groups.Values.Skip(1).Select(group => int.Parse(group.Value, System.Globalization.CultureInfo.InvariantCulture)).SequenceEqual(tree))
                .Select(match => $"'{match.Value}', where the tree has {string.Join(", ", tree)}"));
        }

        Assert.True(wrong.Count == 0, "The README states a count the tree does not have: " + string.Join(" / ", wrong));
    }

    /// <summary>
    /// A person's own agent that lands on the README sees, within its first screen, what Jason is, why it was built, that
    /// installing it is the install page, and the prompt: the title, one paragraph, the short section on why Jason was
    /// built, the alpha box, then "Installing" with the prompt under it. What the project is, its architecture and its
    /// layout follow.
    /// </summary>
    [Fact]
    public void The_readme_opens_with_what_it_is_then_the_install_and_the_prompt()
    {
        var lines = OutsideFences(Page("docs", "handbook.md"));

        Assert.Equal(
            ["## Why we built Jason", "## Installing", "## What this is", "## Architecture", "## Repository layout"],
            lines.Where(line => line.StartsWith("## ", StringComparison.Ordinal)).Take(5));

        var installing = Array.IndexOf(lines, "## Installing");
        var what = Array.IndexOf(lines, "## What this is");
        Assert.Equal(["### Ask an agent to do it"], lines[installing..what].Where(line => line.StartsWith("### ", StringComparison.Ordinal)));

        var why = Array.IndexOf(lines, "## Why we built Jason");
        var alpha = Array.FindIndex(lines, line => line.StartsWith('>'));
        Assert.True(why >= 0 && why < alpha && alpha < installing, $"The README's opening is out of order: why at line {why}, the alpha box at {alpha}, \"## Installing\" at {installing}.");

        var opening = string.Join('\n', lines[..why].Concat(lines[alpha..installing]))
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(block => !block.StartsWith("# ", StringComparison.Ordinal) && !block.StartsWith('>'))
            .ToList();
        Assert.True(opening.Count == 2, $"Before \"## Installing\" the handbook has {opening.Count} paragraphs besides the title and the alpha box; it has the sentence that says what the page is, then one.");
        Assert.StartsWith("This is the technical page of a Jason installation:", opening[0], StringComparison.Ordinal);
    }

    /// <summary>
    /// A person who runs only a one-liner learns on the first screen that it is not the whole of it: between the licence
    /// sentence and the prompt, one paragraph says a one-liner installs the executable, links the install page for the
    /// rest, and names teaching it its skills, without which every role it launches runs untaught.
    /// </summary>
    [Fact]
    public void The_readme_s_first_screen_says_the_install_page_holds_the_rest()
    {
        var readme = Page("docs", "handbook.md");
        var licence = readme.IndexOf("Jason is released under the MIT licence: [LICENSE](../LICENSE).", StringComparison.Ordinal);
        var prompt = readme.IndexOf("### Ask an agent to do it", StringComparison.Ordinal);
        Assert.True(licence > 0 && prompt > licence, "the README no longer has the licence sentence followed by the prompt's heading.");

        var pointer = readme[licence..prompt].Split("\n\n").Select(Flat).SingleOrDefault(paragraph => paragraph.Contains("[docs/INSTALL.md](INSTALL.md)", StringComparison.Ordinal));
        Assert.True(pointer is not null, "Between the licence sentence and the prompt, no paragraph links the install page.");
        Assert.Contains("A one-liner installs the executable.", pointer, StringComparison.Ordinal);
        Assert.Contains("teaching it its skills", pointer, StringComparison.Ordinal);
    }

    /// <summary>
    /// Before anything runs, the agent asks the person one question: what it will install and from where, what it writes
    /// and where, and each act the page marks, one by one — and offers the other way, a mode that asks before every
    /// command. The person's answer is the consent the rest of the install acts on.
    /// </summary>
    [Fact]
    public void The_install_prompt_asks_one_question_before_anything_runs() =>
        Assert.Contains(
            "Before you run anything, ask me one question in plain words: what you will install and from where, what it writes and where (the executable and its directory, the PATH entry, Jason's data directory, the role skills), and each act the instructions mark \"Ask first\", offered to me one by one as a choice. Offer me the other way too: switching this session to a mode that asks me before every command. Then do the steps yourself, in order from section 2, doing only what I said yes to.",
            Flat(Prompt()),
            StringComparison.Ordinal);

    /// <summary>
    /// The prompt names where Jason comes from, in the person's own words: the releases at the product's address, which
    /// redirect to the public repository's, or the package the install page names for the platform. It names no package
    /// and no package manager — those are the page's to name — so the guard against commands holds.
    /// </summary>
    [Fact]
    public void The_install_prompt_names_the_release_source_in_the_person_s_voice() =>
        Assert.Contains(
            "Jason is downloaded from its releases at <https://get.jasonharness.ai/releases/>, which redirect to <https://github.com/reply-team/jason-ai/releases>; install it from there, or with the package section 2 names for this platform.",
            Flat(Prompt()),
            StringComparison.Ordinal);

    /// <summary>
    /// The agent's report at the end names the next step, so an install never ends on whether it worked alone: after a
    /// ready installation the next step is the question of what comes next, and after one that is not ready it is the
    /// step that remains.
    /// </summary>
    [Fact]
    public void The_install_prompt_asks_for_the_next_step() =>
        Assert.Contains(
            "At the end, tell me whether Jason can start work, what the next step is, and exactly what you ran and what it changed.",
            Flat(Prompt()),
            StringComparison.Ordinal);

    /// <summary>
    /// And when Jason can start work, the person is asked once, in their own voice: a synthetic walk to meet it first, or
    /// their real work at once. A prompt that said nothing of it would leave the choice to the install page alone, which
    /// the person pasting the prompt never reads; one that asked at every turn would keep them from the work.
    /// </summary>
    [Fact]
    public void The_install_prompt_asks_once_what_comes_next_when_Jason_can_start_work() =>
        Assert.Contains(
            "If it can, ask me once whether I want a synthetic walk to meet Jason first or my real work at once.",
            Flat(Prompt()),
            StringComparison.Ordinal);

    /// <summary>
    /// The prompt sends the agent to the install page as a single plain file: the raw address at <c>main</c>, which is
    /// the latest release, because the public <c>main</c> moves only when a release is published.
    /// </summary>
    [Fact]
    public void The_install_prompt_points_at_the_install_page_at_main() =>
        Assert.Contains("<https://raw.githubusercontent.com/reply-team/jason-ai/main/docs/INSTALL.md>", Prompt(), StringComparison.Ordinal);

    /// <summary>
    /// The steps live on the install page alone. The prompt names no command and no option, so steps cannot creep back
    /// into it and disagree with the page, as its <c>--source .</c> once did.
    /// </summary>
    [Fact]
    public void The_install_prompt_names_no_command()
    {
        var prompt = Fenced(Page("README.md"), "```text\nInstall Jason on this machine");

        Assert.DoesNotContain("`", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("--", prompt, StringComparison.Ordinal);
        Assert.DoesNotMatch(new Regex(@"(?<![\w/-])jason(?![\w-])"), prompt);
        Assert.DoesNotMatch(new Regex(@"\b(?:curl|irm|iex|sh|pwsh|powershell|git|dotnet|winget|brew|npm)\b"), prompt);
    }

    /// <summary>
    /// The acts that need the person's word are marked with one phrase, spelled one way in the prompt and on the install
    /// page. Every mark on the page opens a paragraph inside §2–§6, the sections an agent performs; outside them the
    /// phrase appears only in the section for an agent, which says what it means and which of its own acts carry it.
    /// </summary>
    [Fact]
    public void Ask_first_is_spelled_one_way_and_marks_acts_in_sections_2_to_6()
    {
        Assert.Contains("\"Ask first\"", Flat(Prompt()), StringComparison.Ordinal);
        Assert.All(AskFirst().Matches(Prompt()), match => Assert.Equal("Ask first", match.Value));

        var install = Page("docs", "INSTALL.md");
        var found = AskFirst().Matches(install).ToList();
        Assert.All(found, match => Assert.Equal("Ask first", match.Value));

        var start = install.IndexOf("\n## 2. ", StringComparison.Ordinal);
        var end = install.IndexOf("\n## 7. ", StringComparison.Ordinal);
        Assert.True(start > 0 && end > start, "docs/INSTALL.md no longer has §2 followed by §7.");

        var marks = found.Where(match => match.Index > start && match.Index < end).ToList();
        Assert.True(marks.Count == 4, $"docs/INSTALL.md marks {marks.Count} acts in §2–§6 \"Ask first.\"; it marks four.");
        Assert.All(marks, match => Assert.True(
            install.AsSpan(match.Index - 4).StartsWith("\n\n**Ask first.** ", StringComparison.Ordinal),
            $"The mark at offset {match.Index} of docs/INSTALL.md does not open a paragraph as \"**Ask first.**\"."));

        var outside = found.Where(match => match.Index < start || match.Index > end).ToList();
        var agent = install.IndexOf("### If an agent installs it", StringComparison.Ordinal);
        Assert.NotEmpty(outside);
        Assert.All(outside, match => Assert.InRange(match.Index, agent, start));
    }

    /// <summary>
    /// Each act that needs the person's word carries the mark where it is described: putting Jason on the PATH by hand
    /// (§2) and by the repair <c>jason status</c> prints (§5), deploying the packs into the agent's own configuration
    /// (§4), and registering the runtime to start at logon (§6). The mark opens the paragraph before the act's commands,
    /// or the one after the paragraph that introduces it.
    /// </summary>
    [Theory]
    [InlineData("## 2. ", "**Putting it on the PATH yourself.**", 1)]
    [InlineData("## 4. ", "```sh\njason skills install --dry-run\n", -1)]
    [InlineData("## 5. ", "which you type rather than hand back to `jason`.", 1)]
    [InlineData("## 6. ", "```sh\njason runtime autostart enable\n", -1)]
    public void Each_act_that_needs_the_person_s_word_is_marked(string section, string act, int neighbour)
    {
        var blocks = Section(Page("docs", "INSTALL.md"), section).Split("\n\n");
        var at = Array.FindIndex(blocks, block => block.Contains(act, StringComparison.Ordinal));
        Assert.True(at >= 0, $"{section.Trim()} no longer describes the act: {act}");

        var marked = blocks.ElementAtOrDefault(at + neighbour) ?? string.Empty;
        Assert.True(
            marked.StartsWith("**Ask first.** ", StringComparison.Ordinal),
            $"{section.Trim()} does not mark the act with \"**Ask first.**\"; the paragraph there reads: {marked}");
    }

    /// <summary>
    /// A release teaches itself from the repository at its published tag, so no page shows a release a command with
    /// <c>--source</c>: outside a checkout <c>--source .</c> exits 1 with "'.' holds no skills directory". The install
    /// page keeps <c>--source &lt;checkout&gt;</c> for a working tree, in the paragraph that says so.
    /// </summary>
    [Theory]
    [InlineData("README.md", null)]
    [InlineData("docs/handbook.md", null)]
    [InlineData("docs/INSTALL.md", "**From a working tree.**")]
    public void No_page_teaches_a_release_with_source(string page, string? workingTree)
    {
        var text = Page(page.Split('/'));
        var fenced = false;
        var taught = new List<string>();
        foreach (var line in text.Split('\n'))
        {
            if (FenceLine().IsMatch(line))
            {
                fenced = !fenced;
                continue;
            }

            if (fenced && line.Trim().StartsWith("jason skills", StringComparison.Ordinal) && line.Contains("--source", StringComparison.Ordinal))
            {
                taught.Add(line.Trim());
            }
        }

        Assert.True(taught.Count == 0, $"{page} shows a release how to teach itself with --source:\n" + string.Join('\n', taught));

        if (workingTree is not null)
        {
            var paragraph = Section(text, "## 4. ").Split("\n\n").SingleOrDefault(block => block.StartsWith(workingTree, StringComparison.Ordinal));
            Assert.True(paragraph is not null, $"§4 has no paragraph opening with {workingTree}");
            Assert.Contains("`--source <checkout>`", paragraph, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The examples of a pinned install name the release this tree builds everywhere they are printed — the install
    /// page's two lines and its sentence, and each script's own help — in the address and in the option alike. A release
    /// publishes this tree at the tag of the version it builds, so the published page names the release it comes with;
    /// and a development bump that moves the version without them is red until it moves them too.
    /// </summary>
    [Fact]
    public void The_pinned_examples_name_the_release_this_tree_builds()
    {
        var found = new List<(string File, string Version)>();
        foreach (var (file, text) in new[]
                 {
                     ("docs/INSTALL.md", Section(Page("docs", "INSTALL.md"), "## 2. ")),
                     ("install/install.sh", Page("install", "install.sh")),
                     ("install/install.ps1", Page("install", "install.ps1")),
                 })
        {
            foreach (Match match in PinnedVersion().Matches(text))
            {
                found.AddRange(match.Groups["version"].Captures.Select(capture => (file, capture.Value)));
            }
        }

        var expected = new[] { ("docs/INSTALL.md", 6), ("install/install.sh", 2), ("install/install.ps1", 2) };
        Assert.Equal(expected, found.GroupBy(pin => pin.File).Select(group => (group.Key, group.Count())));
        var builds = TreeVersion.Prefix();
        var other = found.Where(pin => pin.Version != builds).ToList();
        Assert.True(
            other.Count == 0,
            $"This tree builds {builds} (VersionPrefix in dotnet/Directory.Build.props), and the pinned examples name: "
            + string.Join(", ", other.Select(pin => $"{pin.File} {pin.Version}")));
    }

    /// <summary>
    /// §2 says how to install without running a downloaded script: the steps the script takes, in its order. The digest is
    /// checked, and a mismatch stops, before anything is unpacked; the notices keep the names the uninstall removes them
    /// by; the executable is run by its full path; and only then does the PATH come, as the act its own paragraph marks.
    /// Last, what the steps left in the temporary directory may go, named one by one so that nothing else goes with it.
    /// </summary>
    [Fact]
    public void The_install_page_s_hand_path_checks_the_digest_before_it_unpacks()
    {
        var hand = Flat(Subsection(Section(Page("docs", "INSTALL.md"), "## 2. "), "### By hand"));

        string[] steps =
        [
            "1. Download the archive for this machine and `checksums.txt` beside it: for the latest release,",
            "2. Check the archive's SHA-256 against its line in `checksums.txt`",
            "and stop if they differ.",
            "3. Unpack it into a new temporary directory",
            "named `jason-LICENSE` and `jason-THIRD-PARTY-NOTICES`.",
            "4. Run `jason --version` there, by its full path.",
            "5. Put that directory on the PATH, as *Putting it on the PATH yourself* above says",
            "Once `jason --version` answers there, the archive, `checksums.txt` and the temporary directory you unpacked into have done their work: you may delete those three — only those — with `Remove-Item -Recurse <path>` on Windows and `rm -r <path>` elsewhere.",
        ];

        var at = -1;
        foreach (var step in steps)
        {
            var next = hand.IndexOf(step, StringComparison.Ordinal);
            Assert.True(next > at, $"§2 *By hand* does not say, after what comes before it: {step}");
            at = next;
        }
    }

    /// <summary>
    /// The archive is unpacked somewhere of its own and the executable moved in. Unpacked straight into the install
    /// directory, it would write a bare <c>LICENSE</c> there — and on macOS and Linux that directory is
    /// <c>~/.local/bin</c>, which other tools share, so the file it overwrote would be another tool's.
    /// </summary>
    [Fact]
    public void The_hand_path_unpacks_outside_the_install_directory()
    {
        var hand = Flat(Subsection(Section(Page("docs", "INSTALL.md"), "## 2. "), "### By hand"));

        Assert.Contains("Unpack it into a new temporary directory, and move the executable into the install directory", hand, StringComparison.Ordinal);
        Assert.DoesNotContain("Unpack it into the install directory", hand, StringComparison.Ordinal);
    }

    /// <summary>
    /// An archive can carry the mark of the web, and what it unpacks inherits it, so an executable placed by hand is
    /// stopped the first time it runs. The script clears the mark from the executable it unpacks; the steps by hand say
    /// to do the same.
    /// </summary>
    [Fact]
    public void The_hand_path_unblocks_the_executable_on_Windows() =>
        Assert.Contains(
            "`Unblock-File` the executable, as `install.ps1` does.",
            Flat(Subsection(Section(Page("docs", "INSTALL.md"), "## 2. "), "### By hand")),
            StringComparison.Ordinal);

    /// <summary>
    /// The install page ends with the choice of what comes next: §6's last paragraph says an agent asks once, links the
    /// wiring check as a synthetic walk that is not the real work, says a skipped walk is not offered again, and says the
    /// real work begins with the person telling about their first campaign in their own words. A person who followed the
    /// install to its end — or an agent that did it for them — knows both ways on, and that the walk is not the work.
    /// </summary>
    [Fact]
    public void The_install_page_ends_with_a_choice_asked_once_and_the_real_work_after_it()
    {
        var start = Section(Page("docs", "INSTALL.md"), "## 6. ").TrimEnd().TrimEnd('-').TrimEnd();
        var last = Flat(start[(start.LastIndexOf("\n\n", StringComparison.Ordinal) + 2)..]);

        Assert.Contains(
            "What comes next is yours to choose, and an agent that installed Jason asks you once: [the wiring check](golden-path.md) — a page of its own, a synthetic walk to meet the system, your provider connected and proven with your own address, and not the real work — or the real work at once.",
            last,
            StringComparison.Ordinal);
        Assert.Contains("Skip the walk and it is not offered again.", last, StringComparison.Ordinal);
        Assert.Contains(
            "The real work, at once or after the walk, begins with you telling your agent about your first campaign in your own words — typed, a link, a file or a voice note, or an example to start from — and becomes a campaign planned with the business skills, [`campaign-planning`](../skills/business/campaign-planning/SKILL.md) first, and worked by Jason's roles.",
            last,
            StringComparison.Ordinal);
        Assert.DoesNotContain("one word from you", last, StringComparison.Ordinal);
    }

    /// <summary>
    /// The page is named what it is. It proves the wiring — runtime, plugin, grant, route, one contact, one enrolment, read
    /// back — and its opening says so and points at the real work; so does the first place it sends a reader next.
    /// </summary>
    [Fact]
    public void The_page_is_named_the_wiring_check_and_points_at_the_real_work()
    {
        var page = Page("docs", "golden-path.md");
        var opening = Flat(page.Split("\n\n")[1]);

        Assert.StartsWith("# The wiring check\n", page, StringComparison.Ordinal);
        Assert.StartsWith(
            "This page checks the wiring — the runtime, the plugin, its grant and its route, one contact and one enrolment — and reads it back. It is not the real work, which begins with a campaign planned with the business skills, [`campaign-planning`](../skills/business/campaign-planning/SKILL.md) first, and worked by [Jason's roles](../skills/runtime/roles).",
            opening,
            StringComparison.Ordinal);

        var further = Section(page, "## Where to go next");
        var first = further.IndexOf("\n- ", StringComparison.Ordinal) + 1;
        var second = further.IndexOf("\n- ", first, StringComparison.Ordinal);
        Assert.True(first > 0 && second > first, "\"Where to go next\" no longer opens with a list of at least two places.");
        var next = Flat(further[first..second]).Trim();
        Assert.Equal(
            "- [`campaign-planning`](../skills/business/campaign-planning/SKILL.md) — the real work, after this check or instead of it: a campaign planned with the business skills and worked by [Jason's roles](../skills/runtime/roles).",
            next);
    }

    /// <summary>
    /// Right after its opening, the page says it is a synthetic familiarisation run a person may skip, offered once, and
    /// what it makes at the provider: one real draft campaign, never started, holding nobody but them — so nobody walks it
    /// believing nothing real lands. After it, or instead of it, the real work begins the same way, with the person
    /// telling about their first campaign in their own words. And it says what a skip leaves undone: no provider is
    /// connected until the work needs one, and where on this page one is connected then. A reader who met the walk as the
    /// start of the work, or skipped it and met <c>no_route</c> with nothing said, would have been told less than the
    /// page knows. The paragraph is held whole, so a sentence added to it — one that made the finished walk the person's
    /// first campaign, say — is as red as one taken away.
    /// </summary>
    [Fact]
    public void The_wiring_check_says_it_is_an_optional_synthetic_run_right_after_its_opening()
    {
        var blocks = Page("docs", "golden-path.md").Split("\n\n");
        Assert.True(blocks.Length > 2, "docs/golden-path.md has no paragraph after its opening.");

        Assert.Equal(
            "It is an explicitly synthetic familiarisation run, and you may skip it: offered once when an installation is ready, it meets the system and proves its wiring with your own address as the only contact. "
            + "What it makes at your provider is real, and small: one draft campaign in your account, which nobody starts and which holds nobody but you. "
            + "Skipped or finished, the real work begins the same way: with you telling your agent about your first campaign in your own words — typed, a link, a file or a voice note, or an example to start from. "
            + "Skipping it connects no provider: that waits until the work first needs one, and this page's Reply CLI section and steps 3 to 5 are how it is connected then.",
            Flat(blocks[2]));
    }

    /// <summary>
    /// Where the page names its provider, it says what the provider's own web app is for: four acts a person does there by
    /// a link — connecting a LinkedIn account, a mailbox by single sign-on, signing in, the first API key — and never the
    /// work, which no step of this page or of any Jason flow does there. The paragraph is held whole, right after the one
    /// that names the provider, so a use added to it is as red as one taken away.
    /// </summary>
    [Fact]
    public void The_wiring_check_names_the_four_things_the_provider_s_web_app_is_for()
    {
        var blocks = Page("docs", "golden-path.md").Split("\n\n");
        Assert.True(blocks.Length > 4, "docs/golden-path.md has no paragraph after the one that names its provider.");

        Assert.StartsWith("The provider here is [Reply](https://reply.io)", Flat(blocks[3]), StringComparison.Ordinal);
        Assert.Equal(ProviderWebApp, Flat(blocks[4]));
    }

    /// <summary>The wiring check's paragraph on the provider's web app, flattened, as its fact holds it.</summary>
    private const string ProviderWebApp =
        "Reply's own web app is no step of this page, or of any Jason flow. "
        + "You open it by a link for four things only: connecting a LinkedIn account, connecting a mailbox by single sign-on, signing in, and creating your first API key. "
        + "Jason never sends you there for the work itself.";

    /// <summary>
    /// The README names the page for what it proves, says where the real work begins, and says the page is optional: a
    /// synthetic walk offered once, which a person may skip for the real work at once.
    /// </summary>
    [Fact]
    public void The_readme_names_the_wiring_check_and_where_the_real_work_begins()
    {
        var readme = Flat(Page("docs", "handbook.md"));

        Assert.Contains("**The wiring check** — an empty installation brought to a routed plugin, one enrolment of your own address approved by you and performed against the provider, and the history it leaves — is [docs/golden-path.md](golden-path.md), and every command on that page is executed by a test.", readme, StringComparison.Ordinal);
        Assert.Contains(
            "It proves the wiring and is not the real work, which begins with a campaign planned with the business skills ([`campaign-planning`](../skills/business/campaign-planning/SKILL.md) first) and worked by Jason's roles.",
            readme,
            StringComparison.Ordinal);
        Assert.Contains(
            "It is optional: a synthetic walk, offered once when an installation is ready, which you may skip for the real work at once — and that begins with you telling your agent about your first campaign in your own words.",
            readme,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// No page that hands a reader to the wiring check calls it by a name it had before — the first run, the golden path,
    /// the first-use walkthrough: it is a check of the wiring, and the real work is a campaign. Nor a sandbox: it makes a
    /// real draft campaign at the person's provider and enrols their own address, and the published pages keep that word
    /// for an isolation boundary. A file path is not prose, and the executable's own "first runs" is another matter, so
    /// neither is read as one. Codex's sandbox is that isolation boundary, the meaning the word is kept for, so the word
    /// may stand in the phrases "its sandbox" and "Codex's sandbox" alone: a sentence that names Codex and runs on to list
    /// other hosts says nothing else about a sandbox.
    /// </summary>
    [Theory]
    [InlineData("README.md")]
    [InlineData("docs/handbook.md")]
    [InlineData("docs/INSTALL.md")]
    [InlineData("docs/golden-path.md")]
    [InlineData("skills/runtime/operating-the-installation/SKILL.md")]
    [InlineData("skills/runtime/managed-campaign-work/SKILL.md")]
    public void No_page_calls_the_wiring_check_by_a_name_it_had_before(string page)
    {
        var said = Regex.Split(Flat(Page(page.Split('/'))), @"(?<=[.!?])\s+")
            .SelectMany(sentence => Regex.Matches(sentence, @"\b(?:first run|golden path|first[- ]use walkthrough|(?<!\b(?:its|Codex's) )sandbox)\b", RegexOptions.IgnoreCase)
                .Select(name => $"'{name.Value}' in: {sentence}"))
            .ToList();
        Assert.True(said.Count == 0, $"{page} still calls the wiring check by a name it had before: " + string.Join(" / ", said));
    }

    /// <summary>
    /// The golden path's section for an agent, byte for byte, is held by its digest, as the install page's is: the rules an
    /// agent follows on a first run against a real account are in it, and a change to them is a deliberate one.
    /// </summary>
    [Fact]
    public void The_golden_path_s_agent_section_is_the_text_the_hosts_read() =>
        Assert.Equal(GoldenAgentSectionDigest, "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(GoldenAgent()))));

    /// <summary>
    /// The golden path marks two kinds of act: one that needs the person's word, and one that is the person's own and never
    /// the agent's. Each phrase is spelled one way. Each mark opens a paragraph where its act is described, and each phrase
    /// appears once more, in the section for an agent, where it is said what it means.
    /// </summary>
    [Fact]
    public void The_golden_path_marks_are_spelled_one_way()
    {
        var page = Page("docs", "golden-path.md");
        var agent = page.IndexOf("\n### If an agent runs it\n", StringComparison.Ordinal);
        var end = agent + GoldenAgent().Length;
        Assert.True(agent > 0, "docs/golden-path.md has no section for an agent that runs it.");

        foreach (var mark in new[] { "Ask first", "Yours to do" })
        {
            var found = Regex.Matches(page, Regex.Escape(mark), RegexOptions.IgnoreCase).ToList();
            Assert.All(found, match => Assert.Equal(mark, match.Value));

            var inside = found.Where(match => match.Index > agent && match.Index < end).ToList();
            Assert.True(inside.Count == 1, $"The section for an agent names \"{mark}.\" {inside.Count} times; it names it once.");

            var outside = found.Where(match => match.Index < agent || match.Index > end).ToList();
            Assert.NotEmpty(outside);
            Assert.All(outside, match => Assert.True(
                page.AsSpan(match.Index - 4).StartsWith($"\n\n**{mark}.** ", StringComparison.Ordinal),
                $"The mark \"{mark}.\" at offset {match.Index} of docs/golden-path.md does not open a paragraph."));
        }
    }

    /// <summary>
    /// Each act of the wiring check that is the person's carries its mark where it is described: installing Node is a
    /// system package, theirs to allow; the sign-in is theirs alone, so the key never passes through an agent; making the
    /// provider's campaign is the first write to their account there, so it waits for their word; the approval is their
    /// decision; and the way back takes them out of the provider's campaign on their word.
    /// </summary>
    [Theory]
    [InlineData("### The Reply CLI", "Where Node is missing, or older than 20", 0, "Ask first.")]
    [InlineData("### The Reply CLI", "```\nreply auth login\n", -1, "Yours to do.")]
    [InlineData("## 6. ", "--operation campaign.create", -1, "Ask first.")]
    [InlineData("## 7. ", "```\njason approval list\n", -1, "Ask first.")]
    [InlineData("### The way back", "--operation enrollment.stop", -1, "Ask first.")]
    public void Each_act_on_the_golden_path_that_is_the_person_s_is_marked(string section, string act, int neighbour, string mark)
    {
        var page = Page("docs", "golden-path.md");
        var text = section.StartsWith("## ", StringComparison.Ordinal) ? Section(page, section) : Subsection(page, section);
        var blocks = text.Split("\n\n");
        var at = Array.FindIndex(blocks, block => block.Contains(act, StringComparison.Ordinal));
        Assert.True(at >= 0, $"{section.Trim()} no longer describes the act: {act}");

        var marked = blocks.ElementAtOrDefault(at + neighbour) ?? string.Empty;
        Assert.True(
            marked.StartsWith($"**{mark}** ", StringComparison.Ordinal),
            $"{section.Trim()} does not mark the act with \"**{mark}**\"; the paragraph there reads: {marked}");
    }

    /// <summary>
    /// A stranger has no checkout, so the package comes from the release they run: <c>jason-plugin-reply.zip</c> and
    /// <c>checksums.txt</c>, downloaded from the product's address at <c>v&lt;version&gt;</c>, and the package unpacked in
    /// the same line that checks it against the other, so that nothing can unpack it after a failed check — in sh for
    /// macOS and Linux and in PowerShell for Windows. The tree <c>jason skills install</c> fetched is the fallback; nothing
    /// is cloned, and nothing is copied out of a checkout. That the lines install the tree's package, and unpack nothing
    /// that fails its check however they are run, is executed elsewhere, against a stand-in for the release.
    /// </summary>
    [Fact]
    public void The_golden_path_installs_the_package_from_the_release()
    {
        var page = Page("docs", "golden-path.md");
        var step = Section(page, "## 3. ");

        Assert.Equal(["jason plugin install reply"], FencedLines(Regex.Match(step, "```\n.*?\n```", RegexOptions.Singleline).Value));

        var said = Flat(step);
        Assert.Contains("Every release of Jason from 0.11.0 on publishes the Reply plugin's directory as `jason-plugin-reply.zip`", said, StringComparison.Ordinal);
        Assert.Contains("holds it to its line in `checksums.txt` and, unpacked, to that digest, and only then moves it in whole as `~/.jason/plugins/reply`.", said, StringComparison.Ordinal);
        Assert.Contains("A package that does not match places nothing: the verb exits 1, says why, and you stop and say so too.", said, StringComparison.Ordinal);
        Assert.Contains("`--replace` swaps it for the release's.", said, StringComparison.Ordinal);
        Assert.Contains("Without a runtime the package is placed all the same, and the runtime loads it at its next start; the verb never starts one.", said, StringComparison.Ordinal);
        Assert.Contains("Where the release cannot be reached, the tree `jason skills install` fetched", said, StringComparison.Ordinal);
        Assert.Contains("cp -r ~/.jason/skills/staged/v<version>/plugins/reply ~/.jason/plugins/reply", step, StringComparison.Ordinal);
        Assert.DoesNotContain("There is no install verb", said, StringComparison.Ordinal);
        Assert.DoesNotContain("curl ", step, StringComparison.Ordinal);
        Assert.DoesNotContain("irm ", step, StringComparison.Ordinal);
        Assert.DoesNotContain("git clone", step, StringComparison.Ordinal);
        Assert.DoesNotContain("cp -r plugins/reply ~/.jason/plugins/reply", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// The section for an agent says where every file the run makes goes — the package step 3 places, into Jason's data
    /// directory, and the one file step 6 writes, into the system's temporary directory — never the folder the run was
    /// started in.
    /// </summary>
    [Fact]
    public void The_golden_path_s_agent_says_where_the_run_s_files_go() =>
        Assert.Contains(
            "nothing in this run writes there — the package step 3 places goes into Jason's data directory, and the one file step 6 makes goes in the system's temporary directory.",
            Flat(GoldenAgent()),
            StringComparison.Ordinal);

    /// <summary>
    /// The first run is started from wherever the person happens to be, which is often no repository at all, so the page
    /// assumes none: nothing in the run writes into that folder, and the one file it makes goes to the temporary directory.
    /// </summary>
    [Fact]
    public void The_golden_path_assumes_no_repository_or_folder()
    {
        var page = Flat(Page("docs", "golden-path.md"));

        Assert.Contains("It assumes no repository and no particular folder: start in any folder of yours, and nothing in this run writes there", Flat(GoldenAgent()), StringComparison.Ordinal);
        Assert.Contains("`contacts.json`, in the system's temporary directory, holds your address and nothing else.", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// Step 6 is two acts, and an agent that joins them into one shell line hands its harness one command to refuse whole:
    /// the file first, written by the agent's own file tool, then each command alone. The file is printed as it is
    /// meant to be, the address and nothing else, and the command names it where it was written.
    /// </summary>
    [Fact]
    public void The_golden_path_writes_the_contacts_file_on_its_own_and_runs_each_command_alone()
    {
        var step = Section(Page("docs", "golden-path.md"), "## 6. ");

        Assert.Contains("First the file, on its own:", Flat(step), StringComparison.Ordinal);
        Assert.Contains("An agent writes it with its own file tool, not from a shell line:", Flat(step), StringComparison.Ordinal);
        Assert.Contains("Then the commands, one at a time:", Flat(step), StringComparison.Ordinal);

        var fenced = FencedLines(step);
        Assert.Contains("[{\"channels\":[{\"channel\":\"email\",\"value\":\"<your address>\"}]}]", fenced);
        Assert.DoesNotContain("first_name", step, StringComparison.Ordinal);
        Assert.Contains("jason campaign add-contacts cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --file <temporary directory>/contacts.json --match-by email", fenced);
        Assert.DoesNotContain(fenced, line => line.Contains("--file contacts.json", StringComparison.Ordinal));
    }

    /// <summary>
    /// The golden path's arguments are single-quoted JSON, which bash, zsh and PowerShell 7.3 and later hand over as
    /// written; a double-quoted argument with escaped quotes inside reaches the CLI split in two under PowerShell. Windows
    /// PowerShell 5.1 strips the quotes inside even single quotes, so the page names the form that takes no quoting at
    /// all: the arguments in a file, given with <c>--file</c>.
    /// </summary>
    [Fact]
    public void The_golden_path_gives_its_arguments_in_a_form_each_shell_takes()
    {
        var page = Page("docs", "golden-path.md");
        var inputs = FencedLines(page).Where(line => line.Contains("--input", StringComparison.Ordinal)).ToList();

        Assert.Equal(6, inputs.Count);
        Assert.All(inputs, line => Assert.Contains("--input '{", line, StringComparison.Ordinal));
        Assert.DoesNotContain(inputs, line => line.Contains("\\\"", StringComparison.Ordinal));
        Assert.Contains(
            "Windows PowerShell 5.1 strips the quotes inside them, and the CLI then refuses the line with exit 2 before anything is made. There, write the arguments into a file as `{\"context\":{\"input\":…}}`, with your own file tool if you are an agent, and pass it with `--file` in place of `--input`.",
            Flat(Section(page, "## 6. ")),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The Reply CLI signs in by its own route, which may be a page in the browser: said where the sign-in is, so that a
    /// person is not surprised by it, and outside the section for an agent, whose digest it does not move.
    /// </summary>
    [Fact]
    public void The_golden_path_says_the_sign_in_may_open_a_browser() =>
        Assert.Contains(
            "**Yours to do.** Sign in, in your own terminal, as the person whose account it is. It may open your browser:",
            Subsection(Page("docs", "golden-path.md"), "### The Reply CLI"),
            StringComparison.Ordinal);

    /// <summary>
    /// The wiring check enrols only into a provider's campaign nobody has started and that holds nobody: the read of it
    /// must say it is a draft, and the read of its people that their count is 0. Anything else — a started campaign,
    /// somebody already in it, a count the provider would not give — stops the run before anything is written to it.
    /// </summary>
    [Fact]
    public void The_wiring_check_stops_on_anything_but_a_draft_that_holds_nobody()
    {
        var agent = Flat(GoldenAgent());

        Assert.Contains("`campaign.get` must answer `draft` before anything is written to it. Any other status stops the run, and you say why.", agent, StringComparison.Ordinal);
        Assert.Contains("`enrollment.list` must answer a `total` of 0 before the enrolment. Any other count, null included, stops the run.", agent, StringComparison.Ordinal);
        Assert.Contains("The enrolment refuses a collision (`collision: refuse`), and `total` is 1 after it.", agent, StringComparison.Ordinal);
        Assert.Contains(
            "Anything else stops the run here: a provider's campaign that has started, or that already holds somebody, is not one this run enrols into.",
            Flat(Section(Page("docs", "golden-path.md"), "## 6. ")),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// What keeps the wiring check from sending is that nothing in it starts the provider's campaign: the page says so,
    /// tells an agent never to start one, and prints no command that would.
    /// </summary>
    [Fact]
    [Critical(Family.D1, "the wiring check stays synthetic")]
    public void The_wiring_check_never_starts_the_provider_s_campaign()
    {
        var page = Page("docs", "golden-path.md");

        Assert.Contains("A provider's campaign nobody started sends nothing, and this run never starts one.", Flat(page), StringComparison.Ordinal);
        Assert.Contains("A provider's campaign nobody started sends nothing, and this run never starts one: never run `campaign.activate`.", Flat(GoldenAgent()), StringComparison.Ordinal);

        // The run does start something: Jason's own campaign, so its work can run. Said beside the command, so that
        // "this run never starts one" is not read as forbidding it, nor the command as starting the provider's campaign.
        Assert.Contains(
            "`jason campaign start` starts Jason's own campaign, so the work below can run; nothing at the provider is started.",
            Flat(Section(page, "## 6. ")),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            FencedLines(page),
            line => line.Contains("campaign.activate", StringComparison.Ordinal) || line.Contains("sequence start", StringComparison.Ordinal));
    }

    /// <summary>
    /// Making the provider's campaign is the first thing an installation writes to the person's account there. Its
    /// contract runs it without a question — a draft that holds nobody reaches nobody — so the question is the page's
    /// own, said with its reason, and the one question an agent asks first names it among what the run writes. The
    /// person no longer makes it themselves.
    /// </summary>
    [Fact]
    public void The_wiring_check_asks_before_its_first_write_to_the_provider_and_says_why()
    {
        var step = Flat(Section(Page("docs", "golden-path.md"), "## 6. "));
        var agent = Flat(GoldenAgent());

        Assert.Contains(
            "**Ask first.** The next command makes the provider's campaign, the first thing this installation writes to your account at the provider. Its contract asks no question, because a draft that holds nobody reaches nobody; this page asks, because it is the first write.",
            step,
            StringComparison.Ordinal);
        Assert.Contains(
            "what this run writes — the package into Jason's data directory, the grant and the route into its `settings.json`, and one draft campaign in their account at the provider;",
            agent,
            StringComparison.Ordinal);
        Assert.Contains("- The provider's campaign is the one step 6 makes on their yes, never one that was there before.", agent, StringComparison.Ordinal);
        Assert.DoesNotContain("making the sequence", agent, StringComparison.Ordinal);
    }

    /// <summary>
    /// Two things are called <c>campaign.create</c>. The page says which one it runs — the provider operation, which makes
    /// the provider's campaign for Jason's campaign by its name, in the voice the approval summaries use — and that it is
    /// not the verb that made Jason's own; and a name the provider already holds is said with what to do next.
    /// </summary>
    [Fact]
    public void The_wiring_check_says_which_campaign_create_it_means()
    {
        var step = Flat(Section(Page("docs", "golden-path.md"), "## 6. "));

        Assert.Contains(
            "This `campaign.create` is the provider operation: it makes the provider's campaign for Jason's campaign Wiring check — at Reply, a sequence — and links the two. It is not `jason campaign create` above, which made Jason's own campaign:",
            step,
            StringComparison.Ordinal);
        Assert.Contains("](contracts/README.md)", step, StringComparison.Ordinal);
        Assert.Contains(
            "Its `result` holds one item, which says what was made: `state` is `draft`, `enrolled` is 0 and `steps_added` is 1; its `campaign_external_id` is the provider's identifier for it,",
            step,
            StringComparison.Ordinal);
        Assert.Contains(
            "`campaign_external_id` is the provider's identifier for it, which Jason's campaign now carries as its pin — `jason campaign get` lists it under `external_ids`. Every command below works by that pin and names no provider identifier.",
            step,
            StringComparison.Ordinal);
        Assert.Contains("fails `campaign_name_taken`, and nothing is made: ask the person for another name, and create it with a key of its own.", step, StringComparison.Ordinal);
    }

    /// <summary>
    /// A provider operation is claimed only in an active Jason campaign — a draft one is a backlog — so Jason's campaign is
    /// started before the create's work item, and every other provider operation comes after the create: it works by the
    /// pin the create leaves, and names no provider identifier of its own.
    /// </summary>
    [Fact]
    public void The_wiring_check_starts_Jason_s_campaign_before_the_create_and_reads_after_it()
    {
        var page = Page("docs", "golden-path.md");
        var lines = FencedLines(Section(page, "## 6. "));
        var start = lines.FindIndex(line => line.StartsWith("jason campaign start ", StringComparison.Ordinal));
        var create = lines.FindIndex(line => line.Contains("--operation campaign.create ", StringComparison.Ordinal));
        var others = lines
            .Select((line, index) => (line, index))
            .Where(row => row.line.Contains("--kind provider_op", StringComparison.Ordinal) && row.index != create)
            .ToList();

        Assert.True(start >= 0 && create > start, $"Step 6 creates the provider's campaign at line {create} of its commands and starts Jason's campaign at line {start}.");
        Assert.NotEmpty(others);
        Assert.All(others, row => Assert.True(row.index > create, $"Step 6 runs this before the create: {row.line}"));
        Assert.DoesNotContain(
            FencedLines(page),
            line => line.Contains("--kind provider_op", StringComparison.Ordinal) && line.Contains("external_id", StringComparison.Ordinal));
    }

    /// <summary>
    /// No step of the wiring check sends the person to the provider's own interface. The one act that is theirs alone is
    /// the sign-in, because the key must not pass through an agent; so outside the section for an agent, which says what
    /// the mark means, "Yours to do." marks that act and no other.
    /// </summary>
    [Fact]
    public void The_only_person_s_own_act_on_the_wiring_check_is_the_sign_in()
    {
        const string Mark = "**Yours to do.**";
        var page = Page("docs", "golden-path.md");
        var agent = page.IndexOf("\n### If an agent runs it\n", StringComparison.Ordinal);
        var end = agent + GoldenAgent().Length;
        Assert.True(agent > 0, "docs/golden-path.md has no section for an agent that runs it.");

        var marked = Regex.Matches(page, Regex.Escape(Mark))
            .Where(match => match.Index < agent || match.Index > end)
            .Select(match => page[match.Index..page.IndexOf("\n", match.Index, StringComparison.Ordinal)])
            .ToList();
        Assert.True(
            marked.Count == 1 && marked[0].StartsWith($"{Mark} Sign in, in your own terminal", StringComparison.Ordinal),
            "Outside the section for an agent, docs/golden-path.md marks these acts as the person's own:\n" + string.Join('\n', marked));
    }

    /// <summary>
    /// Each create on the page carries a key of its own. A key the page gave twice would answer the second create with
    /// <c>idempotency_key_reused</c>, exit 1, in front of a person following it.
    /// </summary>
    [Fact]
    public void Every_create_on_the_wiring_check_has_a_key_of_its_own()
    {
        var keys = FencedLines(Page("docs", "golden-path.md"))
            .Select(line => Regex.Match(line, @"--idempotency-key (\S+)"))
            .Where(match => match.Success)
            .Select(match => match.Groups[1].Value)
            .ToList();
        var twice = keys.GroupBy(key => key, StringComparer.Ordinal).Where(group => group.Count() > 1).Select(group => group.Key).ToList();

        Assert.True(keys.Count >= 7, $"docs/golden-path.md gives {keys.Count} creates a key: {string.Join(", ", keys)}.");
        Assert.True(twice.Count == 0, $"docs/golden-path.md gives more than one create the key {string.Join(", ", twice)}.");
    }

    /// <summary>
    /// The page names Jason's campaign in its prose by the name its command creates it with, so the sentence that tells
    /// the two campaigns apart is about the campaign the reader made.
    /// </summary>
    [Fact]
    public void The_wiring_check_names_Jason_s_campaign_as_it_creates_it()
    {
        var step = Section(Page("docs", "golden-path.md"), "## 6. ");
        var create = Assert.Single(FencedLines(step), line => line.StartsWith("jason campaign create ", StringComparison.Ordinal));
        var name = Regex.Match(create, "--name \"([^\"]+)\"");
        Assert.True(name.Success, $"Step 6 creates Jason's campaign without a quoted name: {create}");

        Assert.Contains($"the provider's campaign for Jason's campaign {name.Groups[1].Value} —", Flat(step), StringComparison.Ordinal);
    }

    /// <summary>
    /// What the page claims about the provider's campaign it made is three facts, said once: it was never started, it holds
    /// nobody but you, and its one step is a task, there only so that it can exist. Nothing about a mailbox, a sender or a
    /// schedule: the provider attaches its own default mailbox and schedule to a campaign it makes, so a sentence saying
    /// none is attached would be false.
    /// </summary>
    /// <remarks>
    /// The mailbox and sender words are refused on the whole page but one paragraph: the one that names what the
    /// provider's web app is for, whose mailbox is a person connecting their own in that app rather than anything said
    /// about the campaign, and which its own fact holds word for word, so nothing else can arrive in it. The schedule
    /// words are refused from step 6 to the end of step 8, where the provider's campaign is made, read and left, because
    /// step 1 says rightly that the runtime owns "the schedule" of its own work.
    /// </remarks>
    [Fact]
    public void The_wiring_check_claims_three_facts_about_the_provider_s_campaign_and_nothing_about_a_sender()
    {
        var page = Page("docs", "golden-path.md");

        Assert.Contains(
            "So the provider's campaign this run made was never started, holds nobody but you, and has one task step, added only so that it can exist.",
            Flat(Section(page, "## 8. ")),
            StringComparison.Ordinal);

        var scanned = string.Join("\n\n", page.Split("\n\n").Where(block => Flat(block) != ProviderWebApp));
        var sender = Regex.Match(scanned, @"\b(?:mailbox(?:es)?|senders?|sending accounts?|email accounts?)\b", RegexOptions.IgnoreCase);
        Assert.False(sender.Success, $"docs/golden-path.md says something about the provider's sending side: '{sender.Value}' at offset {sender.Index} of the page without the paragraph on the provider's web app.");

        var from = page.IndexOf("\n## 6. ", StringComparison.Ordinal);
        var to = page.IndexOf("\n## What this guarantees", StringComparison.Ordinal);
        Assert.True(from > 0 && to > from, "docs/golden-path.md no longer has step 6 followed by \"What this guarantees\".");
        var made = Regex.Match(page[from..to], @"\b(?:schedules?|sending address(?:es)?|sends? from)\b", RegexOptions.IgnoreCase);
        Assert.False(made.Success, $"Steps 6 to 8 say something about the provider's schedule or sending address: '{made.Value}' at offset {from + made.Index}.");
    }

    /// <summary>
    /// The way back takes the person out of the provider's campaign, on their word, and then leaves the campaign where it
    /// is: it holds nobody and sends nothing. Archiving or deleting it is the person's choice and never a step, and the
    /// page has one name for it — no "trial" anything. Its commands are the stop and, after it, the pause of Jason's own
    /// campaign, and nothing else.
    /// </summary>
    [Fact]
    public void The_way_back_takes_you_out_and_then_leaves_the_provider_s_campaign()
    {
        const string LeaveIt = "Then leave it: the provider's campaign this run made holds nobody and sends nothing. Archiving or deleting it is yours to choose, whenever you like, and no step of this page.";
        var page = Page("docs", "golden-path.md");
        var back = Subsection(page, "### The way back");

        Assert.Contains(LeaveIt, Flat(back), StringComparison.Ordinal);

        // Deleting and archiving are named once, as the person's choice, and nowhere else as something to do.
        var act = Regex.Match(Flat(back).Replace(LeaveIt, string.Empty, StringComparison.Ordinal), @"\b(?:delet|archiv)\w*", RegexOptions.IgnoreCase);
        Assert.False(act.Success, $"The way back names '{act.Value}' outside the sentence that leaves the provider's campaign where it is.");
        var commands = FencedLines(back);
        Assert.True(
            commands.Count == 2
                && commands[0].Contains("--operation enrollment.stop ", StringComparison.Ordinal)
                && commands[1] == "jason campaign pause cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD",
            "The way back prints commands other than the stop and then the pause of Jason's campaign:\n" + string.Join('\n', commands));
        Assert.DoesNotContain("Yours to do", back, StringComparison.Ordinal);

        var trial = Regex.Match(page, @"\btrial\b", RegexOptions.IgnoreCase);
        Assert.False(trial.Success, $"docs/golden-path.md still says '{trial.Value}' at offset {trial.Index}.");
    }

    /// <summary>
    /// The way back ends by pausing Jason's campaign. The pause is Jason's own, undone by a start, and reaches nothing
    /// outside Jason, so nobody is asked for it. It comes once the stop's work item has finished — work waits in a paused
    /// campaign, the stop's included — and it stands when the person said no to the stop as well, which leaves them in
    /// the provider's campaign, never started and sending nothing; the introduction says so. Paused, Jason stops reading
    /// the provider's campaign, so nothing the person later does with that campaign makes a read of Jason's fail.
    /// </summary>
    [Fact]
    public void The_way_back_ends_by_pausing_Jason_s_campaign_once_the_stop_has_finished()
    {
        var back = Subsection(Page("docs", "golden-path.md"), "### The way back");
        var blocks = back.Split("\n\n");
        var stop = Array.FindIndex(blocks, block => block.Contains("--operation enrollment.stop ", StringComparison.Ordinal));
        var pause = Array.FindIndex(blocks, block => block.Contains("\njason campaign pause cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD\n", StringComparison.Ordinal));

        Assert.True(stop > 0 && pause > stop, $"The way back pauses Jason's campaign in its block {pause} and stops the enrolment in its block {stop}.");
        Assert.True(
            Flat(blocks[pause - 1]) == "Last, once the stop's work item has finished — or at once, if you said no and the stop was never made: you then stay in the provider's campaign, which was never started and sends nothing — pause Jason's campaign:",
            $"The way back introduces the pause otherwise: {blocks[pause - 1]}");
        Assert.Contains(
            "With Jason's campaign paused, Jason stops reading the provider's campaign, and nothing at the provider changes: the pause is Jason's own, `jason campaign start` undoes it, and step 8's chronicle is kept. It waits for the stop to finish because work in a paused campaign waits too, the stop's included; and whatever you later do with the provider's campaign, no read of Jason's fails over it.",
            Flat(back),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The way back asks the person once, for the stop, which takes them out of the provider's campaign. The pause after
    /// it is Jason's own and reaches nothing outside Jason, so it asks nobody, in its own line or in a paragraph of its own.
    /// </summary>
    [Fact]
    public void The_way_back_asks_first_only_for_the_stop()
    {
        var blocks = Subsection(Page("docs", "golden-path.md"), "### The way back").Split("\n\n");
        var asked = blocks.Select((block, index) => (block, index)).Where(row => row.block.Contains("Ask first", StringComparison.Ordinal)).ToList();

        Assert.True(
            asked.Count == 1 && blocks[asked[0].index + 1].Contains("--operation enrollment.stop ", StringComparison.Ordinal),
            "The way back asks first here, and only the paragraph before the stop may:\n" + string.Join("\n---\n", asked.Select(row => row.block)));
    }

    /// <summary>
    /// The page's prose wraps at its width, 120 characters, as the rest of the repository's pages do; a command in a fence
    /// is typed whole and keeps its length.
    /// </summary>
    [Fact]
    public void The_wiring_check_s_prose_wraps_at_the_page_s_width()
    {
        var lines = Page("docs", "golden-path.md").Split('\n');
        var fenced = false;
        var wide = new List<string>();
        for (var index = 0; index < lines.Length; index++)
        {
            if (FenceLine().IsMatch(lines[index]))
            {
                fenced = !fenced;
                continue;
            }

            if (!fenced && lines[index].Length > 120)
            {
                wide.Add($"line {index + 1}, {lines[index].Length} characters");
            }
        }

        Assert.True(wide.Count == 0, "docs/golden-path.md has prose wider than 120 characters: " + string.Join("; ", wide));
    }

    /// <summary>
    /// A wiring check that stops — at a read that said no, at a refusal — names what comes next, and the end of the run
    /// gives the person the way back: the two commands, the stop and the pause after it.
    /// </summary>
    [Fact]
    public void The_golden_path_names_the_next_step_when_it_stops() =>
        Assert.Contains(
            "When you stop, name the next step. At the end, give the person the way back in two lines: the stop, and the pause after it.",
            Flat(GoldenAgent()),
            StringComparison.Ordinal);

    /// <summary>
    /// What working around a refusal is, is said once, on the install page. The golden path refers to it and says nothing
    /// of its own about it, so the two pages cannot come to say different things.
    /// </summary>
    [Fact]
    public void The_golden_path_refers_a_refusal_to_the_install_page_s_rule()
    {
        var page = Flat(Page("docs", "golden-path.md"));

        Assert.Contains(
            "A refusal by your harness, by npm or by the provider is reported once, as it stands, and never worked around, as [the install page](INSTALL.md) says.",
            Flat(GoldenAgent()),
            StringComparison.Ordinal);
        Assert.DoesNotContain("rewrapped", page, StringComparison.Ordinal);
        Assert.DoesNotContain("by another means", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// Before anything runs, an agent asks the person one question that names what the first run installs and from where —
    /// the Reply plugin from the release they run, the Reply CLI from npm's registry, and Node where it is missing — so the
    /// person's yes names every download.
    /// </summary>
    [Fact]
    public void The_golden_path_asks_one_question_naming_its_sources_first() =>
        Assert.Contains(
            "Before anything, ask the person one question in plain words: what you will install and from where — the Reply plugin from the release of Jason they run, the Reply CLI from npm's registry, and Node if it is missing;",
            Flat(GoldenAgent()),
            StringComparison.Ordinal);

    /// <summary>
    /// The public repository has a release, so a 404 from a one-liner no longer means there is none yet: it is a fault on
    /// the way to the release, reported as it is, and never a reason to build. Both pages that print the one-liners say
    /// so, and neither keeps the sentence from before the repository was public.
    /// </summary>
    [Theory]
    [InlineData("docs/handbook.md")]
    [InlineData("docs/INSTALL.md")]
    public void No_page_says_a_404_means_no_release_yet(string page)
    {
        var text = Flat(Page(page.Split('/')));

        Assert.DoesNotContain("no published release to install yet", text, StringComparison.Ordinal);
        Assert.DoesNotContain("nothing to install yet", text, StringComparison.Ordinal);
        Assert.DoesNotContain("has no latest", text, StringComparison.Ordinal);
        Assert.Contains(
            "If either answers 404, the release could not be reached — through the address or on GitHub — and that is not a failure of your machine: report it as it stands rather than run the line again, and build nothing instead",
            text,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// An agent performs §2, then §4 to §6. §3 is for a build from the runtime's repository, and §7 takes the
    /// installation off again. And a package manager's own change to the PATH is part of what it was asked to do, as the
    /// one-liner's is when the person types it; only a PATH edit of its own is the person's to allow.
    /// </summary>
    [Fact]
    public void The_agent_section_does_section_2_then_4_to_6()
    {
        var agent = Flat(Agent());

        Assert.Contains("Do §2, then §4 to §6, in order, and stop there.", agent, StringComparison.Ordinal);
        Assert.Contains("§3 is for a build from the runtime's repository, and §7 takes the installation off again.", agent, StringComparison.Ordinal);
        Assert.DoesNotContain("§2 to §6", agent, StringComparison.Ordinal);
        Assert.Contains(
            "A package manager's own PATH change is part of the install you were asked for, and so is the one-liner's when the person types it; a PATH edit of your own, by hand (§2) or by the repair (§5), is marked **Ask first.**",
            agent,
            StringComparison.Ordinal);
        Assert.DoesNotContain("The one-liner's own PATH change is part of the install you were asked for", agent, StringComparison.Ordinal);
    }

    /// <summary>
    /// The optional checks `jason status` prints in its help, and the two pages that copy them by hand: the README's
    /// table and INSTALL's. A check added to one and not the others is a drift nobody held before.
    /// </summary>
    [Theory]
    [InlineData("docs/handbook.md")]
    [InlineData("docs/INSTALL.md")]
    public async Task The_pages_list_the_optional_checks_status_prints_in_its_help(string page)
    {
        var output = new StringWriter();
        using (var dir = new TempPaths())
        {
            await CliApp.RunAsync(["status", "--help"], new CliEnvironment(output, new StringWriter(), dir.Paths), TestContext.Current.CancellationToken);
        }

        var help = Flat(output.ToString());
        var start = help.IndexOf("Optional — absent never fails:", StringComparison.Ordinal) + "Optional — absent never fails:".Length;
        var end = help.IndexOf("The path check", start, StringComparison.Ordinal);
        var printed = help[start..end].Split(" | ", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        var rows = Page(page.Split('/'))
            .Split('\n')
            .SkipWhile(line => !line.StartsWith("| Required — a failure exits 1 | Optional — absent never fails |", StringComparison.Ordinal))
            .Skip(2)
            .TakeWhile(line => line.StartsWith('|'))
            .Select(line => line.Split('|', StringSplitOptions.TrimEntries)[2].Replace("**", string.Empty, StringComparison.Ordinal))
            .Where(cell => cell.Length > 0)
            .ToList();

        Assert.Equal(printed, rows);
        Assert.Contains("notifications reach you", rows);
        Assert.Contains("every enabled profile names an agent host detected on this machine", rows);
    }

    /// <summary>
    /// The required check that the running runtime is the build this CLI is, in both pages' tables as the help lists it. A
    /// required check missing from the table an agent reads before it branches on <c>ready</c> is a <c>false</c> nobody
    /// told it could come.
    /// </summary>
    [Theory]
    [InlineData("docs/handbook.md")]
    [InlineData("docs/INSTALL.md")]
    public void The_pages_list_the_running_build_among_the_required_checks(string page)
    {
        var rows = Page(page.Split('/'))
            .Split('\n')
            .SkipWhile(line => !line.StartsWith("| Required — a failure exits 1 | Optional — absent never fails |", StringComparison.Ordinal))
            .Skip(2)
            .TakeWhile(line => line.StartsWith('|'))
            .Select(line => line.Split('|', StringSplitOptions.TrimEntries)[1])
            .Where(cell => cell.Length > 0)
            .ToList();

        Assert.Contains("the running runtime is the build this CLI is", rows);
    }

    /// <summary>
    /// The install page's section on taking Jason off says what <c>jason uninstall</c> leaves to a package manager that
    /// installed it, and the command that takes that off.
    /// </summary>
    [Fact]
    public void The_install_page_says_what_an_uninstall_leaves_to_a_package_manager()
    {
        var section = Flat(Section(Page("docs", "INSTALL.md"), "## 7. Take it off again"));

        Assert.Contains("Where Homebrew or winget installed Jason", section, StringComparison.Ordinal);
        Assert.Contains("`brew uninstall jason`", section, StringComparison.Ordinal);
        Assert.Contains("`winget uninstall Reply.Jason`", section, StringComparison.Ordinal);
    }

    /// <summary>
    /// Where the install page says what the scripts do, it shows the two lines they print about where Jason will keep what
    /// it learns, word for word as the scripts' runs are held to them, for a made-up account's home on Windows.
    /// </summary>
    [Fact]
    public void The_install_page_shows_the_lines_the_scripts_print_about_the_files()
    {
        var install = Section(Page("docs", "INSTALL.md"), "## 2. ");
        var does = install.IndexOf("Each script works out your platform", StringComparison.Ordinal);
        var lines = install.IndexOf(
            "```text\n" + HarnessRootLines.Where(@"C:\Users\Marta\Jason") + "\n" + HarnessRootLines.DefaultProject + "\n```\n",
            StringComparison.Ordinal);
        var held = install.IndexOf("On Windows, `install.ps1` waits", StringComparison.Ordinal);

        Assert.True(does >= 0 && lines > does && held > lines, $"§2 does not show, after what each script does and before what install.ps1 waits for, the two lines the scripts print (at {does}, {lines}, {held}).");
    }

    /// <summary>
    /// And it says what the lines leave unsaid: where the folder is elsewhere and which one is in force, that the install
    /// makes none, what each level gets — the marker, the memory file and each file a host reads beside it — how a
    /// folder already there is taken and when it is not, that the setting before the first create is all it takes and a
    /// folder moved later is recorded again, and that <c>git</c> in it is the person's act, never Jason's.
    /// </summary>
    [Theory]
    [InlineData("That folder, the harness root, is `Jason` in your home directory — `/home/marta/Jason` on Linux, `/Users/marta/Jason` on macOS — unless `Harness:Root` in `~/.jason/config/settings.json` names another;")]
    [InlineData("a script cannot know what the runtime makes of its settings, so it says \"by default\", and `jason status` names the root in force.")]
    [InlineData("The install makes no folder there. The runtime makes the root the first time a project or a campaign is created,")]
    [InlineData("each with the marker that makes it Jason's, `.jason/marker.json`, an empty `AGENTS.md` for your own words, and one line, `@AGENTS.md`, in each file a host reads in its place (`~/Jason/CLAUDE.md` at the root for Claude Code; the [host matrix](execution-profiles.md#12-the-hosts) lists them), so that it reads them too;")]
    [InlineData("a `Jason` folder already there is taken as the root then, its marker written and nothing it holds changed, unless it is marked as something else, which `jason status` reports.")]
    [InlineData("`Harness:Root` set before that first create is all it takes. A folder you move later is recorded again with `jason project link`, the root's once `Harness:Root` names where it is now.")]
    [InlineData("Jason never writes a word of those memory files, never runs `git` there and never writes a `.gitignore`.")]
    [InlineData("`git init` in the root is yours to run: it gives the folder a history, and lets Codex — which reads the `AGENTS.md` files from the top of a repository down to the folder it starts in — read the root's and the project's with a campaign's.")]
    public void The_install_page_says_where_the_root_is_that_install_makes_none_and_that_git_is_the_person_s(string sentence) =>
        Assert.Contains(sentence, Flat(Section(Page("docs", "INSTALL.md"), "## 2. ")), StringComparison.Ordinal);

    /// <summary>
    /// The install page's section on taking Jason off names the harness root among what is never touched, the purge
    /// included, and among the data directories the purge refuses.
    /// </summary>
    [Fact]
    public void The_install_page_says_an_uninstall_never_touches_the_harness_root()
    {
        var section = Flat(Section(Page("docs", "INSTALL.md"), "## 7. Take it off again"));

        Assert.Contains(
            "and none of the notifications already in the notification center. Nor is anything under the harness root (§2) — your memory files and every project's and campaign's folder — whatever `--purge-data` says.",
            section,
            StringComparison.Ordinal);
        Assert.Contains(
            "or a folder of the harness root, one inside it, or one holding such a folder directly in it or anywhere in a folder the purge would delete, known by its `.jason/marker.json`, or holding a folder there that cannot be looked into, which a stray `JASON_DATA_DIR` can name — is refused before anything is removed.",
            section,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The install page's section on taking Jason off says that a link in the data directory goes as itself and that what
    /// it points to is never touched, as the verb's help says it.
    /// </summary>
    [Fact]
    public void The_install_page_says_a_link_in_the_data_directory_goes_as_itself() =>
        Assert.Contains(
            "A link in the data directory, a symbolic link or a junction, is removed as itself, and what it points to is never touched.",
            Flat(Section(Page("docs", "INSTALL.md"), "## 7. Take it off again")),
            StringComparison.Ordinal);

    /// <summary>And so does the README's account of taking it off.</summary>
    [Fact]
    public void The_readme_says_an_uninstall_never_touches_the_folder_of_the_person_s_files() =>
        Assert.Contains(
            "The folder Jason keeps what it learns about your business in — `~/Jason` unless `Harness:Root` names another — is never touched, `--purge-data` included: it is yours.",
            Flat(Page("docs", "handbook.md")),
            StringComparison.Ordinal);

    /// <summary>
    /// The four names the pages give for what an installation reports about its plugins are the names the code writes:
    /// <c>jason status</c> names the one check, <c>jason update check</c> the three fields of its answer. A name the code
    /// does not write is a name no agent will find in an answer.
    /// </summary>
    [Theory]
    [InlineData("plugin_availability")]
    [InlineData("plugin_updates")]
    [InlineData("install_first")]
    [InlineData("upgrade_command")]
    public async Task The_code_writes_the_names_the_pages_give_for_what_is_reported_about_plugins(string name)
    {
        using var dir = new TempPaths();
        StatusCommandTests.Running(dir);
        var output = new StringWriter();
        await CliApp.RunAsync(["status"], StatusCommandTests.Machine(dir, output), TestContext.Current.CancellationToken);
        var written = StatusCommandTests.Read(output).Checks.Select(check => check.Name)
            .Concat(JsonNode.Parse(JsonSerializer.Serialize(
                new UpdateCheckResponse("0.1.0", "0.2.0", true, DateTimeOffset.UnixEpoch, null, "jason update apply", null, null),
                JasonJson.Options))!.AsObject().Select(field => field.Key))
            .ToList();

        Assert.Contains(name, written);
    }

    /// <summary>
    /// And the pages that say them say them as written. The install page names the check and the fields that send an
    /// installation below the floor through the release that reaches it; the operating skill names all four, since it is
    /// the page that reads the answers out; the troubleshooting skill names the check an unavailable plugin is read from.
    /// </summary>
    [Theory]
    [InlineData("docs/INSTALL.md", "plugin_availability")]
    [InlineData("docs/INSTALL.md", "install_first")]
    [InlineData("docs/INSTALL.md", "upgrade_command")]
    [InlineData("skills/runtime/operating-the-installation/SKILL.md", "plugin_availability")]
    [InlineData("skills/runtime/operating-the-installation/SKILL.md", "plugin_updates")]
    [InlineData("skills/runtime/operating-the-installation/SKILL.md", "install_first")]
    [InlineData("skills/runtime/operating-the-installation/SKILL.md", "upgrade_command")]
    [InlineData("skills/runtime/troubleshooting-jason/SKILL.md", "plugin_availability")]
    public void The_pages_name_what_is_reported_about_plugins_as_the_code_writes_it(string page, string name) =>
        Assert.Contains($"`{name}`", Page(page.Split('/')), StringComparison.Ordinal);

    /// <summary>
    /// The golden path says the check names a plugin's problem codes, as the check does and as the install page says: the
    /// text of a problem is a sentence about this machine, and the check never quotes it.
    /// </summary>
    [Fact]
    public void The_golden_path_says_the_check_names_problem_codes()
    {
        var text = Flat(Page("docs", "golden-path.md"));

        Assert.Contains("unavailable, with its problem codes, in its `plugin_availability` check.", text, StringComparison.Ordinal);
        Assert.DoesNotContain("with its problems, in its `plugin_availability` check", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// An installation below the newest release's floor is told the version to install first. Its own
    /// <c>jason update check</c> names that version as <c>install_first</c> and its apply as <c>upgrade_command</c>; the
    /// pages once said the check still named a plain <c>jason update apply</c>, which sent the reader into the refusal.
    /// </summary>
    [Theory]
    [InlineData("docs/INSTALL.md")]
    [InlineData("skills/runtime/operating-the-installation/SKILL.md")]
    public void The_pages_say_the_check_names_the_version_to_install_first_below_the_floor(string page)
    {
        var text = Flat(Page(page.Split('/')));

        Assert.Contains(
            "Its own `jason update check` names 0.19.0 as `install_first` and its apply as `upgrade_command`, and a plain `jason update apply` refuses with `update_not_directly_applicable`, naming 0.19.0.",
            text,
            StringComparison.Ordinal);
        Assert.DoesNotContain("still names `jason update apply`", text, StringComparison.Ordinal);
    }

    /// <summary>The digest of the README's prompt.</summary>
    private const string PromptDigest = "sha256:7d325587da26b104f707b10d842557f68d0062edc1740dc390cdcf8e33e01396";

    /// <summary>The digest of the install page's section for an agent, as <see cref="Agent"/> reads it.</summary>
    private const string AgentSectionDigest = "sha256:3e2b21971398ee3fa53a79a7d2c8599bb61fe2be8258c7f94afeec930b1e6edf";

    /// <summary>The digest of the golden path's section for an agent, as <see cref="GoldenAgent"/> reads it.</summary>
    private const string GoldenAgentSectionDigest = "sha256:ceb6ff1ad6c0344f89171f57246378cd70dc0ad395bb415aad7a8b0ec43421e4";

    /// <summary>
    /// The text between the line that opens a fence and the next line that is exactly three backticks, each line
    /// ending in a newline.
    /// </summary>
    private static string Fenced(string page, string opener)
    {
        var open = page.IndexOf(opener, StringComparison.Ordinal);
        Assert.True(open >= 0, $"the page has no fence opening with '{opener}'.");

        var start = page.IndexOf('\n', open) + 1;
        var close = page.IndexOf("\n```\n", start - 1, StringComparison.Ordinal);
        Assert.True(close >= start, "the fence is never closed.");
        return page[start..(close + 1)];
    }

    /// <summary>A page with every run of white space made one space, so a sentence is found however it is wrapped.</summary>
    private static string Flat(string text) => System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ");

    /// <summary>A page with the marks of a quoted block taken off, so a sentence quoted across lines reads as one.</summary>
    private static string Unquoted(string text) => System.Text.RegularExpressions.Regex.Replace(text, "(?m)^> ?", "");

    /// <summary>The README's paste-in prompt: the fenced block that opens with its first words.</summary>
    private static string Prompt()
    {
        var readme = Page("README.md");
        var open = readme.IndexOf("```text\nInstall Jason on this machine", StringComparison.Ordinal);
        Assert.True(open >= 0, "the README no longer has the prompt as a fenced block.");

        var close = readme.IndexOf("```", open + 7, StringComparison.Ordinal);
        Assert.True(close > open, "the README's prompt is never closed.");
        return readme[open..close];
    }

    /// <summary>The install page's section for an agent that installs Jason: from its heading to the next heading.</summary>
    private static string Agent()
    {
        const string Heading = "\n### If an agent installs it\n";
        var install = Page("docs", "INSTALL.md");
        var heading = install.IndexOf(Heading, StringComparison.Ordinal);
        Assert.True(heading >= 0, "docs/INSTALL.md has no section for an agent that installs it.");

        var next = NextHeading().Match(install, heading + Heading.Length);
        return install[heading..(next.Success ? next.Index : install.Length)];
    }

    /// <summary>The section of a page that opens with a line starting <paramref name="heading"/>, up to the next of its rank.</summary>
    private static string Section(string page, string heading)
    {
        var start = page.IndexOf("\n" + heading, StringComparison.Ordinal);
        Assert.True(start >= 0, $"the page has no section opening with '{heading.Trim()}'.");

        var end = page.IndexOf("\n" + heading[..(heading.IndexOf(' ') + 1)], start + 1, StringComparison.Ordinal);
        return page[(start + 1)..(end > start ? end : page.Length)];
    }

    /// <summary>The golden path's section for an agent that runs it: from its heading to the next heading.</summary>
    private static string GoldenAgent()
    {
        const string Heading = "\n### If an agent runs it\n";
        var page = Page("docs", "golden-path.md");
        var heading = page.IndexOf(Heading, StringComparison.Ordinal);
        Assert.True(heading >= 0, "docs/golden-path.md has no section for an agent that runs it.");

        var next = NextHeading().Match(page, heading + Heading.Length);
        return page[heading..(next.Success ? next.Index : page.Length)];
    }

    /// <summary>The lines of fenced code in a page, without their fences.</summary>
    private static List<string> FencedLines(string page)
    {
        var lines = new List<string>();
        var fenced = false;
        foreach (var line in page.Split('\n'))
        {
            if (FenceLine().IsMatch(line))
            {
                fenced = !fenced;
                continue;
            }

            if (fenced)
            {
                lines.Add(line);
            }
        }

        return lines;
    }

    /// <summary>The part of a section under a heading of its own: from that heading to the next heading of any rank.</summary>
    private static string Subsection(string section, string heading)
    {
        var start = section.IndexOf("\n" + heading + "\n", StringComparison.Ordinal);
        Assert.True(start >= 0, $"the section has no subsection headed '{heading}'.");

        var next = NextHeading().Match(section, start + heading.Length + 2);
        return section[(start + 1)..(next.Success ? next.Index : section.Length)];
    }

    /// <summary>A page's lines with every line of fenced code, its fences included, made empty.</summary>
    private static string[] OutsideFences(string page)
    {
        var lines = page.Split('\n');
        var fenced = false;
        for (var index = 0; index < lines.Length; index++)
        {
            var fence = FenceLine().IsMatch(lines[index]);
            if (fenced || fence)
            {
                lines[index] = string.Empty;
            }

            if (fence)
            {
                fenced = !fenced;
            }
        }

        return lines;
    }

    [GeneratedRegex("ask first", RegexOptions.IgnoreCase)]
    private static partial Regex AskFirst();

    [GeneratedRegex(@"^ {0,3}(?:```|~~~)")]
    private static partial Regex FenceLine();

    [GeneratedRegex(@"(?m)^#{1,6} ")]
    private static partial Regex NextHeading();

    /// <summary>A pinned install's version: in a release's address, after the option that names it, or in the sentence that says so.</summary>
    [GeneratedRegex(@"releases/download/v(?<version>[0-9]+\.[0-9]+\.[0-9]+)/|(?:--version|-Version) (?<version>[0-9]+\.[0-9]+\.[0-9]+)|These install (?<version>[0-9]+\.[0-9]+\.[0-9]+) with (?<version>[0-9]+\.[0-9]+\.[0-9]+)'s own script")]
    private static partial Regex PinnedVersion();

    private static string Page(params string[] path) =>
        File.ReadAllText(Path.Combine([Root(), .. path])).Replace("\r\n", "\n", StringComparison.Ordinal);

    /// <summary>The repository's root: the nearest directory above the test assembly that holds <c>global.json</c>.</summary>
    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory.FullName;
    }
}
