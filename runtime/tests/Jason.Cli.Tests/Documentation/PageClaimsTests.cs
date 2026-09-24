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

    /// <summary>Four names have no API operation behind them, and no page says two.</summary>
    [Theory]
    [InlineData("README.md")]
    [InlineData("docs/INSTALL.md")]
    [InlineData("CLAUDE.md")]
    public void No_page_counts_fewer_names_without_an_operation_than_the_code_declares(string page)
    {
        var text = Page(page.Split('/'));

        Assert.DoesNotContain("two commands", text, StringComparison.Ordinal);
        Assert.DoesNotContain("two declared exceptions", text, StringComparison.Ordinal);
        Assert.DoesNotContain("the only command here", text, StringComparison.Ordinal);
        Assert.Contains("four", text, StringComparison.Ordinal);
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

    /// <summary>The sixth sentence a release would have made false, in both places it was written.</summary>
    [Theory]
    [InlineData("skills/README.md")]
    [InlineData("runtime/src/Jason.Cli/Skills/SkillsSource.cs")]
    public void No_page_says_git_is_today_the_only_way_skills_can_work(string page) =>
        Assert.DoesNotContain("the only way it can work at all", Page(page.Split('/')), StringComparison.Ordinal);

    /// <summary>
    /// The skill T24-R reads says what the verb's exit code now means: 1 when it did not get there, with every step
    /// listed either way — not "understood and refused", which predated the report written after a failure.
    /// </summary>
    [Fact]
    public void The_operating_skill_states_the_uninstall_exit_code_as_it_is()
    {
        var skill = Page("skills", "runtime", "operating-the-installation", "SKILL.md");

        Assert.DoesNotContain("1 when it understood and refused", skill, StringComparison.Ordinal);
        Assert.Contains("stopped part-way", skill, StringComparison.Ordinal);
        Assert.Contains("every step that did happen is listed either way", skill, StringComparison.Ordinal);
    }

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
