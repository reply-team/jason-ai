using YamlDotNet.RepresentationModel;
using static Jason.Runtime.Tests.Documentation.Workflows;

namespace Jason.Runtime.Tests.Documentation;

/// <summary>
/// The release's proof that Jason installs, updates and uninstalls as one installation, on the signed packages, before the
/// release job may publish them: three jobs, each needed by the release job, each holding nothing a release holds, each
/// running the install scripts as the release publishes them.
/// </summary>
public partial class ReleaseWorkflowTests
{
    private static readonly string[] ReleaseProofJobs = ["proof-windows", "proof-macos", "proof-macos-intel"];

    /// <summary>
    /// The release job needs every proof job, so nothing is published that one of them did not install and uninstall.
    /// </summary>
    [Fact]
    public void The_release_is_published_only_after_its_three_proof_jobs()
    {
        var jobs = Mapping(Parse(Read(Release)), "jobs");
        var needs = Node(Mapping(jobs, "release"), "needs");
        var needed = ((YamlSequenceNode)needs!).Children.Select(node => node.ToString()).ToList();
        Assert.All(ReleaseProofJobs, job => Assert.Contains(job, needed));

        Assert.Equal(["version", "windows", "skills"], ((YamlSequenceNode)Node(Mapping(jobs, "proof-windows"), "needs")!).Children.Select(node => node.ToString()));
        Assert.Equal(["version", "macos", "skills"], ((YamlSequenceNode)Node(Mapping(jobs, "proof-macos"), "needs")!).Children.Select(node => node.ToString()));
        Assert.Equal(["version", "macos"], ((YamlSequenceNode)Node(Mapping(jobs, "proof-macos-intel"), "needs")!).Children.Select(node => node.ToString()));
        Assert.Equal("macos-26-intel", Scalar(Mapping(jobs, "proof-macos-intel"), "runs-on"));
    }

    /// <summary>
    /// A proof job holds no secret, no environment and no permission but the token's read of the repository; it downloads
    /// only what the platform's and the skills' jobs uploaded, and keeps what it writes under a name outside jason-*, which
    /// the release job's download takes; and it never builds, signs or copies anything.
    /// </summary>
    [Fact]
    public void The_release_s_proof_jobs_hold_nothing_a_release_holds()
    {
        var release = Read(Release);
        var jobs = Mapping(Parse(release), "jobs");
        foreach (var name in ReleaseProofJobs)
        {
            var job = Mapping(jobs, name);
            var text = Job(release, name);
            Assert.DoesNotContain("secrets.", text, StringComparison.Ordinal);
            Assert.DoesNotContain("github.token", text, StringComparison.Ordinal);
            Assert.Null(Node(job, "environment"));
            Assert.Null(Node(job, "if"));
            var permissions = Mapping(job, "permissions");
            Assert.Equal(["contents"], permissions.Children.Keys.Select(key => key.ToString()));
            Assert.Equal("read", Scalar(permissions, "contents"));
            Assert.NotNull(Scalar(job, "timeout-minutes"));

            foreach (var step in JobSteps(job))
            {
                var uses = Scalar(step, "uses");
                Assert.True(uses is null or "actions/checkout@v7" or "actions/download-artifact@v7" or "actions/upload-artifact@v7", $"{name} uses {uses}");
                if (uses == "actions/download-artifact@v7")
                {
                    Assert.Contains(Scalar(Mapping(step, "with"), "name"), new[] { "jason-win-x64", "jason-desktop-win-x64", "jason-osx-arm64", "jason-desktop-osx-x64", "jason-skills" });
                }

                if (uses == "actions/upload-artifact@v7")
                {
                    var kept = Scalar(Mapping(step, "with"), "name");
                    Assert.False(kept is null || kept.StartsWith("jason-", StringComparison.Ordinal), $"{name} keeps {kept}, a name the release's download takes");
                }
            }

            foreach (var never in new[] { "JASON_DESKTOP_TEST_BUILD", "stand-in", "New-SelfSignedCertificate", "Set-AuthenticodeSignature", "codesign --sign", "codesign -s ", "./.github/actions/package" })
            {
                Assert.DoesNotContain(never, text, StringComparison.Ordinal);
            }
        }
    }

    /// <summary>
    /// The proof runs the install scripts as the release publishes them, unchanged, from the tag's own tree; on the Mac it
    /// asks Gatekeeper about the application where the script put it, after the plain install and after one from a
    /// quarantined download; and every job ends on the comparison of each uninstall's listing with the machine before.
    /// </summary>
    [Fact]
    public void The_release_s_proof_installs_with_the_published_scripts_and_compares_every_uninstall()
    {
        var release = Read(Release);
        var windows = Job(release, "proof-windows");
        Assert.Contains("-Step Install -Out $env:OUT -Feed $env:FEED -Version $env:VERSION -Script (Join-Path $env:GITHUB_WORKSPACE 'install\\install.ps1')", windows, StringComparison.Ordinal);
        foreach (var step in new[] { "UninstallFromTerminal", "InstallOverPrevious", "UninstallFromInstalledApps", "FromPrevious", "Held" })
        {
            Assert.Contains($"-Step {step} ", windows, StringComparison.Ordinal);
        }

        var macos = Job(release, "proof-macos");
        Assert.Equal(2, CountOf(macos, "test-installation-macos.sh install \"$FEED\" \"$VERSION\" \"$GITHUB_WORKSPACE/install/install.sh\"") + CountOf(macos, "test-installation-macos.sh install \"$RUNNER_TEMP/feed-quarantined\" \"$VERSION\" \"$GITHUB_WORKSPACE/install/install.sh\""));
        Assert.Equal(2, CountOf(macos, "test-installation-macos.sh assess"));
        Assert.True(macos.IndexOf("test-installation-macos.sh quarantine", StringComparison.Ordinal) < macos.IndexOf("test-installation-macos.sh install \"$RUNNER_TEMP/feed-quarantined\"", StringComparison.Ordinal));
        foreach (var step in new[] { "refuse-while-open", "uninstall-terminal", "uninstall-menu", "from-previous", "held" })
        {
            Assert.Contains($"test-installation-macos.sh {step}", macos, StringComparison.Ordinal);
        }

        Assert.Contains("The 0.48 Jason Desktop's own updater on a Mac", macos, StringComparison.Ordinal);
        Assert.Contains("test-installation-macos.sh intel \"$GITHUB_WORKSPACE/feed/jason-desktop-macos-x64.zip\" \"$VERSION\"", Job(release, "proof-macos-intel"), StringComparison.Ordinal);
        Assert.Contains("test-installation-macos.sh held", Job(release, "proof-macos-intel"), StringComparison.Ordinal);

        // The feed a proof job installs from names its platform alone, and the release's own manifest is written once, by
        // the release job.
        var feed = Read([".github", "scripts", "Write-ProofFeed.ps1"]);
        Assert.Contains("[ValidateSet('win-x64', 'osx-arm64')]", feed, StringComparison.Ordinal);
        Assert.DoesNotContain("MinUpgradeFrom", feed, StringComparison.Ordinal);
    }

    private static int CountOf(string text, string what)
    {
        var count = 0;
        for (var at = text.IndexOf(what, StringComparison.Ordinal); at >= 0; at = text.IndexOf(what, at + what.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}
