using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Jason.Cli;
using Jason.Contracts.Discovery;
using Jason.Contracts.Plugins;
using Jason.Contracts.Update;
using static Jason.Runtime.Tests.Documentation.Workflows;

namespace Jason.Runtime.Tests.Documentation;

/// <summary>
/// The plugins' packages as a release publishes them, each <c>jason-plugin-&lt;id&gt;.zip</c> of
/// <see cref="ReleaseAssets.Published"/>: written once per release by <c>.github/scripts/Write-PluginArchive.ps1</c> from
/// <c>plugins/&lt;id&gt;</c> at the tag. What holds an archive to its package is the package's content digest,
/// <see cref="PackageDigest"/>: unpacked, the archive has the digest of the directory it was made from, so what a person
/// installs from a release is the tag's package, byte for byte.
/// </summary>
/// <remarks>
/// The script is executed, not read, and its output is measured by the runtime's own digest rather than by a second
/// description of it. The release's pull request runs this suite on every platform on the tree the tag names, so the
/// fact holds of the very tree the release archives; nothing at the tag runs it again.
/// </remarks>
public sealed class PluginAssetTests
{
    private static readonly string[] ArchiveScript = [".github", "scripts", "Write-PluginArchive.ps1"];

    private static readonly Dictionary<string, string> NoEnvironment = [];

    private const string PackagePrefix = "jason-plugin-";

    private const string PackageSuffix = ".zip";

    /// <summary>
    /// Every plugin package a release publishes, by its file name: the <c>jason-plugin-&lt;id&gt;.zip</c> names of
    /// <see cref="ReleaseAssets.Published"/>, so a package the release starts to publish is a row here without a word of
    /// this class changing.
    /// </summary>
    public static TheoryData<string> PublishedPackages() =>
        [.. ReleaseAssets.Published.Where(name => name.StartsWith(PackagePrefix, StringComparison.Ordinal)
            && name.EndsWith(PackageSuffix, StringComparison.Ordinal))];

    /// <summary>The plugin id a published package's file name carries, which is its directory under <c>plugins/</c>.</summary>
    private static string IdOf(string asset) => asset[PackagePrefix.Length..^PackageSuffix.Length];

    /// <summary>
    /// The archive of each real package a release publishes holds its files and nothing else, at the root — no folder
    /// around them, no entry for a directory — in ordinal order, whatever order the file system lists them in, and
    /// unpacked into an empty directory it has the package's digest.
    /// </summary>
    [Theory]
    [MemberData(nameof(PublishedPackages))]
    public void The_archive_holds_exactly_the_package_s_files_at_its_root_and_unpacks_to_its_digest(string asset)
    {
        using var tree = new TempTree();
        var id = IdOf(asset);
        var source = Path.Combine(RepositoryRoot(), "plugins", id);
        var archive = WriteArchive(tree, source, asset);

        var entries = EntryNames(archive);
        var files = PackageDigest.Files(source);
        Assert.True(
            entries.SequenceEqual(files),
            $"The archive holds, in its order, {Listed(entries)}; the package is, in ordinal order, {Listed(files)}.");

        var unpacked = tree.NewDirectory(id);
        ZipFile.ExtractToDirectory(archive, unpacked);
        var expected = PackageDigest.Compute(source);
        var actual = PackageDigest.Compute(unpacked);
        Assert.True(
            actual.Digest == expected.Digest,
            $"Unpacked, {asset} is {actual.Digest} over {actual.FileCount} files; plugins/{id} is {expected.Digest} over {expected.FileCount}.");
    }

