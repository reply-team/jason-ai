using System.Text.RegularExpressions;
using Jason.Contracts.Update;
using YamlDotNet.RepresentationModel;
using static Jason.Runtime.Tests.Documentation.Workflows;

namespace Jason.Runtime.Tests.Documentation;

/// <summary>
/// The desktop's packages in a release: which package calls build them, under which conditions, from which tooling, signed
/// with which client, and uploaded as exactly the files the release publishes.
/// </summary>
public partial class ReleaseWorkflowTests
{
    private const string WindowsDesktop = "inputs.rid == 'win-x64' && inputs.sign-windows == 'true' && inputs.desktop == 'true'";
    private const string MacDesktop = "inputs.rid == 'osx-x64' && inputs.sign-macos == 'true' && inputs.desktop == 'true'";

    /// <summary>
    /// The action builds no desktop unless a call asks, and only the release asks, on the two calls that hold a signing
    /// identity the desktop is signed with: win-x64 and osx-x64. A pull request's build never builds one.
    /// </summary>
    [Fact]
    public void Only_the_release_s_win_x64_and_osx_x64_calls_build_the_desktop()
    {
        var input = Mapping(Mapping(Parse(Read(Action)), "inputs"), "desktop");
        Assert.Equal("false", Scalar(input, "default"));
        Assert.Equal("false", Scalar(input, "required"));

        var asking = PackagingCalls(Release)
            .Where(call => Scalar(Mapping(call, "with"), "desktop") is { } said && said != "false")
            .Select(call => (Rid: Scalar(Mapping(call, "with"), "rid"), Said: Scalar(Mapping(call, "with"), "desktop")))
            .OrderBy(call => call.Rid, StringComparer.Ordinal)
            .ToList();
        Assert.Equal([("osx-x64", "true"), ("win-x64", "true")], asking);

        Assert.All(PackagingCalls(Ci), call => Assert.Null(Node(Mapping(call, "with"), "desktop")));
    }

    /// <summary>
    /// Every step of the action that builds, checks or uploads a desktop runs only on its platform's call, with that
    /// platform's signing asked for and the desktop asked for, and stands while the identity is still held: after the
    /// last check of the call's own executable and before the step that forgets the identity.
    /// </summary>
    [Fact]
    public void The_desktop_s_steps_run_only_where_they_are_signed_and_before_the_identity_is_forgotten()
    {
        var steps = ActionSteps();
        var names = steps.Select(step => Scalar(step, "name") ?? Scalar(step, "uses") ?? "").ToList();

        // Every step that reads the desktop input runs under one of the two whole conditions, nothing looser.
        Assert.All(steps.Where(step => (Scalar(step, "if") ?? "").Contains("inputs.desktop", StringComparison.Ordinal)),
            step => Assert.Contains(Scalar(step, "if"), new[] { WindowsDesktop, MacDesktop }));
        // And so is every step that works in desktop/ or reads what it built.
        Assert.All(steps.Where(step => Scalar(step, "working-directory") == "desktop"
                || (Scalar(step, "run") ?? "").Contains("desktop/", StringComparison.Ordinal)
                || (Scalar(Node(step, "with") as YamlMappingNode ?? new YamlMappingNode(), "path") ?? "").Contains("desktop/", StringComparison.Ordinal)),
            step => Assert.Contains(Scalar(step, "if"), new[] { WindowsDesktop, MacDesktop }));

        foreach (var (condition, after, forget) in new[]
        {
            (WindowsDesktop, "Verify the signature of the Windows executable", "Forget the Azure sign-in"),
            (MacDesktop, "Assess the macOS executable as Gatekeeper does", "Forget the Developer ID identity"),
        })
        {
            var held = steps.Select((step, index) => (Step: step, Index: index)).Where(each => Scalar(each.Step, "if") == condition).ToList();
            Assert.True(held.Count >= 4, $"Fewer than four steps run under '{condition}'.");

            var last = names.IndexOf(after);
            var forgotten = names.IndexOf(forget);
            Assert.True(last >= 0, $"The action has no step named '{after}'.");
            Assert.True(forgotten >= 0, $"The action has no step named '{forget}'.");
            Assert.All(held, each => Assert.InRange(each.Index, last + 1, forgotten - 1));
            Assert.Equal(held.Count, held[^1].Index - held[0].Index + 1);
        }
    }

