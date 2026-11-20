using System.Text.RegularExpressions;
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
    /// The release job needs every proof job and runs only when every job it needs succeeded, so nothing is published that
    /// one of them did not install and uninstall. A leg runs unless the job was cancelled, so that a failed leg stops no leg
    /// after it; that changes nothing the release waits for: a failed step fails its job, no proof job or step may continue
    /// on an error, and the held step fails the job for every leg that failed or was not run. Every proof job, and CI's
    /// union jobs, run on a GitHub-hosted runner, the one place the scripts' guards let them change a machine.
    /// </summary>
    [Fact]
    [Critical(Family.B7, "a release job that does not need a proof job, or runs where one failed, publishes what that job never proved")]
    public void The_release_is_published_only_after_its_three_proof_jobs()
    {
        var jobs = Mapping(Parse(Read(Release)), "jobs");
        var needs = Node(Mapping(jobs, "release"), "needs");
        var needed = ((YamlSequenceNode)needs!).Children.Select(node => node.ToString()).ToList();
        Assert.All(ReleaseProofJobs, job => Assert.Contains(job, needed));
        Assert.Null(Node(Mapping(jobs, "release"), "if"));
        Assert.Null(Node(Mapping(jobs, "release"), "continue-on-error"));

        var ci = Mapping(Parse(Read(Ci)), "jobs");
        foreach (var (job, name) in ReleaseProofJobs.Select(name => (Mapping(jobs, name), name)).Concat(ProofJobs.Select(name => (Mapping(ci, name), name))))
        {
            Assert.Null(Node(job, "continue-on-error"));
            Assert.All(JobSteps(job), step => Assert.Null(Node(step, "continue-on-error")));
            Assert.Contains(Scalar(job, "runs-on"), new[] { "windows-latest", "macos-latest", "macos-26-intel" });
            var held = Assert.Single(JobSteps(job), step => Scalar(step, "name") == "The machine after every uninstall is the machine before");
            Assert.EndsWith(name.EndsWith("-windows", StringComparison.Ordinal) ? "-Step Held -Out $env:OUT" : "test-installation-macos.sh held", Scalar(held, "run"), StringComparison.Ordinal);
        }

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
    [Critical(Family.I1, "a proof job that holds a secret, builds or signs, or keeps a file under a name the release takes, can change what is published")]
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
    /// quarantined download, and about this release's application once the one-installation release's own updater has put
    /// it in place; and every job ends on the comparison of each uninstall's listing with the machine before.
    /// </summary>
    [Fact]
    [Critical(Family.B7, "a proof that runs anything but the published scripts, or skips a compare, passes on what the release does not ship")]
    public void The_release_s_proof_installs_with_the_published_scripts_and_compares_every_uninstall()
    {
        var release = Read(Release);
        var windows = Job(release, "proof-windows");
        Assert.Contains("-Step Install -Out $env:OUT -Feed $env:FEED -Version $env:VERSION -Script (Join-Path $env:GITHUB_WORKSPACE 'install\\install.ps1')", windows, StringComparison.Ordinal);
        foreach (var step in new[] { "UninstallFromTerminal", "InstallOverPrevious", "UninstallFromInstalledApps", "FromPrevious", "FromOneInstallation", "Held" })
        {
            Assert.Contains($"-Step {step} ", windows, StringComparison.Ordinal);
        }

        var macos = Job(release, "proof-macos");
        Assert.Equal(2, CountOf(macos, "test-installation-macos.sh install \"$FEED\" \"$VERSION\" \"$GITHUB_WORKSPACE/install/install.sh\"") + CountOf(macos, "test-installation-macos.sh install \"$RUNNER_TEMP/feed-quarantined\" \"$VERSION\" \"$GITHUB_WORKSPACE/install/install.sh\""));
        Assert.Equal(3, CountOf(macos, "test-installation-macos.sh assess"));
        Assert.True(macos.IndexOf("test-installation-macos.sh quarantine", StringComparison.Ordinal) < macos.IndexOf("test-installation-macos.sh install \"$RUNNER_TEMP/feed-quarantined\"", StringComparison.Ordinal));
        Assert.True(macos.IndexOf("test-installation-macos.sh from-one-installation ", StringComparison.Ordinal) < macos.LastIndexOf("test-installation-macos.sh assess", StringComparison.Ordinal), "the Mac's proof does not ask Gatekeeper about the application the one-installation release's updater put in place");
        foreach (var step in new[] { "refuse-while-open", "uninstall-terminal", "uninstall-menu", "from-previous", "from-one-installation", "held" })
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

    /// <summary>
    /// The previous release every proof job updates from, named, in ci.yml's union jobs and the release's proof jobs alike.
    /// These legs begin in the world before one installation, where a command-line Jason and Jason Desktop were installed
    /// apart, and v0.48.7, a release of that world, is the 0.48 release these legs were last read against: its install puts
    /// a command-line Jason of its own where every leg that starts from a previous release expects one, then its own
    /// <c>jason update apply</c> and the move into Jason Desktop. It is never "the latest published release": once 0.49.0
    /// is published that is a release of one installation, which installs Jason Desktop alone, and every such leg would
    /// fail on it, so the first fix after 0.49 could not be published, and CI's union jobs would fail on main and on the
    /// next release's pull request in the same way. When these legs are given a 0.49 release to start from, this value and
    /// the legs change together. A release of the world after is the other pin, <see cref="OneInstallationRelease"/>, with
    /// a fetch and a leg of its own.
    /// </summary>
    private const string PreviousRelease = "v0.48.7";

    /// <summary>
    /// The one-installation release the release's proof jobs update from as well, named: a release of the world after,
    /// where Jason Desktop is installed alone and carries the jason a terminal runs. There are two pins because a person
    /// updates from either world: <see cref="PreviousRelease"/>'s legs begin with a command-line Jason and Jason Desktop
    /// installed apart and reach this release through that release's own update and the move; this release's leg begins
    /// with Jason Desktop alone, no window open, and reaches this release through its carried <c>jason update apply</c>,
    /// which hands the update to its own Jason Desktop's updater and the rest to this release's carried jason. Only the
    /// release's proof jobs run it: that updater installs an update signed as the release is, which a test build never is.
    /// It is v0.49.0 or later and before the version this tree builds, and it is never read as the latest published
    /// release, which moves under a proof that ran and, once this release is published, is this release itself.
    /// </summary>
    private const string OneInstallationRelease = "v0.49.0";

    /// <summary>The name of the step each release proof job fetches <see cref="OneInstallationRelease"/> by.</summary>
    private const string FetchOneInstallation = "Fetch the one-installation release this one updates from";

    /// <summary>The leg of each release proof job that updates from <see cref="OneInstallationRelease"/>.</summary>
    private const string FromOneInstallationLeg = "Update the one-installation release's Jason Desktop with its own jason update apply, finished by this release's jason";

    /// <summary>
    /// The legs and framing steps that are named otherwise in the release's proof job than in CI's union job, by the
    /// release's name, each with the reason.
    /// </summary>
    private static readonly (string Release, string Union, string Why)[] ReleaseNamedOtherwise =
    [
        ("Read the folder this release's jason extracts its native libraries to", "Read the folder this build's jason extracts its native libraries to",
            "the release reads its own archive, where CI reads its test build"),
        ("Install as an agent does, with the released install.ps1", "Install as an agent does, with install.ps1",
            "the release runs install.ps1 as it publishes it, where CI runs a copy naming its test build's signer"),
        ("Install this release over the previous release's Jason Desktop, as its updater runs an installer", "Install this build over the previous release's Jason Desktop, as its updater runs an installer",
            "the release installs itself, where CI installs a test build"),
        ("Install as an agent does, with the released install.sh, and assess it where it was put", "Install as an agent does, with install.sh",
            "the release runs install.sh as it publishes it, and Gatekeeper assesses only a notarized application"),
        ("Install from a quarantined download, and uninstall from the application's Uninstall Jason item", "Install again, and uninstall from the application's Uninstall Jason item",
            "only a notarized application opens from a quarantined download, so CI's ad hoc build installs again unquarantined"),
    ];

    /// <summary>The steps of a union job its release proof job does not run, each with the reason.</summary>
    private static readonly (string Job, string Step, string Why)[] UnionOnly =
    [
        ("union-windows", "./.github/actions/setup-dotnet", "CI builds this tree; the release proves what its platform's job built"),
        ("union-windows", "actions/setup-node@v5", "CI builds this tree; the release proves what its platform's job built"),
        ("union-windows", "Package jason for win-x64 as a release does, as a test build", "CI builds this tree; the release proves what its platform's job built"),
        ("union-windows", "Build Jason Desktop around it as a test build", "CI builds this tree; the release proves what its platform's job built"),
        ("union-windows", "The released install.ps1 refuses the unsigned test build", "the test build's stand-in signer; a release is signed by its own"),
        ("union-windows", "Sign the test build with this runner's own certificate, and copy install.ps1 to name it", "the test build's stand-in signer; a release is signed by its own"),
        ("union-windows", "Uninstall from a terminal while the window opens, so the waiter asks", "the release runs the legs it ran before; this one joins once it is green in CI"),
        ("union-windows", "Install by the pinned line, and uninstall from a new terminal", "the release runs the legs it ran before; this one joins once it is green in CI"),
        ("union-windows", "Install from a feed served on this machine, over the web in Windows PowerShell 5.1, and uninstall from a new terminal", "the release runs the legs it ran before; this one joins once it is green in CI"),
        ("union-windows", "Run this build's line over the previous release's command-line Jason while its runtime runs", "the release runs the legs it ran before; this one joins once it is green in CI"),
        ("union-windows", "Install this build as an update over the previous release's Jason Desktop and its command-line Jason side by side", "the release runs the legs it ran before; this one joins once it is green in CI"),
        ("union-windows", "Move again from the previous release, then go back to it as the update page says", "the release runs the legs it ran before; this one joins once it is green in CI"),
        ("union-windows", "Go back to the previous release, then move again with its own update", "the release runs the legs it ran before; this one joins once it is green in CI"),
        ("union-windows", "Package jason at the next version", "a next version, which the release does not build, and its summary says so"),
        ("union-windows", "Build the next version as a test build", "a next version, which the release does not build, and its summary says so"),
        ("union-windows", "Install this build silently over the previous release", "a next version, which the release does not build, and its summary says so"),
        ("union-windows", "Update the test build from a terminal with jason update apply", "a next version, which the release does not build, and its summary says so"),
        ("union-macos", "./.github/actions/setup-dotnet", "CI builds this tree; the release proves what its platform's job built"),
        ("union-macos", "actions/setup-node@v5", "CI builds this tree; the release proves what its platform's job built"),
        ("union-macos", "Package jason for osx-arm64 as a pull request does, as a test build", "CI builds this tree; the release proves what its platform's job built"),
        ("union-macos", "Build Jason Desktop around it as a test build", "CI builds this tree; the release proves what its platform's job built"),
        ("union-macos", "The released install.sh refuses the ad hoc test build, and a copy names its signature", "the test build's stand-in signature; a release is signed by its own"),
        ("union-macos", "Hold install.sh's requirement against the latest published application", "the release's own install.sh holds the release's own application to it in every leg"),
        ("union-macos", "Install by the pinned line, see whether autostart enable starts a runtime, and uninstall from a new terminal", "the release runs the legs it ran before; this one joins once it is green in CI"),
        ("union-macos", "This build's application, put in Applications by hand on a clean machine and opened, puts jason in a new terminal", "the release runs the legs it ran before; this one joins once it is green in CI"),
        ("union-macos", "This build's install.sh refuses the previous release's Jason Desktop, then installs once it is moved out", "the release runs the legs it ran before; this one joins once it is green in CI"),
        ("union-macos", "Install this build by install.sh over the previous release's running command-line Jason", "the release runs the legs it ran before; this one joins once it is green in CI"),
        ("union-macos", "The previous release's Jason Desktop, its bundle replaced by this build's as its updater replaces it, sets the machine up at its first start", "the release runs the legs it ran before; this one joins once it is green in CI"),
        ("union-macos", "The previous release's Jason Desktop and its running command-line Jason, the bundle replaced by this build's as its updater replaces it, set up at its first start", "the release runs the legs it ran before; this one joins once it is green in CI"),
        ("union-macos", "Open this build from its disk image, then eject it", "the release runs the legs it ran before; this one joins once it is green in CI"),
        ("union-macos", "Say what the previous release's own uninstall leaves in launchd", "the release runs the legs it ran before; this one joins once it is green in CI"),
        ("union-macos", "Move again from the previous release, then go back to it as the update page says", "the release runs the legs it ran before; this one joins once it is green in CI"),
        ("union-macos", "Go back to the previous release, then move again with its own update", "the release runs the legs it ran before; this one joins once it is green in CI"),
    ];

    /// <summary>The steps of a release proof job its union job does not run, each with the reason.</summary>
    private static readonly (string Job, string Step, string Why)[] ReleaseOnly =
    [
        ("proof-windows", "actions/download-artifact@v7", "the release proves the packages its platform's and the skills' jobs uploaded, where CI builds its own"),
        ("proof-macos", "actions/download-artifact@v7", "the release proves the packages its platform's and the skills' jobs uploaded, where CI builds its own"),
        ("proof-windows", FetchOneInstallation, "the one-installation release's updater runs only a setup signed by the release's publisher, which CI's test build never is"),
        ("proof-windows", FromOneInstallationLeg, "the one-installation release's updater runs only a setup signed by the release's publisher, which CI's test build never is"),
        ("proof-macos", FetchOneInstallation, "Squirrel installs only an update signed by the running application's own team, a Developer ID, which CI's ad hoc build is not"),
        ("proof-macos", FromOneInstallationLeg, "Squirrel installs only an update signed by the running application's own team, a Developer ID, which CI's ad hoc build is not"),
    ];

    /// <summary>The script steps a release proof job's legs run that its union job's do not, each with the reason.</summary>
    private static readonly (string Step, string Why)[] ReleaseOnlyVerbs =
    [
        ("assess", "Gatekeeper's assessment passes only a notarized application, which CI's ad hoc build is not"),
        ("quarantine", "only a notarized application opens from a quarantined download, which CI's ad hoc build is not"),
    ];

    /// <summary>The framing steps whose script is the same text in a union job and its release proof job.</summary>
    private static readonly string[] FramingRunAlike =
    [
        "Give the job's steps the temporary folder a new shell of this account has",
        "Give the account's Path the shape an ordinary account has, ExpandString with an entry spelled through a variable",
        "The machine before anything is installed",
        "Fetch the previous release",
        "The machine after every uninstall is the machine before",
    ];

    /// <summary>
    /// Each release proof job runs its union job's legs and framing, step for step, in the same order: the same script
    /// steps in each leg, the same listings and compares, the same legs recorded as the previous release's, and the same
    /// framing text, with only the differences named above, each with its reason. A leg or a framing step added to one
    /// and not the other turns this red, and so does a named difference that no longer exists. Every leg of every proof
    /// job, the Intel Mac's one included, runs unless the job was cancelled, between a begin and an end, and leaves when
    /// its begin says it was not run; the held step runs last, unless the job was cancelled; and on Windows the job's one
    /// temporary folder is given first.
    /// </summary>
    [Fact]
    [Critical(Family.B7, "a leg or framing step added to CI's union job and not to the release's proof job, or the other way, lets one prove what the other never runs")]
    public void Each_release_proof_job_runs_its_union_job_s_legs_and_framing_step_for_step()
    {
        Assert.All(ReleaseNamedOtherwise, named => Assert.False(string.IsNullOrWhiteSpace(named.Why)));
        Assert.All(UnionOnly, step => Assert.False(string.IsNullOrWhiteSpace(step.Why)));
        Assert.All(ReleaseOnly, step => Assert.False(string.IsNullOrWhiteSpace(step.Why)));
        Assert.All(ReleaseOnlyVerbs, verb => Assert.False(string.IsNullOrWhiteSpace(verb.Why)));

        var ci = Mapping(Parse(Read(Ci)), "jobs");
        var release = Mapping(Parse(Read(Release)), "jobs");
        var renamed = ReleaseNamedOtherwise.ToDictionary(named => named.Release, named => named.Union, StringComparer.Ordinal);
        var releaseOnlyVerbs = ReleaseOnlyVerbs.Select(verb => verb.Step).ToHashSet(StringComparer.Ordinal);
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (union, proof) in new[] { ("union-windows", "proof-windows"), ("union-macos", "proof-macos") })
        {
            var unionSteps = JobSteps(Mapping(ci, union)).ToList();
            var proofSteps = JobSteps(Mapping(release, proof)).ToList();
            var unionOnly = UnionOnly.Where(step => step.Job == union).Select(step => step.Step).ToHashSet(StringComparer.Ordinal);
            var releaseOnly = ReleaseOnly.Where(step => step.Job == proof).Select(step => step.Step).ToHashSet(StringComparer.Ordinal);
            Assert.All(unionOnly, step => Assert.Contains(step, unionSteps.Select(StepName)));
            Assert.All(releaseOnly, step => Assert.Contains(step, proofSteps.Select(StepName)));

            var expected = unionSteps.Where(step => !unionOnly.Contains(StepName(step))).Select(step => Shape(step, StepName(step), new HashSet<string>(StringComparer.Ordinal)));
            var actual = proofSteps.Where(step => !releaseOnly.Contains(StepName(step))).Select(step =>
            {
                var name = StepName(step);
                if (renamed.TryGetValue(name, out var unionName))
                {
                    used.Add(name);
                    name = unionName;
                }

                return Shape(step, name, releaseOnlyVerbs);
            });
            var want = expected.ToList();
            var got = actual.ToList();
            for (var at = 0; at < Math.Max(want.Count, got.Count); at++)
            {
                var wanted = at < want.Count ? want[at] : "(no step)";
                var found = at < got.Count ? got[at] : "(no step)";
                Assert.True(wanted == found, $"step {at + 1} past the named differences: {union} has\n  {wanted}\nand {proof} has\n  {found}");
            }

            foreach (var name in FramingRunAlike.Where(name => unionSteps.Any(step => StepName(step) == name)))
            {
                Assert.Equal(Scalar(Assert.Single(unionSteps, step => StepName(step) == name), "run"), Scalar(Assert.Single(proofSteps, step => StepName(step) == name), "run"));
            }
        }

        Assert.Equal(ReleaseNamedOtherwise.Select(named => named.Release).Order(StringComparer.Ordinal), used.Order(StringComparer.Ordinal));
        foreach (var verb in releaseOnlyVerbs)
        {
            Assert.Contains($"test-installation-macos.sh {verb}", Job(Read(Release), "proof-macos"), StringComparison.Ordinal);
        }

        // The framing every proof job holds, the Intel Mac's included.
        foreach (var (jobs, name) in new[] { (ci, "union-windows"), (ci, "union-macos"), (release, "proof-windows"), (release, "proof-macos"), (release, "proof-macos-intel") })
        {
            var steps = JobSteps(Mapping(jobs, name)).ToList();
            var windows = name.EndsWith("-windows", StringComparison.Ordinal);
            if (windows)
            {
                Assert.True(Scalar(steps[0], "uses") == "actions/checkout@v7", $"{name} does not begin with the checkout");
                Assert.True(Scalar(steps[1], "name") == "Give the job's steps the temporary folder a new shell of this account has", $"{name} does not give its steps the one temporary folder first, after the checkout");
            }

            var before = Assert.Single(steps, step => Scalar(step, "name") == "The machine before anything is installed");
            Assert.Equal("before", Scalar(before, "id"));
            Assert.Equal("${{ !cancelled() }}", Scalar(before, "if"));
            var held = steps.FindLastIndex(step => Scalar(step, "name") == "The machine after every uninstall is the machine before");
            Assert.True(held > 0, $"{name} has no held step");
            Assert.Equal("${{ !cancelled() }}", Scalar(steps[held], "if"));
            Assert.All(steps.Skip(held + 1), step => Assert.Equal("always()", Scalar(step, "if")));

            var legs = steps.Where(step => Node(step, "env") is YamlMappingNode env && Node(env, "LEG") is not null).ToList();
            Assert.NotEmpty(legs);
            Assert.All(legs, leg =>
            {
                var legName = Scalar(leg, "name");
                Assert.Equal(legName, Scalar(Mapping(leg, "env"), "LEG"));
                Assert.Equal("${{ !cancelled() }}", Scalar(leg, "if"));
                Assert.True(steps.IndexOf(leg) < held, $"{legName} runs after the held step");
                var lines = Lines(leg);
                Assert.True(lines.Count >= 3, $"{legName} is not framed");
                Assert.True(lines[0].StartsWith(windows ? "./.github/scripts/Test-InstallationWindows.ps1 -Step Begin -Out $env:OUT -Leg $env:LEG" : "bash .github/scripts/test-installation-macos.sh begin \"$LEG\"", StringComparison.Ordinal), $"{name}: the leg '{legName}' does not start with its begin");
                Assert.True(lines[1] == (windows ? "if (Test-Path -LiteralPath (Join-Path $env:OUT 'skip')) { exit 0 }" : "[ ! -e \"$OUT/skip\" ] || exit 0"), $"{name}: the leg '{legName}' does not leave where its begin says it was not run");
                Assert.True(lines[^1] == (windows ? "./.github/scripts/Test-InstallationWindows.ps1 -Step End -Out $env:OUT -Leg $env:LEG" : "bash .github/scripts/test-installation-macos.sh end \"$LEG\""), $"{name}: the leg '{legName}' does not end with its end");
            });
        }
    }

    /// <summary>
    /// Every job that updates from a previous release fetches the one <see cref="PreviousRelease"/> names, given to its
    /// fetch by name, and never reads which release is the latest published one: these legs begin in the world before one
    /// installation, and v0.48.7, a release of that world, is the 0.48 release these legs were last read against. The fetch
    /// takes its tag from the pin and holds the files it verifies to that release's checksums.txt by their digests.
    /// </summary>
    [Fact]
    [Critical(Family.B7, "a proof job that updates from the latest published release fails every previous-release leg once 0.49.0 is published, and blocks the first fix after it")]
    public void Every_proof_job_updates_from_the_one_named_previous_release_never_the_latest_published()
    {
        Assert.Matches(@"^v0\.48\.\d+$", PreviousRelease);
        foreach (var (workflow, name) in new[] { (Ci, "union-windows"), (Ci, "union-macos"), (Release, "proof-windows"), (Release, "proof-macos") })
        {
            var fetches = JobSteps(Mapping(Mapping(Parse(Read(workflow)), "jobs"), name)).Where(step => Scalar(step, "name") == "Fetch the previous release").ToList();
            var fetch = Assert.Single(fetches);
            Assert.Equal("previous", Scalar(fetch, "id"));
            Assert.Equal(PreviousRelease, Scalar(Mapping(fetch, "env"), "PREVIOUS_RELEASE"));
            var run = Scalar(fetch, "run")!;
            Assert.DoesNotContain("releases/latest", run, StringComparison.Ordinal);
            Assert.Contains("PREVIOUS_RELEASE", run, StringComparison.Ordinal);
            Assert.Contains("https://github.com/reply-team/jason-ai/releases/download/$tag/", run, StringComparison.Ordinal);
            AssertTheFetchHoldsItsFiles(run, name, "PREVIOUS_RELEASE", "PREVIOUS");
        }
    }

    /// <summary>
    /// A fetch of a pinned release takes the release's tag from the pin's own variable, once, and holds each file it
    /// verifies to that release's checksums.txt: a file with no line there fails, and so does one whose SHA-256 differs,
    /// read by Get-FileHash on Windows and by shasum -a 256 -c on a Mac.
    /// </summary>
    private static void AssertTheFetchHoldsItsFiles(string run, string job, string pin, string folder)
    {
        if (job.EndsWith("-windows", StringComparison.Ordinal))
        {
            Assert.Single(Regex.Matches(run, @"(?m)^\$tag = "));
            Assert.Contains($"\n$tag = $env:{pin}\n", run, StringComparison.Ordinal);
            Assert.Contains("if (-not $listed.ContainsKey($name)) { throw \"$name has no line in $tag's checksums.txt\" }", run, StringComparison.Ordinal);
            Assert.Contains($"if ((Get-FileHash -LiteralPath (Join-Path $env:{folder} $name) -Algorithm SHA256).Hash -ne $listed[$name]) {{ throw \"$name does not match $tag's checksums.txt\" }}", run, StringComparison.Ordinal);
        }
        else
        {
            Assert.Single(Regex.Matches(run, @"(?m)^tag="));
            Assert.Contains($"\ntag=\"${pin}\"\n", run, StringComparison.Ordinal);
            Assert.Contains("[ -n \"$line\" ] || { echo \"::error::$name has no line in $tag's checksums.txt\"; exit 1; }", run, StringComparison.Ordinal);
            Assert.Contains($"(cd \"${folder}\" && printf '%s\\n' \"$line\" | shasum -a 256 -c - > /dev/null) || {{ echo \"::error::$name does not match $tag's checksums.txt\"; exit 1; }}", run, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Each release proof job updates from two named releases, each fetched by its own step: <see cref="PreviousRelease"/>, a
    /// 0.48 release, and <see cref="OneInstallationRelease"/>, at or after v0.49.0 and before the version this tree builds,
    /// given to its fetch by name and never read as the latest published. That fetch takes its tag from the pin, holds the
    /// files the release's own install script installs to its checksums.txt by their digests and says the others
    /// unverified; it comes before the one leg that needs it, which hands the folder it fetched to the step that updates from
    /// it, and compares the uninstall after it with the machine before. The job's summary says that release's handing proven
    /// only where the leg's step succeeded having run to its end, and otherwise that the leg failed, was cancelled or was not
    /// run. CI's union jobs fetch no one-installation release (<see cref="ReleaseOnly"/> says why).
    /// </summary>
    [Fact]
    [Critical(Family.B7, "a one-installation pin read as the latest published, or not before the version a release builds, fails the leg updating from it and blocks the release")]
    public void Each_release_proof_job_updates_from_the_one_named_one_installation_release_as_well()
    {
        Assert.Matches(@"^v0\.48\.\d+$", PreviousRelease);
        Assert.Matches(@"^v\d+\.\d+\.\d+$", OneInstallationRelease);
        var pinned = Version.Parse(OneInstallationRelease[1..]);
        var builds = Version.Parse(Regex.Match(Read(["dotnet", "Directory.Build.props"]), @"<VersionPrefix>(?<version>\d+\.\d+\.\d+)</VersionPrefix>").Groups["version"].Value);
        Assert.True(pinned >= new Version(0, 49, 0), $"{OneInstallationRelease} is a release before one installation");
        Assert.True(pinned < builds, $"{OneInstallationRelease} is not before {builds}, the version this tree builds");

        var release = Mapping(Parse(Read(Release)), "jobs");
        var releaseText = Read(Release).Replace("\r\n", "\n", StringComparison.Ordinal);
        foreach (var (name, verified, unverifiable, step, compare, judged) in new[]
        {
            ("proof-windows", "$verified = @('jason-desktop-windows-x64-setup.exe')", "$unverifiable = @('install.ps1', 'manifest.json')",
                "./.github/scripts/Test-InstallationWindows.ps1 -Step FromOneInstallation -Out $env:OUT -Feed $env:FEED -Version $env:VERSION -OneInstallation $env:ONE_INSTALLATION",
                "./.github/scripts/Test-InstallationWindows.ps1 -Step Compare -Out $env:OUT -Name after-one-installation-uninstall -Against before",
                "$ended = Join-Path $env:OUT 'legs-ended.txt'\n"
                    + "$handed = if ($env:ONE_INSTALLATION_LEG -ceq 'success' -and (Test-Path -LiteralPath $ended) -and @(Get-Content -LiteralPath $ended) -ccontains $env:ONE_INSTALLATION_LEG_NAME) { 'is proven here, by its leg' } elseif ($env:ONE_INSTALLATION_LEG -ceq 'failure') { 'is not proven here: its leg failed' } elseif ($env:ONE_INSTALLATION_LEG -ceq 'cancelled') { 'is not proven here: its leg was cancelled' } else { 'is not proven here: its leg was not run' }\n"),
            ("proof-macos", "verified=\"jason-desktop-macos-arm64.zip\"", "unverifiable=\"install.sh manifest.json\"",
                "bash .github/scripts/test-installation-macos.sh from-one-installation \"$ONE_INSTALLATION\" \"$FEED\" \"$VERSION\"",
                "bash .github/scripts/test-installation-macos.sh compare after-one-installation-uninstall before",
                "if [ \"$ONE_INSTALLATION_LEG\" = success ] && grep -qxF -- \"$ONE_INSTALLATION_LEG_NAME\" \"$OUT/legs-ended.txt\" 2>/dev/null; then handed=\"is proven here, by its leg\"\n"
                    + "elif [ \"$ONE_INSTALLATION_LEG\" = failure ]; then handed=\"is not proven here: its leg failed\"\n"
                    + "elif [ \"$ONE_INSTALLATION_LEG\" = cancelled ]; then handed=\"is not proven here: its leg was cancelled\"\n"
                    + "else handed=\"is not proven here: its leg was not run\"; fi\n"),
        })
        {
            var job = Mapping(release, name);
            Assert.EndsWith("one-installation", Scalar(Mapping(job, "env"), "ONE_INSTALLATION"), StringComparison.Ordinal);
            var steps = JobSteps(job).ToList();
            Assert.Single(steps, candidate => Scalar(candidate, "name") == "Fetch the previous release");
            var fetch = Assert.Single(steps, candidate => Scalar(candidate, "name") == FetchOneInstallation);
            Assert.Equal("one-installation", Scalar(fetch, "id"));
            Assert.Equal("${{ !cancelled() }}", Scalar(fetch, "if"));
            Assert.Equal(OneInstallationRelease, Scalar(Mapping(fetch, "env"), "ONE_INSTALLATION_RELEASE"));
            var run = Scalar(fetch, "run")!;
            Assert.DoesNotContain("releases/latest", run, StringComparison.Ordinal);
            Assert.DoesNotContain("PREVIOUS", run, StringComparison.Ordinal);
            Assert.Contains("ONE_INSTALLATION_RELEASE", run, StringComparison.Ordinal);
            Assert.Contains("https://github.com/reply-team/jason-ai/releases/download/$tag/", run, StringComparison.Ordinal);
            Assert.Contains(verified, run, StringComparison.Ordinal);
            Assert.Contains(unverifiable, run, StringComparison.Ordinal);
            AssertTheFetchHoldsItsFiles(run, name, "ONE_INSTALLATION_RELEASE", "ONE_INSTALLATION");

            // The comment over the fetch says the pin is never read as the latest published release.
            var jobText = Job(releaseText, name);
            var fetchAt = jobText.IndexOf($"\n      - name: {FetchOneInstallation}\n", StringComparison.Ordinal);
            Assert.True(fetchAt > 0, $"{name} has no step '{FetchOneInstallation}' at a step's indentation");
            var comment = jobText[(jobText.LastIndexOf("\n\n", fetchAt, StringComparison.Ordinal) + 2)..fetchAt];
            Assert.Contains("# Never read as the latest published release", comment, StringComparison.Ordinal);

            var needing = steps.Where(candidate => (Scalar(candidate, "run") ?? "").Contains($"{FetchOneInstallation}=${{{{ steps.one-installation.outcome }}}}", StringComparison.Ordinal)).ToList();
            var leg = Assert.Single(needing);
            Assert.Equal(FromOneInstallationLeg, Scalar(leg, "name"));
            Assert.Equal("from-one-installation", Scalar(leg, "id"));
            Assert.True(steps.IndexOf(fetch) < steps.IndexOf(leg), $"{name} runs '{FromOneInstallationLeg}' before it fetches what that leg needs");
            var lines = Lines(leg);
            Assert.Contains(step, lines);
            Assert.Contains(compare, lines);
            Assert.True(lines.IndexOf(step) < lines.IndexOf(compare), $"{name}: the leg compares before it updates");

            // What the job does not prove says the one-installation release's handing proven only where the leg's step
            // succeeded having run to its end, which a leg its begin did not run never reaches; failed where the step failed;
            // and not run otherwise.
            var summary = Assert.Single(steps, candidate => Scalar(candidate, "name") == "Say what this job does not prove");
            Assert.Equal("always()", Scalar(summary, "if"));
            Assert.Equal("${{ steps.from-one-installation.outcome }}", Scalar(Mapping(summary, "env"), "ONE_INSTALLATION_LEG"));
            Assert.Equal(FromOneInstallationLeg, Scalar(Mapping(summary, "env"), "ONE_INSTALLATION_LEG_NAME"));
            var says = Scalar(summary, "run")!;
            Assert.Contains(judged, says, StringComparison.Ordinal);
            Assert.Contains("the one-installation release's command line handing this release to its Jason Desktop, with no window, $handed.", says, StringComparison.Ordinal);
            Assert.True(says.IndexOf(judged, StringComparison.Ordinal) < says.IndexOf("$handed.", StringComparison.Ordinal), $"{name}: the summary says the handing before it judges it");
        }

        Assert.DoesNotContain("ONE_INSTALLATION", Read(Ci), StringComparison.Ordinal);
    }

    /// <summary>A step's name, or what it uses where it has none.</summary>
    private static string StepName(YamlMappingNode step) => Scalar(step, "name") ?? Scalar(step, "uses") ?? "";

    /// <summary>The lines of a step's script, each trimmed, the empty ones and the comments left out.</summary>
    private static List<string> Lines(YamlMappingNode step) =>
        [.. (Scalar(step, "run") ?? "").Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0 && !line.StartsWith('#'))];

    /// <summary>
    /// A step as the comparison sees it: a framing step by its name; a leg by its name, whether its begin needs the
    /// previous release, and the script steps it runs, in order, with a begin's mode and the whole line of a listing or a
    /// compare, the verbs a release alone runs left out.
    /// </summary>
    private static string Shape(YamlMappingNode step, string name, IReadOnlySet<string> leftOut)
    {
        if (Node(step, "env") is not YamlMappingNode env || Node(env, "LEG") is null)
        {
            return name;
        }

        const string windows = "./.github/scripts/Test-InstallationWindows.ps1 -Step ";
        const string macos = "bash .github/scripts/test-installation-macos.sh ";
        var steps = new List<string>();
        foreach (var line in Lines(step))
        {
            string verb;
            if (line.StartsWith(windows, StringComparison.Ordinal))
            {
                verb = line[windows.Length..].Split(' ')[0];
                verb = verb switch
                {
                    "Begin" => line.Contains(" -Clean", StringComparison.Ordinal) ? "Begin -Clean" : line.Contains(" -Reading", StringComparison.Ordinal) ? "Begin -Reading" : "Begin",
                    "Listing" or "Compare" => line,
                    _ => verb,
                };
            }
            else if (line.StartsWith(macos, StringComparison.Ordinal))
            {
                verb = line[macos.Length..].Split(' ')[0];
                verb = verb switch
                {
                    "begin" => line.StartsWith(macos + "begin \"$LEG\" clean", StringComparison.Ordinal) ? "begin clean" : "begin",
                    "listing" or "compare" => line,
                    _ => verb,
                };
            }
            else
            {
                continue;
            }

            if (!leftOut.Contains(verb))
            {
                steps.Add(verb);
            }
        }

        var previous = (Scalar(step, "run") ?? "").Contains("Fetch the previous release=${{ steps.previous.outcome }}", StringComparison.Ordinal);
        return $"{name} | needs the previous release: {previous} | {string.Join(" / ", steps)}";
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