    /// <summary>
    /// What the digest does not count is not in the archive either: a path with a segment starting with a dot, and a
    /// directory with nothing in it. So nothing ships outside the package's identity. And what it does count is in it,
    /// including a file Windows marks hidden without a dot in its name. The real package holds none of these, so a
    /// package of the test's own is what puts the script's rule beside the digest's.
    /// </summary>
    [Fact]
    public void The_archive_leaves_out_what_the_digest_does_not_count()
    {
        using var tree = new TempTree();
        var source = tree.NewDirectory("example");
        foreach (var file in (string[])["plugin.yaml", "main.js", "modules/send.js", "modules/deep/read.js", "modules/marked.js", ".hidden", "modules/.cache/old.js", ".git/config"])
        {
            var path = Path.Combine(source, file.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, $"// {file}\n");
        }

        // Hidden by attribute rather than by name: the digest counts it, and only a listing that asks for hidden files
        // finds it. Elsewhere a file is hidden by its name alone, and this one is an ordinary file there.
        if (OperatingSystem.IsWindows())
        {
            var marked = Path.Combine(source, "modules", "marked.js");
            File.SetAttributes(marked, File.GetAttributes(marked) | FileAttributes.Hidden);
        }

        Directory.CreateDirectory(Path.Combine(source, "empty"));

        var entries = EntryNames(WriteArchive(tree, source));
        var counted = PackageDigest.Files(source);
        Assert.Equal(["main.js", "modules/deep/read.js", "modules/marked.js", "modules/send.js", "plugin.yaml"], counted);
        Assert.True(
            entries.SequenceEqual(counted),
            $"The archive holds, in its order, {Listed(entries)}; the digest counts, in ordinal order, {Listed(counted)}.");
    }

