using System.Text.RegularExpressions;
using YamlDotNet.RepresentationModel;
using static Jason.Runtime.Tests.Documentation.Workflows;

namespace Jason.Runtime.Tests.Documentation;

/// <summary>
/// The demo's sample in a release: made once, on Linux, by a job of its own, with the release's jason and the desktop's
/// own seed; held to being whole and to carrying no folder of the runner; handed to the three jobs that build the desktop,
/// whose packages carry it; and never published as a release asset of its own.
/// </summary>
public partial class ReleaseWorkflowTests
{
    private const string DemoJob = "demo";
    private const string DemoArtifact = "demo-archive";
    private const string DemoDownloaded = "artifacts/demo/jason-demo.zip";
    private const string DemoScript = ".github/scripts/make-demo-archive.mts";

    private static YamlMappingNode ReleaseJobs() => Mapping(Parse(Read(Release)), "jobs");

    private static List<string> Needs(YamlMappingNode job) => Node(job, "needs") switch
    {
        YamlScalarNode one => [one.Value!],
        YamlSequenceNode many => [.. many.Children.Select(need => need.ToString())],
        _ => [],
    };

    /// <summary>
    /// One job makes the sample: on Linux, after the version job and nothing else, reading the tree and holding no secret,
    /// in no environment. It publishes the release's jason for Linux at the release's version through the desktop's own
    /// prepare script, which also puts the demo provider's package beside it; runs the script that has the desktop's own
    /// code make the demo installation and seed it, in a home of a plain name of its own; holds the archive to carrying no
    /// folder of the runner; and uploads it, failing when there is none.
    /// </summary>
    [Fact]
    public void The_demo_s_sample_is_made_once_on_linux_by_a_job_of_its_own()
    {
        var job = Mapping(ReleaseJobs(), DemoJob);
        Assert.Equal("ubuntu-latest", Scalar(job, "runs-on"));
        Assert.Equal(["version"], Needs(job));
        Assert.Null(Node(job, "strategy"));
        Assert.Null(Node(job, "environment"));
        Assert.Equal(["contents=read"], Mapping(job, "permissions").Children.Select(pair => $"{pair.Key}={pair.Value}"));
        Assert.DoesNotContain("secrets.", Job(Read(Release), DemoJob), StringComparison.Ordinal);
        Assert.DoesNotContain(JobSteps(job), step => Scalar(step, "uses") == "./.github/actions/package");

        var steps = JobSteps(job).ToList();
        Assert.StartsWith("actions/checkout@", Scalar(steps[0], "uses"), StringComparison.Ordinal);
        var dotnet = Assert.Single(steps, step => Scalar(step, "uses") == "./.github/actions/setup-dotnet");
        Assert.Equal("${{ needs.version.outputs.sdk }}", Scalar(Mapping(dotnet, "with"), "sdk-version"));
        var node = Assert.Single(steps, step => Scalar(step, "uses")?.StartsWith("actions/setup-node@", StringComparison.Ordinal) == true);
        Assert.Equal("24", Scalar(Mapping(node, "with"), "node-version"));

        var publish = Assert.Single(steps, step => Scalar(step, "run")?.Contains("scripts/prepare.mjs", StringComparison.Ordinal) == true);
        Assert.Equal("desktop", Scalar(publish, "working-directory"));
        Assert.Equal("${{ needs.version.outputs.version }}", Scalar(Mapping(publish, "env"), "VERSION"));
        Assert.Equal("node scripts/prepare.mjs --rid linux-x64 --output bundles/demo --version \"$VERSION\"", Scalar(publish, "run")!.Trim());

        var make = Assert.Single(steps, step => Scalar(step, "run")?.Contains(DemoScript, StringComparison.Ordinal) == true);
        var making = Scalar(make, "run")!;
        Assert.Contains("sudo mkdir /home/demo", making, StringComparison.Ordinal);
        Assert.Contains(
            $"node --experimental-strip-types {DemoScript} --bundle desktop/bundles/demo --home /home/demo --output \"$RUNNER_TEMP/demo/jason-demo.zip\"",
            making,
            StringComparison.Ordinal);

        var held = Assert.Single(steps, step => Scalar(step, "run")?.Contains("unzip -q", StringComparison.Ordinal) == true);
        var holding = Scalar(held, "run")!;
        Assert.Contains("for path in \"$HOME\" \"$GITHUB_WORKSPACE\" \"$RUNNER_TEMP\" \"$RUNNER_TOOL_CACHE\"; do", holding, StringComparison.Ordinal);
        Assert.Contains("grep -r -a -l -F -- \"$path\" \"$unpacked\"", holding, StringComparison.Ordinal);
        Assert.Contains("grep -q -F '\"home\": \"/home/demo\"' \"$unpacked/export.json\"", holding, StringComparison.Ordinal);

        var upload = Assert.Single(steps, step => Scalar(step, "uses")?.StartsWith("actions/upload-artifact@", StringComparison.Ordinal) == true);
        Assert.Equal(DemoArtifact, Scalar(Mapping(upload, "with"), "name"));
        Assert.Equal("${{ runner.temp }}/demo/jason-demo.zip", Scalar(Mapping(upload, "with"), "path"));
        Assert.Equal("error", Scalar(Mapping(upload, "with"), "if-no-files-found"));

        Assert.True(
            steps.IndexOf(publish) < steps.IndexOf(make) && steps.IndexOf(make) < steps.IndexOf(held) && steps.IndexOf(held) < steps.IndexOf(upload),
            "The sample is made from the published jason, held to carrying nothing of the runner, and only then uploaded.");
    }

