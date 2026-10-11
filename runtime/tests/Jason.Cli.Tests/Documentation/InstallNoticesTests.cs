using System.Collections.Concurrent;
using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Jason.Contracts.Update;

namespace Jason.Cli.Tests.Documentation;

/// <summary>
/// What the install scripts say about the licence once an install has succeeded, and the two notice files they keep
/// beside the executable. Each script is run for real, on the platform it is written for, against a feed of the test's
/// own: <c>install.ps1</c> on Windows, <c>install.sh</c> on Linux and macOS. Every row is a theory over the two, so each
/// CI leg runs one script's rows and names the other's as skipped.
/// </summary>
/// <remarks>
/// The executable in the feed is a stand-in that prints the version a row gives it: on Windows a one-line program built
/// by the .NET Framework's C# compiler, which every Windows carries; elsewhere a shell script. The scripts run nothing
/// of what they install but its <c>--version</c>.
/// </remarks>
public sealed partial class InstallNoticesTests : IDisposable
{
    public const string Sh = "install.sh";

    public const string Ps1 = "install.ps1";

    private readonly TempTree _tree = new();

    public void Dispose() => _tree.Dispose();

    /// <summary>The line names the version and the licence page at that version's tag, and it is the last thing said.</summary>
    [Theory]
    [InlineData(Sh)]
    [InlineData(Ps1)]
    public void A_fresh_install_says_the_licence_last(string script)
    {
        var installer = Installer.For(script, _tree);

        var run = installer.Run(installer.Feed("0.6.0"));

        Assert.True(run.Exit == 0, run.Stderr);
        var lines = Lines(run.Stdout);
        var downloading = lines.FindIndex(line => line.StartsWith("Downloading Jason 0.6.0 ", StringComparison.Ordinal));
        var installed = lines.FindIndex(line => line.StartsWith("Installed Jason 0.6.0 to ", StringComparison.Ordinal));
        Assert.True(downloading >= 0 && installed > downloading, run.Stdout);
        Assert.Equal(LicenceLine("0.6.0"), lines[^1]);
        Assert.Single(lines, line => line.Contains(" is licensed under ", StringComparison.Ordinal));
    }

    /// <summary>A run over the version already installed describes what is installed, so it says the licence too.</summary>
    [Theory]
    [InlineData(Sh)]
    [InlineData(Ps1)]
    public void An_install_already_there_says_the_licence_too(string script)
    {
        var installer = Installer.For(script, _tree);
        var feed = installer.Feed("0.6.0");
        var first = installer.Run(feed);
        Assert.True(first.Exit == 0, first.Stderr);

        var again = installer.Run(feed);

        Assert.True(again.Exit == 0, again.Stderr);
        var lines = Lines(again.Stdout);
        Assert.Contains(lines, line => line.StartsWith("Jason 0.6.0 is already installed at ", StringComparison.Ordinal));
        Assert.Equal(LicenceLine("0.6.0"), lines[^1]);
    }

    /// <summary>A run that installs nothing says nothing about the licence of what it did not install.</summary>
    [Theory]
    [InlineData(Sh)]
    [InlineData(Ps1)]
    public void A_failed_install_says_no_licence(string script)
    {
        var installer = Installer.For(script, _tree);
        var feed = installer.Feed("0.6.0");
        File.WriteAllText(Path.Combine(feed, ReleaseAssets.Checksums), new string('0', 64) + "  " + installer.Asset + "\n");

        var run = installer.Run(feed);

        Assert.NotEqual(0, run.Exit);
        AssertSaid("does not match checksums.txt", run.Stderr);
        Assert.DoesNotContain("islicensedunder", Squeezed(run.Stdout + run.Stderr), StringComparison.Ordinal);
        Assert.False(File.Exists(installer.Target));
    }

