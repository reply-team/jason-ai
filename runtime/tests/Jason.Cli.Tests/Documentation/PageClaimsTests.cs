using System.Security.Cryptography;
using System.Text;
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
public class PageClaimsTests
{
    /// <summary>
    /// The README prompt repairs required checks only. Its step 4 ran every check's repair, optional ones
    /// included: the autostart repair registers a runtime to start at logon and the harness repair writes into the
    /// agent's own configuration — both of which the prompt's own closing rule forbids.
    /// </summary>
    [Fact]
    public void The_prompt_repairs_required_checks_only()
    {
        var prompt = Prompt();

        Assert.Contains("For each required check that is not ok, run the repair it prints.", prompt, StringComparison.Ordinal);
        Assert.Contains("Optional checks are reported, not repaired", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("For each check that is not ok", prompt, StringComparison.Ordinal);
    }

    /// <summary>
    /// And names where the executable unpacks its native libraries on each platform: under the temporary directory
    /// on Windows, under <c>~/.net</c> on macOS and Linux — which is what CI's published executables report.
    /// </summary>
    [Fact]
    public void The_pages_name_where_the_executable_unpacks_on_each_platform()
    {
        Assert.Contains("~/.net", Prompt(), StringComparison.Ordinal);
        Assert.Contains("under\n`~/.net` on macOS and Linux", Page("docs", "INSTALL.md"), StringComparison.Ordinal);

        var release = Page("docs", "release-and-update.md");
        Assert.Contains("`~/.net/jason/<hash>/`", release, StringComparison.Ordinal);
        Assert.DoesNotContain("/tmp/.net/jason", release, StringComparison.Ordinal);
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
        + "plugin status, and journal and work-item metadata without values, with the home directory and the account's names rewritten; "
        + "journal values travel only on an explicit flag with a printed warning; it reaches no network and is never uploaded; it is listed before it is written. "
        + "`jason logs` reads the installation's files and asks the runtime nothing; `jason doctor` asks the runtime only through its API and never opens the database; "
        + "they are the fifth and sixth declared verb-map exceptions. |";

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

        Assert.Contains(Flat(DoctorComposer.JournalCarries), text, StringComparison.Ordinal);
        Assert.DoesNotContain("contact fields, decisions", text, StringComparison.Ordinal);
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
    /// A build from source leaves debugging symbols beside the executable, and §3 says to copy the executable alone.
    /// A directory that holds the <c>.pdb</c> files as well is not one the uninstall takes for Jason's own, so a
    /// reader who copied the whole publish folder would keep both its PATH entry and the directory. The golden path
    /// builds from source too, and said to put the executable "somewhere on your PATH".
    /// </summary>
    [Theory]
    [InlineData("docs/INSTALL.md", "Copy that file alone: the `.pdb` files beside it are debugging symbols")]
    [InlineData("docs/golden-path.md", "copy the single file it produces alone into a directory of its own")]
    public void The_pages_say_to_copy_the_executable_alone(string page, string said)
    {
        var text = Flat(Page(page.Split('/')));

        Assert.Contains(said, text, StringComparison.Ordinal);
        Assert.DoesNotContain("somewhere on your `PATH`", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// The prompt needs no connector, and a session started with the account's connectors offers tools the install
    /// never asked for. That is said to the person who starts the session, above the prompt, so the prompt itself —
    /// the text real hosts were measured reading — does not change by a byte.
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

    /// <summary>The prompt, byte for byte, is the one the real-host runs read; its digest is held here.</summary>
    [Fact]
    public void The_install_prompt_is_the_text_the_hosts_read_and_names_no_connector()
    {
        var prompt = Fenced(Page("README.md"), "```text\nInstall Jason on this machine");
        Assert.DoesNotContain("connector", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(PromptDigest, "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(prompt))));
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

    /// <summary>The digest of the README's prompt as the real-host runs read it.</summary>
    private const string PromptDigest = "sha256:725e55a1b19d56e258aeb3ad16402c60521b36bbea8ea50b0b2230ef71850b7c";

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