    /// <summary>
    /// The archive lands where it was asked for, also when that is a path relative to where the caller stands, as the
    /// release job's own line gives it.
    /// </summary>
    [Fact]
    public void The_archive_lands_where_a_relative_path_names_it()
    {
        using var tree = new TempTree();
        var standing = tree.NewDirectory("workspace");
        var script = Path.Combine([RepositoryRoot(), .. ArchiveScript]).Replace("'", "''", StringComparison.Ordinal);
        var package = Path.Combine(RepositoryRoot(), "plugins", "reply").Replace("'", "''", StringComparison.Ordinal);

        var (exit, _, stderr) = Pwsh(
            NoEnvironment,
            "-Command",
            $"$ErrorActionPreference = 'Stop'; Set-Location -LiteralPath '{standing.Replace("'", "''", StringComparison.Ordinal)}'; & '{script}' -Package '{package}' -Archive artifacts/release/{ReleaseAssets.ReplyPlugin}");

        Assert.True(exit == 0, $"Write-PluginArchive.ps1 with a relative -Archive exited {exit}: {stderr}");
        Assert.True(
            File.Exists(Path.Combine(standing, "artifacts", "release", ReleaseAssets.ReplyPlugin)),
            $"Write-PluginArchive.ps1 was asked for artifacts/release/{ReleaseAssets.ReplyPlugin} from {standing}, and it is not there.");
    }

    /// <summary>
    /// The wiring check's step 3, executed for each package a release publishes: the page's own line, naming that
    /// package's id where it names <c>reply</c>, installs the package the release at <c>v&lt;version&gt;</c> names in its
    /// manifest, fetched beside its <c>checksums.txt</c> and checked against it. What it installs has the content digest
    /// of <c>plugins/&lt;id&gt;</c>.
    /// </summary>
    /// <remarks>
    /// The release is <see cref="LoopbackReleases"/>: the product's address on the loopback interface, answering as the
    /// address answers, with the archive <see cref="WriteArchive"/> built from this tree, a <c>checksums.txt</c> holding its
    /// line and a manifest naming it by its content digest. The line is the page's own, run through the program itself;
    /// the only changes are the runner's: the release feed's address made the loopback one, the version, and Jason's data
    /// directory made the test's own.
    /// </remarks>
    [Theory]
    [MemberData(nameof(PublishedPackages))]
    public async Task Step_3_installs_the_release_s_package_with_the_tree_s_digest(string asset)
    {
        using var tree = new TempTree();
        var id = IdOf(asset);
        var source = Path.Combine(RepositoryRoot(), "plugins", id);
        using var releases = new LoopbackReleases(Release(tree, id, asset, source, Mismatch.None));
        var data = DataDirectory(tree);

        var (exit, output) = await RunStepThreeAsync(releases.Releases, data, id);

        Assert.True(exit == 0, $"Step 3's line for {id} exited {exit}:\n{output}");
        var installed = new JasonPaths(data).PluginPackageDirectory(id);
        var expected = PackageDigest.Compute(source);
        var actual = PackageDigest.Compute(installed);
        Assert.True(
            actual.Digest == expected.Digest,
            $"Step 3 installed {actual.Digest} over {actual.FileCount} files; plugins/{id} is {expected.Digest} over {expected.FileCount}.");
        Assert.Equal(PackageDigest.Files(source), PackageDigest.Files(installed));

        // Each file from the address first and then from where it redirects, at the version asked for.
        Assert.Equal(
            [
                $"GET /releases/download/v{Version}/{ReleaseAssets.Manifest}",
                $"GET /store/releases/download/v{Version}/{ReleaseAssets.Manifest}",
                $"GET /releases/download/v{Version}/{ReleaseAssets.Checksums}",
                $"GET /store/releases/download/v{Version}/{ReleaseAssets.Checksums}",
                $"GET /releases/download/v{Version}/{asset}",
                $"GET /store/releases/download/v{Version}/{asset}",
            ],
            releases.Asked);
    }

    /// <summary>
    /// A package that does not match its line in <c>checksums.txt</c>, or that has no line there, is never unpacked: the
    /// line exits 1 and the plugins directory holds nothing.
    /// </summary>
    [Theory]
    [InlineData(Mismatch.AnotherDigest)]
    [InlineData(Mismatch.NoLine)]
    public async Task Step_3_unpacks_nothing_that_does_not_match_its_checksum(Mismatch mismatch)
    {
        using var tree = new TempTree();
        using var releases = new LoopbackReleases(
            Release(tree, "reply", ReleaseAssets.ReplyPlugin, Path.Combine(RepositoryRoot(), "plugins", "reply"), mismatch));
        var data = DataDirectory(tree);

        var (exit, output) = await RunStepThreeAsync(releases.Releases, data, "reply");

        Assert.True(exit == 1, $"Step 3's line exited {exit} over a release whose checksums.txt has {mismatch}:\n{output}");
        var plugins = new JasonPaths(data).PluginsDirectory;
        Assert.False(
            Directory.EnumerateFileSystemEntries(plugins).Any(),
            $"Step 3's line left {string.Join(", ", Directory.EnumerateFileSystemEntries(plugins).Select(Path.GetFileName))} in {plugins} from a package whose checksums.txt has {mismatch}:\n{output}");
    }

    /// <summary>What a release's <c>checksums.txt</c> says of the package.</summary>
    public enum Mismatch
    {
        /// <summary>Its line, naming its digest.</summary>
        None,

        /// <summary>A line naming another digest.</summary>
        AnotherDigest,

        /// <summary>No line at all.</summary>
        NoLine,
    }

    /// <summary>
    /// The plugin's own README says the same as the wiring check: the package comes from the release, as
    /// <c>jason-plugin-reply.zip</c>, placed by <c>jason plugin install reply</c> once it matches <c>checksums.txt</c>, with
    /// the staged tree as the fallback — and never a copy out of a checkout, which a person who installed Jason does not
    /// have.
    /// </summary>
    [Fact]
    public void The_plugin_s_readme_installs_the_package_from_the_release()
    {
        var readme = Read(["plugins", "reply", "README.md"]).ReplaceLineEndings("\n");
        var start = readme.IndexOf("\n## Installing it today\n", StringComparison.Ordinal);
        var end = readme.IndexOf("\n## ", start + 1, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "plugins/reply/README.md has no section 'Installing it today'.");
        var section = readme[start..end];
        var said = Regex.Replace(section, @"\s+", " ");

        Assert.Contains($"Every release of Jason from 0.11.0 on publishes it as `{ReleaseAssets.ReplyPlugin}`, listed in that release's `{ReleaseAssets.Checksums}`.", said, StringComparison.Ordinal);
        Assert.Contains("jason plugin install reply", section, StringComparison.Ordinal);
        Assert.Contains("It checks the package against its line in `checksums.txt` and, unpacked, against the content digest the release's manifest gives, places it as `~/.jason/plugins/reply` only once both hold", said, StringComparison.Ordinal);
        Assert.Contains("A different package already there is kept unless `--replace` says otherwise.", said, StringComparison.Ordinal);
        Assert.Contains("It is step 3 of [the wiring check](../../docs/golden-path.md).", said, StringComparison.Ordinal);
        Assert.Contains("Where the release cannot be reached, the tree `jason skills install` fetched holds the same package,", said, StringComparison.Ordinal);
        Assert.Contains("cp -r ~/.jason/skills/staged/v<version>/plugins/reply ~/.jason/plugins/reply", section, StringComparison.Ordinal);
        Assert.DoesNotContain("cp -r plugins/reply", readme, StringComparison.Ordinal);
        Assert.DoesNotContain("no install verb", readme, StringComparison.Ordinal);
    }

    /// <summary>
    /// The architecture's register of accepted decisions records that the plugins' packages a release publishes ship as
    /// release assets, as it stands, beside the row that says both installation paths are legitimate.
    /// </summary>
    [Fact]
    public void The_decision_register_records_that_the_plugin_ships_as_a_release_asset()
    {
        var architecture = Read(["docs", "architecture.md"]).ReplaceLineEndings("\n");
        var start = architecture.IndexOf("\n## 18. Accepted decision register", StringComparison.Ordinal);
        var end = architecture.IndexOf("\n## 19. ", StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "docs/architecture.md no longer has §18 followed by §19.");

        var rows = architecture[start..end].Split('\n');
        var paths = Array.FindIndex(rows, row => row.StartsWith("| DEC-INSTALL-001 |", StringComparison.Ordinal));
        Assert.True(paths >= 0, "§18 no longer has the row on the two installation paths.");
        Assert.Equal(PluginAssetDecision, rows[paths + 1]);
    }

    /// <summary>The register's row for the plugins' packages as release assets, as it stands.</summary>
    private const string PluginAssetDecision =
        "| DEC-INSTALL-002 | The Reply plugin, from 0.30.0 on the journal forwarder, and from 0.32.0 on ntfy, Slack and Telegram ship as release assets: every release publishes `jason-plugin-<id>.zip` for each, the files of `plugins/<id>` at the tag at the archive's root, "
        + "each built once and listed in `checksums.txt`, and named in the manifest's `plugins` with its version, file name, content digest and the digest of every version its list records, "
        + "never among the manifest's artifacts, which are platforms. "
        + "Installing one is `jason plugin install <id>`: download, verify against `checksums.txt` and the manifest's content digest, unpack into the plugins directory; "
        + "a package's content digest is its identity, and both installation paths of §15.5 rest on those assets. |";

    /// <summary>The version the lines are run at, as <c>jason --version</c> prints it up to any <c>+</c>.</summary>
    private const string Version = "0.11.0";

    /// <summary>
    /// A release's store: the package as the release job writes it, under <paramref name="asset"/>, a
    /// <c>checksums.txt</c> with a line per archive and what <paramref name="mismatch"/> says of the package's line, and a
    /// manifest naming the package under <paramref name="id"/> by the content digest of <paramref name="source"/>.
    /// </summary>
    private static string Release(TempTree tree, string id, string asset, string source, Mismatch mismatch)
    {
        var archive = WriteArchive(tree, source, asset);
        var lines = ReleaseAssets.Rids.Select(rid => $"{new string('0', 64)}  {ReleaseAssets.For(rid)}");
        if (mismatch != Mismatch.NoLine)
        {
            var digest = Convert.ToHexStringLower(SHA256.HashData(mismatch == Mismatch.AnotherDigest ? Encoding.UTF8.GetBytes("another package") : File.ReadAllBytes(archive)));
            lines = lines.Append($"{digest}  {asset}");
        }

        var store = Path.GetDirectoryName(archive)!;
        File.WriteAllText(Path.Combine(store, ReleaseAssets.Checksums), string.Join('\n', lines) + "\n");
        var content = PackageDigest.Compute(source).Digest;
        File.WriteAllText(Path.Combine(store, ReleaseAssets.Manifest), $$"""
            {
              "schema": 1,
              "version": "{{Version}}",
              "published_at": "2026-10-01T00:00:00Z",
              "release_notes_url": null,
              "min_upgrade_from": null,
              "artifacts": {
                "linux-x64": { "asset": "{{ReleaseAssets.For("linux-x64")}}", "sha256": "{{new string('0', 64)}}", "size": 1 }
              },
              "plugins": {
                "{{id}}": { "version": "0.0.0", "asset": "{{asset}}", "digest": "{{content}}", "recorded": { "0.0.0": "{{content}}" } }
              }
            }
            """);
        return store;
    }

    /// <summary>A data directory as step 1's <c>jason runtime start</c> leaves it: the runtime's layout, plugins included.</summary>
    private static string DataDirectory(TempTree tree)
    {
        var data = tree.NewDirectory("data");
        foreach (var directory in new JasonPaths(data).Layout)
        {
            Directory.CreateDirectory(directory);
        }

        return data;
    }

    /// <summary>
    /// Step 3's line, read from the page, run through the program with the runner's substitutions made: the package it
    /// names is <paramref name="id"/>, the default feed is the loopback release's, and the data directory and the version
    /// are the test's own. No runtime answers, so the package is placed and loads at the next start.
    /// </summary>
    private static async Task<(int Exit, string Output)> RunStepThreeAsync(string releases, string data, string id)
    {
        var line = StepThreeLine();
        var arguments = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("jason", arguments[0]);
        Assert.Equal("reply", arguments[^1]);
        arguments[^1] = id;

        var feed = UpdateFeed.Default.AbsoluteUri.Replace(ProductAddress.Releases, releases, StringComparison.Ordinal);
        Assert.NotEqual(UpdateFeed.Default.AbsoluteUri, feed);
        var output = new StringWriter();
        var exit = await CliApp.RunAsync(
            [.. arguments[1..], "--feed", feed],
            new CliEnvironment(output, output, new JasonPaths(data), BuildVersion: Version),
            TestContext.Current.CancellationToken);
        return (exit, output.ToString());
    }

    /// <summary>The one line of step 3 that runs <c>jason plugin install</c>.</summary>
    private static string StepThreeLine()
    {
        var page = Read(["docs", "golden-path.md"]).ReplaceLineEndings("\n");
        var start = page.IndexOf("\n## 3. ", StringComparison.Ordinal);
        Assert.True(start >= 0, "docs/golden-path.md has no step 3.");
        var end = page.IndexOf("\n## ", start + 1, StringComparison.Ordinal);
        var step = page[start..(end < 0 ? page.Length : end)];

        return Assert.Single(step.Split('\n'), line => line.StartsWith("jason plugin install ", StringComparison.Ordinal));
    }

    /// <summary>
    /// The script run as the release job runs it, over <paramref name="package"/>, and the archive it wrote, named
    /// <paramref name="asset"/>.
    /// </summary>
    internal static string WriteArchive(TempTree tree, string package, string asset = ReleaseAssets.ReplyPlugin)
    {
        var archive = Path.Combine(tree.NewDirectory("release"), asset);
        var (exit, stdout, stderr) = Script(
            NoEnvironment,
            Path.Combine([RepositoryRoot(), .. ArchiveScript]),
            "-Package", package,
            "-Archive", archive);
        Assert.True(exit == 0, $"Write-PluginArchive.ps1 exited {exit}: {stderr}");

        // A script that ended with 0 and no archive has been seen once, on macOS. Its last line names the archive it wrote
        // and how many files it holds, so what it said and what its directory holds tell how far it got.
        if (!File.Exists(archive))
        {
            var directory = new DirectoryInfo(Path.GetDirectoryName(archive)!);
            var holds = directory.Exists ? Listed(directory.EnumerateFileSystemInfos().Select(entry => entry.Name)) : "gone";
            Assert.Fail($"Write-PluginArchive.ps1 exited 0 and {archive} is not there. Its stdout: '{stdout.Trim()}'. Its stderr: '{stderr.Trim()}'. Its directory: {holds}.");
        }

        return archive;
    }

    /// <summary>Every entry of a zip by its full name, in the archive's own order: a directory entry ends with a slash.</summary>
    private static List<string> EntryNames(string archive)
    {
        using var zip = ZipFile.OpenRead(archive);
        return [.. zip.Entries.Select(entry => entry.FullName)];
    }

    private static string Listed(IEnumerable<string> names) => "[" + string.Join(", ", names) + "]";
}