    /// <summary>
    /// <c>LICENSE-RUNTIME.md</c> first exists at v0.6.0: an earlier release carried a notice of its own, which is not the
    /// Jason Runtime Licence, so a pinned install of one says nothing about it. The version's numbers decide, its
    /// pre-release part ignored: a candidate of 0.6.0 is licensed as 0.6.0 is, and 0.10.0 comes after 0.6.0.
    /// </summary>
    [Theory]
    [InlineData(Sh, "0.5.0", false)]
    [InlineData(Ps1, "0.5.0", false)]
    [InlineData(Sh, "0.6.0-rc.1", true)]
    [InlineData(Ps1, "0.6.0-rc.1", true)]
    [InlineData(Sh, "0.10.0", true)]
    [InlineData(Ps1, "0.10.0", true)]
    [InlineData(Sh, "1.0.0", true)]
    [InlineData(Ps1, "1.0.0", true)]
    public void Only_a_release_from_0_6_0_on_says_the_runtime_licence(string script, string version, bool says)
    {
        var installer = Installer.For(script, _tree);

        var run = installer.Run(installer.Feed(version), "--version", version);

        Assert.True(run.Exit == 0, run.Stderr);
        var lines = Lines(run.Stdout);
        Assert.Contains(lines, line => line.StartsWith($"Installed Jason {version} to ", StringComparison.Ordinal));
        if (says)
        {
            Assert.Equal(LicenceLine(version), lines[^1]);
        }
        else
        {
            Assert.DoesNotContain(" is licensed under ", run.Stdout, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The line is the last of the run, after what the PATH step says. Run where the step writes into a profile of the
    /// test's own; on Windows it writes the account's registry, so the Windows half of this row is the order in the
    /// script's text below.
    /// </summary>
    [Fact]
    public void The_licence_line_follows_what_the_path_step_says()
    {
        var installer = Installer.For(Sh, _tree);

        var run = installer.Run(installer.Feed("0.6.0"), modifyPath: true);

        Assert.True(run.Exit == 0, run.Stderr);
        var lines = Lines(run.Stdout);
        Assert.StartsWith($"Added {installer.InstallDir} to the PATH in ", lines[^2], StringComparison.Ordinal);
        Assert.Equal(LicenceLine("0.6.0"), lines[^1]);
    }

    [Fact]
    public void Install_ps1_says_the_licence_after_the_path_step()
    {
        var code = Code(Ps1);
        var path = code.IndexOf("is on your PATH; open a new terminal", StringComparison.Ordinal);
        var licence = code.IndexOf("is licensed under the Jason Runtime Licence: ", StringComparison.Ordinal);

        Assert.True(path >= 0 && licence > path, "install.ps1 does not say the licence after the PATH step.");
        Assert.DoesNotContain("if ($NoModifyPath) { return }", code, StringComparison.Ordinal);
    }

    /// <summary>
    /// The licence page is a page of the repository on GitHub, and the downloads are a release's assets, fetched through
    /// the product's own address: the downloads moved there and the licence page stayed where it is. So each script has a
    /// base for each, the two scripts' licence bases are the same address, and neither is built from the download base.
    /// </summary>
    [Fact]
    public void The_licence_page_and_the_downloads_have_bases_of_their_own()
    {
        var sh = Code(Sh);
        var ps1 = Code(Ps1);

        var shBase = Regex.Match(sh, "^LICENCE_BASE=\"([^\"]*)\"$", RegexOptions.Multiline);
        var ps1Base = Regex.Match(ps1, @"^\s*\$licenceBase = '([^']*)'$", RegexOptions.Multiline);
        Assert.True(shBase.Success, "install.sh has no LICENCE_BASE of its own.");
        Assert.True(ps1Base.Success, "install.ps1 has no $licenceBase of its own.");
        Assert.Equal("https://github.com/reply-team/jason-ai/blob", shBase.Groups[1].Value);
        Assert.Equal(shBase.Groups[1].Value, ps1Base.Groups[1].Value);

        Assert.Contains("$LICENCE_BASE/v$LATEST/LICENSE-RUNTIME.md", sh, StringComparison.Ordinal);
        Assert.Contains("$licenceBase/v$latest/LICENSE-RUNTIME.md", ps1, StringComparison.Ordinal);
        Assert.DoesNotContain("RELEASES_URL", shBase.Value, StringComparison.Ordinal);
        Assert.DoesNotContain("releasesUrl", ps1Base.Value, StringComparison.Ordinal);
    }

    /// <summary>
    /// The one-liners and the scripts' own downloads name the product's address, which answers each request with a
    /// redirect to where the release is stored. Each script follows it for every file it fetches: the manifest, the
    /// checksums and the archive are each asked of the address first and then of the place it named, and the install
    /// completes.
    /// </summary>
    [Theory]
    [InlineData(Sh)]
    [InlineData(Ps1)]
    public void An_install_from_the_address_follows_it_for_every_file(string script)
    {
        var installer = Installer.For(script, _tree);
        using var address = new Jason.Runtime.Tests.LoopbackReleases(installer.Feed("0.6.0"));

        var run = installer.Run($"{address.Releases}/latest/download");

        Assert.True(run.Exit == 0, run.Stderr);
        Assert.Equal(LicenceLine("0.6.0"), Lines(run.Stdout)[^1]);
        var asked = address.Asked.ToList();
        foreach (var file in (string[])[ReleaseAssets.Manifest, ReleaseAssets.Checksums, installer.Asset])
        {
            var first = asked.IndexOf($"GET /releases/latest/download/{file}");
            var then = asked.IndexOf($"GET /store/releases/latest/download/{file}");
            Assert.True(first >= 0 && then > first, $"{script} did not fetch {file} through the address: {string.Join(", ", asked)}");
        }
    }

    /// <summary>One entry of an archive beside the executable: a file with its text, a directory, or a link to its text.</summary>
    internal sealed record Entry(string Name, string Text, EntryKind Kind = EntryKind.File);

    internal enum EntryKind
    {
        File,
        Directory,
        SymbolicLink,
    }

    private static string LicenceText(string version) => $"The licence of {version}.\n";

    private static string NoticesText(string version) => $"The notices of {version}.\n";

    private static string LicenceLine(string version) =>
        $"Jason {version} is licensed under the Jason Runtime Licence: https://github.com/reply-team/jason-ai/blob/v{version}/LICENSE-RUNTIME.md";

    /// <summary>
    /// A refusal, as said. PowerShell's error view wraps a message at its width, colours each line and puts a gutter in
    /// front of it, so both texts are read without the colours, the gutter and white space.
    /// </summary>
    private static void AssertSaid(string sentence, string said) =>
        Assert.True(Squeezed(said).Contains(Squeezed(sentence), StringComparison.Ordinal), $"Expected: {sentence}{Environment.NewLine}Said: {said}");

    private static string Squeezed(string text)
    {
        var plain = Regex.Replace(text, @"\x1b\[[0-9;?]*[A-Za-z]", string.Empty);
        var ungutted = Regex.Replace(plain, @"(?m)^\s*\|", string.Empty);
        return Regex.Replace(ungutted, @"\s+", string.Empty);
    }

    private static List<string> Lines(string output) =>
        [.. output.ReplaceLineEndings("\n").Split('\n').Select(line => line.TrimEnd()).Where(line => line.Length > 0)];

    /// <summary>One of the two scripts, on the platform it runs on, with a feed and an install directory of the test's own.</summary>
    private sealed class Installer
    {
        private static readonly ConcurrentDictionary<string, byte[]> StandIns = new(StringComparer.Ordinal);

        private readonly TempTree _tree;

        private Installer(string script, TempTree tree)
        {
            Script = script;
            _tree = tree;
            InstallDir = tree.NewDirectory("bin");
            Home = tree.NewDirectory("home");
        }

        public string Script { get; }

        public string InstallDir { get; }

        public string Home { get; }

        public bool Windows => Script == Ps1;

        public string ExecutableName => Windows ? "jason.exe" : "jason";

        public string Target => Path.Combine(InstallDir, ExecutableName);

        /// <summary>The asset this machine installs.</summary>
        public string Asset => ReleaseAssets.For(Windows ? "win-x64" : OperatingSystem.IsMacOS() ? "osx-arm64" : "linux-x64");

        /// <summary>The script, where it runs: each refuses every platform but its own before it does anything else.</summary>
        public static Installer For(string script, TempTree tree)
        {
            if (script == Ps1)
            {
                Assert.SkipUnless(OperatingSystem.IsWindows(), "install.ps1 is the Windows installer; CI's Windows leg runs this row.");
            }
            else
            {
                Assert.SkipWhen(OperatingSystem.IsWindows(), "install.sh refuses a Windows shell by name; CI's Linux and macOS legs run this row.");
                Assert.SkipUnless(
                    (OperatingSystem.IsLinux() && RuntimeInformation.OSArchitecture == Architecture.X64)
                    || (OperatingSystem.IsMacOS() && RuntimeInformation.OSArchitecture == Architecture.Arm64),
                    "No release is built for this machine, and install.sh refuses it by name.");
            }

            return new Installer(script, tree);
        }

        /// <summary>
        /// A feed holding a release of <paramref name="version"/>: its manifest, its checksums and this machine's archive,
        /// which holds the stand-in executable and <paramref name="notices"/> — by default <c>LICENSE</c> and
        /// <c>THIRD-PARTY-NOTICES</c>, each a regular file whose text names the version.
        /// </summary>
        public string Feed(string version, IReadOnlyList<Entry>? notices = null)
        {
            var feed = _tree.NewDirectory("feed");
            var archive = Path.Combine(feed, Asset);
            var standIn = StandIn(version);
            notices ??= [new Entry("LICENSE", LicenceText(version)), new Entry("THIRD-PARTY-NOTICES", NoticesText(version))];

            if (Windows)
            {
                using var zip = ZipFile.Open(archive, ZipArchiveMode.Create);
                using (var stream = zip.CreateEntry(ExecutableName).Open())
                {
                    stream.Write(standIn);
                }

                foreach (var entry in notices)
                {
                    Assert.True(entry.Kind is not EntryKind.SymbolicLink, "A zip carries no link that Expand-Archive makes into one.");
                    var written = zip.CreateEntry(entry.Kind is EntryKind.Directory ? entry.Name + "/" : entry.Name);
                    if (entry.Kind is EntryKind.File)
                    {
                        using var stream = written.Open();
                        stream.Write(Encoding.UTF8.GetBytes(entry.Text));
                    }
                }
            }
            else
            {
                using var file = File.Create(archive);
                using var gzip = new GZipStream(file, CompressionLevel.Fastest);
                using var tar = new TarWriter(gzip, TarEntryFormat.Pax);
                tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, ExecutableName)
                {
                    Mode = (UnixFileMode)0b111_101_101,
                    DataStream = new MemoryStream(standIn),
                });
                foreach (var entry in notices)
                {
                    tar.WriteEntry(entry.Kind switch
                    {
                        EntryKind.Directory => new PaxTarEntry(TarEntryType.Directory, entry.Name + "/") { Mode = (UnixFileMode)0b111_101_101 },
                        EntryKind.SymbolicLink => new PaxTarEntry(TarEntryType.SymbolicLink, entry.Name) { LinkName = entry.Text },
                        _ => new PaxTarEntry(TarEntryType.RegularFile, entry.Name)
                        {
                            Mode = (UnixFileMode)0b110_100_100,
                            DataStream = new MemoryStream(Encoding.UTF8.GetBytes(entry.Text)),
                        },
                    });
                }
            }

            File.WriteAllText(Path.Combine(feed, ReleaseAssets.Manifest), "{\n  \"schema\": 1,\n  \"version\": \"" + version + "\"\n}\n");
            File.WriteAllText(
                Path.Combine(feed, ReleaseAssets.Checksums),
                Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(archive))) + "  " + Asset + "\n");
            return feed;
        }

