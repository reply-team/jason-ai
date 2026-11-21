using System.Text.RegularExpressions;

namespace Jason.Cli.Tests.Documentation;

/// <summary>
/// The way back from 0.49 that the update page gives, "### Going back from 0.49": take Jason off keeping the data
/// directory, install 0.48.7 by its pinned line, put back what the uninstall took. Each row holds one of its claims to
/// what makes it true, so the section goes red the day the claim stops being so.
/// </summary>
public partial class PageClaimsTests
{
    /// <summary>The release the section goes back to.</summary>
    private const string GoingBackTo = "0.48.7";

    /// <summary>The newest migration 0.48.7 shipped: a database 0.49 opened is one 0.48.7 opens only while no later one exists.</summary>
    private const string GoingBackToNewestMigration = "20261116053811_DecisionThread";

    /// <summary>
    /// The newest line of each package's digest list at v0.48.7, the version and the digest of the package that release
    /// published, for every package but Reply's. A plugin 0.49 placed is one 0.48.7 runs as it is while it is the same
    /// package. Reply's is the one that moves in 0.49: its line is the newest of the tree, the 0.19.0 this release opened,
    /// which 0.48.7 reads as it is because it adds no manifest key, and the section says so. Its digest is rewritten with
    /// the package until the release ships, and this line with it.
    /// </summary>
    private static readonly (string Plugin, string Line)[] GoingBackToPackages =
    [
        ("journal-forwarder", "1.7.0 sha256:81e778ca4083860bbf6b203348963f5fa3f4745cc47dcafd93fd94962b73fae8"),
        ("ntfy", "1.0.3 sha256:d50982cc87d84c724eab1ae6bc216daed0d11055b0bd5e4592a88e8870f5d87f"),
        ("reply", "0.19.0 sha256:4cf0b393e585ce886e284ec865f08c76b6b09f7d8fe1649397789ea725dff9a8"),
        ("slack", "1.0.0 sha256:cba4bc14aa70fd27feceb24ebdb608149fe4648ff35fde4940927c2133e448c9"),
        ("telegram", "1.0.0 sha256:808fcbbd7cc58860ed128a6ed98d348ad2dbf1b6c11d5e89fcba6a12376cdaa9"),
    ];

    /// <summary>
    /// The section's commands, in their order: the uninstall, the two pinned install lines, and what is put back. The
    /// two install lines are the install scripts' own pinned forms, read from the scripts, with the release this tree
    /// builds replaced by 0.48.7, and nothing added: an option that keeps the PATH unchanged would leave the new
    /// terminal of step 2 without <c>jason</c>.
    /// </summary>
    [Fact]
    public void Going_back_from_0_49_installs_0_48_7_by_the_scripts_own_pinned_lines()
    {
        var section = GoingBack();
        var builds = TreeVersion.Prefix();

        var windows = PinnedLine(Page("install", "install.ps1"), $"releases/download/v{builds}/install.ps1");
        var mac = PinnedLine(Page("install", "install.sh"), $"releases/download/v{builds}/install.sh");
        Assert.StartsWith("& ([scriptblock]::Create((irm ", windows, StringComparison.Ordinal);
        Assert.StartsWith("curl -fsSL ", mac, StringComparison.Ordinal);

        string[] expected =
        [
            "jason uninstall",
            windows.Replace(builds, GoingBackTo, StringComparison.Ordinal),
            mac.Replace(builds, GoingBackTo, StringComparison.Ordinal),
            "jason skills install",
            "jason ui install",
            "jason runtime start",
        ];
        Assert.Equal(expected, FencedLines(section).Select(line => line.Trim()));
        Assert.DoesNotContain("-NoModifyPath", section, StringComparison.Ordinal);
        Assert.DoesNotContain("--no-modify-path", section, StringComparison.Ordinal);
        Assert.Contains(
            "and `jason runtime autostart enable` where the runtime started at logon before.", Flat(section), StringComparison.Ordinal);
        Assert.Contains($"[0.48.7's release page](https://get.jasonharness.ai/releases/tag/v{GoingBackTo})", Flat(section), StringComparison.Ordinal);
    }

