using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Jason.Contracts.Update;

namespace Jason.Runtime.Tests.Documentation;

/// <summary>
/// The install one-liners fetch the script from a release, never from the default branch: a script on `main` can be
/// ahead of every release, and a pinned install must run the script that release was published with. One list of
/// names, <see cref="ReleaseAssets.Published"/>, is what every page, the template, the workflow and the code agree on.
/// </summary>
public sealed partial class ScriptSwitchTests
{
    /// <summary>Every file that hands a person or an agent an install line.</summary>
    private static readonly string[] Seven =
    [
        "README.md",
        "docs/INSTALL.md",
        "docs/release-and-update.md",
        ".github/release-notes-template.md",
        "skills/runtime/operating-the-installation/SKILL.md",
        "install/install.sh",
        "install/install.ps1",
    ];

    [Fact]
    public void Every_release_download_named_anywhere_is_a_name_a_release_publishes()
    {
        var seen = 0;
        foreach (var file in Seven)
        {
            foreach (Match url in Download().Matches(Source(file)))
            {
                seen++;
                Assert.True(
                    ReleaseAssets.Published.Contains(url.Groups["name"].Value),
                    $"{file}: '{url.Value}' downloads '{url.Groups["name"].Value}', which no release publishes.");
            }
        }

        // A detector that matched nothing would pass the loop above on any page: the pages name a release download
        // twenty times today, so fewer than half that means the detector, not the pages, changed.
        Assert.True(seen >= 10, $"the detector found {seen} release download(s) in {Seven.Length} files");
    }

    /// <summary>
    /// The detector, before the pages: the pinned forms the template and the pages write each yield the script's
    /// name, and a script's own <c>releases/download/v$VERSION</c> base, which a variable completes, yields none.
    /// </summary>
    [Theory]
    [InlineData("curl -fsSL https://github.com/reply-team/jason-ai/releases/latest/download/install.sh | sh", "install.sh")]
    [InlineData("irm https://github.com/reply-team/jason-ai/releases/download/v0.1.0/install.ps1 | iex", "install.ps1")]
    [InlineData("curl -fsSL https://github.com/reply-team/jason-ai/releases/download/v{{version}}/install.sh | sh", "install.sh")]
    [InlineData("https://github.com/reply-team/jason-ai/releases/download/v1.0.0-rc.1/manifest.json", "manifest.json")]
    [InlineData("curl -fsSL https://jasonharness.ai/releases/download/v{{version}}/install.sh | sh", "install.sh")]
    [InlineData("irm https://jasonharness.ai/releases/latest/download/install.ps1 | iex", "install.ps1")]
    [InlineData("BASE=\"$RELEASES_URL/download/v$VERSION\"", null)]
    [InlineData("$base = \"$releasesUrl/latest/download\"", null)]
    public void The_download_detector_reads_the_asset_a_line_names(string line, string? name)
    {
        var found = Download().Matches(line).Select(match => match.Groups["name"].Value).ToList();

        if (name is null)
        {
            Assert.Empty(found);
        }
        else
        {
            Assert.Equal([name], found);
        }
    }

    [Fact]
    public void A_pinned_install_fetches_the_script_of_the_release_it_installs_and_says_which()
    {
        var pinned = 0;
        foreach (var file in Seven)
        {
            foreach (var line in Source(file).Split('\n').Where(l => Pinned().IsMatch(l)))
            {
                pinned++;
                var tag = Pinned().Match(line).Groups["version"].Value;
                Assert.True(
                    line.Contains($"--version {tag}", StringComparison.Ordinal) || line.Contains($"-Version {tag}", StringComparison.Ordinal),
                    $"{file}: '{line.Trim()}' fetches the script of v{tag} without asking for {tag}");
            }
        }

        // INSTALL's pair and the template's pair at least: a guard that finds no pinned line holds nothing.
        Assert.True(pinned >= 4, $"only {pinned} pinned install lines were found.");
    }

