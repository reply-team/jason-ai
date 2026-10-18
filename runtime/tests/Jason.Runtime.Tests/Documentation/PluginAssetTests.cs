using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Jason.Contracts.Discovery;
using Jason.Contracts.Plugins;
using Jason.Contracts.Update;
using static Jason.Runtime.Tests.Documentation.Workflows;

namespace Jason.Runtime.Tests.Documentation;

/// <summary>
/// The Reply plugin's package as a release publishes it, <see cref="ReleaseAssets.ReplyPlugin"/>: written once per release
/// by <c>.github/scripts/Write-PluginArchive.ps1</c> from <c>plugins/reply</c> at the tag. What holds the archive to the
/// package is the package's content digest, <see cref="PackageDigest"/>: unpacked, the archive has the digest of the
/// directory it was made from, so what a person installs from a release is the tag's package, byte for byte.
/// </summary>
/// <remarks>
/// The script is executed, not read, and its output is measured by the runtime's own digest rather than by a second
/// description of it. The build legs of a release run this suite at the tag before anything is published, so the
/// fact holds of the very tree the release archives.
/// </remarks>
public sealed class PluginAssetTests
{
    private static readonly string[] ArchiveScript = [".github", "scripts", "Write-PluginArchive.ps1"];

    private static readonly Dictionary<string, string> NoEnvironment = [];