    /// <summary>
    /// Step 1 says the uninstall keeps the data directory and the harness root. That is what §9 says the verb does without
    /// <c>--purge-data</c>, and what the verb's own help says, so the step, the section and the verb are one claim.
    /// </summary>
    [Fact]
    public async Task Going_back_from_0_49_keeps_what_the_uninstall_keeps()
    {
        var section = Flat(GoingBack());
        Assert.Contains("It removes Jason Desktop, the terminal command, the logon registration and the skills it deployed (§9), and keeps `~/.jason` and the harness root.", section, StringComparison.Ordinal);
        Assert.DoesNotContain("--purge-data", section, StringComparison.Ordinal);

        var off = Flat(Section(Page("docs", "release-and-update.md"), "## 9. "));
        Assert.Contains("**The data directory is kept** — §10 below says what is in it — unless `--purge-data` says otherwise", off, StringComparison.Ordinal);
        Assert.Contains("and the harness root with every folder and file in it, the person's memory files and every project's and campaign's folder ([projects](projects.md) §8), `--purge-data` included.", off, StringComparison.Ordinal);

        using var dir = new TempPaths();
        var output = new StringWriter();
        await CliApp.RunAsync(["uninstall", "--help"], new CliEnvironment(output, new StringWriter(), dir.Paths), TestContext.Current.CancellationToken);
        var help = Flat(output.ToString());
        Assert.Contains("Kept unless --purge-data: the data directory", help, StringComparison.Ordinal);
        Assert.Contains("the data directory kept unless --purge-data, the harness root never", help, StringComparison.Ordinal);
    }

    /// <summary>
    /// "0.49 adds no migration to the database": the newest migration this tree compiles is the newest 0.48.7 shipped. A
    /// migration added in 0.49 turns this red, and the section with it, since 0.48.7 refuses data a newer Jason migrated.
    /// </summary>
    [Fact]
    public void Going_back_from_0_49_holds_while_0_49_adds_no_migration()
    {
        Assert.Contains("0.49 adds no migration to the database and keeps the layout 0.48.7 runs, so 0.48.7 opens the same data as it is, and there is no backup to put back.", Flat(GoingBack()), StringComparison.Ordinal);

        var migrations = Directory.GetFiles(Path.Combine(Root(), "dotnet", "src", "Jason.Runtime", "Persistence", "Migrations"), "*.Designer.cs")
            .Select(file => MigrationId().Match(File.ReadAllText(file)))
            .Where(match => match.Success)
            .Select(match => match.Groups["id"].Value)
            .Order(StringComparer.Ordinal)
            .ToList();
        Assert.NotEmpty(migrations);
        Assert.Equal(GoingBackToNewestMigration, migrations[^1]);
    }