    [Fact]
    [Critical(Family.I1, "installs are pinned to a release")]
    public void No_line_anywhere_installs_from_the_default_branch()
    {
        // Built from parts, so this file is not itself an offender.
        var raw = string.Concat("raw.githubusercontent.com/reply-team/jason-ai/", "main/install/");
        var offenders = TextFiles().Where(f => File.ReadAllText(f).Contains(raw, StringComparison.Ordinal)).ToList();
        Assert.Empty(offenders);
    }

    [Fact]
    public void The_workflow_attaches_exactly_what_a_release_publishes()
        => Assert.Equal(ReleaseAssets.Published, ReleaseAssetList());

    [Fact]
    public void The_template_s_assets_table_lists_exactly_what_a_release_publishes()
        => Assert.Equal(
            ReleaseAssets.Published.Order(StringComparer.Ordinal),
            AssetRows(Source(".github/release-notes-template.md"), "## Assets").Order(StringComparer.Ordinal));

    [Fact]
    public void The_page_s_table_of_published_files_lists_exactly_what_a_release_publishes()
        => Assert.Equal(
            ReleaseAssets.Published.Order(StringComparer.Ordinal),
            AssetRows(Source("docs/release-and-update.md"), "## 2. The files a release publishes").Order(StringComparer.Ordinal));

    /// <summary>
    /// The fixture under install/feeds/schema-1 is 0.1.0's feed layout with the values taken out. Filled with the values
    /// v0.1.0 published, it is v0.1.0's two files again, byte for byte — which is what makes a CI install against it a
    /// proof that today's scripts still read the feeds a published tag can point them at.
    /// </summary>
    [Fact]
    public void The_schema_1_fixture_filled_with_0_1_0_s_values_is_0_1_0_s_feed()
    {
        var filled = FeedFixture.Fill(Source("install/feeds/schema-1/manifest.json"), Source("install/feeds/schema-1/checksums.txt"), V010.Values);
        Assert.Equal(V010.Manifest, filled.Manifest);
        Assert.Equal(V010.Checksums, filled.Checksums);
    }