    /// <summary>
    /// The script makes the demo installation and its sample with the desktop's own code, never a copy of it, runs every
    /// verb from the home it was given with the bundle copied into it, fails unless the seed's own marker says the sample
    /// is whole, and removes what the run logged before the export.
    /// </summary>
    [Fact]
    public void The_demo_s_script_uses_the_desktop_s_own_seed_and_exports_only_a_whole_sample()
    {
        var script = Read([.. DemoScript.Split('/')]);
        foreach (var module in new[] { "cli", "demo-folders", "demo-plugin", "demo-seed", "demo-settings", "demo-start", "mode" })
        {
            Assert.Contains($"from '../../desktop/src/{module}.ts';", script, StringComparison.Ordinal);
        }

        Assert.Contains("await readyTheSample(deps, ", script, StringComparison.Ordinal);
        Assert.Contains("if (marker === null || !marker.complete || marker.version !== seedVersion)", script, StringComparison.Ordinal);
        Assert.Contains("cpSync(prepared, bundle, { recursive: true });", script, StringComparison.Ordinal);
        Assert.Contains("cwd: options?.cwd ?? home", script, StringComparison.Ordinal);

        var logs = script.IndexOf("rmSync(join(places.data, 'logs'), { recursive: true, force: true });", StringComparison.Ordinal);
        var export = script.IndexOf("await verb(cli, ['export', output]);", StringComparison.Ordinal);
        var stop = script.IndexOf("await verb(cli, ['runtime', 'stop']);", StringComparison.Ordinal);
        Assert.True(stop >= 0 && logs > stop && export > logs, "The runtime is stopped, its logs removed, and only then is the archive written.");
    }

    /// <summary>
    /// The three jobs that build the desktop wait for the sample, take it from the demo job's artifact, and hand it to the
    /// package call that builds their desktop, and to none other.
    /// </summary>
    [Fact]
    public void The_three_desktop_builds_wait_for_the_sample_and_carry_it()
    {
        var jobs = ReleaseJobs();
        var building = jobs.Children
            .Select(pair => (Name: pair.Key.ToString(), Job: (YamlMappingNode)pair.Value))
            .Where(found => JobSteps(found.Job).Any(step => Scalar(step, "uses") == "./.github/actions/package"))
            .ToList();
        Assert.Equal(3, building.Count);

        foreach (var (name, job) in building)
        {
            Assert.Equal(["version", DemoJob], Needs(job));
            var steps = JobSteps(job).ToList();
            var download = Assert.Single(steps, step => Scalar(step, "uses")?.StartsWith("actions/download-artifact@", StringComparison.Ordinal) == true);
            Assert.Equal(DemoArtifact, Scalar(Mapping(download, "with"), "name"));
            Assert.Equal("artifacts/demo", Scalar(Mapping(download, "with"), "path"));

            var calls = steps.Where(step => Scalar(step, "uses") == "./.github/actions/package").ToList();
            var desktop = Assert.Single(calls, call => Scalar(Mapping(call, "with"), "desktop") == "true");
            Assert.Equal(DemoDownloaded, Scalar(Mapping(desktop, "with"), "demo-archive"));
            Assert.All(calls.Where(call => call != desktop), call => Assert.Null(Node(Mapping(call, "with"), "demo-archive")));
            Assert.True(steps.IndexOf(download) < steps.IndexOf(desktop), $"The {name} job builds its desktop before it has the sample.");
        }
    }

