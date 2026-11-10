using System.Security.Cryptography;
using System.Text;
using Jason.Contracts.Update;
using static Jason.Runtime.Tests.Documentation.Workflows;

namespace Jason.Runtime.Tests.Documentation;

/// <summary>
/// The desktop's update feeds, latest.yml and latest-mac.yml, held to the files a release publishes beside them, and what
/// the Windows check reads out of the installer beyond the four files it holds to the release's signer.
/// </summary>
public partial class ReleaseWorkflowTests
{
    private static readonly string[] FeedCheck = [".github", "scripts", "Assert-UpdateFeed.ps1"];

    private const string WindowsDesktopVerify = "Verify the desktop's Windows installer and what it carries";

    private const string MacDesktopVerify = "Verify the desktop's Mac packages and what they carry";

    /// <summary>
    /// The feed check, run over a made-up folder of two packages and the feed that names them: it passes the feed as
    /// electron-builder writes it, printing each file with its digest and its size, and refuses, saying so, a feed whose
    /// size or digest for a file differs from the bytes beside it, whose path names a digest other than its file's, that
    /// names a file that is not there or one outside its folder, that names no file, a file without its size, or a
    /// version other than the release's, and one that does not list exactly the files the release expects in it: a Mac
    /// feed that names one architecture's zip alone, or a third file.
    /// </summary>
    [Theory]
    [InlineData("as built", 0, null)]
    [InlineData("a size off by one", 1, "is 1001 bytes, and the file beside it is 1000")]
    [InlineData("a digest of other bytes", 1, "has the SHA-512")]
    [InlineData("the path's digest of other bytes", 1, "says its path")]
    [InlineData("a file it names is not there", 1, "which is not beside it")]
    [InlineData("a file outside its folder", 1, "outside the folder it is in")]
    [InlineData("no file named", 1, "names no file")]
    [InlineData("a file without its size", 1, "without its sha512 and its size")]
    [InlineData("another version", 1, "does not name 0.34.0")]
    [InlineData("one architecture alone", 1, "lists jason-desktop-macos-arm64.zip, and it must list exactly jason-desktop-macos-arm64.zip, jason-desktop-macos-x64.zip.")]
    [InlineData("a third file", 1, "lists jason-desktop-macos-arm64.zip, jason-desktop-macos-universal.zip, jason-desktop-macos-x64.zip, and it must list exactly jason-desktop-macos-arm64.zip, jason-desktop-macos-x64.zip.")]
    public void The_feed_check_passes_a_feed_whose_every_file_is_the_one_beside_it_alone(string row, int exit, string? said)
    {
        using var tree = new TempTree();
        var first = Path.Combine(tree.Root, "jason-desktop-macos-arm64.zip");
        var second = Path.Combine(tree.Root, "jason-desktop-macos-x64.zip");
        File.WriteAllBytes(first, Enumerable.Repeat((byte)'a', 1000).ToArray());
        File.WriteAllBytes(second, Enumerable.Repeat((byte)'b', 2000).ToArray());
        var third = Path.Combine(tree.Root, "jason-desktop-macos-universal.zip");
        File.WriteAllBytes(third, Enumerable.Repeat((byte)'c', 3000).ToArray());
        string Digest(string file) => Convert.ToBase64String(SHA512.HashData(File.ReadAllBytes(file)));
        var other = Convert.ToBase64String(SHA512.HashData(Encoding.UTF8.GetBytes("other bytes")));

        var feed = new StringBuilder();
        feed.Append(row == "another version" ? "version: 0.33.0\n" : "version: 0.34.0\n");
        feed.Append("files:\n");
        if (row != "no file named")
        {
            feed.Append($"  - url: {(row == "a file outside its folder" ? "../jason-desktop-macos-arm64.zip" : "jason-desktop-macos-arm64.zip")}\n");
            feed.Append($"    sha512: {(row == "a digest of other bytes" ? other : Digest(first))}\n");
            if (row != "a file without its size") feed.Append($"    size: {(row == "a size off by one" ? 1001 : 1000)}\n");
            if (row != "one architecture alone")
            {
                feed.Append($"  - url: {(row == "a file it names is not there" ? "jason-desktop-macos-x86.zip" : "jason-desktop-macos-x64.zip")}\n");
                feed.Append($"    sha512: {Digest(second)}\n");
                feed.Append("    size: 2000\n");
                feed.Append("    blockMapSize: 312\n");
            }

            if (row == "a third file")
            {
                feed.Append("  - url: jason-desktop-macos-universal.zip\n");
                feed.Append($"    sha512: {Digest(third)}\n");
                feed.Append("    size: 3000\n");
            }
        }

        feed.Append("path: jason-desktop-macos-arm64.zip\n");
        feed.Append($"sha512: {(row == "the path's digest of other bytes" ? other : Digest(first))}\n");
        feed.Append("releaseDate: '2026-11-10T06:54:27.481Z'\n");
        var written = Path.Combine(tree.Root, "latest-mac.yml");
        File.WriteAllText(written, feed.ToString());

        var ran = Script(NoEnvironment, Path.Combine([RepositoryRoot(), .. FeedCheck]), "-Feed", written, "-Version", "0.34.0", "-Expect", "jason-desktop-macos-x64.zip,jason-desktop-macos-arm64.zip");

        Assert.True(ran.Exit == exit, $"{row}: exit {ran.Exit}\n{ran.Stdout}\n{ran.Stderr}");
        if (said is null)
        {
            Assert.Equal(string.Empty, ran.Stderr.Trim());
            Assert.Contains($"jason-desktop-macos-arm64.zip: sha512 {Digest(first)}, 1000 bytes, as latest-mac.yml says", ran.Stdout, StringComparison.Ordinal);
            Assert.Contains($"jason-desktop-macos-x64.zip: sha512 {Digest(second)}, 2000 bytes, as latest-mac.yml says", ran.Stdout, StringComparison.Ordinal);
        }
        else
        {
            Assert.Contains(said, ran.Stderr, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Both desktop checks hold their feed to the files beside it through the one script, as the feed's last check, and
    /// a refusal fails the step: the Windows step's call is followed by its exit code's check, and the Mac step, a bash
    /// one, fails on the script's exit through <c>||</c>. Each expects in its feed exactly the files of its platform a
    /// release publishes and electron-updater downloads: the Windows installer, and both Mac zips.
    /// </summary>
    [Fact]
    public void Both_desktop_checks_hold_their_feed_to_every_file_it_names()
    {
        var windows = Scalar(ActionStep(WindowsDesktopVerify), "run")!.Split('\n').Select(line => line.Trim()).ToList();
        var call = windows.IndexOf("./.github/scripts/Assert-UpdateFeed.ps1 -Feed \"$dist/latest.yml\" -Version $env:VERSION -Expect 'jason-desktop-windows-x64-setup.exe'");
        Assert.True(call >= 0, "The Windows check never holds latest.yml to the files it names.");
        Assert.Equal("if ($LASTEXITCODE -ne 0) { throw \"latest.yml does not describe the files beside it as they will be published.\" }", windows[call + 1]);

        var mac = Scalar(ActionStep(MacDesktopVerify), "run")!;
        Assert.Contains(
            "pwsh -NoProfile -File .github/scripts/Assert-UpdateFeed.ps1 -Feed \"$feed\" -Version \"$VERSION\" -Expect jason-desktop-macos-arm64.zip,jason-desktop-macos-x64.zip || { echo \"::error title=Desktop::latest-mac.yml does not describe the files beside it as they will be published.\"; exit 1; }",
            mac,
            StringComparison.Ordinal);

        // What the two calls expect is the release's own list of desktop files, by the kind electron-updater downloads.
        Assert.Equal(["jason-desktop-windows-x64-setup.exe"], ReleaseAssets.Desktop.Where(name => name.EndsWith(".exe", StringComparison.Ordinal)));
        Assert.Equal(["jason-desktop-macos-arm64.zip", "jason-desktop-macos-x64.zip"], ReleaseAssets.Desktop.Where(name => name.EndsWith(".zip", StringComparison.Ordinal)).Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// Both checks read the built package back against the configuration it was built from, rather than trust the
    /// configuration: <c>desktop/scripts/check-package.mjs</c> reads the app-update.yml electron-builder wrote into the
    /// package (the feed it asks, and on Windows the publisher an update must be signed by, without which electron-updater
    /// checks no signature) and the fuse wire of the binary Electron reads its fuses from. The Windows check reads the
    /// application the installer carries; the Mac check reads each architecture's application and the one inside its zip,
    /// which is what an update installs. Each call fails its step on a refusal. The script's own rows are
    /// <c>desktop/test/check-package.test.ts</c>.
    /// </summary>
    [Fact]
    public void Both_desktop_checks_read_the_feed_and_the_fuses_back_from_the_built_package()
    {
        Assert.True(File.Exists(Path.Combine(RepositoryRoot(), "desktop", "scripts", "check-package.mjs")), "desktop/scripts/check-package.mjs is not in the tree.");
        Assert.True(File.Exists(Path.Combine(RepositoryRoot(), "desktop", "test", "check-package.test.ts")), "desktop/test/check-package.test.ts is not in the tree.");

        var windows = Scalar(ActionStep(WindowsDesktopVerify), "run")!.Split('\n').Select(line => line.Trim()).ToList();
        var call = windows.IndexOf("node desktop/scripts/check-package.mjs --app $app --platform win32");
        Assert.True(call >= 0, "The Windows check never reads the feed and the fuses back from the application the installer carries.");
        Assert.Equal("if ($LASTEXITCODE -ne 0) { throw \"The application the installer carries is not what it was configured to be: its feed or its fuses.\" }", windows[call + 1]);
        Assert.True(call > windows.IndexOf("& $sevenZip x (Join-Path $opened '$PLUGINSDIR/app-64.7z') \"-o$app\" -y | Out-Null"), "The Windows check reads the application before the installer is opened.");

        var mac = Scalar(ActionStep(MacDesktopVerify), "run")!.Split('\n').Select(line => line.Trim()).ToList();
        var loop = mac.IndexOf("for arch in arm64 x64; do");
        var end = mac.IndexOf("done");
        Assert.True(loop >= 0 && end > loop, "The Mac check has no loop over both architectures.");
        foreach (var (app, what) in new[] { ("\"$app\"", "application"), ("\"$unzipped/Jason.app\"", "application inside the zip") })
        {
            var line = $"node desktop/scripts/check-package.mjs --app {app} --platform darwin || {{ echo \"::error title=Desktop::The $arch {what} is not what it was configured to be: its feed or its fuses.\"; exit 1; }}";
            var at = mac.IndexOf(line);
            Assert.True(at > loop && at < end, $"The Mac check never reads the feed and the fuses back from each architecture's {what}.");
        }

        Assert.True(
            mac.IndexOf("node desktop/scripts/check-package.mjs --app \"$unzipped/Jason.app\" --platform darwin || { echo \"::error title=Desktop::The $arch application inside the zip is not what it was configured to be: its feed or its fuses.\"; exit 1; }")
                > mac.IndexOf("ditto -x -k \"$zip\" \"$unzipped\""),
            "The Mac check reads the application inside the zip before the zip is opened.");
    }

    /// <summary>
    /// Beyond the four files held to the release's signer by name, the Windows check reads the application manifest out
    /// of the bundled jason.exe as the job read it out of the executable it archives, and holds every program and library
    /// the application carries to a signature signtool and Windows accept: the release's signer's, or, for a library that
    /// came signed by Microsoft, Microsoft's own, which the sign hook leaves as it came. The libraries the installer itself
    /// loads from its own archive, the nsis plugins electron-builder brings, are listed with their signers as a reading.
    /// </summary>
    [Fact]
    public void The_windows_check_reads_the_bundled_manifest_and_every_program_and_library_the_application_carries()
    {
        var run = Scalar(ActionStep(WindowsDesktopVerify), "run")!;
        var lines = run.Split('\n').Select(line => line.Trim()).ToList();

        var manifest = lines.IndexOf("./.github/scripts/Assert-ApplicationManifest.ps1 -Executable (Join-Path $app 'resources/bundle/jason.exe')");
        Assert.True(manifest >= 0, "The bundled jason.exe is never read for its application manifest.");
        Assert.Equal("if ($LASTEXITCODE -ne 0) { throw \"The bundled jason.exe does not declare what the runtime's logon start needs.\" }", lines[manifest + 1]);

        Assert.Contains("$carried = @(Get-ChildItem -LiteralPath $app -Recurse -File | Where-Object { $_.Extension -in '.exe', '.dll' } | Sort-Object FullName)", lines);
        Assert.Contains("if ($carried.Count -lt 3) { throw \"The application the installer carries holds $($carried.Count) programs and libraries, fewer than Jason.exe, jason.exe and Electron's libraries.\" }", lines);
        Assert.Contains("$publisher = if ($by -ceq 'Microsoft Corporation') { 'Microsoft Corporation' } else { 'ReplyApp Inc.' }", lines);
        Assert.Contains("./.github/scripts/Assert-WindowsSignature.ps1 -Executable $file.FullName -Publisher $publisher", lines);
        Assert.Contains("if ($LASTEXITCODE -ne 0) { throw \"$($file.Name) is signed by neither ReplyApp Inc. nor, as it came, Microsoft Corporation.\" }", lines);

        // The installer's own plugins are read, never refused: electron-builder brings them, and the installer that
        // loads them is itself held to the release's signer.
        Assert.Contains("foreach ($plugin in @(Get-ChildItem -LiteralPath (Join-Path $opened '$PLUGINSDIR') -File -Filter '*.dll' | Sort-Object Name)) {", lines);
        Assert.Contains("Write-Host \"nsis plugin $($plugin.Name): $($signature.Status), signed by $signer\"", lines);
    }
}