        /// <summary>
        /// The script, run to completion against <paramref name="feed"/> into <see cref="InstallDir"/>. The PATH is left
        /// alone unless <paramref name="modifyPath"/>, which only <c>install.sh</c> is run with: its profile is in a home
        /// directory of the test's own, where <c>install.ps1</c>'s PATH is the account's registry.
        /// </summary>
        public (int Exit, string Stdout, string Stderr) Run(string feed, params string[] arguments) => Run(feed, false, arguments);

        public (int Exit, string Stdout, string Stderr) Run(string feed, bool modifyPath, params string[] arguments)
        {
            var script = Path.Combine(RepositoryRoot(), "install", Script);
            if (Windows)
            {
                Assert.False(modifyPath, "install.ps1 is never run here with the PATH step: it writes the account's registry.");
                var translated = arguments.Select(argument => argument == "--version" ? "-Version" : argument);
                return RunToEnd(
                    Pwsh(),
                    new Dictionary<string, string> { ["LOCALAPPDATA"] = _tree.Root, ["POWERSHELL_TELEMETRY_OPTOUT"] = "1" },
                    ["-NoProfile", "-NonInteractive", "-File", script, "-InstallDir", InstallDir, "-NoModifyPath", "-Feed", feed, .. translated]);
            }

            string[] path = modifyPath ? [] : ["--no-modify-path"];
            return RunToEnd(
                "/bin/sh",
                new Dictionary<string, string> { ["HOME"] = Home },
                [script, "--feed", feed, "--install-dir", InstallDir, .. path, .. arguments]);
        }

