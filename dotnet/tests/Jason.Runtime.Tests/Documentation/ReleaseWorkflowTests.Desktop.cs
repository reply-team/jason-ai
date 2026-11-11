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
    private const string LinuxDesktop = "inputs.rid == 'linux-x64' && inputs.desktop == 'true'";

    private const string LinuxDesktopBuild = "Build the desktop's Linux packages";
    private const string LinuxDesktopVerify = "Verify the desktop's Linux packages and what they carry";
    private const string LinuxDesktopStart = "Start the desktop's Linux packages under a virtual display";

    /// <summary>
    /// The action builds no desktop unless a call asks. The release asks on the two calls that hold a signing identity the
    /// desktop is signed with, win-x64 and osx-x64, and on its Linux leg, whose matrix is linux-x64 alone and which signs
    /// nothing. CI asks once, in its desktop job, for linux-x64, under an artifact of that job's own; no platform build of
    /// a pull request builds one.
    /// </summary>
    [Fact]
    public void Only_the_release_s_three_desktop_calls_and_ci_s_desktop_job_build_the_desktop()
    {
        var input = Mapping(Mapping(Parse(Read(Action)), "inputs"), "desktop");
        Assert.Equal("false", Scalar(input, "default"));
        Assert.Equal("false", Scalar(input, "required"));

        // The Linux leg's call names its platform by the matrix, which holds linux-x64 alone.
        var legs = Assert.IsType<YamlSequenceNode>(Node(Mapping(MatrixJob(), "strategy"), "matrix") is YamlMappingNode matrix ? Node(matrix, "include") : null);
        var leg = Assert.IsType<YamlMappingNode>(Assert.Single(legs.Children));
        Assert.Equal("linux-x64", Scalar(leg, "rid"));
        string? Rid(YamlMappingNode call) => Scalar(Mapping(call, "with"), "rid") is "${{ matrix.rid }}" ? Scalar(leg, "rid") : Scalar(Mapping(call, "with"), "rid");

        var asking = PackagingCalls(Release)
            .Where(call => Scalar(Mapping(call, "with"), "desktop") is { } said && said != "false")
            .Select(call => (Rid: Rid(call), Said: Scalar(Mapping(call, "with"), "desktop")))
            .OrderBy(call => call.Rid, StringComparer.Ordinal)
            .ToList();
        Assert.Equal([("linux-x64", "true"), ("osx-x64", "true"), ("win-x64", "true")], asking);

        var ci = PackagingCalls(Ci).Where(call => Node(Mapping(call, "with"), "desktop") is not null).ToList();
        var desktop = Assert.Single(ci);
        Assert.Equal("linux-x64", Scalar(Mapping(desktop, "with"), "rid"));
        Assert.Equal("true", Scalar(Mapping(desktop, "with"), "desktop"));
        Assert.Equal("ci-desktop-linux-x64", Scalar(Mapping(desktop, "with"), "artifact"));
        var inJob = Assert.Single(JobSteps(Mapping(Mapping(Parse(Read(Ci)), "jobs"), "desktop")), step => Scalar(step, "uses") == "./.github/actions/package");
        Assert.Equal("true", Scalar(Mapping(inJob, "with"), "desktop"));
    }

    /// <summary>
    /// Every step of the action that builds, checks or uploads a desktop runs only on its platform's call, with that
    /// platform's signing asked for and the desktop asked for. Of them, only what the identity is for stands while it is
    /// held: on Windows the signing client's fetch, and on both the build, which signs, and the check of what it built,
    /// one after another, right after the last check of the call's own executable and right before the step that forgets
    /// the identity. Node is set up before the identity is taken, so the action that installs it never runs beside the
    /// identity, and the upload, which needs none, comes right after the identity is forgotten.
    /// </summary>
    [Fact]
    public void Only_the_desktop_s_build_and_its_check_run_while_the_identity_is_held()
    {
        var steps = ActionSteps();
        var names = steps.Select(step => Scalar(step, "name") ?? Scalar(step, "uses") ?? "").ToList();

        // Every step that reads the desktop input runs under one of the three whole conditions, nothing looser.
        Assert.All(steps.Where(step => (Scalar(step, "if") ?? "").Contains("inputs.desktop", StringComparison.Ordinal)),
            step => Assert.Contains(Scalar(step, "if"), new[] { WindowsDesktop, MacDesktop, LinuxDesktop }));
        // And so is every step that works in desktop/ or reads what it built.
        Assert.All(steps.Where(step => Scalar(step, "working-directory") == "desktop"
                || (Scalar(step, "run") ?? "").Contains("desktop/", StringComparison.Ordinal)
                || (Scalar(Node(step, "with") as YamlMappingNode ?? new YamlMappingNode(), "path") ?? "").Contains("desktop/", StringComparison.Ordinal)),
            step => Assert.Contains(Scalar(step, "if"), new[] { WindowsDesktop, MacDesktop, LinuxDesktop }));

        // The Linux call holds no identity: its desktop steps are Node, the build, the check, the start and the upload, one
        // after another, and none of them, nor any step of a Linux call, names a secret or a signing input.
        var linux = steps.Select((step, index) => (Step: step, Index: index)).Where(each => Scalar(each.Step, "if") == LinuxDesktop).ToList();
        Assert.Equal(Enumerable.Range(linux[0].Index, linux.Count), linux.Select(each => each.Index));
        Assert.Equal(
            ["actions/setup-node@v5", LinuxDesktopBuild, LinuxDesktopVerify, LinuxDesktopStart, "actions/upload-artifact@v7"],
            linux.Select(each => Scalar(each.Step, "uses") ?? Scalar(each.Step, "name")));
        Assert.All(linux, each => Assert.DoesNotMatch(@"secrets\.|inputs\.(sign|apple|azure)|IDENTITY|KEYCHAIN|JASON_WINDOWS_SIGN", string.Join('\n', Scalars(each.Step))));

        foreach (var (condition, taken, after, window, forget) in new[]
        {
            (WindowsDesktop, "Sign in to Azure with this job's OIDC token", "Verify the signature of the Windows executable",
                new[] { "Fetch the Trusted Signing client and name the account it signs with", "Build the desktop's Windows installer, each file signed as it is made", WindowsDesktopVerify },
                "Forget the Azure sign-in"),
            (MacDesktop, "Import the Developer ID identity into a keychain of this job's own", "Assess the macOS executable as Gatekeeper does",
                new[] { "Build the desktop's Mac packages, signed and notarized", MacDesktopVerify },
                "Forget the Developer ID identity"),
        })
        {
            int At(string name)
            {
                var index = names.IndexOf(name);
                Assert.True(index >= 0, $"The action has no step named '{name}'.");
                return index;
            }

            var held = steps.Select((step, index) => (Step: step, Index: index)).Where(each => Scalar(each.Step, "if") == condition).Select(each => each.Index).ToList();
            var node = Assert.Single(held, index => Scalar(steps[index], "uses") == "actions/setup-node@v5");
            var upload = Assert.Single(held, index => Scalar(steps[index], "uses") == "actions/upload-artifact@v7");
            var inside = window.Select(At).ToList();

            Assert.Equal([node, .. inside, upload], held);
            Assert.True(node < At(taken), $"Node is set up under '{condition}' after the identity is taken.");
            Assert.Equal(Enumerable.Range(At(after) + 1, inside.Count), inside);
            Assert.Equal(At(forget) - 1, inside[^1]);
            Assert.Equal(At(forget) + 1, upload);
        }
    }

    /// <summary>
    /// The build tooling is the lockfile's, installed without any package's install scripts, and only the Windows build
    /// names the sign hook; the Mac build is signed by electron-builder's own signing with the identity the call holds.
    /// The only package-manager commands each build runs are exactly those two, in that order: no second install, no
    /// rebuild, no <c>npm exec</c> or <c>npx</c>. What <c>npm run package</c> runs, and that nothing runs around it or
    /// in a rebuild of the dependencies, is held by the desktop's own packaging rows.
    /// </summary>
    [Fact]
    public void The_desktop_is_built_from_the_lockfile_without_install_scripts()
    {
        var windows = ActionStep("Build the desktop's Windows installer, each file signed as it is made");
        var mac = ActionStep("Build the desktop's Mac packages, signed and notarized");
        var linux = ActionStep(LinuxDesktopBuild);
        foreach (var (build, package) in new[] { (windows, "npm run package -- --win --x64"), (mac, "npm run package -- --mac --arm64 --x64"), (linux, "npm run package -- --linux --x64") })
        {
            var run = Scalar(build, "run") ?? "";
            Assert.Equal("desktop", Scalar(build, "working-directory"));
            Assert.Contains("npm ci --ignore-scripts\n", run, StringComparison.Ordinal);
            Assert.DoesNotContain("npm install", run, StringComparison.Ordinal);
            Assert.True(run.IndexOf("npm ci", StringComparison.Ordinal) < run.IndexOf("npm run package", StringComparison.Ordinal),
                "The desktop is packaged before its tooling is installed.");

            // Each command a line runs, a continued line read as one, from the first package-manager word on; a line that
            // only says what failed is no command.
            var commands = run.Replace("\\\n", " ", StringComparison.Ordinal).Split('\n')
                .Select(line => line.Trim())
                .Where(line => !Regex.IsMatch(line, "^if \\(\\$LASTEXITCODE -ne 0\\) \\{ throw \"[^\"]*\" \\}$"))
                .Select(line => Regex.Match(line, @"(?<![\w./-])(npm|npx|pnpm|yarn|corepack)(?![\w./-]).*$"))
                .Where(match => match.Success)
                .Select(match => match.Value)
                .ToList();
            Assert.Equal(["npm ci --ignore-scripts", package], commands);
        }

        Assert.Contains("$env:JASON_WINDOWS_SIGN = '1'", Scalar(windows, "run"), StringComparison.Ordinal);
        Assert.DoesNotContain("JASON_WINDOWS_SIGN", Scalar(mac, "run"), StringComparison.Ordinal);
        Assert.DoesNotContain("JASON_WINDOWS_SIGN", Scalar(linux, "run"), StringComparison.Ordinal);
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
        Assert.Contains("microsoft.artifactsigning.client/$version/microsoft.artifactsigning.client.$version.nupkg\" -OutFile $package", run, StringComparison.Ordinal);

        // What the sign hook runs is what this step fetched and checked, and nothing else.
        var hook = Mapping(ActionStep("Build the desktop's Windows installer, each file signed as it is made"), "env");
        Assert.Equal("${{ steps.desktop-signing.outputs.signtool }}", Scalar(hook, "JASON_SIGNTOOL"));
        Assert.Equal("${{ steps.desktop-signing.outputs.dlib }}", Scalar(hook, "JASON_SIGNING_DLIB"));
        Assert.Equal("${{ steps.desktop-signing.outputs.metadata }}", Scalar(hook, "JASON_SIGNING_METADATA"));
    }

    /// <summary>
    /// The signing client's one credential is the Azure CLI's sign-in: the metadata it is handed excludes, by name, the
    /// same nine sources the signing action excludes, and names the account and the profile the action signs with.
    /// </summary>
    [Fact]
    public void The_signing_client_tries_the_azure_cli_s_sign_in_alone()
    {
        var run = Scalar(Assert.Single(ActionSteps(), step => Scalar(step, "id") == "desktop-signing"), "run")!;
        var lines = run.Split('\n').Select(line => line.Trim()).ToList();

        var listed = Assert.Single(lines, line => line.StartsWith("$excluded = @(", StringComparison.Ordinal));
        var excluded = Regex.Matches(listed, "'([A-Za-z]+)'").Select(match => match.Groups[1].Value).ToList();
        Assert.Equal(@"$excluded = @(" + string.Join(", ", excluded.Select(name => $"'{name}'")) + ")", listed);

        // The action's own exclusions, by its input names: exclude-environment-credential is EnvironmentCredential.
        var action = Mapping(ActionStep("Sign the Windows executable with Artifact Signing"), "with");
        var byTheAction = action.Children
            .Select(pair => (Name: ((YamlScalarNode)pair.Key).Value!, Value: (pair.Value as YamlScalarNode)?.Value))
            .Where(pair => pair.Name.StartsWith("exclude-", StringComparison.Ordinal) && pair.Value == "true")
            .Select(pair => string.Concat(pair.Name["exclude-".Length..].Split('-').Select(word => char.ToUpperInvariant(word[0]) + word[1..])))
            .Select(name => name.Replace("Powershell", "PowerShell", StringComparison.Ordinal))
            .ToList();
        Assert.Equal(9, byTheAction.Count);
        Assert.Equal(byTheAction.Order(StringComparer.Ordinal), excluded.Order(StringComparer.Ordinal));
        Assert.DoesNotContain("AzureCliCredential", excluded);
        Assert.Equal("false", Scalar(action, "exclude-azure-cli-credential"));

        Assert.Contains("$named = [ordered]@{ Endpoint = $env:SIGNING_ENDPOINT; CodeSigningAccountName = $env:SIGNING_ACCOUNT; CertificateProfileName = $env:SIGNING_PROFILE; ExcludeCredentials = $excluded }", lines);
        Assert.Contains("Set-Content -LiteralPath $metadata -Value ($named | ConvertTo-Json) -NoNewline", lines);
    }

    /// <summary>
    /// What the three desktop uploads carry is exactly the desktop's part of what a release publishes, each under an
    /// artifact the release's download collects; the osx-x64 executable, which a release never publishes, is uploaded
    /// under a name that download does not collect, and so are CI's Linux packages and the archive its desktop job makes.
    /// </summary>
    [Fact]
    public void The_desktop_s_uploads_are_exactly_the_desktop_files_a_release_publishes()
    {
        var uploads = ActionSteps()
            .Where(step => Scalar(step, "uses") == "actions/upload-artifact@v7" && (Scalar(step, "if") ?? "").Contains("inputs.desktop == 'true'", StringComparison.Ordinal))
            .ToList();
        const string linuxName = "${{ inputs.artifact != '' && format('{0}-desktop', inputs.artifact) || 'jason-desktop-linux-x64' }}";
        Assert.Equal([linuxName, "jason-desktop-osx-x64", "jason-desktop-win-x64"], uploads.Select(step => Scalar(Mapping(step, "with"), "name")).Order(StringComparer.Ordinal));

        var files = uploads
            .SelectMany(step => (Scalar(Mapping(step, "with"), "path") ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToList();
        Assert.All(files, file => Assert.StartsWith("desktop/dist/", file, StringComparison.Ordinal));
        Assert.Equal(ReleaseAssets.Desktop.Order(StringComparer.Ordinal), files.Select(file => file["desktop/dist/".Length..]).Order(StringComparer.Ordinal));
        Assert.All(uploads, step => Assert.Equal("error", Scalar(Mapping(step, "with"), "if-no-files-found")));

        var download = Assert.Single(Read(Release).Split('\n'), line => line.Trim().StartsWith("pattern:", StringComparison.Ordinal)).Trim()["pattern:".Length..].Trim();
        Assert.Equal("jason-*", download);
        var collected = new Regex("^" + Regex.Escape(download).Replace(@"\*", ".*", StringComparison.Ordinal) + "$");
        // The release names no artifact on its Linux leg, so its Linux packages go up as jason-desktop-linux-x64.
        Assert.All(uploads, step => Assert.Matches(collected, (Scalar(Mapping(step, "with"), "name") ?? "").Replace(linuxName, "jason-desktop-linux-x64", StringComparison.Ordinal)));
        var leg = Assert.Single(PackagingCalls(Release), call => Scalar(Mapping(call, "with"), "rid") == "${{ matrix.rid }}");
        Assert.Null(Node(Mapping(leg, "with"), "artifact"));

        var x64 = Assert.Single(PackagingCalls(Release), call => Scalar(Mapping(call, "with"), "rid") == "osx-x64");
        Assert.DoesNotMatch(collected, Scalar(Mapping(x64, "with"), "artifact") ?? "");

        // CI's desktop job names its own artifact, so neither its archive nor its packages are what a jason-* download
        // collects: install.sh's job assembles its feed from exactly that pattern.
        var ci = Assert.Single(PackagingCalls(Ci), call => Node(Mapping(call, "with"), "desktop") is not null);
        var named = Scalar(Mapping(ci, "with"), "artifact") ?? "";
        Assert.DoesNotMatch(collected, named);
        Assert.DoesNotMatch(collected, named + "-desktop");
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
            "in five packages: Windows on x64, macOS on Apple silicon, macOS on Intel, and Linux on x64 as a deb and as an AppImage.",
            "the test suite does not run on an Intel Mac.",
            "on your word, and to stable releases only.",
            "Installing an update restarts the runtime: the runtime the application carries is stopped first, with `jason runtime stop`, as the menu's Restart Jason and Its Runtime to Install says, and the new version starts it again when it opens.",
            "A runtime that does not stop leaves the update uninstalled, saying why, and a runtime started from another `jason` is never stopped.",
            "the application as it was is copied under `~/.jason/desktop/rollback/<version>/`, and the Jason menu names that copy until the next update replaces it.",
            "to go back, quit Jason and put that folder back in the place of the installed application by hand.",
            "Uninstalling the desktop on Windows stops the runtime the application carries first, and removes nothing when that runtime does not stop.",
            "On Linux, choose Remove What Jason Placed… in the Jason menu first:",
            "What stays is the data directory (`~/.jason`), the harness root (`~/Jason`) and the updater's cache, `~/.cache/jason-desktop-updater`.",
            "On macOS, choose Remove What Jason Placed… in the Jason menu first, then move Jason.app to the Trash.",
            "What stays is the data directory (`~/.jason`), the harness root (`~/Jason`) and the updater's cache, `~/Library/Caches/jason-desktop-updater`.",
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
            "Removing the desktop on Linux starts with Remove What Jason Placed… in the Jason menu,",
            "and the data directory, the harness root and the updater's cache, `~/.cache/jason-desktop-updater`, stay.",
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
        Assert.Equal("${{ steps.publish.outputs.executable }}", Scalar(Mapping(ActionStep("Build the desktop's Windows installer, each file signed as it is made"), "env"), "EXECUTABLE"));
        Assert.Contains("node scripts/prepare.mjs --rid win-x64 --output bundles/x64 --version $env:VERSION --executable (Join-Path $env:GITHUB_WORKSPACE $env:EXECUTABLE)", windows, StringComparison.Ordinal);
        Assert.Contains("$env:JASON_VERSION = $env:VERSION", windows, StringComparison.Ordinal);

        var mac = Scalar(ActionStep("Build the desktop's Mac packages, signed and notarized"), "run")!;
        Assert.Equal("${{ inputs.version }}", Scalar(Mapping(ActionStep("Build the desktop's Mac packages, signed and notarized"), "env"), "VERSION"));
        Assert.Equal("${{ steps.publish.outputs.executable }}", Scalar(Mapping(ActionStep("Build the desktop's Mac packages, signed and notarized"), "env"), "EXECUTABLE"));
        Assert.Contains("node scripts/prepare.mjs --rid osx-arm64 --output bundles/arm64 --version \"$VERSION\" --executable \"$arm64\"", mac, StringComparison.Ordinal);
        Assert.Contains("node scripts/prepare.mjs --rid osx-x64 --output bundles/x64 --version \"$VERSION\" --executable \"$GITHUB_WORKSPACE/$EXECUTABLE\"", mac, StringComparison.Ordinal);
        Assert.Contains("JASON_VERSION=\"$VERSION\"", mac, StringComparison.Ordinal);

        var linux = Scalar(ActionStep(LinuxDesktopBuild), "run")!;
        Assert.Equal("${{ inputs.version }}", Scalar(Mapping(ActionStep(LinuxDesktopBuild), "env"), "VERSION"));
        Assert.Equal("${{ steps.publish.outputs.executable }}", Scalar(Mapping(ActionStep(LinuxDesktopBuild), "env"), "EXECUTABLE"));
        Assert.Contains("node scripts/prepare.mjs --rid linux-x64 --output bundles/x64 --version \"$VERSION\" --executable \"$GITHUB_WORKSPACE/$EXECUTABLE\"", linux, StringComparison.Ordinal);
        Assert.Contains("JASON_VERSION=\"$VERSION\" npm run package -- --linux --x64", linux, StringComparison.Ordinal);

        var configuration = Read(["desktop", "electron-builder.config.mjs"]);
        Assert.Contains("            checkBundle(bundleFolder(context.arch), version, context.electronPlatformName);\n", configuration, StringComparison.Ordinal);
        Assert.Contains("beforePack: async (context) => {", configuration, StringComparison.Ordinal);
        Assert.Contains("return configuration({ version: releaseVersion(process.env['JASON_VERSION']), sign });", configuration, StringComparison.Ordinal);

        // The executable says the version itself: each call's smoke test refuses another, and so does the Rosetta start.
        var release = Read(Release);
        Assert.Equal(3, Regex.Count(release, Regex.Escape("if (($version -split '\\+')[0] -ne $env:VERSION) { throw \"jason --version says '$version', and the tag says $env:VERSION\" }")));
        Assert.Contains("[ \"${said%%+*}\" = \"$VERSION\" ] || { echo \"::error title=osx-x64::The osx-x64 executable says '$said', and the tag says $VERSION.\"; exit 1; }", release, StringComparison.Ordinal);
    }

    /// <summary>
    /// The deb's file name, wherever the desktop names it to a person (the update's going-back notice and the sandbox
    /// refusal both take <c>debFile</c> from <c>desktop/src/updates.ts</c>, and the README's apt line), is the name of a
    /// file a release publishes.
    /// </summary>
    [Fact]
    public void The_deb_the_desktop_names_is_a_file_a_release_publishes()
    {
        var updates = Read(["desktop", "src", "updates.ts"]);
        var named = Regex.Match(updates, "^export const debFile = '([^']+)';$", RegexOptions.Multiline);
        Assert.True(named.Success, "desktop/src/updates.ts names no debFile.");
        Assert.Contains(named.Groups[1].Value, ReleaseAssets.Published);

        var readme = Read(["desktop", "README.md"]);
        var apt = Regex.Matches(readme, @"sudo apt install \./(?<file>[A-Za-z0-9._-]+\.deb)");
        Assert.NotEmpty(apt);
        Assert.All(apt, match => Assert.Contains(match.Groups["file"].Value, ReleaseAssets.Published));

        Assert.Contains("${debFile}", Read(["desktop", "src", "sandbox.ts"]), StringComparison.Ordinal);
    }

    // --- the Linux packages: what the check reads back, how each is started, and CI's desktop job ---------------------

    private static List<string> LinuxLines(string step) =>
        [.. Scalar(ActionStep(step), "run")!.Split('\n').Select(line => line.Trim())];

    /// <summary>
    /// The Linux check holds its feed to the two files electron-updater downloads from it, the deb and the AppImage, which
    /// are the release's own Linux files but the feed, through the same script the other two checks run, as its last line;
    /// a refusal fails the step.
    /// </summary>
    [Fact]
    public void The_linux_check_holds_its_feed_to_the_deb_and_the_appimage()
    {
        var lines = LinuxLines(LinuxDesktopVerify);
        Assert.Equal(
            "pwsh -NoProfile -File .github/scripts/Assert-UpdateFeed.ps1 -Feed \"$feed\" -Version \"$VERSION\" -Expect jason-desktop-linux-x64.deb,jason-desktop-linux-x64.AppImage || { echo \"::error title=Desktop::latest-linux.yml does not describe the files beside it as they will be published.\"; exit 1; }",
            lines.Last(line => line.Length > 0));
        Assert.Contains("feed=\"$dist/latest-linux.yml\"", lines);
        Assert.Equal(
            ["jason-desktop-linux-x64.AppImage", "jason-desktop-linux-x64.deb"],
            ReleaseAssets.Desktop.Where(name => name.StartsWith("jason-desktop-linux-", StringComparison.Ordinal)).Order(StringComparer.Ordinal));
        Assert.Contains("latest-linux.yml", ReleaseAssets.Desktop);
    }

    /// <summary>
    /// The deb is read for its control fields (the package jason-desktop, the release's version as dpkg spells it, amd64),
    /// unpacked, and its /opt/Jason held by check-package to the configuration as a deb, which must carry package-type;
    /// the AppImage's runtime is refused when it names a shared library or an interpreter, which is how it needs no
    /// libfuse2, and its contents, extracted by that runtime, are held as an AppImage, which must carry none. Each carries
    /// the executable this call published, byte for byte and runnable, with its notices and version.json, and the deb's menu
    /// entry is refused when it starts the program with --no-sandbox.
    /// </summary>
    [Fact]
    public void The_linux_check_reads_the_deb_and_the_appimage_back()
    {
        var lines = LinuxLines(LinuxDesktopVerify);
        InOrder(lines,
        [
            "[ \"$(dpkg-deb --field \"$deb\" Package)\" = jason-desktop ] || { echo \"::error title=Desktop::The deb is not the package jason-desktop.\"; exit 1; }",
            "[ \"$(dpkg-deb --field \"$deb\" Version)\" = \"${VERSION//-/\\~}\" ] || { echo \"::error title=Desktop::The deb's version is not $VERSION as dpkg spells it.\"; exit 1; }",
            "[ \"$(dpkg-deb --field \"$deb\" Architecture)\" = amd64 ] || { echo \"::error title=Desktop::The deb is not for amd64.\"; exit 1; }",
            "dpkg-deb --field \"$deb\" Recommends | grep -q 'libicu74 | libicu72' || { echo \"::error title=Desktop::The deb does not recommend the ICU library its jason needs.\"; exit 1; }",
            "if dpkg-deb --field \"$deb\" Depends | grep -q libicu; then echo \"::error title=Desktop::The deb depends on an ICU library by one name; it recommends it by alternatives.\"; exit 1; fi",
            "dpkg-deb --extract \"$deb\" \"$opened\"",
            "node desktop/scripts/check-package.mjs --app \"$opened/opt/Jason\" --platform linux --form deb || { echo \"::error title=Desktop::The deb's application is not what it was configured to be: its feed, its fuses, its updater or its AppArmor profile.\"; exit 1; }",
            "dpkg-deb --control \"$deb\" \"$control\"",
            "grep -q 'apparmor_parser --replace' \"$control/postinst\" || { echo \"::error title=Desktop::The deb's after-install loads no AppArmor profile for its sandbox.\"; exit 1; }",
            "carries \"$opened/opt/Jason\" deb",
            "if grep -q -e '--no-sandbox' \"${entries[@]}\"; then echo \"::error title=Desktop::The deb's menu entry starts the program with --no-sandbox.\"; exit 1; fi",
            "readelf -lW -d \"$appimage\" | grep -E '\\(NEEDED\\)|^ *INTERP ' && { echo \"::error title=Desktop::The AppImage's runtime names an interpreter or a shared library, libfuse2 among them maybe.\"; exit 1; }",
            "(cd \"$extracted\" && \"$appimage\" --appimage-extract > /dev/null)",
            "node desktop/scripts/check-package.mjs --app \"$extracted/squashfs-root\" --platform linux --form appimage || { echo \"::error title=Desktop::The AppImage's application is not what it was configured to be: its feed, its fuses or its updater.\"; exit 1; }",
            "carries \"$extracted/squashfs-root\" AppImage",
        ],
            "The Linux check");

        // What each form carries: the executable the publish step names, byte for byte and runnable, its notices and the
        // release's version.
        InOrder(lines,
        [
            "cmp \"$bundle/jason\" \"$published\" || { echo \"::error title=Desktop::The $2's jason is not the executable this call published.\"; return 1; }",
            "[ -x \"$bundle/jason\" ] || { echo \"::error title=Desktop::The $2's jason is not executable.\"; return 1; }",
            "for notice in \"$bundle/LICENSE\" \"$bundle/THIRD-PARTY-NOTICES\" \"$1/resources/notices/LICENSES.chromium.html\"; do",
            "grep -q \"\\\"version\\\": \\\"$VERSION\\\"\" \"$bundle/version.json\" || { echo \"::error title=Desktop::The $2's version.json does not name $VERSION.\"; return 1; }",
        ],
            "The Linux check's carries");
        Assert.Contains("published=\"$GITHUB_WORKSPACE/$EXECUTABLE\"", lines);
        Assert.Equal("${{ steps.publish.outputs.executable }}", Scalar(Mapping(ActionStep(LinuxDesktopVerify), "env"), "EXECUTABLE"));
        Assert.Equal("${{ inputs.version }}", Scalar(Mapping(ActionStep(LinuxDesktopVerify), "env"), "VERSION"));
    }

    /// <summary>
    /// Each Linux package is started once before anything is published, as a person starts it: the deb installed by apt
    /// and started by the link it puts on the PATH, then removed; the AppImage run as the file it is, through FUSE where the
    /// runner has it and by its runtime's own extraction where it does not. Each start has a data directory and a harness
    /// root of its own, never the runner's home, with a one-page UI build in ui/custom, and the runtime's update check off.
    /// A start passes when the desktop logs desktop_started and window_loaded, its program runs without --no-sandbox, it
    /// quits when told, and the runtime it left running stops with the jason that package carries.
    /// </summary>
    [Fact]
    public void Each_linux_package_is_started_once_under_a_virtual_display()
    {
        var step = ActionStep(LinuxDesktopStart);
        Assert.Equal("false", Scalar(Mapping(step, "env"), "JASON_Update__CheckEnabled"));
        var lines = LinuxLines(LinuxDesktopStart);
        InOrder(lines,
        [
            "program='^[^ ]*/jason-desktop-app( |$)'",
            "if pgrep -f -- \"$program\" > /dev/null; then pgrep -a -f -- \"$program\"; echo \"::error title=Desktop::A process of the application runs before the $form starts.\"; return 1; fi",
            "mkdir -p \"$data/ui/custom\"",
            "JASON_DATA_DIR=\"$data\" JASON_Harness__Root=\"$data-harness\" xvfb-run --auto-servernum \"$@\" > \"$data.out\" 2>&1 &",
            "if grep -qs '\"EventName\":\"window_loaded\"' \"$data\"/logs/desktop-*.jsonl; then loaded=yes; break; fi",
            "grep -qs '\"EventName\":\"desktop_started\"' \"$data\"/logs/desktop-*.jsonl || { echo \"::error title=Desktop::The $form logged no desktop_started.\"; return 1; }",
            "case \" $said \" in *' --no-sandbox '*) echo \"::error title=Desktop::The $form runs with --no-sandbox.\"; return 1 ;; esac",
            "kill -TERM \"$main\"",
            "if kill -0 \"$main\" 2> /dev/null; then echo \"::error title=Desktop::The $form did not quit.\"; return 1; fi",
            "if pgrep -f -- \"$program\" > /dev/null; then pgrep -a -f -- \"$program\"; echo \"::error title=Desktop::The $form left a process of the application running.\"; return 1; fi",
            "JASON_DATA_DIR=\"$data\" \"$jason\" runtime stop || { echo \"::error title=Desktop::The runtime the $form started did not stop with the jason it carries.\"; return 1; }",
            "sudo apt-get -o Acquire::Retries=5 update -qq",
            "sudo apt-get -o Acquire::Retries=5 install -y --no-install-recommends \"$dist/jason-desktop-linux-x64.deb\"",
            "if [ -e /etc/apparmor.d/jason-desktop-app ]; then echo \"The deb installed its AppArmor profile.\"; else echo \"The deb installed no AppArmor profile: this runner's AppArmor reads none.\"; fi",
            "started deb \"$RUNNER_TEMP/jason-desktop-deb-data\" /opt/Jason/resources/bundle/jason /usr/bin/jason-desktop-app",
            "sudo apt-get -o Acquire::Retries=5 remove -y jason-desktop",
            "if ! unshare -Ur true 2> /dev/null; then",
            "sudo sysctl -w kernel.apparmor_restrict_unprivileged_userns=0",
            "unshare -Ur true || { echo \"::error title=Desktop::This runner refuses unprivileged user namespaces with AppArmor's restriction off.\"; exit 1; }",
            "export APPIMAGE_EXTRACT_AND_RUN=1",
            "started AppImage \"$data\" \"$data/desktop/bundle/$VERSION/jason\" \"$dist/jason-desktop-linux-x64.AppImage\"",
        ],
            "The Linux start");
        Assert.All(lines.Where(line => line.Contains("JASON_DATA_DIR=", StringComparison.Ordinal)), line => Assert.Contains("\"$data\"", line, StringComparison.Ordinal));
        Assert.Contains("data=\"$RUNNER_TEMP/jason-desktop-appimage-data\"", lines);
        Assert.DoesNotContain(lines, line => Regex.IsMatch(line, @"\$HOME|~/"));

        // Every apt-get call asks again when the archive does not answer at once: a hosted runner's name lookups fail
        // now and then, before anything of the step has changed the system.
        var apt = lines.Where(line => line.Contains("apt-get", StringComparison.Ordinal)).ToList();
        Assert.Equal(3, apt.Count);
        Assert.All(apt, line => Assert.StartsWith("sudo apt-get -o Acquire::Retries=5 ", line, StringComparison.Ordinal));

        // The switch is named on one line alone, the one that refuses a start carrying it.
        const string refusal = "case \" $said \" in *' --no-sandbox '*) echo \"::error title=Desktop::The $form runs with --no-sandbox.\"; return 1 ;; esac";
        Assert.Equal([refusal], lines.Where(line => line.Contains("--no-sandbox", StringComparison.Ordinal)));

        // The application's processes are found by the program's own first word, never a wrapper's command line that
        // ends with it: no other pattern is handed to pgrep or pkill, and nothing is killed by a pattern at all.
        Assert.All(lines.Where(line => Regex.IsMatch(line, @"\bpgrep\b")), line => Assert.DoesNotMatch(@"pgrep [^|;]*'", line));
        Assert.All(lines.Where(line => Regex.IsMatch(line, @"\bpgrep\b")), line => Assert.Contains("-- \"$program\"", line, StringComparison.Ordinal));
        Assert.DoesNotContain(lines, line => line.Contains("pkill", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, line => line.Contains("kill -0 \"$desktop\" 2> /dev/null; then", StringComparison.Ordinal));

        // The runner's restriction is lifted only inside the one test that finds it refusing user namespaces, after the
        // deb has started as shipped, and nowhere else.
        var sysctl = Assert.Single(lines.Select((line, index) => (Line: line, Index: index)), each => each.Line.Contains("sysctl", StringComparison.Ordinal));
        Assert.Equal("sudo sysctl -w kernel.apparmor_restrict_unprivileged_userns=0", sysctl.Line);
        var opened = lines.FindLastIndex(sysctl.Index, line => line.StartsWith("if ", StringComparison.Ordinal) || line == "fi");
        Assert.Equal("if ! unshare -Ur true 2> /dev/null; then", lines[opened]);
        var closed = lines.FindIndex(sysctl.Index, line => line == "fi" || line.StartsWith("if ", StringComparison.Ordinal));
        Assert.Equal("fi", lines[closed]);
        Assert.True(opened > lines.FindIndex(line => line.StartsWith("started deb ", StringComparison.Ordinal)), "The runner's restriction is lifted before the deb starts as shipped.");
    }

    /// <summary>
    /// CI's desktop job builds the Linux packages as a release does, through the same action, after its node tests and on
    /// the run's own SDK, at a candidate's version: the release this tree is on the way to with a ci word and the run's
    /// number, since a package is never built at the -dev version the tree carries. It adds steps and no job.
    /// </summary>
    [Fact]
    public void Ci_s_desktop_job_builds_the_linux_packages_as_a_release_does()
    {
        var job = Mapping(Mapping(Parse(Read(Ci)), "jobs"), "desktop");
        Assert.Equal("The desktop on ubuntu", Scalar(job, "name"));
        Assert.Equal("ubuntu-latest", Scalar(job, "runs-on"));
        var steps = JobSteps(job).ToList();
        var names = steps.Select(step => Scalar(step, "name") ?? Scalar(step, "uses")).ToList();
        Assert.Equal(
            ["actions/checkout@v7", "actions/setup-node@v5", "Install", "Check (types)", "Node tests", "./.github/actions/setup-dotnet", "Package the Linux desktop as a release does"],
            names);
        Assert.Equal("${{ needs.version.outputs.sdk }}", Scalar(Mapping(steps[5], "with"), "sdk-version"));
        var with = Mapping(steps[6], "with");
        Assert.Equal("./.github/actions/package", Scalar(steps[6], "uses"));
        Assert.Equal("linux-x64", Scalar(with, "rid"));
        Assert.Equal("${{ needs.version.outputs.release }}-ci.${{ github.run_number }}", Scalar(with, "version"));
        Assert.Equal("true", Scalar(with, "desktop"));
        Assert.Equal("3", Scalar(with, "retention-days"));
    }
}