    /// <summary>
    /// The build tooling is the lockfile's, installed without any package's install scripts, and only the Windows build
    /// names the sign hook; the Mac build is signed by electron-builder's own signing with the identity the call holds.
    /// </summary>
    [Fact]
    public void The_desktop_is_built_from_the_lockfile_without_install_scripts()
    {
        var windows = ActionStep("Build the desktop's Windows installer, each file signed as it is made");
        var mac = ActionStep("Build the desktop's Mac packages, signed and notarized");
        foreach (var build in new[] { windows, mac })
        {
            var run = Scalar(build, "run") ?? "";
            Assert.Equal("desktop", Scalar(build, "working-directory"));
            Assert.Contains("npm ci --ignore-scripts\n", run, StringComparison.Ordinal);
            Assert.DoesNotContain("npm install", run, StringComparison.Ordinal);
            Assert.True(run.IndexOf("npm ci", StringComparison.Ordinal) < run.IndexOf("npm run package", StringComparison.Ordinal),
                "The desktop is packaged before its tooling is installed.");
        }

        Assert.Contains("$env:JASON_WINDOWS_SIGN = '1'", Scalar(windows, "run"), StringComparison.Ordinal);
        Assert.DoesNotContain("JASON_WINDOWS_SIGN", Scalar(mac, "run"), StringComparison.Ordinal);
        Assert.All(ActionSteps().Where(step => Scalar(step, "uses") == "actions/setup-node@v5"),
            step => Assert.Equal("24", Scalar(Mapping(step, "with"), "node-version")));
    }

    /// <summary>
    /// The Trusted Signing client is one pinned version, refused unless the package downloaded hashes to the pinned
    /// digest, and that check stands before the package is unpacked, so nothing of a different package is ever read.
    /// </summary>
    [Fact]
    public void The_signing_client_is_a_pinned_version_checked_before_it_is_unpacked()
    {
        var run = Scalar(Assert.Single(ActionSteps(), step => Scalar(step, "id") == "desktop-signing"), "run") ?? "";

        Assert.Matches(new Regex(@"^\$version = '\d+\.\d+\.\d+'$", RegexOptions.Multiline), run);
        Assert.Matches(new Regex("^\\$pinned = '[0-9a-f]{64}'$", RegexOptions.Multiline), run);
        Assert.Contains("$digest = (Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash.ToLowerInvariant()", run, StringComparison.Ordinal);

        var check = run.IndexOf("if ($digest -ne $pinned) { throw", StringComparison.Ordinal);
        var unpack = run.IndexOf("Expand-Archive", StringComparison.Ordinal);
        Assert.True(check >= 0, "The client's digest is never compared with the pinned one.");
        Assert.True(unpack > check, "The client is unpacked before its digest is checked.");
        Assert.Equal(1, Regex.Count(run, "Expand-Archive"));
    }