        /// <summary>The executable a release of <paramref name="version"/> would hold, as far as the scripts can tell.</summary>
        private byte[] StandIn(string version) => Windows
            ? StandIns.GetOrAdd(version, Compile)
            : Encoding.UTF8.GetBytes($"#!/bin/sh\necho '{version}+standin'\n");

        private static byte[] Compile(string version)
        {
            var csc = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Microsoft.NET", "Framework64", "v4.0.30319", "csc.exe");
            Assert.SkipUnless(File.Exists(csc), "This Windows has no .NET Framework C# compiler to build the stand-in executable with.");

            var directory = Path.Combine(Path.GetTempPath(), "jason-cli-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                var source = Path.Combine(directory, "standin.cs");
                var output = Path.Combine(directory, "jason.exe");
                File.WriteAllText(source, "static class Program { static void Main() { System.Console.WriteLine(\"" + version + "+standin\"); } }\n");
                var (exit, stdout, stderr) = RunToEnd(csc, new Dictionary<string, string>(), ["/nologo", "/target:exe", "/out:" + output, source]);
                Assert.True(exit == 0, stdout + stderr);
                return File.ReadAllBytes(output);
            }
            finally
            {
                try
                {
                    Directory.Delete(directory, recursive: true);
                }
                catch (IOException)
                {
                }
            }
        }

