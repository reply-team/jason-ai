using System.Text.RegularExpressions;
using YamlDotNet.RepresentationModel;
using static Jason.Runtime.Tests.Documentation.Workflows;

namespace Jason.Runtime.Tests.Documentation;

/// <summary>
/// The test build, CI's alone: the desktop package built with every signature step and check off
/// (<c>JASON_DESKTOP_TEST_BUILD</c>), and the copies of the install scripts that name its stand-in signer, which CI's
/// proof jobs install and uninstall. Nothing a release builds or runs may reach either: the switch and the copies live in
/// ci.yml's two proof jobs alone, the scripts a release publishes each name one signer in one line that nothing changes, and
/// the step that trusts a stand-in certificate refuses to run anywhere but a GitHub-hosted runner.
/// </summary>
public partial class ReleaseWorkflowTests
{
    private const string TestBuildSwitch = "JASON_DESKTOP_TEST_BUILD";
    private static readonly string[] ProofJobs = ["union-windows", "union-macos"];
    private static readonly string[] Ps1Script = ["install", "install.ps1"];
    private static readonly string[] ShScript = ["install", "install.sh"];

    /// <summary>
    /// Neither the release nor the action it packages through ever names the switch, and in ci.yml only the two proof jobs
    /// do, each in the one step that builds the desktop; the configuration reads it only in its default export.
    /// </summary>
    [Fact]
    public void The_test_build_switch_is_named_by_ci_s_proof_jobs_alone_and_never_by_a_release()
    {
        Assert.DoesNotContain(TestBuildSwitch, Read(Release), StringComparison.Ordinal);
        Assert.DoesNotContain(TestBuildSwitch, Read(Action), StringComparison.Ordinal);
        // The one script the proof jobs build the test build with checks that its step asked for one, and never sets it.
        foreach (var other in Directory.GetFiles(Path.Combine(RepositoryRoot(), ".github"), "*", SearchOption.AllDirectories))
        {
            if (Path.GetFileName(other) is "ci.yml" or "Build-DesktopTestBuild.ps1")
            {
                continue;
            }

            Assert.False(File.ReadAllText(other).Contains(TestBuildSwitch, StringComparison.Ordinal), $"{other} names {TestBuildSwitch}");
        }

        var building = Read([".github", "scripts", "Build-DesktopTestBuild.ps1"]);
        Assert.Contains("if ($env:JASON_DESKTOP_TEST_BUILD -ne '1') { throw", building, StringComparison.Ordinal);
        Assert.DoesNotContain("$env:JASON_DESKTOP_TEST_BUILD =", building, StringComparison.Ordinal);
        Assert.DoesNotContain("Build-DesktopTestBuild", Read(Release) + Read(Action), StringComparison.Ordinal);

        var jobs = Mapping(Parse(Read(Ci)), "jobs");
        var naming = jobs.Children
            .SelectMany(pair => JobSteps((YamlMappingNode)pair.Value).Select(step => (Job: pair.Key.ToString(), Step: step)))
            .Where(found => ScalarNodes(found.Step).Any(node => node.Value?.Contains(TestBuildSwitch, StringComparison.Ordinal) == true)
                || (Node(found.Step, "env") is YamlMappingNode env && env.Children.Keys.Any(key => key.ToString() == TestBuildSwitch)))
            .ToList();
        Assert.Equal(ProofJobs.Order(StringComparer.Ordinal), naming.Select(found => found.Job).Order(StringComparer.Ordinal));
        Assert.All(naming, found =>
        {
            Assert.Equal("1", Scalar(Mapping(found.Step, "env"), TestBuildSwitch));
            Assert.StartsWith("../.github/scripts/Build-DesktopTestBuild.ps1 ", Scalar(found.Step, "run"), StringComparison.Ordinal);
        });

        var configuration = Read(["desktop", "electron-builder.config.mjs"]);
        Assert.Contains("export const testBuildVariable = 'JASON_DESKTOP_TEST_BUILD';", configuration, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(configuration, @"environment\[testBuildVariable\] === '1'"));
        Assert.DoesNotContain("process.env[testBuildVariable]", configuration, StringComparison.Ordinal);
    }

