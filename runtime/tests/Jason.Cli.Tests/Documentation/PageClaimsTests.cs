using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Jason.Cli.Doctor;

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
    /// Six names have no API operation behind them — <c>status</c>, <c>uninstall</c>, <c>logs</c> and <c>doctor</c>,
    /// and the <c>skills</c> and <c>update</c> nouns — and no page says two, or the four it said before the two verbs
    /// that read the installation's files arrived.
    /// </summary>
    [Theory]
    [InlineData("README.md", "It is one of six names in this CLI with no API operation behind it")]
    [InlineData("docs/INSTALL.md", "It is one of six names in this CLI with no API operation behind it")]
    [InlineData("CLAUDE.md", "The verb map is one-to-one with API operations with **six declared exceptions**")]
    public void No_page_counts_fewer_names_without_an_operation_than_the_code_declares(string page, string said)
    {
        var text = Flat(Page(page.Split('/')));

        Assert.DoesNotContain("two commands", text, StringComparison.Ordinal);
        Assert.DoesNotContain("two declared exceptions", text, StringComparison.Ordinal);
        Assert.DoesNotContain("the only command here", text, StringComparison.Ordinal);
        Assert.DoesNotContain("one of four names", text, StringComparison.Ordinal);
        Assert.DoesNotContain("four declared exceptions", text, StringComparison.Ordinal);
        Assert.Contains(said, text, StringComparison.Ordinal);
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
    /// The routes travel, and the pages that say what a bundle holds say so: the working notes name them among what the
    /// runtime is asked, and the README says what a route is written as and that its account never is.
    /// </summary>
    [Fact]
    public void The_pages_say_a_bundle_carries_the_routes_and_never_a_route_s_account()
    {
        var notes = Flat(Page("CLAUDE.md"));
        var readme = Flat(Page("README.md"));

        Assert.Contains(
            "of the runtime it asks only what the runtime says about itself, its roles and routes, and the journal and work-item pages it counts",
            notes,
            StringComparison.Ordinal);
        Assert.Contains(
            "which plugin performs which operation, here and per campaign, by plugin id and operation name — never which account a route binds",
            readme,
            StringComparison.Ordinal);
        Assert.Contains("the database, plugin packages, or a route's account and its identity", readme, StringComparison.Ordinal);
    }

    /// <summary>
    /// What <c>--include-journal</c> adds, said on each page in the composer's own text: the one the warning and the
    /// never-carried list quote, so a page cannot promise a bundle carries less than the flag puts in it. The list the
    /// pages said before named six things and left out most of what the journal's entries hold.
    /// </summary>
    [Theory]
    [InlineData("README.md")]
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
    [InlineData("docs/INSTALL.md")]
    [InlineData("docs/release-and-update.md")]
    [InlineData("CLAUDE.md")]
    [InlineData("skills/runtime/operating-the-installation/SKILL.md")]
    [InlineData("runtime/src/Jason.Cli/Uninstall/UninstallCommand.cs")]
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
    [InlineData("runtime/src/Jason.Cli/Skills/SkillsSource.cs")]
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
    [InlineData("README.md", "only where the installer wrote it and its directory holds nothing but Jason")]
    [InlineData("docs/INSTALL.md", "is one rule on both platforms")]
    [InlineData("docs/release-and-update.md", "only where the installer wrote it and its directory holds nothing but Jason")]
    [InlineData("runtime/src/Jason.Cli/Uninstall/UninstallCommand.cs", "only where the installer wrote it and its directory holds nothing but Jason")]
    public void The_pages_hold_every_platform_to_the_directory_rule(string page, string said)
    {
        var text = Flat(Page(page.Split('/')));

        Assert.Contains(said, text, StringComparison.Ordinal);
        Assert.DoesNotContain("on Windows, only where the directory holds nothing but Jason", text, StringComparison.Ordinal);
        Assert.DoesNotContain("only an entry for a directory that holds nothing but Jason", text, StringComparison.Ordinal);
        Assert.DoesNotContain("is a rule of its own on each platform", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Beside every install command a person or an agent is handed, the same sentence says that installing is accepting
    /// the runtime's licence, linked as each place can link it: within the tree on the pages a reader reads in it, at the
    /// release's own tag in the release's notes, and at <c>main</c> in the prompt, which is pasted outside the tree and
    /// installs the latest release.
    /// </summary>
    [Theory]
    [InlineData("README.md", "[LICENSE-RUNTIME.md](LICENSE-RUNTIME.md)")]
    [InlineData("docs/INSTALL.md", "[LICENSE-RUNTIME.md](../LICENSE-RUNTIME.md)")]
    [InlineData(".github/release-notes-template.md", "<https://github.com/reply-team/jason-ai/blob/v{{version}}/LICENSE-RUNTIME.md>")]
    public void Every_page_with_the_install_line_says_installing_is_accepting_the_licence(string page, string link)
    {
        var text = Page(page.Split('/'));
        var sentence = $"By installing Jason you accept the Jason Runtime Licence: {link}. If you do not accept it, do not install Jason.";

        var oneLiner = text.IndexOf("install.ps1 | iex", StringComparison.Ordinal);
        Assert.True(oneLiner > 0, $"{page} has no one-liner.");
        var said = Flat(text).IndexOf(sentence, StringComparison.Ordinal);
        Assert.True(said > Flat(text[..oneLiner]).Length, $"{page} does not say, after its one-liners: {sentence}");
    }

    /// <summary>
    /// The update check, the install scripts and <c>jason update</c> go to the product's own address, which redirects
    /// them to the public repository's releases. The pages that say what leaves a machine say where those requests go
    /// and who operates the address, and that the skills still come from the repository directly.
    /// </summary>
    [Theory]
    [InlineData("README.md", "The check, the install scripts and `jason update` go to `jasonharness.ai`, an address Reply.io operates, which redirects them to this repository's releases; the skills come from this repository directly.")]
    [InlineData("docs/INSTALL.md", "That request, and the downloads the install scripts and `jason update apply` make, go to `jasonharness.ai`, an address Reply.io operates, which redirects them to this repository's releases on GitHub.")]
    [InlineData("docs/release-and-update.md", "**Where it goes.** The default feed is on `jasonharness.ai`, an address Reply.io operates, which redirects every request under `/releases/` to the same path in the public repository's releases on GitHub.")]
    public void The_pages_say_where_a_release_request_goes_and_who_operates_the_address(string page, string said) =>
        Assert.Contains(said, Flat(Unquoted(Page(page.Split('/')))), StringComparison.Ordinal);

    /// <summary>
    /// What the address keeps of those requests, and for how long, is the runtime's licence's to say, in its section
    /// 7.2. The pages that say where the requests go point there, right after saying it, rather than repeat it.
    /// </summary>
    [Theory]
    [InlineData("README.md", "the skills come from this repository directly. What that address keeps, and for how long, is section 7.2 of the [runtime's licence](LICENSE-RUNTIME.md).")]
    [InlineData("docs/INSTALL.md", "which redirects them to this repository's releases on GitHub. What that address keeps, and for how long, is section 7.2 of the [licence](../LICENSE-RUNTIME.md).")]
    public void The_pages_point_at_what_the_address_keeps_where_they_say_where_requests_go(string page, string said) =>
        Assert.Contains(said, Flat(Unquoted(Page(page.Split('/')))), StringComparison.Ordinal);

    /// <summary>
    /// The names are trademarks, and neither licence grants a right to use them, so README says so where it names the
    /// licences, and points at the note that says what anyone may do with them. It no longer has a section saying
    /// "Jason AI" is used descriptively, which beside that note is not so.
    /// </summary>
    [Fact]
    public void The_readme_names_the_marks_beside_the_licences()
    {
        var text = Flat(Page("README.md"));
        var licences = text.IndexOf("## License", StringComparison.Ordinal);
        Assert.True(licences >= 0, "README has no License section.");

        Assert.Contains(
            "The names \"Jason\", \"Jason AI\", \"Reply\" and \"Reply.io\", and their logos, are trademarks of ReplyApp Inc. Neither licence grants a right to use them; [TRADEMARKS.md](TRADEMARKS.md) says what you may do with them without asking.",
            text[licences..],
            StringComparison.Ordinal);
        Assert.DoesNotContain("## Naming", text, StringComparison.Ordinal);
        Assert.DoesNotContain("used descriptively", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_install_prompt_says_installing_is_accepting_the_licence_at_main()
    {
        Assert.Contains(
            "By installing Jason you accept the Jason Runtime Licence: <https://github.com/reply-team/jason-ai/blob/main/LICENSE-RUNTIME.md>. If you do not accept it, do not install Jason.",
            Flat(Prompt()),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The pages that say what an install leaves say it keeps the two notice files beside the executable, under the names
    /// it keeps them as; that a run over the installed version writes one that is missing; and that a person who places
    /// the executable by hand keeps them too. The uninstall's pages say the files go with it, by name.
    /// </summary>
    [Theory]
    [InlineData("README.md", "with its licence and the third-party notices beside it, as `jason-LICENSE` and `jason-THIRD-PARTY-NOTICES`")]
    [InlineData("docs/INSTALL.md", "with its licence and the third-party notices beside it, as `jason-LICENSE` and `jason-THIRD-PARTY-NOTICES`")]
    [InlineData("README.md", "every archive carries the licence and the third-party notices, and an install keeps both beside the executable as `jason-LICENSE` and `jason-THIRD-PARTY-NOTICES`")]
    [InlineData("docs/INSTALL.md", "unless a licence file is missing beside the executable")]
    [InlineData(".github/release-notes-template.md", "unless a licence file is missing beside the executable")]
    [InlineData(".github/release-notes-template.md", "If you then put the executable in place by hand, keep `LICENSE` and `THIRD-PARTY-NOTICES` beside it, named `jason-LICENSE` and `jason-THIRD-PARTY-NOTICES`.")]
    [InlineData("docs/INSTALL.md", "nothing but the executable, its two notice files (`jason-LICENSE`, `jason-THIRD-PARTY-NOTICES`) and the `jason.previous.exe` an upgrade can leave beside it")]
    [InlineData("docs/INSTALL.md", "on macOS and Linux it removes those two files by name from a directory other tools share")]
    [InlineData("README.md", "holds nothing but Jason — the executable, its two notice files and what an upgrade left beside it")]
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
    [InlineData("docs/release-and-update.md", "| `jason-win-x64.zip` | the Windows executable, `LICENSE` (the runtime's licence) and `THIRD-PARTY-NOTICES` |")]
    [InlineData("docs/release-and-update.md", "| `jason-linux-x64.tar.gz` | the Linux executable, `LICENSE` (the runtime's licence) and `THIRD-PARTY-NOTICES` |")]
    [InlineData("docs/release-and-update.md", "| `jason-osx-arm64.tar.gz` | the macOS (Apple silicon) executable, `LICENSE` (the runtime's licence) and `THIRD-PARTY-NOTICES` |")]
    [InlineData(".github/release-notes-template.md", "Each archive holds the executable, `LICENSE` (the runtime's licence; the repository's MIT does not cover the runtime) and `THIRD-PARTY-NOTICES`, nothing else.")]
    public void The_pages_say_every_archive_carries_the_notices(string page, string said)
    {
        var text = Flat(Page(page.Split('/')));

        Assert.Contains(said, text, StringComparison.Ordinal);
        Assert.DoesNotContain("holds the executable and `LICENSE`", text, StringComparison.Ordinal);
    }

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
        var readme = Page("README.md");
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
    /// A session in a mode that runs commands without asking refuses the one-liner: Claude Code's auto mode turned down
    /// the installer an agent had downloaded and checked, as code from outside, and the install stopped at its first
    /// step. So the person who starts the session is told, above the prompt, to start it in a mode that asks.
    /// </summary>
    [Fact]
    public void The_readme_says_to_start_the_install_session_in_a_mode_that_asks_above_the_prompt()
    {
        var readme = Page("README.md");
        var fence = readme.IndexOf("```text\nInstall Jason on this machine", StringComparison.Ordinal);
        Assert.True(fence > 0, "the prompt's fence is where the page has it");

        Assert.Contains(
            "Start it, too, in a mode that asks before it runs a command: on Claude Code, `--permission-mode manual`, not auto mode, which refuses to run a downloaded installer and stops the install at its first step.",
            Flat(readme[..fence]),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The prompt, byte for byte, is held here by its digest, so a change to it is a deliberate one. No host has read
    /// this text yet: the reading still to come is a person's own, pasting it from the published page into a fresh
    /// agent session after the release that publishes it. Until then the digest stands for a deliberate change, not
    /// for a reading.
    /// </summary>
    [Fact]
    public void The_install_prompt_is_the_text_the_hosts_read_and_names_no_connector()
    {
        var prompt = Fenced(Page("README.md"), "```text\nInstall Jason on this machine");
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
    /// The runtime's source is not published, so nothing sends an agent to build it: the prompt says nothing of a build,
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
    /// A harness may refuse to run the one-liner as code from outside, as Claude Code's auto mode does. An agent that
    /// met that stopped and reported, which is right; the section for an agent says so, and names the two ways on that
    /// are the person's: typing the line themselves, or restarting the session in a mode that asks.
    /// </summary>
    [Fact]
    public void The_agent_section_says_not_to_work_around_a_harness_that_refuses_the_one_liner() =>
        Assert.Contains(
            "If your harness refuses to run the one-liner as code from outside — Claude Code's auto mode does — do not work around it: say so, and ask the person to type the line themselves, or to restart the session in a mode that asks before each command.",
            Flat(Agent()),
            StringComparison.Ordinal);

    /// <summary>
    /// A person's own agent that lands on the README sees, within its first screen, what Jason is, that installing it
    /// is the install page, and the prompt: the title, one paragraph, the alpha box, then "Installing" with the prompt
    /// under it. What the project is, its architecture and its layout follow.
    /// </summary>
    [Fact]
    public void The_readme_opens_with_what_it_is_then_the_install_and_the_prompt()
    {
        var lines = OutsideFences(Page("README.md"));

        Assert.Equal(
            ["## Installing", "## What this is", "## Architecture", "## Repository layout"],
            lines.Where(line => line.StartsWith("## ", StringComparison.Ordinal)).Take(4));

        var installing = Array.IndexOf(lines, "## Installing");
        var what = Array.IndexOf(lines, "## What this is");
        Assert.Equal(["### Ask an agent to do it"], lines[installing..what].Where(line => line.StartsWith("### ", StringComparison.Ordinal)));

        var opening = string.Join('\n', lines[..installing])
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(block => !block.StartsWith("# ", StringComparison.Ordinal) && !block.StartsWith('>'))
            .ToList();
        Assert.True(opening.Count == 1, $"Before \"## Installing\" the README has {opening.Count} paragraphs besides the title and the alpha box; it has one.");
    }

    /// <summary>
    /// A person who runs only a one-liner learns on the first screen that it is not the whole of it: between the licence
    /// sentence and the prompt, one paragraph says a one-liner installs the executable, links the install page for the
    /// rest, and names teaching it its skills, without which every role it launches runs untaught.
    /// </summary>
    [Fact]
    public void The_readme_s_first_screen_says_the_install_page_holds_the_rest()
    {
        var readme = Page("README.md");
        var licence = readme.IndexOf("By installing Jason you accept the Jason Runtime Licence: [LICENSE-RUNTIME.md](LICENSE-RUNTIME.md).", StringComparison.Ordinal);
        var prompt = readme.IndexOf("### Ask an agent to do it", StringComparison.Ordinal);
        Assert.True(licence > 0 && prompt > licence, "the README no longer has the licence sentence followed by the prompt's heading.");

        var pointer = readme[licence..prompt].Split("\n\n").Select(Flat).SingleOrDefault(paragraph => paragraph.Contains("[docs/INSTALL.md](docs/INSTALL.md)", StringComparison.Ordinal));
        Assert.True(pointer is not null, "Between the licence sentence and the prompt, no paragraph links the install page.");
        Assert.Contains("A one-liner installs the executable.", pointer, StringComparison.Ordinal);
        Assert.Contains("teaching it its skills", pointer, StringComparison.Ordinal);
    }

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
    /// phrase appears once, where the section for an agent says what it means.
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

        var outside = Assert.Single(found, match => match.Index < start || match.Index > end);
        var agent = install.IndexOf("### If an agent installs it", StringComparison.Ordinal);
        Assert.InRange(outside.Index, agent, start);
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
            $"This tree builds {builds} (VersionPrefix in runtime/Directory.Build.props), and the pinned examples name: "
            + string.Join(", ", other.Select(pin => $"{pin.File} {pin.Version}")));
    }

    /// <summary>
    /// The public repository has a release, so a 404 from a one-liner no longer means there is none yet: it is a fault on
    /// the way to the release, reported as it is, and never a reason to build. Both pages that print the one-liners say
    /// so, and neither keeps the sentence from before the repository was public.
    /// </summary>
    [Theory]
    [InlineData("README.md")]
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
    /// An agent performs §2, then §4 to §6. §3 is for a person who has the runtime's source, and §7 takes the
    /// installation off again. And the one-liner's own change to the PATH is part of what it was asked to do; only a
    /// PATH edit of its own is the person's to allow.
    /// </summary>
    [Fact]
    public void The_agent_section_does_section_2_then_4_to_6()
    {
        var agent = Flat(Agent());

        Assert.Contains("Do §2, then §4 to §6, in order, and stop there.", agent, StringComparison.Ordinal);
        Assert.Contains("§3 is for a person who has the runtime's source, which is not published here, and §7 takes the installation off again.", agent, StringComparison.Ordinal);
        Assert.DoesNotContain("§2 to §6", agent, StringComparison.Ordinal);
        Assert.Contains("The one-liner's own PATH change is part of the install you were asked for", agent, StringComparison.Ordinal);
    }

    /// <summary>
    /// The optional checks `jason status` prints in its help, and the two pages that copy them by hand: the README's
    /// table and INSTALL's. A check added to one and not the others is a drift nobody held before.
    /// </summary>
    [Theory]
    [InlineData("README.md")]
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
        var printed = help[start..end].Split(" · ", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

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
    }

    /// <summary>The digest of the README's prompt.</summary>
    private const string PromptDigest = "sha256:41ae5b247f8e1f5897930c8b4c69212ae889c224f558c5580b0c84968894e77b";

    /// <summary>The digest of the install page's section for an agent, as <see cref="Agent"/> reads it.</summary>
    private const string AgentSectionDigest = "sha256:8627ca8b26522a2fdc78a61af7980bc1e80762617e93fbf68eae45f1b781a77f";

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

    /// <summary>The README's paste-in prompt: the fenced block under its own heading.</summary>
    private static string Prompt()
    {
        var readme = Page("README.md");
        var heading = readme.IndexOf("### Ask an agent to do it", StringComparison.Ordinal);
        Assert.True(heading >= 0, "the README no longer has the prompt's heading.");

        var open = readme.IndexOf("```text", heading, StringComparison.Ordinal);
        var close = readme.IndexOf("```", open + 7, StringComparison.Ordinal);
        Assert.True(open > heading && close > open, "the README's prompt is no longer a fenced block under its heading.");
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

    private static string Page(params string[] path)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine([directory.FullName, .. path])).Replace("\r\n", "\n", StringComparison.Ordinal);
    }
}