    /// <summary>A placeholder the values do not name is refused rather than left in a feed an installer then reads.</summary>
    [Fact]
    public void A_fixture_filled_without_one_of_its_values_is_refused()
    {
        var values = V010.Values.Where(pair => pair.Key != "size:osx-arm64").ToDictionary();
        var refused = Assert.Throws<InvalidOperationException>(() =>
            FeedFixture.Fill(Source("install/feeds/schema-1/manifest.json"), Source("install/feeds/schema-1/checksums.txt"), values));
        Assert.Contains("size:osx-arm64", refused.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The script CI runs and its twin here fill the same placeholders with the same values: run over three
    /// archives of a test's own, the script's two files are the twin's, and the archives are copied beside them.
    /// </summary>
    [Fact]
    public void The_fill_script_writes_what_its_twin_writes()
    {
        using var tree = new TempTree();
        var archives = tree.NewDirectory("archives");
        var values = new Dictionary<string, string> { ["version"] = "0.2.0", ["published_at"] = "2026-09-30T12:00:00Z" };
        foreach (var (rid, index) in ReleaseAssets.Rids.Select((rid, index) => (rid, index)))
        {
            var bytes = Encoding.ASCII.GetBytes(new string((char)('a' + index), 100 + index));
            File.WriteAllBytes(Path.Combine(archives, ReleaseAssets.For(rid)), bytes);
            values[$"sha256:{rid}"] = Convert.ToHexStringLower(SHA256.HashData(bytes));
            values[$"size:{rid}"] = bytes.Length.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        var output = Path.Combine(tree.Root, "feed");
        var (exit, _, stderr) = Pwsh(
            "-File", Path.Combine(RepositoryRoot(), ".github", "scripts", "Fill-FeedFixture.ps1"),
            "-Fixture", Path.Combine(RepositoryRoot(), "install", "feeds", "schema-1"),
            "-Archives", archives,
            "-Version", "0.2.0",
            "-PublishedAt", "2026-09-30T12:00:00Z",
            "-Out", output);
        Assert.True(exit == 0, stderr);

        var twin = FeedFixture.Fill(Source("install/feeds/schema-1/manifest.json"), Source("install/feeds/schema-1/checksums.txt"), values);
        Assert.Equal(twin.Manifest, File.ReadAllText(Path.Combine(output, ReleaseAssets.Manifest)));
        Assert.Equal(twin.Checksums, File.ReadAllText(Path.Combine(output, ReleaseAssets.Checksums)));
        foreach (var rid in ReleaseAssets.Rids)
        {
            Assert.Equal(
                File.ReadAllBytes(Path.Combine(archives, ReleaseAssets.For(rid))),
                File.ReadAllBytes(Path.Combine(output, ReleaseAssets.For(rid))));
        }

        // And what it wrote is a manifest this build reads, naming the version it was told.
        Assert.Equal("0.2.0", UpdateManifest.Read(twin.Manifest).Version.ToString());
    }

    [GeneratedRegex(@"releases/(?:latest/)?download/(?:v(?:[0-9][^/]*|\{\{version\}\})/)?(?<name>[A-Za-z0-9._-]+)(?![A-Za-z0-9._$-])")]
    private static partial Regex Download();

    [GeneratedRegex(@"releases/download/v(?<version>[0-9]+\.[0-9]+\.[0-9]+|\{\{version\}\})/install\.(?:sh|ps1)")]
    private static partial Regex Pinned();

    [GeneratedRegex(@"'([^']+)'")]
    private static partial Regex Quoted();

    /// <summary>
    /// The names the release workflow attaches, in its order: the single-quoted paths of its <c>$assets</c> line,
    /// each by its file name, which is the name <c>gh release create</c> gives an asset.
    /// </summary>
    private static IReadOnlyList<string> ReleaseAssetList()
    {
        var line = Assert.Single(
            Source(".github/workflows/release.yml").Split('\n'),
            l => l.TrimStart().StartsWith("$assets = ", StringComparison.Ordinal));
        return [.. Quoted().Matches(line).Select(match => Path.GetFileName(match.Groups[1].Value))];
    }

    /// <summary>The first cell of every row of the table under a heading, without its backticks.</summary>
    private static IReadOnlyList<string> AssetRows(string page, string heading)
    {
        var start = page.IndexOf(heading + "\n", StringComparison.Ordinal);
        Assert.True(start >= 0, $"the page has no heading '{heading}'.");

        return
        [
            .. page[start..].Split('\n').Skip(1)
                .SkipWhile(line => !line.StartsWith('|'))
                .TakeWhile(line => line.StartsWith('|'))
                .Skip(2)
                .Select(line => line.Split('|', StringSplitOptions.TrimEntries)[1].Trim('`')),
        ];
    }

    /// <summary>
    /// Every text file the repository tracks, as git lists them: what a clone carries, and nothing a working copy
    /// happens to hold beside it.
    /// </summary>
    private static IEnumerable<string> TextFiles()
    {
        string[] text = [".md", ".yml", ".yaml", ".sh", ".ps1", ".cs", ".json", ".js", ".txt", ".csproj", ".props"];

        var start = new ProcessStartInfo("git")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            WorkingDirectory = RepositoryRoot(),
        };
        start.ArgumentList.Add("ls-files");
        start.ArgumentList.Add("-z");

        using var git = Process.Start(start)!;
        var listed = git.StandardOutput.ReadToEnd();
        git.WaitForExit();
        Assert.True(git.ExitCode == 0, "git ls-files did not answer: " + git.StandardError.ReadToEnd());

        var files = listed.Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Where(file => text.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
            .Select(file => Path.Combine(RepositoryRoot(), file.Replace('/', Path.DirectorySeparatorChar)))
            .Where(File.Exists)
            .ToList();
        Assert.True(files.Count > 100, $"git listed only {files.Count} text files, so the scan would prove nothing.");
        return files;
    }

    private static (int Exit, string Stdout, string Stderr) Pwsh(params string[] args)
    {
        var pwsh = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .SelectMany(directory => new[] { Path.Combine(directory, "pwsh.exe"), Path.Combine(directory, "pwsh") })
            .FirstOrDefault(File.Exists);
        Assert.SkipWhen(pwsh is null, "pwsh is not on this machine's PATH; every CI runner has it, and the fill script only ever runs there.");

        var start = new ProcessStartInfo(pwsh)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        // PowerShell reports telemetry over the network unless told not to, and no test in this repository
        // reaches the network.
        start.Environment["POWERSHELL_TELEMETRY_OPTOUT"] = "1";
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        foreach (var argument in args)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(TimeSpan.FromSeconds(120)))
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail("pwsh did not finish in two minutes.");
        }

        return (process.ExitCode, stdout.Result, stderr.Result);
    }

    /// <summary>One of this repository's files, with the line endings git checks it out with made LF.</summary>
    private static string Source(string relativePath) =>
        File.ReadAllText(Path.Combine(RepositoryRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar))).Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string RepositoryRoot()
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

/// <summary>
/// The C# twin of <c>.github/scripts/Fill-FeedFixture.ps1</c>: every <c>{{name}}</c> in the fixture replaced by its
/// value, and a placeholder with no value refused.
/// </summary>
internal static partial class FeedFixture
{
    public static (string Manifest, string Checksums) Fill(string manifest, string checksums, IReadOnlyDictionary<string, string> values)
        => (Replace(manifest, values), Replace(checksums, values));