        private static string Pwsh()
        {
            var pwsh = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                .Select(directory => Path.Combine(directory, "pwsh.exe"))
                .FirstOrDefault(File.Exists);
            Assert.SkipWhen(pwsh is null, "pwsh is not on this machine's PATH; every CI runner has it.");
            return pwsh!;
        }
    }

    /// <summary>One program, run to completion, with the environment a test gives it on top of this process's own.</summary>
    private static (int Exit, string Stdout, string Stderr) RunToEnd(string executable, IReadOnlyDictionary<string, string> environment, IEnumerable<string> arguments)
    {
        var start = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        foreach (var (name, value) in environment)
        {
            start.Environment[name] = value;
        }

        using var process = System.Diagnostics.Process.Start(start)!;
        process.StandardInput.Close();
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(TimeSpan.FromSeconds(120)))
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail($"{Path.GetFileName(executable)} did not finish in two minutes.");
        }

        return (process.ExitCode, stdout.Result, stderr.Result);
    }

    /// <summary>A script without its comments: the lines that run.</summary>
    private static string Code(string name) =>
        string.Join('\n', File.ReadAllText(Path.Combine(RepositoryRoot(), "install", name)).ReplaceLineEndings("\n").Split('\n')
            .Where(line => !line.TrimStart().StartsWith('#')));

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
