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
    [InlineData("docs/INSTALL.md", "with `LICENSE` and `THIRD-PARTY-NOTICES` beside it, named `jason-LICENSE` and `jason-THIRD-PARTY-NOTICES`")]
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
    /// What auto mode does at the install depends on whether anything can ask the person. A session that can ask asks
    /// before the download runs, and with the person's yes the install goes on; a session nothing can ask — one run
    /// headless — refuses the download and says so. So the page no longer says auto mode stops the install at its first
    /// step, which is true only of the second. It says to expect either, that both are right, what the person does about
    /// each, and that a mode asking before every command is the other way, with one agent's spelling of it as the example.
    /// </summary>
    [Fact]
    public void The_readme_says_what_auto_mode_does_at_the_install_above_the_prompt()
    {
        var readme = Page("README.md");
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
        var oneLiner = install.IndexOf("irm https://jasonharness.ai/releases/latest/download/install.ps1 | iex", StringComparison.Ordinal);
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
    /// runtime started, the stop asked first, and why on each platform. <c>jason update check</c>'s one command leaves
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
            "Ask before you run `jason runtime stop`: nothing new is claimed from then on, what is in flight finishes or ends at its own lease, and a stop does not take back a send already under way.",
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
    /// A ready installation is not the end of what the person came for: the agent says it can work, and that one word
    /// starts its first run in the same session.
    /// </summary>
    [Fact]
    public void The_agent_section_points_at_the_first_run_after_ready() =>
        Assert.Contains(
            "When `ready` is true, tell the person Jason is installed and can work, and that one word from them starts [its first run](golden-path.md) in this session.",
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
            "Jason is downloaded from its releases at <https://jasonharness.ai/releases/>, which redirect to <https://github.com/reply-team/jason-ai/releases>; install it from there, or with the package section 2 names for this platform.",
            Flat(Prompt()),
            StringComparison.Ordinal);

    /// <summary>
    /// The agent's report at the end names the next step, so an install never ends on whether it worked alone: after a
    /// ready installation the next step is the first run, and after one that is not ready it is the step that remains.
    /// </summary>
    [Fact]
    public void The_install_prompt_asks_for_the_next_step() =>
        Assert.Contains(
            "At the end, tell me whether Jason can start work, what the next step is, and exactly what you ran and what it changed.",
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
    /// The install page ends where the first run begins: §6's last paragraph links the page that walks it, so a person who
    /// followed the install to its end — or an agent that did it for them — knows what comes next.
    /// </summary>
    [Fact]
    public void The_install_page_ends_with_the_first_run()
    {
        var start = Section(Page("docs", "INSTALL.md"), "## 6. ").TrimEnd().TrimEnd('-').TrimEnd();
        var last = start[(start.LastIndexOf("\n\n", StringComparison.Ordinal) + 2)..];

        Assert.Contains("](golden-path.md)", last, StringComparison.Ordinal);
        Assert.Contains("one word from you", Flat(last), StringComparison.Ordinal);
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
    /// Each act of the first run that is the person's carries its mark where it is described: installing Node is a system
    /// package, theirs to allow; the sign-in is theirs alone, so the key never passes through an agent; so is making the
    /// sequence the run uses; the approval is their decision; and of the two ways back, deleting the sequence is theirs
    /// and taking themselves out of it asks for their word.
    /// </summary>
    [Theory]
    [InlineData("### The Reply CLI", "Where Node is missing, or older than 20", 0, "Ask first.")]
    [InlineData("### The Reply CLI", "```\nreply auth login\n", -1, "Yours to do.")]
    [InlineData("## 6. ", "Make a sequence at Reply for this run: add nobody to it, and never start it.", 0, "Yours to do.")]
    [InlineData("## 7. ", "```\njason approval list\n", -1, "Ask first.")]
    [InlineData("### The way back", "Delete the trial sequence at Reply.", 0, "Yours to do.")]
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
    /// A stranger has no checkout, so the package is copied from the tree <c>jason skills install</c> already fetched: this
    /// repository at the runtime's own tag, kept in the data directory. Where that tree is not there, the release's tree is
    /// fetched into a temporary directory first. Neither is a copy out of a checkout.
    /// </summary>
    [Fact]
    public void The_golden_path_copies_the_package_from_the_staged_tree()
    {
        var page = Page("docs", "golden-path.md");
        var step = Section(page, "## 3. ");

        Assert.Contains("cp -r ~/.jason/skills/staged/v<version>/plugins/reply ~/.jason/plugins/reply", step, StringComparison.Ordinal);
        Assert.Contains("git clone --depth 1 --branch v<version> https://github.com/reply-team/jason-ai <temporary directory>", step, StringComparison.Ordinal);
        Assert.Contains("cp -r <temporary directory>/plugins/reply ~/.jason/plugins/reply", step, StringComparison.Ordinal);
        Assert.DoesNotContain("cp -r plugins/reply ~/.jason/plugins/reply", page, StringComparison.Ordinal);
    }

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

        Assert.Equal(5, inputs.Count);
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
    /// The first run enrols only into a sequence nobody has started and that holds nobody: the read of the sequence must
    /// say it is a draft, and the read of its people that their count is 0. Anything else — a started sequence, somebody
    /// already in it, a count the provider would not give — stops the run before anything is written.
    /// </summary>
    [Fact]
    public void The_golden_path_stops_on_anything_but_a_draft_sequence_and_an_empty_one()
    {
        var agent = Flat(GoldenAgent());

        Assert.Contains("`campaign.get` must answer `draft` before anything else. Any other status stops the run, and you say why.", agent, StringComparison.Ordinal);
        Assert.Contains("`enrollment.list` must answer a `total` of 0 before the enrolment. Any other count, null included, stops the run.", agent, StringComparison.Ordinal);
        Assert.Contains("The enrolment refuses a collision (`collision: refuse`), and `total` is 1 after it.", agent, StringComparison.Ordinal);
        Assert.Contains(
            "Anything else stops the run here: a sequence that has started, or that already holds somebody, is not one this run enrols into.",
            Flat(Section(Page("docs", "golden-path.md"), "## 6. ")),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// What keeps a first run from sending is that nothing in it starts the sequence: the page says so, tells an agent never
    /// to start one, and prints no command that would.
    /// </summary>
    [Fact]
    public void The_golden_path_never_starts_the_trial_sequence()
    {
        var page = Page("docs", "golden-path.md");

        Assert.Contains("A sequence nobody started sends nothing, and this run never starts one.", Flat(page), StringComparison.Ordinal);
        Assert.Contains("never run `campaign.activate`", Flat(GoldenAgent()), StringComparison.Ordinal);

        // The run does start something: Jason's own campaign, so its work can run. Said beside the command, so that
        // "this run never starts one" is not read as forbidding it, nor the command as starting the sequence.
        Assert.Contains(
            "`jason campaign start` starts Jason's own campaign, so the work below can run; your sequence at Reply stays unstarted.",
            Flat(Section(page, "## 6. ")),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            FencedLines(page),
            line => line.Contains("campaign.activate", StringComparison.Ordinal) || line.Contains("sequence start", StringComparison.Ordinal));
    }

    /// <summary>
    /// A first run that stops — at a read that said no, at a refusal — names what comes next, and the end of the run gives
    /// the person the way back.
    /// </summary>
    [Fact]
    public void The_golden_path_names_the_next_step_when_it_stops() =>
        Assert.Contains(
            "When you stop, name the next step. At the end, give the person the way back, in two lines.",
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
    /// the Reply CLI from npm's registry, and Node where it is missing — so the person's yes names its sources.
    /// </summary>
    [Fact]
    public void The_golden_path_asks_one_question_naming_its_sources_first() =>
        Assert.Contains(
            "Before anything, ask the person one question in plain words: what you will install and from where — the Reply CLI from npm's registry, and Node if it is missing;",
            Flat(GoldenAgent()),
            StringComparison.Ordinal);

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
    /// installation off again. And a package manager's own change to the PATH is part of what it was asked to do, as the
    /// one-liner's is when the person types it; only a PATH edit of its own is the person's to allow.
    /// </summary>
    [Fact]
    public void The_agent_section_does_section_2_then_4_to_6()
    {
        var agent = Flat(Agent());

        Assert.Contains("Do §2, then §4 to §6, in order, and stop there.", agent, StringComparison.Ordinal);
        Assert.Contains("§3 is for a person who has the runtime's source, which is not published here, and §7 takes the installation off again.", agent, StringComparison.Ordinal);
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

    /// <summary>
    /// The required check that the running runtime is the build this CLI is, in both pages' tables as the help lists it. A
    /// required check missing from the table an agent reads before it branches on <c>ready</c> is a <c>false</c> nobody
    /// told it could come.
    /// </summary>
    [Theory]
    [InlineData("README.md")]
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

    /// <summary>The digest of the README's prompt.</summary>
    private const string PromptDigest = "sha256:a218dac2de2fb843c275be0aeff7586ee6c99a5ceed033f63857941d04a162e7";

    /// <summary>The digest of the install page's section for an agent, as <see cref="Agent"/> reads it.</summary>
    private const string AgentSectionDigest = "sha256:1f35159aa82fe5f4c53ecbb02611a1dad4507b9f86f8ccb843e8fadbac0522df";

    /// <summary>The digest of the golden path's section for an agent, as <see cref="GoldenAgent"/> reads it.</summary>
    private const string GoldenAgentSectionDigest = "sha256:37ea25245d6df2823a4ad5d8358cfcfed5178bfa605104596d7cd96c33a51434";

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