    /// <summary>
    /// The proof jobs hold nothing a release holds: no secret, no environment, the token's read of the repository alone,
    /// and what they upload is named outside <c>jason-*</c>, which the release's download takes.
    /// </summary>
    [Fact]
    public void Ci_s_proof_jobs_hold_no_secret_and_upload_nothing_a_release_takes()
    {
        var ci = Read(Ci);
        var jobs = Mapping(Parse(ci), "jobs");
        foreach (var name in ProofJobs)
        {
            var job = Mapping(jobs, name);
            var text = Job(ci, name);
            Assert.DoesNotContain("secrets.", text, StringComparison.Ordinal);
            Assert.Null(Node(job, "environment"));
            var permissions = Mapping(job, "permissions");
            Assert.Equal(["contents"], permissions.Children.Keys.Select(key => key.ToString()));
            Assert.Equal("read", Scalar(permissions, "contents"));
            Assert.Equal("needs.version.outputs.full == 'true'", Scalar(job, "if"));

            foreach (var step in JobSteps(job))
            {
                if (Node(step, "with") is not YamlMappingNode with)
                {
                    continue;
                }

                foreach (var key in new[] { "name", "artifact" })
                {
                    var uploaded = Scalar(with, key);
                    Assert.False(uploaded is not null && uploaded.StartsWith("jason-", StringComparison.Ordinal), $"{name} uploads {uploaded}, a name the release's download takes");
                }

                Assert.NotEqual("true", Scalar(with, "desktop"));
                Assert.NotEqual("true", Scalar(with, "sign-windows"));
                Assert.NotEqual("true", Scalar(with, "sign-macos"));
            }
        }
    }

    /// <summary>
    /// The copy of each install script CI runs differs from the released one by the one line that names the signer, and the
    /// step that makes it counts that; the released script still refuses the test build, every run, before the copy is made;
    /// and the step that trusts the stand-in's certificate refuses to run anywhere but a GitHub-hosted runner, before it
    /// makes anything. Neither the release nor the action ever makes a copy, a certificate or a trust.
    /// </summary>
    [Fact]
    public void Ci_runs_a_copy_of_each_install_script_changed_in_the_signer_line_alone_after_the_released_one_refused()
    {
        var ci = Read(Ci);
        var windows = Job(ci, "union-windows");
        var refusing = windows.IndexOf("- name: The released install.ps1 refuses the unsigned test build", StringComparison.Ordinal);
        var standIn = windows.IndexOf("- name: Sign the test build with this runner's own certificate, and copy install.ps1 to name it", StringComparison.Ordinal);
        Assert.True(refusing >= 0 && standIn > refusing, "union-windows does not let the released install.ps1 refuse the test build before it signs it with a stand-in");
        var step = windows[standIn..];
        var hosted = step.IndexOf("$env:RUNNER_ENVIRONMENT -ne 'github-hosted'", StringComparison.Ordinal);
        Assert.True(hosted >= 0 && hosted < step.IndexOf("New-SelfSignedCertificate", StringComparison.Ordinal) && hosted < step.IndexOf("LocalMachine\\Root", StringComparison.Ordinal), "the stand-in's certificate is made or trusted before the step refuses a runner that is not GitHub-hosted");
        Assert.Contains("$changed.Count -ne 1", step, StringComparison.Ordinal);
        Assert.Contains("Join-Path $env:RUNNER_TEMP 'install-stand-in-signer.ps1'", step, StringComparison.Ordinal);

        var macos = Job(ci, "union-macos");
        var refused = macos.IndexOf("sh install/install.sh > \"$RUNNER_TEMP/refused.txt\"", StringComparison.Ordinal);
        var copied = macos.IndexOf("test-installation-macos.sh stand-in desktop/dist/mac-arm64/Jason.app \"$RUNNER_TEMP/install-stand-in-signer.sh\"", StringComparison.Ordinal);
        Assert.True(refused >= 0 && copied > refused, "union-macos does not let the released install.sh refuse the test build before it copies it");
        var standing = Read([".github", "scripts", "test-installation-macos.sh"]);
        Assert.Contains("[ \"$(diff install/install.sh \"$copy\" | grep -c '^[<>]' || true)\" = 2 ] || fail", standing, StringComparison.Ordinal);

        // The copies live in the runner's temporary folder, which no upload names.
        Assert.DoesNotContain("install-stand-in-signer", string.Join('\n', Regex.Matches(ci, @"path: [^\n]+").Select(match => match.Value)), StringComparison.Ordinal);

        foreach (var released in new[] { Read(Release), Read(Action) })
        {
            foreach (var never in new[] { "install-stand-in-signer", "New-SelfSignedCertificate", "LocalMachine\\Root", "Cert:\\", "Jason CI test build", "test-installation-macos.sh stand-in" })
            {
                Assert.DoesNotContain(never, released, StringComparison.Ordinal);
            }
        }
    }