    /// <summary>
    /// The sample is the desktop's to carry and never a release asset of its own: its artifact's name is outside the
    /// pattern the release job downloads and publishes from.
    /// </summary>
    [Fact]
    public void The_demo_s_archive_is_never_a_release_asset()
    {
        var download = Assert.Single(JobSteps(PublishingJob()), step => Scalar(step, "uses")?.StartsWith("actions/download-artifact@", StringComparison.Ordinal) == true);
        var pattern = Scalar(Mapping(download, "with"), "pattern")!;
        Assert.Equal("jason-*", pattern);
        var glob = new Regex("^" + Regex.Escape(pattern).Replace("\\*", ".*", StringComparison.Ordinal) + "$");
        Assert.DoesNotMatch(glob, DemoArtifact);
        Assert.DoesNotContain(DemoJob, Needs(PublishingJob()));
    }

    /// <summary>
    /// The action hands the archive a call names to the desktop's prepare script, which puts it in the bundle, and holds
    /// every package it built to carrying that archive; a call that names none, a pull request's, prepares none and holds
    /// its packages to carrying none.
    /// </summary>
    [Fact]
    public void The_action_carries_the_sample_a_call_names_and_holds_each_package_to_it()
    {
        var input = Mapping(Mapping(Parse(Read(Action)), "inputs"), "demo-archive");
        Assert.Equal("false", Scalar(input, "required"));
        Assert.Equal(string.Empty, Scalar(input, "default"));
        Assert.Null(Node(Mapping(PackagingCalls(Ci).Single(call => Scalar(Mapping(call, "with"), "desktop") == "true"), "with"), "demo-archive"));

        const string WindowsDemo = "$demo = if ($env:DEMO_ARCHIVE) { @('--demo', (Join-Path $env:GITHUB_WORKSPACE $env:DEMO_ARCHIVE)) } else { @() }";
        const string BashDemo = "if [ -n \"$DEMO_ARCHIVE\" ]; then demo=(--demo \"$GITHUB_WORKSPACE/$DEMO_ARCHIVE\"); fi";
        string[] steps =
        [
            "Build the desktop's Windows installer, each file signed as it is made",
            "Verify the desktop's Windows installer and what it carries",
            "Build the desktop's Mac packages, signed and notarized",
            "Verify the desktop's Mac packages and what they carry",
            LinuxDesktopBuild,
            LinuxDesktopVerify,
        ];
        foreach (var name in steps)
        {
            var step = ActionStep(name);
            Assert.Equal("${{ inputs.demo-archive }}", Scalar(Mapping(step, "env"), "DEMO_ARCHIVE"));
            var run = Scalar(step, "run")!;
            Assert.Contains(name.Contains("Windows", StringComparison.Ordinal) ? WindowsDemo : BashDemo, run, StringComparison.Ordinal);
            var lines = run.Split('\n').Select(line => line.Trim()).Where(line => line.StartsWith("node scripts/prepare.mjs ", StringComparison.Ordinal) || line.StartsWith("node desktop/scripts/check-package.mjs ", StringComparison.Ordinal)).ToList();
            Assert.NotEmpty(lines);
            Assert.All(lines, line => Assert.Matches(name.Contains("Windows", StringComparison.Ordinal) ? @" @demo$" : @" ""\$\{demo\[@\]\}""( \|\|.*)?$", line));
        }
    }
}