    /// <summary>
    /// The archive of the real package holds its files and nothing else, at the root — no folder around them, no entry
    /// for a directory — in ordinal order, whatever order the file system lists them in, and unpacked into an empty
    /// directory it has the package's digest.
    /// </summary>
    [Fact]
    public void The_archive_holds_exactly_the_package_s_files_at_its_root_and_unpacks_to_its_digest()
    {
        using var tree = new TempTree();
        var source = Path.Combine(RepositoryRoot(), "plugins", "reply");
        var archive = WriteArchive(tree, source);

        var entries = EntryNames(archive);
        var files = PackageDigest.Files(source);
        Assert.True(
            entries.SequenceEqual(files),
            $"The archive holds, in its order, {Listed(entries)}; the package is, in ordinal order, {Listed(files)}.");

        var unpacked = tree.NewDirectory("reply");
        ZipFile.ExtractToDirectory(archive, unpacked);
        var expected = PackageDigest.Compute(source);
        var actual = PackageDigest.Compute(unpacked);
        Assert.True(
            actual.Digest == expected.Digest,
            $"Unpacked, the archive is {actual.Digest} over {actual.FileCount} files; the package is {expected.Digest} over {expected.FileCount}.");
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
    /// The wiring check's step 3, executed: the page's own lines for one shell download the package and
    /// <c>checksums.txt</c> from the release at <c>v&lt;version&gt;</c>, check the one against the other and unpack the
    /// package into the plugins directory. What they install has the content digest of <c>plugins/reply</c>, and they
    /// write nothing in the folder they were started from.
    /// </summary>
    /// <remarks>
    /// The release is <see cref="LoopbackReleases"/>: the product's address on the loopback interface, answering as the
    /// address answers, with the archive <see cref="WriteArchive"/> built from this tree and a <c>checksums.txt</c> holding
    /// its line. The lines are the page's own; the only changes are the runner's: the address, the version, and the
    /// two directories a person names — the temporary one and Jason's data directory — made the test's own. The sh lines
    /// are for macOS and Linux and run on CI's legs there; the PowerShell lines run in PowerShell 7 everywhere, and in
    /// the Windows PowerShell a fresh Windows has. Each runs the way a script runs them and the way a paste does
    /// (<see cref="Modes"/>).
    /// </remarks>
    [Theory]
    [MemberData(nameof(Modes))]
    public void Step_3_installs_the_release_s_package_with_the_tree_s_digest(string shell)
    {
        using var tree = new TempTree();
        var source = Path.Combine(RepositoryRoot(), "plugins", "reply");
        using var releases = new LoopbackReleases(Release(tree, source, Mismatch.None));
        var data = DataDirectory(tree);
        var folder = tree.NewDirectory("folder");

        var (exit, output) = RunStepThree(shell, tree, releases.Releases, data, folder);

        Assert.True(exit == 0, $"Step 3's {shell} lines exited {exit}:\n{output}");
        var installed = new JasonPaths(data).PluginPackageDirectory("reply");
        var expected = PackageDigest.Compute(source);
        var actual = PackageDigest.Compute(installed);
        Assert.True(
            actual.Digest == expected.Digest,
            $"Step 3 installed {actual.Digest} over {actual.FileCount} files; plugins/reply is {expected.Digest} over {expected.FileCount}.");
        Assert.Equal(PackageDigest.Files(source), PackageDigest.Files(installed));

        // Each file from the address first and then from where it redirects, at the version asked for.
        Assert.Equal(
            [
                $"GET /releases/download/v{Version}/{ReleaseAssets.Checksums}",
                $"GET /store/releases/download/v{Version}/{ReleaseAssets.Checksums}",
                $"GET /releases/download/v{Version}/{ReleaseAssets.ReplyPlugin}",
                $"GET /store/releases/download/v{Version}/{ReleaseAssets.ReplyPlugin}",
            ],
            releases.Asked);
        Assert.Empty(Directory.EnumerateFileSystemEntries(folder));
    }

    /// <summary>
    /// A package that does not match its line in <c>checksums.txt</c>, or that has no line there, is never unpacked: the
    /// unpack is part of the check's own line, so it follows only a check that passed — also where nothing stops the
    /// lines at the first failure, as in a paste or a shell run without <c>-e</c>.
    /// </summary>
    [Theory]
    [MemberData(nameof(Mismatches))]
    public void Step_3_unpacks_nothing_that_does_not_match_its_checksum(string shell, Mismatch mismatch)
    {
        using var tree = new TempTree();
        using var releases = new LoopbackReleases(Release(tree, Path.Combine(RepositoryRoot(), "plugins", "reply"), mismatch));
        var data = DataDirectory(tree);

        var (exit, output) = RunStepThree(shell, tree, releases.Releases, data, tree.NewDirectory("folder"));

        Assert.True(exit != 0, $"Step 3's lines, run as '{shell}', exited 0 over a release whose checksums.txt has {mismatch}:\n{output}");
        var installed = new JasonPaths(data).PluginPackageDirectory("reply");
        Assert.False(
            Directory.Exists(installed) && Directory.EnumerateFileSystemEntries(installed).Any(),
            $"Step 3's lines, run as '{shell}', unpacked into {installed} a package whose checksums.txt has {mismatch}:\n{output}");
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
    /// How step 3's lines are run: as a script that stops at the first failure (<c>sh -e</c>; a PowerShell script, whose
    /// <c>throw</c> ends it), as one that does not (<c>sh</c> without <c>-e</c>, which is what an agent's shell tool does
    /// with several lines), and as a paste, one line at a time, each run whatever the one before it did. And the sh lines
    /// once more with a <c>sha256sum</c> that, like the one macOS carries, checks the files it names and reads no digests
    /// from a pipe, so that every machine with sh holds the line to what a Mac does with it.
    /// </summary>
    public static TheoryData<string> Modes() => ["sh -e", "sh", FilesOnly, "pwsh -File", "pwsh pasted", "powershell -File", "powershell pasted"];

    /// <summary>The sh lines run as <c>sh -e</c> with <see cref="FilesOnlySha256sum"/> first on the search path.</summary>
    private const string FilesOnly = "sh -e, sha256sum -c reading files only";

    /// <summary>
    /// A <c>sha256sum</c> standing in for the one macOS 14 and later carry: with <c>-c</c> and no file named it prints that
    /// program's usage line and exits 1, as that one did on a Mac given the digests on a pipe; otherwise it hands every
    /// argument to this machine's own. <c>@REAL@</c> is that program's path.
    /// </summary>
    private const string FilesOnlySha256sum = """
        #!/bin/sh
        check=no
        files=0
        for argument in "$@"; do
            case "$argument" in
                -c|--check) check=yes ;;
                -*) ;;
                *) files=$((files + 1)) ;;
            esac
        done
        if [ "$check" = yes ] && [ "$files" -eq 0 ]; then
            echo "usage: sha256sum [-bctwz] [files ...]" >&2
            exit 1
        fi
        exec '@REAL@' "$@"
        """;

    public static TheoryData<string, Mismatch> Mismatches()
    {
        var data = new TheoryData<string, Mismatch>();
        foreach (var shell in Modes())
        {
            data.Add(shell, Mismatch.AnotherDigest);
            data.Add(shell, Mismatch.NoLine);
        }

        return data;
    }

    /// <summary>
    /// The plugin's own README says the same as the wiring check: the package comes from the release, as
    /// <c>jason-plugin-reply.zip</c>, checked against <c>checksums.txt</c> and unpacked into <c>~/.jason/plugins/reply</c>,
    /// with the page's step 3 for the lines and the staged tree as the fallback — and never a copy out of a checkout,
    /// which a person who installed Jason does not have.
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
        Assert.Contains("check the package against its line, and unpack it into `~/.jason/plugins/reply` only once it matches", said, StringComparison.Ordinal);
        Assert.Contains("it must not be there yet; the runtime's first start makes `~/.jason/plugins`.", said, StringComparison.Ordinal);
        Assert.Contains("Where the release cannot be reached, or is older than 0.11.0 and does not carry the package,", said, StringComparison.Ordinal);
        Assert.Contains("are step 3 of [the wiring check](../../docs/golden-path.md)", said, StringComparison.Ordinal);
        Assert.Contains("cp -r ~/.jason/skills/staged/v<version>/plugins/reply ~/.jason/plugins/reply", section, StringComparison.Ordinal);
        Assert.DoesNotContain("cp -r plugins/reply", readme, StringComparison.Ordinal);
    }

    /// <summary>
    /// The architecture's register of accepted decisions records that the Reply plugin ships as a release asset, word for
    /// word as it was accepted, beside the row that says both installation paths are legitimate.
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

    /// <summary>The register's row for the plugin as a release asset, as it was accepted.</summary>
    private const string PluginAssetDecision =
        "| DEC-INSTALL-002 | The Reply plugin ships as a release asset: every release publishes `jason-plugin-reply.zip`, the files of `plugins/reply` at the tag at the archive's root, "
        + "built once and listed in `checksums.txt`, never in the manifest, whose artifacts are platforms. "
        + "Installing it is download, verify against `checksums.txt`, unpack into the plugins directory, with no verb; "
        + "the package's content digest is its identity, and both installation paths of §15.5 rest on that asset. |";

    /// <summary>The version the lines are run at, as <c>jason --version</c> prints it up to any <c>+</c>.</summary>
    private const string Version = "0.11.0";

    /// <summary>
    /// A release's store: the package as the release job writes it, and a <c>checksums.txt</c> with a line per archive
    /// and what <paramref name="mismatch"/> says of the package's line.
    /// </summary>
    private static string Release(TempTree tree, string source, Mismatch mismatch)
    {
        var archive = WriteArchive(tree, source);
        var lines = ReleaseAssets.Rids.Select(rid => $"{new string('0', 64)}  {ReleaseAssets.For(rid)}");
        if (mismatch != Mismatch.NoLine)
        {
            var digest = Convert.ToHexStringLower(SHA256.HashData(mismatch == Mismatch.AnotherDigest ? Encoding.UTF8.GetBytes("another package") : File.ReadAllBytes(archive)));
            lines = lines.Append($"{digest}  {ReleaseAssets.ReplyPlugin}");
        }

        var store = Path.GetDirectoryName(archive)!;
        File.WriteAllText(Path.Combine(store, ReleaseAssets.Checksums), string.Join('\n', lines) + "\n");
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
    /// Step 3's block for the shell <paramref name="mode"/> names, read from the page, with the runner's substitutions
    /// made, run to its end from <paramref name="folder"/> in that mode (<see cref="Modes"/>).
    /// </summary>
    private static (int Exit, string Output) RunStepThree(string mode, TempTree tree, string releases, string data, string folder)
    {
        var shell = mode.Split(' ')[0];
        SkipWhereTheShellIsNot(shell);
        var temporary = tree.NewDirectory("temporary");
        var block = StepThreeBlock(shell == "sh" ? "sh" : "powershell");

        // A path is quoted whole, as a person types one that holds a space.
        var lines = block
            .Replace(ProductAddress.Releases, releases, StringComparison.Ordinal)
            .Replace("<version>", Version, StringComparison.Ordinal);
        lines = Regex.Replace(lines, @"<temporary directory>(/[^\s)']*)?", match => "'" + temporary + match.Groups[1].Value + "'");
        lines = Regex.Replace(lines, @"~/\.jason(/[^\s)']*)?", match => "'" + data + match.Groups[1].Value + "'");

        // A line the substitutions did not reach would reach the network, or this machine's own data directory. (A
        // Windows temporary directory can hold a short name such as RUNNER~1, so it is the page's `~/` that is looked for.)
        foreach (var unreached in (string[])["jasonharness.ai", "~/", "<"])
        {
            Assert.False(lines.Contains(unreached, StringComparison.Ordinal), $"Step 3's {shell} lines still hold '{unreached}' after the substitutions:\n{lines}");
        }

        var script = Path.Combine(tree.NewDirectory("script"), shell == "sh" ? "step-3.sh" : "step-3.ps1");
        File.WriteAllText(script, lines);
        var home = tree.NewDirectory("home");

        // A test run from PowerShell 7, as CI's is, hands its children PowerShell 7's module path, and Windows PowerShell
        // started under it finds no Get-FileHash or Expand-Archive of its own. One started from Windows has the machine's.
        var windows = new Dictionary<string, string> { ["PSModulePath"] = Environment.GetEnvironmentVariable("PSModulePath", EnvironmentVariableTarget.Machine) ?? string.Empty };
        string[] powershell = ["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass"];

        // A paste: the lines on standard input, each run as it arrives and whatever the one before it did.
        return mode switch
        {
            "sh -e" => Run(OnPath("sh")!, ["-e", script], folder, new() { ["HOME"] = home }),
            "sh" => Run(OnPath("sh")!, [script], folder, new() { ["HOME"] = home }),
            FilesOnly => Run(OnPath("sh")!, ["-e", script], folder, new() { ["HOME"] = home, ["PATH"] = FilesOnlyPath(tree) }),
            "pwsh -File" => Run(OnPath("pwsh")!, [.. powershell, "-File", script], folder, new() { ["HOME"] = home }),
            "pwsh pasted" => Run(OnPath("pwsh")!, [.. powershell, "-Command", "-"], folder, new() { ["HOME"] = home }, lines),
            "powershell -File" => Run(WindowsPowerShell, [.. powershell, "-File", script], folder, windows),
            "powershell pasted" => Run(WindowsPowerShell, [.. powershell, "-Command", "-"], folder, windows, lines),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "No such way of running step 3's lines."),
        };
    }

    private static string WindowsPowerShell => Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");

    /// <summary>This machine's search path with a directory holding <see cref="FilesOnlySha256sum"/> in front of it.</summary>
    private static string FilesOnlyPath(TempTree tree)
    {
        var directory = tree.NewDirectory("files-only");
        var program = Path.Combine(directory, "sha256sum");
        File.WriteAllText(program, FilesOnlySha256sum.Replace("@REAL@", OnPath("sha256sum")!, StringComparison.Ordinal).ReplaceLineEndings("\n") + "\n");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(program, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        return directory + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH");
    }

    /// <summary>
    /// Skips the row on a machine that cannot run its shell's lines: the sh lines are for macOS and Linux, Windows
    /// PowerShell is only on Windows, and PowerShell 7 is on every CI runner.
    /// </summary>
    private static void SkipWhereTheShellIsNot(string shell)
    {
        switch (shell)
        {
            case "sh":
                Assert.SkipWhen(OperatingSystem.IsWindows(), "The sh lines are for macOS and Linux; CI's legs there run this row.");
                foreach (var tool in (string[])["sh", "curl", "sha256sum", "unzip"])
                {
                    Assert.SkipWhen(OnPath(tool) is null, $"{tool} is not on this machine's PATH; the page names the macOS alternative to sha256sum, and CI's Linux legs run this row.");
                }

                break;

            case "pwsh":
                Assert.SkipWhen(OnPath("pwsh") is null, "pwsh is not on this machine's PATH; every CI runner has it.");
                break;

            default:
                Assert.SkipUnless(OperatingSystem.IsWindows() && File.Exists(WindowsPowerShell), "Windows PowerShell is the one a fresh Windows has, and only Windows has it.");
                break;
        }
    }

    /// <summary>The fenced block of step 3 whose info string is <paramref name="info"/>, without its fences.</summary>
    private static string StepThreeBlock(string info)
    {
        var page = Read(["docs", "golden-path.md"]).ReplaceLineEndings("\n");
        var start = page.IndexOf("\n## 3. ", StringComparison.Ordinal);
        Assert.True(start >= 0, "docs/golden-path.md has no step 3.");
        var end = page.IndexOf("\n## ", start + 1, StringComparison.Ordinal);
        var step = page[start..(end < 0 ? page.Length : end)];

        var block = Assert.Single(Regex.Matches(step, "^```" + Regex.Escape(info) + "\n(.*?)^```$", RegexOptions.Multiline | RegexOptions.Singleline));
        return block.Groups[1].Value;
    }

    /// <summary>A program on this machine's PATH by its name, or null.</summary>
    private static string? OnPath(string name) =>
        (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .SelectMany(directory => new[] { Path.Combine(directory, name + ".exe"), Path.Combine(directory, name) })
            .FirstOrDefault(File.Exists);

    /// <summary>
    /// One program, run to its end from <paramref name="directory"/> with <paramref name="input"/> on its standard input,
    /// its two streams together.
    /// </summary>
    private static (int Exit, string Output) Run(string program, string[] arguments, string directory, Dictionary<string, string> environment, string input = "")
    {
        var start = new ProcessStartInfo(program)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = directory,
            StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        // PowerShell reports telemetry over the network unless told not to, and nothing here leaves the machine: the
        // release is on the loopback interface, and no proxy stands between.
        start.Environment["POWERSHELL_TELEMETRY_OPTOUT"] = "1";
        start.Environment["NO_PROXY"] = "127.0.0.1,localhost";
        start.Environment["no_proxy"] = "127.0.0.1,localhost";
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        foreach (var (name, value) in environment)
        {
            start.Environment[name] = value;
        }

        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        process.StandardInput.Write(input);
        process.StandardInput.Close();
        if (!process.WaitForExit(TimeSpan.FromSeconds(120)))
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail($"{Path.GetFileName(program)} did not finish step 3 in two minutes.");
        }

        return (process.ExitCode, stdout.Result + stderr.Result);
    }

    /// <summary>The script run as the release job runs it, over <paramref name="package"/>, and the archive it wrote.</summary>
    internal static string WriteArchive(TempTree tree, string package)
    {
        var archive = Path.Combine(tree.NewDirectory("release"), ReleaseAssets.ReplyPlugin);
        var (exit, _, stderr) = Script(
            NoEnvironment,
            Path.Combine([RepositoryRoot(), .. ArchiveScript]),
            "-Package", package,
            "-Archive", archive);
        Assert.True(exit == 0, $"Write-PluginArchive.ps1 exited {exit}: {stderr}");
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