    /// <summary>
    /// What the two desktop uploads carry is exactly the desktop's part of what a release publishes, each under an
    /// artifact the release's download collects; the osx-x64 executable, which a release never publishes, is uploaded
    /// under a name that download does not collect.
    /// </summary>
    [Fact]
    public void The_desktop_s_uploads_are_exactly_the_desktop_files_a_release_publishes()
    {
        var uploads = ActionSteps()
            .Where(step => Scalar(step, "uses") == "actions/upload-artifact@v7" && (Scalar(step, "if") ?? "").Contains("inputs.desktop == 'true'", StringComparison.Ordinal))
            .ToList();
        Assert.Equal(["jason-desktop-osx-x64", "jason-desktop-win-x64"], uploads.Select(step => Scalar(Mapping(step, "with"), "name")).Order(StringComparer.Ordinal));

        var files = uploads
            .SelectMany(step => (Scalar(Mapping(step, "with"), "path") ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToList();
        Assert.All(files, file => Assert.StartsWith("desktop/dist/", file, StringComparison.Ordinal));
        Assert.Equal(ReleaseAssets.Desktop.Order(StringComparer.Ordinal), files.Select(file => file["desktop/dist/".Length..]).Order(StringComparer.Ordinal));
        Assert.All(uploads, step => Assert.Equal("error", Scalar(Mapping(step, "with"), "if-no-files-found")));

        var download = Assert.Single(Read(Release).Split('\n'), line => line.Trim().StartsWith("pattern:", StringComparison.Ordinal)).Trim()["pattern:".Length..].Trim();
        Assert.Equal("jason-*", download);
        var collected = new Regex("^" + Regex.Escape(download).Replace(@"\*", ".*", StringComparison.Ordinal) + "$");
        Assert.All(uploads, step => Assert.Matches(collected, Scalar(Mapping(step, "with"), "name") ?? ""));

        var x64 = Assert.Single(PackagingCalls(Release), call => Scalar(Mapping(call, "with"), "rid") == "osx-x64");
        Assert.DoesNotMatch(collected, Scalar(Mapping(x64, "with"), "artifact") ?? "");
    }

    /// <summary>
    /// What the notes and the release guide tell a person about the desktop beyond its files: the three platforms, that the
    /// test suite does not run on an Intel Mac, that it updates to stable releases only, that an update restarts the runtime
    /// the application carries and leaves another alone, how the copy an update keeps is used to go back, and that an
    /// uninstall stops that runtime first or removes nothing.
    /// </summary>
    [Fact]
    public void The_notes_and_the_guide_say_how_the_desktop_updates_goes_back_and_where_it_is_tested()
    {
        var notes = Regex.Replace(Read(Notes), @"\s+", " ");
        foreach (var said in (string[])
        [
            "in three packages: Windows on x64, macOS on Apple silicon and macOS on Intel.",
            "the test suite does not run on an Intel Mac.",
            "on your word, and to stable releases only.",
            "Installing an update restarts the runtime: the runtime the application carries is stopped first, with `jason runtime stop`, as the menu's Restart Jason and Its Runtime to Install says, and the new version starts it again when it opens.",
            "A runtime that does not stop leaves the update uninstalled, saying why, and a runtime started from another `jason` is never stopped.",
            "the application as it was is copied under `~/.jason/desktop/rollback/<version>/`, and the Jason menu names that copy until the next update replaces it.",
            "to go back, quit Jason and put that folder back in the place of the installed application by hand.",
            "Uninstalling the desktop on Windows stops the runtime the application carries first, and removes nothing when that runtime does not stop.",
        ])
        {
            Assert.Contains(said, notes, StringComparison.Ordinal);
        }

        var guide = Regex.Replace(Read(["docs", "release-and-update.md"]), @"\s+", " ");
        foreach (var said in (string[])
        [
            "on an Intel Mac `jason update apply` refuses with `update_not_updatable` and the `jason` the Intel application carries moves only with the desktop's own update; the test suite does not run on an Intel Mac.",
            "the desktop updates to stable releases only.",
            "Installing a desktop update restarts the runtime.",
            "the descriptor's `installed_image` is the application's own `jason`",
            "going back is quitting Jason and putting that folder back in the place of the installed application by hand",
        ])
        {
            Assert.Contains(said, guide, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The desktop's files leave manifest.json as it was: the release's own line, run over a folder that holds them and
    /// again over one that does not with their names taken off <c>-Plugins</c>, writes the same manifest but for the time
    /// it was written. So an updater reads a release with the desktop exactly as it read the one before it, from 0.19.0
    /// on, the manifest's platforms stay the three a <c>jason update</c> applies, and no desktop file is named in it.
    /// </summary>
    [Fact]
    public void The_desktop_s_files_leave_the_manifest_an_updater_reads_as_it_was()
    {
        var line = Assert.Single(Read(Release).Split('\n'), text => text.Contains("./.github/scripts/Write-Manifest.ps1", StringComparison.Ordinal)).Trim();
        var invocation = line[line.IndexOf("./.github/scripts/Write-Manifest.ps1", StringComparison.Ordinal)..];
        using var first = new TempTree();
        using var second = new TempTree();

        var with = Staged(first);
        var (exit, _, stderr) = Pwsh(NoEnvironment, "-Command", ExpandForThisMachine(invocation, with));
        Assert.True(exit == 0, stderr);

        var without = Staged(second);
        foreach (var desktop in ReleaseAssets.Desktop)
        {
            File.Delete(Path.Combine(without, desktop));
        }

        var stripped = invocation.Replace("," + string.Join(',', ReleaseAssets.Desktop) + " ", " ", StringComparison.Ordinal);
        Assert.NotEqual(invocation, stripped);
        (exit, _, stderr) = Pwsh(NoEnvironment, "-Command", ExpandForThisMachine(stripped, without));
        Assert.True(exit == 0, stderr);

        var written = File.ReadAllText(Path.Combine(with, ReleaseAssets.Manifest));
        Assert.Equal(WithoutTheMoment(File.ReadAllText(Path.Combine(without, ReleaseAssets.Manifest))), WithoutTheMoment(written));
        Assert.All(ReleaseAssets.Desktop, desktop => Assert.DoesNotContain(desktop, written, StringComparison.Ordinal));

        var manifest = UpdateManifest.Read(written);
        Assert.Equal(1, manifest.Schema);
        Assert.Equal(["linux-x64", "osx-arm64", "win-x64"], manifest.Artifacts.Keys.Order(StringComparer.Ordinal));

        static string WithoutTheMoment(string json) =>
            Regex.Replace(json, "\"published_at\": \"[^\"]+\"", "\"published_at\": \"\"");
    }

    /// <summary>
    /// One version across what a desktop package carries: each bundle is prepared at the release's version from the
    /// executable the call signed, which writes that version into its <c>version.json</c>; the package is built at the
    /// same version, which the configuration refuses to build unless <c>version.json</c> says it too
    /// (<c>checkBundle</c>, held by the desktop's own rows); and the executable itself is made to say it, by the smoke
    /// test of each call that publishes one and by the start under Rosetta for osx-x64, before any of it is published.
    /// </summary>
    [Fact]
    public void A_desktop_package_its_version_json_and_the_jason_it_carries_are_one_version()
    {
        var windows = Scalar(ActionStep("Build the desktop's Windows installer, each file signed as it is made"), "run")!;
        Assert.Equal("${{ inputs.version }}", Scalar(Mapping(ActionStep("Build the desktop's Windows installer, each file signed as it is made"), "env"), "VERSION"));
        Assert.Contains("node scripts/prepare.mjs --rid win-x64 --output bundles/x64 --version $env:VERSION --executable (Join-Path $env:GITHUB_WORKSPACE $env:EXECUTABLE)", windows, StringComparison.Ordinal);
        Assert.Contains("$env:JASON_VERSION = $env:VERSION", windows, StringComparison.Ordinal);

        var mac = Scalar(ActionStep("Build the desktop's Mac packages, signed and notarized"), "run")!;
        Assert.Equal("${{ inputs.version }}", Scalar(Mapping(ActionStep("Build the desktop's Mac packages, signed and notarized"), "env"), "VERSION"));
        Assert.Contains("node scripts/prepare.mjs --rid osx-arm64 --output bundles/arm64 --version \"$VERSION\" --executable \"$arm64\"", mac, StringComparison.Ordinal);
        Assert.Contains("node scripts/prepare.mjs --rid osx-x64 --output bundles/x64 --version \"$VERSION\" --executable \"$GITHUB_WORKSPACE/$EXECUTABLE\"", mac, StringComparison.Ordinal);
        Assert.Contains("JASON_VERSION=\"$VERSION\"", mac, StringComparison.Ordinal);

        var configuration = Read(["desktop", "electron-builder.config.mjs"]);
        Assert.Contains("beforePack: async (context) => checkBundle(bundleFolder(context.arch), version, context.electronPlatformName),", configuration, StringComparison.Ordinal);
        Assert.Contains("return configuration({ version: releaseVersion(process.env['JASON_VERSION']), sign });", configuration, StringComparison.Ordinal);

        // The executable says the version itself: each call's smoke test refuses another, and so does the Rosetta start.
        var release = Read(Release);
        Assert.Equal(3, Regex.Count(release, Regex.Escape("if (($version -split '\\+')[0] -ne $env:VERSION) { throw \"jason --version says '$version', and the tag says $env:VERSION\" }")));
        Assert.Contains("[ \"${said%%+*}\" = \"$VERSION\" ] || { echo \"::error title=osx-x64::The osx-x64 executable says '$said', and the tag says $VERSION.\"; exit 1; }", release, StringComparison.Ordinal);
    }
}