    /// <summary>
    /// Each install script names exactly one signer, in one line that is a plain value, and nothing it reads - an environment
    /// variable, an option - can change it: the Windows script assigns <c>$signer</c> once, the literal publisher; the macOS
    /// script holds a signature to one requirement naming the release's team, a constant assigned once, which every
    /// <c>codesign -R</c> it runs tests.
    /// </summary>
    [Fact]
    public void Each_install_script_names_one_signer_that_nothing_it_reads_can_change()
    {
        var ps1 = Read(Ps1Script).ReplaceLineEndings("\n").Split('\n');
        var assigned = ps1.Where(line => Regex.IsMatch(line, @"^\s*\$signer\s*=")).ToList();
        Assert.Equal(["        $signer = 'ReplyApp Inc.'"], assigned);
        Assert.DoesNotContain(ps1, line => Regex.IsMatch(line, @"\$signer\s*(\+=|=)") && !assigned.Contains(line));
        Assert.DoesNotContain(ps1, line => Regex.IsMatch(line, @"(?i)\$env:[A-Za-z_]*(SIGN|PUBLISHER)"));
        Assert.DoesNotContain(ps1, line => Regex.IsMatch(line, @"(?i)\[[^\]]*\]\s*\$[A-Za-z]*(Signer|Publisher|Unsigned|SkipSignature)\b"));

        // The macOS script holds a signature to one requirement, in one line, which the copy changes alone; every line that
        // names the release's team is a plain assignment of a constant, made once, that nothing it reads can change; and
        // codesign is asked to test that requirement and no other.
        var sh = Read(ShScript).ReplaceLineEndings("\n").Split('\n');
        var requirement = Assert.Single(sh, line => line.Contains("anchor apple generic", StringComparison.Ordinal));
        Assert.Contains("BD8M7U5YBV", requirement, StringComparison.Ordinal);
        var naming = sh.Where(line => line.Contains("BD8M7U5YBV", StringComparison.Ordinal)).ToList();
        Assert.Contains(requirement, naming);
        foreach (var line in naming)
        {
            var variable = Regex.Match(line, @"^([A-Z_]+)=(['""])[^$'""]*(""[^$'""]*""[^$'""]*)*\2$");
            Assert.True(variable.Success, $"install.sh names the release's team in a line that is not a plain assignment of a constant: {line}");
            var name = variable.Groups[1].Value;
            Assert.Single(sh, other => Regex.IsMatch(other, $@"^\s*(export\s+|readonly\s+|local\s+)?{name}="));
            Assert.DoesNotContain(sh, other => other.Contains("${" + name + ":", StringComparison.Ordinal) || other.Contains("${" + name + "-", StringComparison.Ordinal) || other.Contains("${" + name + "=", StringComparison.Ordinal));
        }

        var held = Regex.Match(requirement, @"^([A-Z_]+)=").Groups[1].Value;
        var tested = sh.Where(line => line.Contains("-R", StringComparison.Ordinal) && line.Contains("codesign", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(tested);
        Assert.All(tested, line => Assert.Contains($"-R=\"${held}\"", line, StringComparison.Ordinal));
        Assert.DoesNotContain(sh, line => Regex.IsMatch(line, @"^\s*--[a-z-]*(sign|team|unsigned|requirement)[a-z-]*\)"));
    }
}