    /// <summary>
    /// "plugins and their grants are data 0.48.7 reads as it is": each package's newest version and digest is the one
    /// v0.48.7 published, Reply's aside, which the section names as the newer one 0.48.7 reads as it is, and there is no
    /// package 0.48.7 did not have. Any other package that moves in 0.49 turns this red, and so does a Reply package that
    /// moves without this list and the section.
    /// </summary>
    [Fact]
    public void Going_back_from_0_49_holds_while_every_package_is_the_one_0_48_7_shipped()
    {
        Assert.Contains("Nothing in the data directory is lost: campaigns, the journal, settings, plugins and their grants are data 0.48.7 reads as it is.", Flat(GoingBack()), StringComparison.Ordinal);
        Assert.Contains("The Reply package is the one exception: it stays the newer one, 0.19.0, which 0.48.7 reads as it is, since it adds no manifest key, and which needs reply-cli 0.22.0 or newer, as this release already asks.", Flat(GoingBack()), StringComparison.Ordinal);

        var newest = Directory.GetFiles(Path.Combine(Root(), "plugins"), "*.digests")
            .Select(file => (Plugin: Path.GetFileNameWithoutExtension(file), Line: File.ReadAllLines(file).Where(line => line.Length > 0 && !line.StartsWith('#')).Last().Trim()))
            .OrderBy(package => package.Plugin, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(GoingBackToPackages, newest);
    }

    /// <summary>
    /// Before step 1, a folder of the person's own under a name Jason ships is moved out of the way of step 3. The section
    /// quotes the words <c>jason status</c> names such a folder with, in its harness_skills check, read from the record
    /// that step 1 takes away: the words in the section are the check's own, so the section goes red the day they change.
    /// </summary>
    [Fact]
    public void Going_back_from_0_49_moves_a_folder_of_yours_out_of_the_way_before_0_48_7_deploys()
    {
        var section = Flat(GoingBack());
        var said = section.IndexOf("Before step 1, run `jason status`.", StringComparison.Ordinal);
        Assert.True(said >= 0 && said < section.IndexOf("1. Quit Jason Desktop", StringComparison.Ordinal), "the section does not say, before step 1, to read jason status.");
        Assert.Contains(
            "Before step 1, run `jason status`. Where it names a folder in an agent's skills folder as one \"Jason did not deploy, under a name Jason ships, left as it is\", move that folder out of the skills folder before step 3 and put it back after step 3. 0.49 leaves such a folder as it is; 0.48.7's `jason skills install` deploys its own skill over a folder of the same name and records it as its own, and 0.48.7's uninstall removes what it recorded. A folder put back after step 3 differs from what 0.48.7 recorded, and 0.48.7 keeps a file it finds changed since it wrote it.",
            section,
            StringComparison.Ordinal);

        var checks = Page("dotnet", "src", "Jason.Cli", "Status", "StatusChecks.cs");
        Assert.Contains("? $\"{paths} is a {what} Jason did not deploy, under a name Jason ships, left as it is: ", checks, StringComparison.Ordinal);
        Assert.Contains("if (LeftAloneStanding(root, standing, HostsReading(root)) is { } said)", checks, StringComparison.Ordinal);
    }

    /// <summary>
    /// "Under 0.48.7, `jason update apply` brings 0.49 back and moves the install into Jason Desktop again": the uninstall
    /// leaves the move's record, which says done, so the sentence holds only while a done record whose application is gone
    /// and whose install path holds a command-line executable again does not stand, and is a move to make again.
    /// </summary>
    [Fact]
    public void Going_back_from_0_49_leaves_a_done_move_record_that_no_longer_stands()
    {
        Assert.Contains("Under 0.48.7, `jason update apply` brings 0.49 back and moves the install into Jason Desktop again,", Flat(GoingBack()), StringComparison.Ordinal);

        using var dir = new TempPaths();
        var from = Path.Combine(dir.Paths.Root, "bin", "jason");
        var application = Path.Combine(dir.Paths.Root, "Applications", "Jason.app");
        Directory.CreateDirectory(Path.GetDirectoryName(from)!);
        File.WriteAllBytes(from, new byte[4096]);
        var record = new Jason.Cli.Update.MoveRecord("0.49.0", from, Jason.Cli.Update.MoveStates.Done, Jason.Cli.Update.MoveSteps.Started, null, application, null, null, DateTimeOffset.UnixEpoch);

        Assert.False(record.DoneAndStanding(windows: false));
    }

    /// <summary>The update page's "### Going back from 0.49", up to the next heading.</summary>
    private static string GoingBack()
    {
        const string Heading = "\n### Going back from 0.49\n";
        var page = Page("docs", "release-and-update.md");
        var heading = page.IndexOf(Heading, StringComparison.Ordinal);
        Assert.True(heading >= 0, "docs/release-and-update.md has no section for going back from 0.49.");

        var next = NextHeading().Match(page, heading + Heading.Length);
        return page[heading..(next.Success ? next.Index : page.Length)];
    }

    /// <summary>The one line of a script that holds <paramref name="address"/>, as a comment or help line, without its indent or mark.</summary>
    private static string PinnedLine(string script, string address)
    {
        var line = Assert.Single(script.Split('\n'), candidate => candidate.Contains(address, StringComparison.Ordinal));
        return line.Trim().TrimStart('#').Trim();
    }

    [GeneratedRegex(@"\[Migration\(""(?<id>[0-9]{14}_[A-Za-z0-9]+)""\)\]")]
    private static partial Regex MigrationId();
}