    private static string Replace(string template, IReadOnlyDictionary<string, string> values)
        => Placeholder().Replace(template, match => values.TryGetValue(match.Groups[1].Value, out var value)
            ? value
            : throw new InvalidOperationException($"The fixture names '{{{{{match.Groups[1].Value}}}}}', and no value was given for it."));

    [GeneratedRegex(@"\{\{([a-z0-9_]+(?::[a-z0-9-]+)?)\}\}")]
    private static partial Regex Placeholder();
}

/// <summary>v0.1.0's two feed files as the release published them (public release data), and the values in them.</summary>
internal static class V010
{
    public static IReadOnlyDictionary<string, string> Values { get; } = new Dictionary<string, string>
    {
        ["version"] = "0.1.0",
        ["published_at"] = "2026-09-27T02:22:37Z",
        ["sha256:win-x64"] = "29910aa9c0e2368a663420e0dc64563aaba7139dcf506dfca76607431a1b7729",
        ["size:win-x64"] = "51504437",
        ["sha256:linux-x64"] = "24bbb3d7de8fda566c13c2ae4c242fda3b4051a98ade5a33f0fb32925a3894e1",
        ["size:linux-x64"] = "49126807",
        ["sha256:osx-arm64"] = "0bd1e6d7c6b15660aa333fee2396c39edea59e4dd8fe678fdeba5c4faeec43b9",
        ["size:osx-arm64"] = "47331628",
    };

    public const string Manifest = """
        {
          "schema": 1,
          "version": "0.1.0",
          "published_at": "2026-09-27T02:22:37Z",
          "release_notes_url": "https://github.com/reply-team/jason-ai/releases/tag/v0.1.0",
          "min_upgrade_from": null,
          "artifacts": {
            "win-x64": {
              "asset": "jason-win-x64.zip",
              "sha256": "29910aa9c0e2368a663420e0dc64563aaba7139dcf506dfca76607431a1b7729",
              "size": 51504437
            },
            "linux-x64": {
              "asset": "jason-linux-x64.tar.gz",
              "sha256": "24bbb3d7de8fda566c13c2ae4c242fda3b4051a98ade5a33f0fb32925a3894e1",
              "size": 49126807
            },
            "osx-arm64": {
              "asset": "jason-osx-arm64.tar.gz",
              "sha256": "0bd1e6d7c6b15660aa333fee2396c39edea59e4dd8fe678fdeba5c4faeec43b9",
              "size": 47331628
            }
          }
        }

        """;

    public const string Checksums = """
        29910aa9c0e2368a663420e0dc64563aaba7139dcf506dfca76607431a1b7729  jason-win-x64.zip
        24bbb3d7de8fda566c13c2ae4c242fda3b4051a98ade5a33f0fb32925a3894e1  jason-linux-x64.tar.gz
        0bd1e6d7c6b15660aa333fee2396c39edea59e4dd8fe678fdeba5c4faeec43b9  jason-osx-arm64.tar.gz

        """;
}
