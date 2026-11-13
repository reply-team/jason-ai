using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using Jason.Contracts.Plugins;
using Jason.Contracts.Update;
using Jason.Runtime.Plugins.Registry;
using YamlDotNet.RepresentationModel;
using static Jason.Runtime.Tests.Documentation.Workflows;

namespace Jason.Runtime.Tests.Documentation;

/// <summary>
/// The release pipeline's files, held to what the code they package already says. The action that packs an
/// executable, the workflow that runs it on a tag and the manifest the runtime later reads cannot see each
/// other, so the names they must agree on are read from <see cref="ReleaseAssets"/> here rather than typed a
/// second time — and what a workflow may do in only one place is asserted on the parsed document, not counted
/// as substrings.
/// <para>
/// Two pieces of the pipeline are plain PowerShell and are executed here rather than read: the step that turns
/// a tag into a version, and the script that turns three fragments into the manifest. Both need <c>pwsh</c>,
/// which every CI runner has; on a machine without it those tests skip and say so.
/// </para>
/// <para>
/// What these tests do not prove is the workflows running: GitHub runs them, and a composite action has no
/// runner on a developer's machine. The packaging half is exercised by every pull request's CI; the release
/// half is not, until a tag exists.
/// </para>
/// </summary>
public partial class ReleaseWorkflowTests
{
    private static readonly string[] Action = [".github", "actions", "package", "action.yml"];
    private static readonly string[] Ci = [".github", "workflows", "ci.yml"];
    private static readonly string[] Release = [".github", "workflows", "release.yml"];
    private static readonly string[] Script = [".github", "scripts", "Write-Manifest.ps1"];
    private static readonly string[] Notes = [".github", "release-notes-template.md"];
    private static readonly Dictionary<string, string> NoEnvironment = [];

    [Fact]
    public void The_packaging_action_names_the_assets_the_code_names()
    {
        var action = Read(Action);
        foreach (var rid in ReleaseAssets.Rids)
        {
            Assert.Contains(rid, action, StringComparison.Ordinal);
            Assert.Contains(ReleaseAssets.For(rid), action, StringComparison.Ordinal);
        }

        // Q5: every archive carries a licence beside the executable, and it is the repository's MIT licence (the test below).
        Assert.Contains("LICENSE", action, StringComparison.Ordinal);
    }

    [Fact]
    public void Ci_packages_through_the_action_rather_than_beside_it()
    {
        var ci = Read(Ci);
        var action = Read(Action);

        Assert.Contains("./.github/actions/package", ci, StringComparison.Ordinal);

        // PB3: the action owns the publish, so ci.yml must no longer carry one — counting publishes in ci.yml
        // would assert the opposite of what is wanted the moment this succeeds.
        Assert.Contains("dotnet publish", action, StringComparison.Ordinal);
        Assert.DoesNotContain("dotnet publish", ci, StringComparison.Ordinal);
    }

    /// <summary>
    /// PB2: the version is an input, so <c>ci.yml</c> and <c>release.yml</c> call the action identically and
    /// neither the action nor the CI workflow reads a tag that, on a pull request, does not exist.
    /// </summary>
    [Fact]
    public void The_action_is_told_the_version_rather_than_reading_the_tag()
    {
        var action = Read(Action);
        var inputs = Mapping(Parse(action), "inputs");

        Assert.Contains("version", inputs.Children.Keys.Select(key => key.ToString()));
        Assert.Contains("rid", inputs.Children.Keys.Select(key => key.ToString()));
        Assert.Contains("-p:Version=", action, StringComparison.Ordinal);
        Assert.DoesNotContain("github.ref_name", action, StringComparison.Ordinal);
        Assert.DoesNotContain("github.ref", action, StringComparison.Ordinal);
    }

    /// <summary>
    /// The archive holds the executable, <c>LICENSE</c> and <c>THIRD-PARTY-NOTICES</c> and nothing else, which is
    /// only true if the executable is one file. A self-contained single-file publish leaves native libraries beside it — SQLite's
    /// among them — unless the project says to bundle them, and nothing in CI would have noticed: the smoke
    /// test never opens a database. The project says so, and the action refuses a publish directory holding
    /// anything but the executable and its symbols, so the property cannot be lost quietly.
    /// </summary>
    [Fact]
    public void The_published_executable_is_one_file_with_its_native_libraries_inside()
    {
        var project = Read(["dotnet", "src", "Jason.App", "Jason.App.csproj"]);
        Assert.Contains("<IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>", project, StringComparison.Ordinal);

        Assert.Contains(".pdb", Read(Action), StringComparison.Ordinal);
    }

    /// <summary>
    /// A runner that has packaged once already — a re-run of a failed job, which keeps the workspace — has the
    /// archive sitting there, and <c>Compress-Archive</c> refuses to write over it. The tar half has never had
    /// the problem, because <c>tar -czf</c> truncates; the zip half is the one that needs saying.
    /// </summary>
    /// <remarks>
    /// The action's own line is run twice against a package directory of this test's own, with the error
    /// preference the runner sets for a <c>pwsh</c> step. Nothing here is a copy of the line: it is read out of
    /// the action and handed to <c>pwsh</c>.
    /// </remarks>
    [Fact]
    public void Every_archive_is_written_even_when_one_is_already_there()
    {
        var zipping = Assert.IsType<YamlSequenceNode>(Node(Mapping(Parse(Read(Action)), "runs"), "steps")).Children.Cast<YamlMappingNode>()
            .Single(step => Scalar(step, "name") == "Archive as zip");

        using var tree = new TempTree();
        var package = Path.Combine(tree.Root, "artifacts", "package", "win-x64");
        Directory.CreateDirectory(package);
        Directory.CreateDirectory(Path.Combine(tree.Root, "artifacts", "release"));
        File.WriteAllText(Path.Combine(package, "jason.exe"), "not really an executable");
        File.WriteAllText(Path.Combine(package, "LICENSE"), "not really the licence");

        var step = Path.Combine(tree.Root, "archive.ps1");
        File.WriteAllText(step, Scalar(zipping, "run"));
        var environment = new Dictionary<string, string>
        {
            ["RID"] = "win-x64",
            ["ARCHIVE"] = $"artifacts/release/{ReleaseAssets.For("win-x64")}",
        };

        // `shell: pwsh` on a runner is `pwsh -Command` with the error preference set to Stop, so a cmdlet that
        // writes an error ends the step. That is what makes the second run a failed job rather than a warning.
        var line = $"$ErrorActionPreference = 'Stop'; Set-Location -LiteralPath '{tree.Root}'; & '{step}'";

        var first = Pwsh(environment, "-Command", line);
        Assert.True(first.Exit == 0, first.Stderr);

        var again = Pwsh(environment, "-Command", line);
        Assert.True(again.Exit == 0, $"A warm runner packages a second time and: {again.Stderr}");
    }

    /// <summary>
    /// The archive carries the repository's own <c>LICENSE</c>: the MIT licence covers the whole repository, the
    /// runtime included, and the executable is built from it.
    /// </summary>
    /// <remarks>
    /// The action's own step is read out of the action and run against a tree of this test's own, so the test sees
    /// which file lands beside the executable rather than what the step mentions.
    /// </remarks>
    [Fact]
    public void The_archive_carries_the_repository_s_licence()
    {
        var staging = Assert.IsType<YamlSequenceNode>(Node(Mapping(Parse(Read(Action)), "runs"), "steps")).Children.Cast<YamlMappingNode>()
            .Single(step => Scalar(step, "name") == "Put LICENSE and THIRD-PARTY-NOTICES beside the executable");

        using var tree = new TempTree();
        File.WriteAllText(Path.Combine(tree.Root, "LICENSE"), "the repository's licence");
        File.WriteAllText(Path.Combine(tree.Root, "THIRD-PARTY-NOTICES"), "the notices");
        var executable = Path.Combine(tree.Root, "jason.exe");
        File.WriteAllText(executable, "not really an executable");

        var step = Path.Combine(tree.Root, "stage.ps1");
        File.WriteAllText(step, Scalar(staging, "run"));
        var environment = new Dictionary<string, string>
        {
            ["RID"] = "win-x64",
            ["EXECUTABLE"] = executable,
        };

        var run = Pwsh(environment, "-Command", $"$ErrorActionPreference = 'Stop'; Set-Location -LiteralPath '{tree.Root}'; & '{step}'");
        Assert.True(run.Exit == 0, run.Stderr);

        var package = Path.Combine(tree.Root, "artifacts", "package", "win-x64");
        Assert.Equal("the repository's licence", File.ReadAllText(Path.Combine(package, "LICENSE")));
        Assert.Equal(["LICENSE", "THIRD-PARTY-NOTICES", "jason.exe"], Directory.GetFiles(package).Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// Every archive holds exactly the executable, <c>LICENSE</c> and <c>THIRD-PARTY-NOTICES</c>. The two halves choose
    /// differently: the zip takes everything the staging step put in the package directory, and the tarball names its
    /// entries, so a file staged and not named would never ship. So both are run here, after the real staging step, and
    /// the archive each writes is listed.
    /// </summary>
    /// <remarks>
    /// The tarball's step is bash, run here as GitHub runs <c>shell: bash</c>. Windows has no bash a test can count on,
    /// so that row runs on CI's Linux and macOS legs and is skipped on Windows.
    /// </remarks>
    [Theory]
    [Critical(Family.I1, "the archive's content")]
    [InlineData("win-x64")]
    [InlineData("linux-x64")]
    public void Every_archive_holds_the_executable_its_licence_and_the_notices_and_nothing_else(string rid)
    {
        var tarball = !rid.StartsWith("win-", StringComparison.Ordinal);
        Assert.SkipWhen(tarball && OperatingSystem.IsWindows(), "The tarball is written by a bash step; CI's Linux and macOS legs run this row.");

        var steps = Assert.IsType<YamlSequenceNode>(Node(Mapping(Parse(Read(Action)), "runs"), "steps")).Children.Cast<YamlMappingNode>().ToList();
        var staging = steps.Single(step => Scalar(step, "name") == "Put LICENSE and THIRD-PARTY-NOTICES beside the executable");
        var archiving = steps.Single(step => Scalar(step, "name") == (tarball ? "Archive as tar.gz" : "Archive as zip"));

        using var tree = new TempTree();
        File.WriteAllText(Path.Combine(tree.Root, "LICENSE"), "the repository's licence");
        File.WriteAllText(Path.Combine(tree.Root, "THIRD-PARTY-NOTICES"), "the notices");
        var executableName = tarball ? "jason" : "jason.exe";
        var executable = Path.Combine(tree.Root, "publish", executableName);
        Directory.CreateDirectory(Path.GetDirectoryName(executable)!);
        File.WriteAllText(executable, "not really an executable");
        var environment = new Dictionary<string, string>
        {
            ["RID"] = rid,
            ["EXECUTABLE"] = executable,
            ["ARCHIVE"] = $"artifacts/release/{ReleaseAssets.For(rid)}",
        };

        var stage = Path.Combine(tree.Root, "stage.ps1");
        File.WriteAllText(stage, Scalar(staging, "run"));
        var staged = Pwsh(environment, "-Command", $"$ErrorActionPreference = 'Stop'; Set-Location -LiteralPath '{tree.Root}'; & '{stage}'");
        Assert.True(staged.Exit == 0, staged.Stderr);

        (int Exit, string Stdout, string Stderr) written;
        if (tarball)
        {
            written = Bash(environment, tree.Root, Scalar(archiving, "run")!);
        }
        else
        {
            var zip = Path.Combine(tree.Root, "archive.ps1");
            File.WriteAllText(zip, Scalar(archiving, "run"));
            written = Pwsh(environment, "-Command", $"$ErrorActionPreference = 'Stop'; Set-Location -LiteralPath '{tree.Root}'; & '{zip}'");
        }

        Assert.True(written.Exit == 0, written.Stderr);
        var entries = Entries(Path.Combine(tree.Root, "artifacts", "release", ReleaseAssets.For(rid)), tarball);
        Assert.Equal(["LICENSE", "THIRD-PARTY-NOTICES", executableName], entries.Keys.Order(StringComparer.Ordinal));
        Assert.Equal("the repository's licence", entries["LICENSE"]);
        Assert.Equal("the notices", entries["THIRD-PARTY-NOTICES"]);
    }

    /// <summary>A zip's or a tarball's entries, each name with its text.</summary>
    private static Dictionary<string, string> Entries(string archive, bool tarball)
    {
        var entries = new Dictionary<string, string>(StringComparer.Ordinal);
        if (tarball)
        {
            using var gzip = new GZipStream(File.OpenRead(archive), CompressionMode.Decompress);
            using var reader = new TarReader(gzip);
            while (reader.GetNextEntry() is { } entry)
            {
                using var text = new StreamReader(entry.DataStream ?? Stream.Null);
                entries.Add(entry.Name, text.ReadToEnd());
            }
        }
        else
        {
            using var zip = ZipFile.OpenRead(archive);
            foreach (var entry in zip.Entries)
            {
                using var text = new StreamReader(entry.Open());
                entries.Add(entry.FullName, text.ReadToEnd());
            }
        }

        return entries;
    }

    /// <summary>
    /// A bash step as GitHub runs <c>shell: bash</c> — <c>bash --noprofile --norc -eo pipefail</c> — in a directory of
    /// the test's own and with the environment the step is given.
    /// </summary>
    private static (int Exit, string Stdout, string Stderr) Bash(IReadOnlyDictionary<string, string> environment, string directory, string script)
    {
        var file = Path.Combine(directory, "step.sh");
        File.WriteAllText(file, script);
        var start = new ProcessStartInfo("bash")
        {
            WorkingDirectory = directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in (string[])["--noprofile", "--norc", "-eo", "pipefail", file])
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
        if (!process.WaitForExit(TimeSpan.FromSeconds(120)))
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail("bash did not finish in two minutes.");
        }

        return (process.ExitCode, stdout.Result, stderr.Result);
    }

    /// <summary>
    /// The Windows executable a release archives is read for its application manifest first: the policy that keeps
    /// a console window off the desktop when the runtime starts at logon, and <c>asInvoker</c>, both read out of
    /// the file about to be archived rather than out of the project that built it. <c>ci.yml</c> and
    /// <c>release.yml</c> both package through this action, so both get the check.
    /// </summary>
    [Fact]
    public void The_packaged_windows_executable_is_read_for_its_manifest_before_it_is_archived()
    {
        var steps = Assert.IsType<YamlSequenceNode>(Node(Mapping(Parse(Read(Action)), "runs"), "steps")).Children.Cast<YamlMappingNode>().ToList();

        var reading = steps.FindIndex(step => Scalar(step, "name") == "Read the application manifest out of the packaged executable");
        Assert.True(reading >= 0, "No step reads the packaged executable's application manifest.");
        Assert.Equal("startsWith(inputs.rid, 'win-')", Scalar(steps[reading], "if"));

        // Exactly this, and nothing after it. The check refuses with exit 1, which fails a pwsh step only as the
        // step's last command: a command appended after it would end the step with its own exit code instead, and
        // a refused executable would be archived.
        Assert.Equal(
            "./.github/scripts/Assert-ApplicationManifest.ps1 -Executable $env:EXECUTABLE",
            Scalar(steps[reading], "run")!.Trim());
        Assert.True(File.Exists(Path.Combine(RepositoryRoot(), ".github", "scripts", "Assert-ApplicationManifest.ps1")));

        // And nothing lets the refusal through: continue-on-error on this step, or on a workflow step that packages
        // through this action, would turn a refused executable into a warning and archive it anyway.
        Assert.Null(Node(steps[reading], "continue-on-error"));
        foreach (var workflow in (string[][])[Ci, Release])
        {
            var jobs = Mapping(Parse(Read(workflow)), "jobs").Children.Values.Cast<YamlMappingNode>();
            var packaging = jobs
                .SelectMany(job => Node(job, "steps") is YamlSequenceNode sequence ? sequence.Children.Cast<YamlMappingNode>() : [])
                .Where(step => Scalar(step, "uses") == "./.github/actions/package")
                .ToList();
            Assert.NotEmpty(packaging);
            Assert.All(packaging, step => Assert.Null(Node(step, "continue-on-error")));
        }

        var published = steps.FindIndex(step => Scalar(step, "id") == "publish");
        var archived = steps.FindIndex(step => Scalar(step, "name")?.StartsWith("Archive as", StringComparison.Ordinal) == true);
        Assert.True(published >= 0 && reading > published, "The manifest is read before there is an executable to read.");
        Assert.True(archived > reading, "The manifest is read after the executable was archived.");
    }

    /// <summary>
    /// Whether the SDK writes a Windows executable's resources when it builds on another host has not been
    /// measured for this project, and an executable without its manifest would open a console window at every
    /// logon. So the project refuses that build, and every CI leg asks the refusal by name: on Windows it must let a
    /// win-x64 build through, elsewhere refuse it.
    /// </summary>
    [Fact]
    public void Ci_proves_a_windows_executable_is_built_on_windows_or_not_at_all()
    {
        var jobs = Mapping(Parse(Read(Ci)), "jobs");
        var steps = Assert.IsType<YamlSequenceNode>(Node(Mapping(jobs, "build-test-publish"), "steps")).Children.Cast<YamlMappingNode>();

        var proving = Assert.Single(steps, step => Scalar(step, "run")?.Contains("-t:RefuseWindowsExecutableElsewhere", StringComparison.Ordinal) == true);
        Assert.Contains("-p:RuntimeIdentifier=win-x64", Scalar(proving, "run"), StringComparison.Ordinal);

        // A pwsh step ends with the last native command's exit code, and on Linux and macOS that is the refusal's own
        // 1: the step printed "refused", which is the pass, and then failed on it. It says how it ended itself.
        Assert.EndsWith("exit 0", Scalar(proving, "run")!.TrimEnd(), StringComparison.Ordinal);

        var project = File.ReadAllText(Path.Combine(RepositoryRoot(), "dotnet", "src", "Jason.App", "Jason.App.csproj"));
        Assert.Contains("<Target Name=\"RefuseWindowsExecutableElsewhere\"", project, StringComparison.Ordinal);

        // The refusal is a precaution, and says so: that the SDK cannot write the manifest elsewhere is what an older
        // SDK's warning said, not something measured with the one this repository pins.
        Assert.Contains("has not been measured", project, StringComparison.Ordinal);
        Assert.DoesNotContain("cannot write", project, StringComparison.Ordinal);
        Assert.DoesNotContain("only on Windows", Read(Ci) + Read(Action), StringComparison.Ordinal);
    }

    /// <summary>The fixture's README says what CI fills it over: every archive a release carries, each by its platform.</summary>
    [Fact]
    public void The_schema_1_fixture_says_it_is_filled_over_all_three_archives()
    {
        var readme = Regex.Replace(Read(["install", "feeds", "schema-1", "README.md"]), @"\s+", " ");
        Assert.Contains("CI fills it over the three archives it has just built", readme, StringComparison.Ordinal);
        Assert.All(ReleaseAssets.Rids, rid => Assert.Contains($"`{rid}`", readme, StringComparison.Ordinal));
    }

    /// <summary>
    /// Every step of either workflow that starts a runtime tells it not to ask the release feed: it reaches no network
    /// because it is told not to, not because the step stops the runtime before its first check. <c>update apply</c>
    /// and <c>update rollback</c> start one too, the version they put in place.
    /// </summary>
    [Fact]
    public void Every_workflow_step_that_starts_a_runtime_turns_the_update_check_off()
    {
        string[] starts = ["runtime start", "update apply", "update rollback"];
        var steps = new[] { Ci, Release }
            .SelectMany(workflow => Mapping(Parse(Read(workflow)), "jobs").Children.Values.Cast<YamlMappingNode>()
                .Where(job => Node(job, "steps") is YamlSequenceNode)
                .SelectMany(job => ((YamlSequenceNode)Node(job, "steps")!).Children.Cast<YamlMappingNode>())
                .Select(step => (Workflow: workflow[^1], Step: step)))
            .Where(found => Scalar(found.Step, "run") is { } run && starts.Any(start => run.Contains(start, StringComparison.Ordinal)))
            .ToList();

        Assert.True(steps.Count >= 3, $"only {steps.Count} step(s) start a runtime; the rule would hold of almost nothing");
        Assert.Contains(steps, found => found.Workflow == Release[^1]);
        var unguarded = steps
            .Where(found => (Node(found.Step, "env") is YamlMappingNode env ? Scalar(env, "JASON_Update__CheckEnabled") : null) != "false")
            .Select(found => $"{found.Workflow}: {Scalar(found.Step, "name")}")
            .ToList();
        Assert.True(unguarded.Count == 0, $"these steps start a runtime that may ask the release feed: {string.Join("; ", unguarded)}");
    }

    /// <summary>
    /// A Windows older than 24H2 has to accept the manifest, and nothing on the page's word alone says it does. The
    /// setting is in a namespace such a loader does not know, and one that rejected it would not start
    /// <c>jason.exe</c> at all. So a job on an older image starts the executable the build packaged, from the build's
    /// own artifact: <c>--version</c>, then a runtime started and stopped. It refuses to count a runner that is 24H2
    /// or later, where the check would pass without proving anything.
    /// </summary>
    [Fact]
    public void Ci_starts_the_packaged_executable_on_a_windows_older_than_its_manifest_s_setting()
    {
        var jobs = Mapping(Parse(Read(Ci)), "jobs");
        var older = Mapping(jobs, "older-windows");

        Assert.Equal("windows-2022", Scalar(older, "runs-on"));
        Assert.Contains(
            "build-test-publish",
            Assert.IsType<YamlSequenceNode>(Node(older, "needs")).Children.Select(need => need.ToString()));

        var steps = Assert.IsType<YamlSequenceNode>(Node(older, "steps")).Children.Cast<YamlMappingNode>().ToList();

        // The executable the build packaged, not one this job builds for itself.
        var download = Assert.Single(steps, step => Scalar(step, "uses")?.StartsWith("actions/download-artifact@", StringComparison.Ordinal) == true);
        Assert.Equal("jason-win-x64", Scalar(Mapping(download, "with"), "name"));
        Assert.DoesNotContain(steps, step => Scalar(step, "run")?.Contains("dotnet ", StringComparison.Ordinal) == true);

        var starting = Assert.Single(steps, step => Scalar(step, "run")?.Contains("--version", StringComparison.Ordinal) == true);
        var run = Scalar(starting, "run")!;
        Assert.Contains("-ge 26100", run, StringComparison.Ordinal);
        Assert.Contains("runtime start", run, StringComparison.Ordinal);
        Assert.Contains("runtime stop", run, StringComparison.Ordinal);
        Assert.Contains("JASON_DATA_DIR", run, StringComparison.Ordinal);
        Assert.EndsWith("exit 0", run.TrimEnd(), StringComparison.Ordinal);

        // It reaches no network because it is told not to, not because the runtime stops before its first check.
        Assert.Equal("false", Scalar(Mapping(starting, "env"), "JASON_Update__CheckEnabled"));

        // And what runtime start hands the runtime on a second build: a console with no window, let go of. One start,
        // so exactly one such line, and that line says it: a second line, or none, fails the step before the match.
        Assert.Contains("$console.Count -ne 1", run, StringComparison.Ordinal);
        Assert.Contains("$console[0] -notmatch '\"Console\":\"freed_hidden\"'", run, StringComparison.Ordinal);
    }

    /// <summary>
    /// A pull request is stamped with <c>-dev</c> after the version and a release with the bare version, and a test
    /// can pass under one and fail under the other: three update tests advertised the release the tree was on its
    /// way to as the newer one, which a <c>-dev</c> build is older than and the release is not, so every pull request
    /// passed and the release's own test step failed. So the whole suite runs once more on every pull request, on
    /// one Linux runner, stamped with what a release of this tree would carry: <c>VersionPrefix</c> as MSBuild
    /// answers it, not a second copy of the number. Every other job keeps the <c>-dev</c> version.
    /// </summary>
    [Fact]
    public void Ci_runs_the_whole_suite_under_the_version_a_release_would_carry()
    {
        var ci = Read(Ci);
        var jobs = Mapping(Parse(ci), "jobs");
        static List<YamlMappingNode> Steps(YamlMappingNode job) =>
            [.. Assert.IsType<YamlSequenceNode>(Node(job, "steps")).Children.Cast<YamlMappingNode>()];

        var version = Mapping(jobs, "version");
        Assert.Equal("${{ steps.version.outputs.release }}", Scalar(Mapping(version, "outputs"), "release"));
        var asking = Scalar(Assert.Single(Steps(version), step => Scalar(step, "id") == "version"), "run")!;
        Assert.Contains("dotnet msbuild dotnet/src/Jason.App/Jason.App.csproj -getProperty:VersionPrefix", asking, StringComparison.Ordinal);
        Assert.Contains("\"release=$release\" >> $env:GITHUB_OUTPUT", asking, StringComparison.Ordinal);

        // One job, on Linux and nowhere else, that is stamped with it for the build and the tests alike.
        var stamped = Mapping(jobs, "release-stamp");
        Assert.Equal("ubuntu-latest", Scalar(stamped, "runs-on"));
        Assert.Null(Node(stamped, "strategy"));
        Assert.Equal("version", Scalar(stamped, "needs"));
        var steps = Steps(stamped);
        var runs = steps.Select(step => Scalar(step, "run")).OfType<string>().ToList();
        Assert.Single(runs, run => run.StartsWith("dotnet build dotnet/Jason.slnx --configuration Release", StringComparison.Ordinal)
            && run.Contains("-p:Version=${{ needs.version.outputs.release }}", StringComparison.Ordinal));
        // The tests run through the script every test step of ci.yml runs them through, told the release's version, and
        // the whole suite, not a part of it: the three tests that failed were not where anybody would have looked. So the
        // step names no project, no namespace and not the critical tier.
        var testing = Assert.Single(steps, step => Scalar(step, "run")?.Contains("Invoke-Tests.ps1", StringComparison.Ordinal) == true);
        var test = Scalar(testing, "run")!;
        Assert.StartsWith("./.github/scripts/Invoke-Tests.ps1 -Version $env:VERSION ", test, StringComparison.Ordinal);
        Assert.Equal("${{ needs.version.outputs.release }}", Scalar(Mapping(testing, "env"), "VERSION"));
        foreach (var narrowing in new[] { "-Projects", "-Namespaces", "-NotNamespaces", "-Classes", "-NotClasses", "-Critical", "filter" })
        {
            Assert.DoesNotContain(narrowing, test, StringComparison.OrdinalIgnoreCase);
        }

        // Run as a runner runs it, with a stand-in for dotnet, the step makes one call: the whole solution, stamped with the
        // version it was told, with the test platform's repeat of a failed test and no filter.
        Assert.Equal(
            "test --solution dotnet/Jason.slnx --configuration Release --no-build -p:Version=9.8.7 --results-directory <results>/1 -- --retry-failed-tests 2 --report-xunit-trx",
            Assert.Single(TestCalls(testing, "9.8.7")));

        // And it proves it was stamped, from what was built: a job that quietly lost the stamp would pass as -dev.
        var check = Assert.Single(steps, step => Scalar(step, "run")?.Contains("FileVersionInfo", StringComparison.Ordinal) == true);
        Assert.Equal("${{ needs.version.outputs.release }}", Scalar(Mapping(check, "env"), "RELEASE"));
        Assert.Contains("throw", Scalar(check, "run"), StringComparison.Ordinal);
        Assert.True(
            steps.IndexOf(check) < steps.IndexOf(testing),
            "The stamp is checked after the tests have already run under it.");

        // Every other job keeps the -dev version, and nothing stamps VersionPrefix itself: that would leave the suffix behind.
        // The one exception is the desktop job's Linux packages, which are never built at a -dev version: they carry the
        // release with a ci word and the run's number, a candidate's shape no release ever has, on that one line.
        foreach (var name in jobs.Children.Keys.Select(key => key.ToString()).Where(name => name is not "version" and not "release-stamp"))
        {
            var text = Job(ci, name);
            if (name == "desktop")
            {
                const string candidate = "version: ${{ needs.version.outputs.release }}-ci.${{ github.run_number }}";
                Assert.Equal(1, Regex.Count(text, Regex.Escape(candidate)));
                text = text.Replace(candidate, string.Empty, StringComparison.Ordinal);
            }

            Assert.DoesNotContain("outputs.release", text, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("-p:VersionPrefix", ci, StringComparison.Ordinal);
    }

    /// <summary>
    /// The <c>dotnet</c> calls a test step of ci.yml makes, run as a runner runs it from the repository's root and told
    /// <paramref name="version"/>, with a stand-in for <c>dotnet</c>: a PowerShell function, which a command name finds
    /// before any program. It records each call, with the results directory written as <c>&lt;results&gt;</c>, and
    /// succeeds having written no report.
    /// </summary>
    private static List<string> TestCalls(YamlMappingNode step, string version)
    {
        using var tree = new TempTree();
        var results = Path.Combine(tree.Root, "test-results");
        var calls = Path.Combine(tree.Root, "calls");
        File.WriteAllText(calls, string.Empty);
        var script = Path.Combine(tree.Root, "step.ps1");
        File.WriteAllText(script, Scalar(step, "run"));
        var harness = Path.Combine(tree.Root, "harness.ps1");
        File.WriteAllText(harness, $$"""
            $ErrorActionPreference = 'Stop'
            function dotnet {
                Add-Content -LiteralPath '{{calls}}' -Value (($args -join ' ').Replace('{{results}}', '<results>').Replace('\', '/'))
                $global:LASTEXITCODE = 0
            }
            Set-Location -LiteralPath '{{RepositoryRoot()}}'
            & '{{script}}'
            exit $LASTEXITCODE
            """);

        var ran = Pwsh(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["VERSION"] = version,
                ["RUNNER_TEMP"] = tree.Root,
                ["GITHUB_STEP_SUMMARY"] = Path.Combine(tree.Root, "summary"),
            },
            "-File",
            harness);
        Assert.True(ran.Exit == 0, ran.Stdout + ran.Stderr);
        return [.. File.ReadAllLines(calls).Where(line => line.Length > 0)];
    }

    /// <summary>
    /// The checks ci.yml reports, its matrix legs expanded, are exactly the twelve this repository's ruleset requires
    /// before a pull request merges. A renamed job is a required check that never reports, and every pull request
    /// would wait for it: a job is renamed here only together with the ruleset. The job that reports a check a rule
    /// skipped reports it under a name another job's leg has, which CiRunsTests holds, so it adds no name here. A
    /// platform's name is reported by the job that waits for that platform's package and every part of its tests, which
    /// CiRunsTests holds cannot be green while one of them is not; the package and the parts report under names of their
    /// own, listed below as optional.
    /// </summary>
    [Fact]
    [Critical(Family.B7, "pipeline and ruleset agree")]
    public void Ci_reports_exactly_the_checks_the_ruleset_requires()
    {
        string[] required =
        [
            "The version this tree carries",
            "The suite under a release's version",
            "win-x64",
            "linux-x64",
            "osx-arm64",
            "jason.exe on a Windows older than 24H2",
            "an update on win-x64",
            "an update on linux-x64",
            "install.sh on ubuntu",
            "install.ps1 on windows",
            "The UI on ubuntu",
            "The desktop on ubuntu",
        ];

        // Reported and not required by the ruleset: each platform's package, and each part of its tests, which the
        // platform's own check above waits for and is green only when they are. Making one required is a ruleset change,
        // made together with moving its name up.
        string[] optional =
        [
            "linux-x64 package",
            "win-x64 package",
            "osx-arm64 package",
            "linux-x64 tests: the critical tier",
            "linux-x64 tests: App",
            "linux-x64 tests: Cli, Contracts and PluginHost",
            "linux-x64 tests: Runtime 1 of 6",
            "linux-x64 tests: Runtime 2 of 6",
            "linux-x64 tests: Runtime 3 of 6",
            "linux-x64 tests: Runtime 4 of 6",
            "linux-x64 tests: Runtime 5 of 6",
            "linux-x64 tests: Runtime 6 of 6",
            "win-x64 tests: App",
            "win-x64 tests: Cli, Contracts and PluginHost",
            "win-x64 tests: Runtime 1 of 6",
            "win-x64 tests: Runtime 2 of 6",
            "win-x64 tests: Runtime 3 of 6",
            "win-x64 tests: Runtime 4 of 6",
            "win-x64 tests: Runtime 5 of 6",
            "win-x64 tests: Runtime 6 of 6",
            "osx-arm64 tests: App",
            "osx-arm64 tests: Cli, Contracts and PluginHost",
            "osx-arm64 tests: Runtime 1 of 6",
            "osx-arm64 tests: Runtime 2 of 6",
            "osx-arm64 tests: Runtime 3 of 6",
            "osx-arm64 tests: Runtime 4 of 6",
            "osx-arm64 tests: Runtime 5 of 6",
            "osx-arm64 tests: Runtime 6 of 6",
        ];

        var jobs = Mapping(Parse(Read(Ci)), "jobs");
        var reported = new List<string>();
        foreach (var (key, value) in jobs.Children.Where(pair => pair.Key.ToString() != "skipped-by-rule"))
        {
            var job = (YamlMappingNode)value;
            var name = Scalar(job, "name") ?? key.ToString();
            var legs = Node(job, "strategy") is YamlMappingNode strategy ? MatrixLegs(jobs, Node(strategy, "matrix")) : null;
            if (legs is null)
            {
                reported.Add(name);
                continue;
            }

            reported.AddRange(legs.Select(leg => Regex.Replace(name, @"\$\{\{\s*matrix\.(\w+)\s*\}\}", match => leg.TryGetValue(match.Groups[1].Value, out var said) ? said : match.Value)));
        }

        Assert.True(
            required.Concat(optional).Order(StringComparer.Ordinal).SequenceEqual(reported.Order(StringComparer.Ordinal)),
            $"ci.yml reports {string.Join(", ", reported.Select(name => $"'{name}'"))}, and the ruleset requires "
            + $"{string.Join(", ", required.Select(name => $"'{name}'"))} beside the optional {string.Join(", ", optional.Select(name => $"'{name}'"))}. "
            + "A check renamed here is renamed in the ruleset in the same change; a new one is listed here as required or optional.");
    }

    /// <summary>
    /// Every leg a matrix can run: a literal <c>include</c> as written, or — where the version job decides the legs — the
    /// whole list that job's deciding step is given, which is what a full run runs. The parts of the tests are every
    /// platform in each part of the whole suite, and the critical tier's part on linux-x64, the one platform a run that
    /// tests the tier builds.
    /// </summary>
    private static List<Dictionary<string, string>>? MatrixLegs(YamlMappingNode jobs, YamlNode? matrix)
    {
        if (matrix is YamlMappingNode written && Node(written, "include") is YamlSequenceNode include)
        {
            return [.. include.Children.Cast<YamlMappingNode>()
                .Select(leg => leg.Children.ToDictionary(pair => pair.Key.ToString(), pair => pair.Value.ToString(), StringComparer.Ordinal))];
        }

        var decided = Regex.Match((matrix as YamlScalarNode)?.Value ?? string.Empty, @"^\$\{\{\s*fromJSON\(needs\.version\.outputs\.(\w+)\)\s*\}\}$");
        if (!decided.Success)
        {
            return null;
        }

        var deciding = Assert.IsType<YamlSequenceNode>(Node(Mapping(jobs, "version"), "steps")).Children.Cast<YamlMappingNode>()
            .Single(step => Scalar(step, "id") == "legs");
        if (decided.Groups[1].Value == "tests")
        {
            var env = Mapping(deciding, "env");
            using var platforms = JsonDocument.Parse(Scalar(env, "PLATFORMS")!);
            using var parts = JsonDocument.Parse(Scalar(env, "PARTS")!);
            return [.. platforms.RootElement.EnumerateArray().SelectMany(platform => parts.RootElement.EnumerateArray()
                .Where(part => part.GetProperty("tier").GetString() == "false" || platform.GetProperty("rid").GetString() == "linux-x64")
                .Select(part => new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["rid"] = platform.GetProperty("rid").GetString()!,
                    ["part"] = part.GetProperty("part").GetString()!,
                }))];
        }

        using var all = JsonDocument.Parse(Scalar(Mapping(deciding, "env"), decided.Groups[1].Value.ToUpperInvariant())!);
        return [.. all.RootElement.EnumerateArray()
            .Select(leg => leg.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.GetString()!, StringComparer.Ordinal))];
    }

    // --- release.yml -------------------------------------------------------------------------------------

    /// <summary>
    /// A tag publishes the release, in the repository the run is in; the button makes a draft there at most, and with
    /// <c>dry_run</c> nothing at all. The inputs say so where a person presses the button.
    /// </summary>
    [Fact]
    public void A_tag_publishes_the_release_and_the_button_makes_a_draft_at_most()
    {
        var triggers = Mapping(Parse(Read(Release)), "on");

        var tags = Assert.IsType<YamlSequenceNode>(Node(Mapping(triggers, "push"), "tags"));
        Assert.Contains("v*", tags.Children.Select(tag => tag.ToString()));
        var inputs = Mapping(Mapping(triggers, "workflow_dispatch"), "inputs");
        Assert.Equal(
            "The version to build, without the v (0.2.0, 1.0.0-rc.1). Nothing is tagged and nothing is published: the result is a draft release in reply-team/jason-ai, and nothing anywhere else, unless dry_run is set.",
            Scalar(Mapping(inputs, "version"), "description"));
        Assert.Equal(
            "Build and assemble everything and publish nothing: no draft, no release, no request to the tap. The assembled release comes back as an artifact of this run.",
            Scalar(Mapping(inputs, "dry_run"), "description"));

        // One step publishes, the tag's release or the button's draft, and only outside a dry run in the public
        // repository; the tap is asked only after a tag's release, there.
        Assert.Equal(PublishesHere, Scalar(PublishingStep(PublishStep), "if"));
        Assert.Equal(TagHere, Scalar(TapJob(), "if"));
    }

    /// <summary>The step that publishes the release, or the button's draft.</summary>
    private const string PublishStep = "Publish the release";

    /// <summary>The condition of the step that publishes: not a dry run, and in the public repository.</summary>
    private const string PublishesHere = "${{ !inputs.dry_run && github.repository == 'reply-team/jason-ai' }}";

    /// <summary>The condition of the job that asks the tap: a tag, in the public repository.</summary>
    private const string TagHere = "${{ github.event_name == 'push' && github.repository == 'reply-team/jason-ai' }}";

    /// <summary>The tap's token, as the one step that sends it names it.</summary>
    private const string TapToken = "${{ secrets.HOMEBREW_TAP_DISPATCH_TOKEN }}";

    /// <summary>
    /// The button's dry run builds and assembles everything a release would — the three platforms, the plugin, the UI,
    /// the manifest, the checksums and the notes — and hands it back as one artifact to read, publishing nothing: every
    /// step that publishes, in any job — a script that publishes, and any action but the three that move the run's own
    /// files in a job that may write — and every job that asks the tap, carries a condition a dry run fails.
    /// </summary>
    [Fact]
    public void A_dry_run_assembles_everything_and_publishes_nothing()
    {
        var release = Parse(Read(Release));
        var inputs = Mapping(Mapping(Mapping(release, "on"), "workflow_dispatch"), "inputs");
        var input = Mapping(inputs, "dry_run");
        Assert.Equal("boolean", Scalar(input, "type"));
        Assert.Equal("false", Scalar(input, "default"));
        Assert.NotEqual("true", Scalar(input, "required"));

        var acting = Acting(release).ToList();
        Assert.Equal([$"release/{PublishStep}", "tap/Dispatch the tap's bump"], acting.Select(act => $"{act.Job}/{act.Step}"));
        Assert.Empty(ReachedByADryRun(release));

        // What a dry run hands back: the assembled release, as one artifact, after the notes are written.
        var steps = PublishingSteps();
        var upload = Assert.Single(steps, step => Scalar(step, "uses")?.StartsWith("actions/upload-artifact@", StringComparison.Ordinal) == true);
        Assert.Equal("${{ inputs.dry_run }}", Scalar(upload, "if"));
        Assert.Equal("artifacts/release", Scalar(Mapping(upload, "with"), "path"));
        Assert.Equal("error", Scalar(Mapping(upload, "with"), "if-no-files-found"));
        Assert.Equal("7", Scalar(Mapping(upload, "with"), "retention-days"));
        var notes = steps.ToList().FindIndex(step => Scalar(step, "name") == "Write the notes");
        Assert.True(notes >= 0 && notes < steps.ToList().IndexOf(upload), "The dry run's artifact is uploaded before the notes it carries are written.");
    }

    /// <summary>
    /// Each step that acts under a condition a dry run can meet, as a line saying which; empty when every one carries
    /// the dry-run clause or runs on a tag push alone.
    /// </summary>
    private static List<string> ReachedByADryRun(YamlMappingNode release) =>
        [.. Acting(release)
            .Where(act => !act.Condition.StartsWith("${{ !inputs.dry_run && ", StringComparison.Ordinal) && !act.Condition.StartsWith("${{ github.event_name == 'push' && ", StringComparison.Ordinal))
            .Select(act => $"'{act.Step}' in the {act.Job} job acts, and its condition '{act.Condition}' lets a dry run reach it")];

    /// <summary>
    /// Every step of the workflow that publishes or asks the tap, with its job and the condition it runs under: its own,
    /// joined to its job's when both have one. A step acts when its script publishes or dispatches, and when it runs an
    /// action in a job that may write: an action is handed the run's token through its default input whatever its
    /// <c>with</c> says, so what it does with the job's write is not read here, and only the three actions that move this
    /// run's own files — the checkout, and the upload and download of its artifacts — are taken as not acting.
    /// </summary>
    private static IEnumerable<(string Job, string Step, string Condition)> Acting(YamlMappingNode release) =>
        Mapping(release, "jobs").Children
            .SelectMany(job => JobSteps((YamlMappingNode)job.Value)
                .Where(step =>
                    (Scalar(step, "run") is { } run && (Publishes().IsMatch(Code(run)) || Dispatches().IsMatch(Code(run)))) ||
                    (Scalar(step, "uses") is { } uses && MayWrite(release, (YamlMappingNode)job.Value) && !MovesThisRunsFiles().IsMatch(uses)))
                .Select(step => (
                    Job: job.Key.ToString(),
                    Step: Scalar(step, "name") ?? "(unnamed)",
                    Condition: string.Join(" and ", new[] { Scalar((YamlMappingNode)job.Value, "if"), Scalar(step, "if") }.OfType<string>()))));

    /// <summary>A step's script without its comment lines, so that what it says about publishing is not read as doing it.</summary>
    private static string Code(string run) =>
        string.Join('\n', run.Split('\n').Where(line => !line.TrimStart().StartsWith('#')));

    /// <summary>A call that publishes or writes: a release made, removed or changed, a pull request, a tag, a commit or a push.</summary>
    [GeneratedRegex(@"\bgh\s+release\s+(?:create|delete|edit|upload)\b|\bgh\s+pr\b|\bgit\b[^\n]*\s(?:commit|push|tag)\b")]
    private static partial Regex Publishes();

    /// <summary>A request that starts a workflow somewhere: a dispatch, through the API or through <c>gh</c>.</summary>
    [GeneratedRegex(@"/dispatches\b|\bgh\s+workflow\s+run\b|\bgh\s+api\b")]
    private static partial Regex Dispatches();

    /// <summary>The three actions that move this run's own files and nothing else: the checkout and the artifacts' upload and download.</summary>
    [GeneratedRegex(@"^actions/(?:checkout|upload-artifact|download-artifact)@")]
    private static partial Regex MovesThisRunsFiles();

    /// <summary>
    /// An action in a job that may write acts, whatever it is for: it is handed the run's token through its default input,
    /// so it carries the dry-run and repository clauses as a script that publishes does, or it is refused. The checkout and
    /// the artifacts' upload and download alone are taken as not acting, and an action in a job that only reads is not
    /// acting either. Held against release.yml as it is and against copies of it: an action that publishes the release
    /// under the dry-run clause alone, the same action under both clauses, an action of this repository's with no
    /// condition, and an action in a job that reads. Each copy names the step that acts, and is refused or not as the row
    /// says.
    /// </summary>
    [Theory]
    [InlineData("as it is", "", false)]
    [InlineData("an action that publishes under the dry-run clause alone", "release/Publish through an action", true)]
    [InlineData("the same action under both clauses", "release/Publish through an action", false)]
    [InlineData("an action of this repository's with no condition", "release/Run an action of this repository's", true)]
    [InlineData("an action in a job that reads", "", false)]
    public void An_action_in_a_job_that_may_write_acts_and_holds_both_clauses(string copy, string acts, bool refused)
    {
        var text = Read(Release).ReplaceLineEndings("\n");
        const string Saying = "      - name: Say that nothing is published here\n";
        const string Install = "      - name: Install\n";

        string Before(string anchor, string step)
        {
            Assert.Equal(1, Regex.Count(text, Regex.Escape(anchor)));
            return text.Replace(anchor, step + anchor, StringComparison.Ordinal);
        }

        const string Action = "      - name: Publish through an action\n        if: @IF@\n        uses: softprops/action-gh-release@v2\n        with:\n          files: artifacts/release/*\n\n";
        var moved = copy switch
        {
            "as it is" => text,
            "an action that publishes under the dry-run clause alone" => Before(Saying, Action.Replace("@IF@", "${{ !inputs.dry_run }}", StringComparison.Ordinal)),
            "the same action under both clauses" => Before(Saying, Action.Replace("@IF@", PublishesHere, StringComparison.Ordinal)),
            "an action of this repository's with no condition" => Before(Saying, "      - name: Run an action of this repository's\n        uses: ./.github/actions/setup-dotnet\n\n"),
            "an action in a job that reads" => Before(Install, "      - uses: actions/setup-python@v6\n\n"),
            _ => throw new ArgumentOutOfRangeException(nameof(copy)),
        };

        var release = Parse(moved);
        var acting = Acting(release).Select(act => $"{act.Job}/{act.Step}").ToList();
        Assert.Equal(
            ((string[])[$"release/{PublishStep}", "tap/Dispatch the tap's bump", acts]).Where(name => name.Length > 0).Order(StringComparer.Ordinal),
            acting.Order(StringComparer.Ordinal));

        var problems = ActsOutsideThePublicRepository(moved).Concat(ReachedByADryRun(release)).ToList();
        Assert.True(refused == (problems.Count > 0), $"{copy}: {string.Join("; ", problems)}");
    }

    /// <summary>
    /// A tag pushed to any other repository that holds this workflow — a fork, a copy, the repository the code came
    /// from — builds what it can and publishes nothing, and asks the tap nothing: every step that publishes, and every
    /// job that asks the tap, runs only when the run is in <c>reply-team/jason-ai</c>, as one clause of a condition that
    /// is all clauses. A copy without the signing secrets stops at the macOS and Windows jobs and assembles nothing; one
    /// that holds them assembles the release, and its release job says in one line that it publishes nothing.
    /// </summary>
    [Fact]
    public void Nothing_is_published_or_asked_of_the_tap_outside_the_public_repository()
    {
        Assert.Empty(ActsOutsideThePublicRepository(Read(Release)));

        var saying = Assert.Single(PublishingSteps(), step => Scalar(step, "if") == "${{ github.repository != 'reply-team/jason-ai' }}");
        Assert.Null(Node(saying, "uses"));
        Assert.Equal("pwsh", Scalar(saying, "shell"));
        Assert.True(
            PublishingSteps().ToList().IndexOf(saying) < PublishingSteps().ToList().IndexOf(PublishingStep(PublishStep)),
            "The release job says it publishes nothing here after the step that would have published.");

        using var tree = new TempTree();
        var script = Path.Combine(tree.Root, "say.ps1");
        File.WriteAllText(script, Scalar(saying, "run"));
        var (exit, stdout, stderr) = Pwsh(new Dictionary<string, string> { ["GITHUB_REPOSITORY"] = "someone/jason-copy" }, "-File", script);

        Assert.True(exit == 0, stderr);
        Assert.Equal(
            "::notice title=Not published::This run is in someone/jason-copy, and a release is published from reply-team/jason-ai alone: nothing is published here and the tap is not asked.",
            stdout.Trim());
    }

    /// <summary>
    /// The repository condition held against release.yml as it is and against copies that loosen it: gone from the
    /// publishing step, gone from the tap's job, joined with an or, naming another repository, and a second step that
    /// publishes without it. Each copy is refused, and release.yml itself is not.
    /// </summary>
    [Theory]
    [InlineData("as it is", false)]
    [InlineData("gone from the publishing step", true)]
    [InlineData("gone from the tap's job", true)]
    [InlineData("joined with an or", true)]
    [InlineData("another repository", true)]
    [InlineData("a second publishing step without it", true)]
    public void The_repository_condition_is_held_on_every_step_that_acts(string copy, bool refused)
    {
        var text = Read(Release).ReplaceLineEndings("\n");
        const string Publish = "        if: " + PublishesHere + "\n";
        const string Tap = "    if: " + TagHere + "\n";
        const string Upload = "      - name: Keep what a dry run assembled\n";

        string Moved(string from, string to)
        {
            Assert.Equal(1, Regex.Count(text, Regex.Escape(from)));
            return text.Replace(from, to, StringComparison.Ordinal);
        }

        var moved = copy switch
        {
            "as it is" => text,
            "gone from the publishing step" => Moved(Publish, "        if: ${{ !inputs.dry_run }}\n"),
            "gone from the tap's job" => Moved(Tap, "    if: ${{ github.event_name == 'push' }}\n"),
            "joined with an or" => Moved(Publish, "        if: ${{ !inputs.dry_run || github.repository == 'reply-team/jason-ai' }}\n"),
            "another repository" => Moved(Publish, "        if: ${{ !inputs.dry_run && github.repository == 'someone/jason-copy' }}\n"),
            "a second publishing step without it" => Moved(
                Upload,
                "      - name: Upload one more file\n        if: ${{ !inputs.dry_run }}\n        shell: pwsh\n        run: gh release upload \"v$env:VERSION\" artifacts/release/manifest.json\n\n" + Upload),
            _ => throw new ArgumentOutOfRangeException(nameof(copy)),
        };

        var problems = ActsOutsideThePublicRepository(moved);
        Assert.True(refused == (problems.Count > 0), $"{copy}: {string.Join("; ", problems)}");
    }

    /// <summary>
    /// Each step that acts — publishes, asks the tap, or runs an action in a job that may write, as <see cref="Acting"/>
    /// reads it — without the public repository as a clause of its condition, as a line saying which; empty when every
    /// one carries it. A clause stands in a condition of clauses joined by <c>&amp;&amp;</c>
    /// alone, on the step or on its job.
    /// </summary>
    private static List<string> ActsOutsideThePublicRepository(string text)
    {
        const string Clause = "github.repository == 'reply-team/jason-ai'";
        var problems = new List<string>();
        foreach (var (job, step, condition) in Acting(Parse(text)))
        {
            var held = condition.Split(" and ").Any(one =>
                one.StartsWith("${{ ", StringComparison.Ordinal) && one.EndsWith(" }}", StringComparison.Ordinal) &&
                !one.Contains("||", StringComparison.Ordinal) &&
                one[4..^3].Split(" && ").Contains(Clause, StringComparer.Ordinal));
            if (!held)
            {
                problems.Add($"'{step}' in the {job} job acts under '{condition}', which does not hold it to reply-team/jason-ai");
            }
        }

        return problems;
    }

    /// <summary>
    /// The one job that may write to this repository is the one that publishes the release, and the token it writes with
    /// is this run's own: no job that builds or tests holds a write, the workflow's default stays read, and no
    /// publication token of any other kind stands anywhere. It publishes only in the public repository and only after
    /// every job that packages a platform, so nothing is published when one of them failed.
    /// </summary>
    [Fact]
    [Critical(Family.B4, "the release's write scope")]
    public void Only_the_release_job_may_write_to_this_repository_and_only_in_the_public_one()
    {
        var text = Read(Release);
        var release = Parse(text);
        Assert.Equal(["contents=read"], Mapping(release, "permissions").Children.Select(pair => $"{pair.Key}={pair.Value}"));

        var jobs = Mapping(release, "jobs");
        var writers = jobs.Children
            .Where(job => Node((YamlMappingNode)job.Value, "permissions") is YamlScalarNode { Value: "write-all" } ||
                (Node((YamlMappingNode)job.Value, "permissions") is YamlMappingNode permissions && Scalar(permissions, "contents") is "write"))
            .Select(job => job.Key.ToString())
            .ToList();
        Assert.Equal([PublishingJobName(jobs)], writers);
        Assert.Equal(["contents=write"], Mapping(PublishingJob(), "permissions").Children.Select(pair => $"{pair.Key}={pair.Value}"));

        // The token it writes with is this run's own, handed to the one step that publishes and to nothing else.
        Assert.Equal(1, Regex.Count(text, Regex.Escape("github.token")));
        Assert.Equal("${{ github.token }}", Scalar(Mapping(PublishingStep(PublishStep), "env"), "GH_TOKEN"));
        Assert.Null(Node(PublishingJob(), "env"));
        Assert.DoesNotContain("PUBLISH_TOKEN", text, StringComparison.Ordinal);

        // Every mention of a secret stands in its place, which SecretsOutOfPlace says; every step that acts holds to the
        // public repository, which ActsOutsideThePublicRepository says; every job declares the least it needs, which
        // PermissionsOutOfPlace says; and no checkout in a job that may write leaves the token in git's config for a
        // later step, which CredentialsLeftBehind says.
        Assert.Empty(SecretsOutOfPlace(text));
        Assert.Empty(ActsOutsideThePublicRepository(text));
        Assert.Empty(PermissionsOutOfPlace(release));
        Assert.Empty(CredentialsLeftBehind(release));

        // It runs after every job that packages a platform, and nothing is published when one of them failed: `needs`
        // is exactly that.
        var packaging = jobs.Children.Where(job => JobSteps((YamlMappingNode)job.Value).Any(step => Scalar(step, "uses") == "./.github/actions/package"))
            .Select(job => job.Key.ToString())
            .ToList();
        Assert.Equal(3, packaging.Count);
        Assert.All(packaging, job => Assert.Contains(job, Assert.IsType<YamlSequenceNode>(Node(PublishingJob(), "needs")).Children.Select(need => need.ToString())));
    }

    /// <summary>
    /// Every job declares its own permissions, the least it needs, rather than taking the workflow's: a job that checks
    /// the tree out reads its contents, the Windows job alone may ask for an OIDC token, the release job alone may write
    /// contents, and nothing else is granted anywhere. The workflow's own default stays read. Held against release.yml as
    /// it is and against copies of it: a job without its own declaration, a permission of another kind, a write of
    /// contents on the matrix job, the shorthands that grant everything, the workflow's default made a write, and a job
    /// that checks out with nothing granted. Each copy is refused, and release.yml itself is not.
    /// </summary>
    [Theory]
    [InlineData("as it is", false)]
    [InlineData("a job without its own", true)]
    [InlineData("actions: write on the plugin job", true)]
    [InlineData("contents: write on the matrix job", true)]
    [InlineData("read-all on a job", true)]
    [InlineData("write-all on a job", true)]
    [InlineData("the workflow's default a write", true)]
    [InlineData("nothing granted to a job that checks out", true)]
    public void Every_job_declares_the_least_permissions_it_needs(string copy, bool refused)
    {
        var release = Parse(Read(Release));
        var jobs = Mapping(release, "jobs");
        void Set(YamlMappingNode node, YamlNode? permissions)
        {
            node.Children.Remove(new YamlScalarNode("permissions"));
            if (permissions is not null)
            {
                node.Children.Add(new YamlScalarNode("permissions"), permissions);
            }
        }

        switch (copy)
        {
            case "as it is":
                break;
            case "a job without its own":
                Set(Mapping(jobs, "plugin"), null);
                break;
            case "actions: write on the plugin job":
                Set(Mapping(jobs, "plugin"), Parse("contents: read\nactions: write\n"));
                break;
            case "contents: write on the matrix job":
                Set(Mapping(jobs, "build"), Parse("contents: write\n"));
                break;
            case "read-all on a job":
                Set(Mapping(jobs, "ui"), new YamlScalarNode("read-all"));
                break;
            case "write-all on a job":
                Set(Mapping(jobs, "ui"), new YamlScalarNode("write-all"));
                break;
            case "the workflow's default a write":
                Set(release, Parse("contents: write\n"));
                break;
            case "nothing granted to a job that checks out":
                Set(Mapping(jobs, "ui"), new YamlMappingNode());
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(copy));
        }

        var problems = PermissionsOutOfPlace(release);
        Assert.True(refused == (problems.Count > 0), $"{copy}: {string.Join("; ", problems)}");
    }

    /// <summary>
    /// Each permission that is more than its job needs, or missing where its job needs it, as a line saying where; empty
    /// when the workflow grants contents: read alone and each job declares a mapping of its own holding only contents and
    /// id-token: contents read by every job that checks out, written by the job that creates the release alone, and an
    /// OIDC token asked for by the job that packages win-x64 alone.
    /// </summary>
    private static List<string> PermissionsOutOfPlace(YamlMappingNode release)
    {
        var problems = new List<string>();
        var workflow = Node(release, "permissions") as YamlMappingNode;
        if (workflow is null || !workflow.Children.Select(pair => $"{pair.Key}={pair.Value}").SequenceEqual(["contents=read"]))
        {
            problems.Add("the workflow's default is not contents: read alone");
        }

        var jobs = Mapping(release, "jobs");
        var releasing = PublishingJobName(jobs);
        foreach (var (name, job) in jobs.Children.Select(pair => (pair.Key.ToString(), (YamlMappingNode)pair.Value)))
        {
            if (Node(job, "permissions") is not YamlMappingNode permissions)
            {
                problems.Add($"the {name} job does not declare its own permissions as a mapping");
                continue;
            }

            var signs = JobSteps(job).Any(step =>
                Scalar(step, "uses") == "./.github/actions/package" && Node(step, "with") is YamlMappingNode with && Scalar(with, "rid") == "win-x64");
            var checksOut = JobSteps(job).Any(step => Scalar(step, "uses")?.StartsWith("actions/checkout@", StringComparison.Ordinal) == true);
            foreach (var (key, value) in permissions.Children.Select(pair => (pair.Key.ToString(), pair.Value.ToString())))
            {
                var allowed = (key, value) switch
                {
                    ("contents", "read") => true,
                    ("contents", "write") => name == releasing,
                    ("id-token", "write") => signs,
                    _ => false,
                };
                if (!allowed)
                {
                    problems.Add($"the {name} job is granted {key}: {value}, which it does not need");
                }
            }

            if (checksOut && Scalar(permissions, "contents") is not ("read" or "write"))
            {
                problems.Add($"the {name} job checks the tree out without contents: read");
            }
        }

        return problems;
    }

    /// <summary>
    /// Whether a job may write anything with the run's token: its own permissions, or the workflow's when it declares
    /// none, grant <c>write-all</c> or a write of any kind but <c>id-token</c>, which asks for an OIDC token and writes
    /// nothing of GitHub's. Permissions declared nowhere are whatever the repository's settings say, so they may write.
    /// </summary>
    private static bool MayWrite(YamlMappingNode release, YamlMappingNode job) =>
        (Node(job, "permissions") ?? Node(release, "permissions")) switch
        {
            null => true,
            YamlScalarNode { Value: "write-all" } => true,
            YamlMappingNode granted => granted.Children.Any(pair => pair.Key.ToString() != "id-token" && pair.Value.ToString() == "write"),
            _ => false,
        };

    /// <summary>
    /// A job that may write never leaves its token where a later step can take it. <c>actions/checkout</c> writes the
    /// token it checks out with into the repository's git config unless it is told not to, and every later step of the
    /// job can then push with it; so each checkout in such a job says <c>persist-credentials: false</c>, and the token
    /// reaches the one step that is handed it and nothing else. Held against release.yml as it is and against copies of
    /// it: the release job's checkout without the input, with it true, a second checkout without it, and a job made to
    /// write whose checkout keeps its default. Each copy is refused, and release.yml itself is not.
    /// </summary>
    [Theory]
    [InlineData("as it is", false)]
    [InlineData("the release job's checkout keeps its default", true)]
    [InlineData("persist-credentials true", true)]
    [InlineData("a second checkout in the release job", true)]
    [InlineData("a job made to write that checks out", true)]
    public void A_job_that_may_write_never_leaves_its_token_in_git_s_config(string copy, bool refused)
    {
        var release = Parse(Read(Release));
        var jobs = Mapping(release, "jobs");
        var steps = Assert.IsType<YamlSequenceNode>(Node(Mapping(jobs, PublishingJobName(jobs)), "steps"));
        var checkout = steps.Children.Cast<YamlMappingNode>().Single(step => Scalar(step, "uses")?.StartsWith("actions/checkout@", StringComparison.Ordinal) == true);

        switch (copy)
        {
            case "as it is":
                break;
            case "the release job's checkout keeps its default":
                checkout.Children.Remove(new YamlScalarNode("with"));
                break;
            case "persist-credentials true":
                Mapping(checkout, "with").Children[new YamlScalarNode("persist-credentials")] = new YamlScalarNode("true");
                break;
            case "a second checkout in the release job":
                steps.Children.Add(Parse("uses: actions/checkout@v7\n"));
                break;
            case "a job made to write that checks out":
                var ui = Mapping(jobs, "ui");
                ui.Children[new YamlScalarNode("permissions")] = Parse("contents: write\n");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(copy));
        }

        var problems = CredentialsLeftBehind(release);
        Assert.True(refused == (problems.Count > 0), $"{copy}: {string.Join("; ", problems)}");
    }

    /// <summary>
    /// Each checkout in a job that may write that leaves the token in git's config, as a line saying where; empty when
    /// every such checkout says <c>persist-credentials: false</c>.
    /// </summary>
    private static List<string> CredentialsLeftBehind(YamlMappingNode release) =>
        [.. Mapping(release, "jobs").Children
            .Where(job => MayWrite(release, (YamlMappingNode)job.Value))
            .SelectMany(job => JobSteps((YamlMappingNode)job.Value)
                .Where(step => Scalar(step, "uses")?.StartsWith("actions/checkout@", StringComparison.Ordinal) == true)
                .Where(step => !(Node(step, "with") is YamlMappingNode with && Scalar(with, "persist-credentials") == "false"))
                .Select(step => $"a checkout in the {job.Key} job, which may write, leaves the token in git's config: it does not say persist-credentials: false"))];

    /// <summary>
    /// Releases are published one at a time. The release job reads which release is the latest and then publishes; two
    /// runs doing that at once would read the same latest and both claim it, so whichever published second would be the
    /// latest, an older line's fix over a newer release among them. The release job of every run shares one concurrency
    /// group, and a run waiting for it never cancels the one publishing.
    /// </summary>
    [Fact]
    public void Releases_are_published_one_at_a_time()
    {
        var concurrency = Assert.IsType<YamlMappingNode>(Node(PublishingJob(), "concurrency"));
        Assert.Equal(["group=release-publish", "cancel-in-progress=false"], concurrency.Children.Select(pair => $"{pair.Key}={pair.Value}"));
    }

    /// <summary>
    /// The token the old release flow published with is gone from everything that runs or describes a release: the
    /// workflows, the actions, the scripts and the pages. A release is published with the run's own token now.
    /// </summary>
    [Fact]
    [Critical(Family.D3, "no page names the publication token")]
    public void No_workflow_script_or_page_names_the_publication_token()
    {
        var root = RepositoryRoot();
        var files = Directory.EnumerateFiles(Path.Combine(root, ".github"), "*", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(Path.Combine(root, "docs"), "*.md", SearchOption.AllDirectories))
            .Concat(Directory.EnumerateFiles(root, "*.md", SearchOption.TopDirectoryOnly));

        var naming = files.Where(file => File.ReadAllText(file).Contains("PUBLISH_" + "TOKEN", StringComparison.Ordinal))
            .Select(file => Path.GetRelativePath(root, file).Replace('\\', '/'))
            .ToList();
        Assert.Empty(naming);
    }

    /// <summary>
    /// Each mention of a secret in a release workflow that stands outside its place, as a line saying where it stands;
    /// empty when every one is where it belongs.
    /// </summary>
    private static List<string> SecretsOutOfPlace(string text)
    {
        var problems = new List<string>();
        var release = Parse(text);
        var jobs = Mapping(release, "jobs");

        // A job that calls another workflow runs steps this workflow does not show, and `secrets: inherit` hands that
        // workflow every secret without naming one in an expression, so neither is ever read past below.
        foreach (var (name, job) in jobs.Children.Select(pair => (pair.Key.ToString(), (YamlMappingNode)pair.Value)))
        {
            if (Node(job, "uses") is not null)
            {
                problems.Add($"the {name} job calls another workflow, whose steps this one does not show");
            }

            if (Node(job, "secrets") is not null)
            {
                problems.Add($"the {name} job hands secrets on to another workflow");
            }
        }

        // For each signed platform, the package call that names it itself, in a job none of whose steps runs a test. A
        // runner is handed every secret its job names, whatever the steps then do with them, so a platform's secrets
        // stand in a job that builds that platform alone and runs no test code and no test-only package beside them:
        // never in a matrix shared with another platform, never where a test runs, never in the other platform's job.
        // There is exactly one such call per platform, and each of its secrets stands there under the input of its own
        // name, its only place.
        var placed = new HashSet<YamlNode>(ReferenceEqualityComparer.Instance);
        foreach (var (rid, secrets) in SigningSecrets)
        {
            var calls = PackageCallsWithoutTests(jobs, rid).Select(call => call.Step).ToList();
            if (calls.Count != 1)
            {
                problems.Add($"{calls.Count} package calls name {rid} in a job that runs no test, where exactly one holds its secrets");
                continue;
            }

            var with = Mapping(calls[0], "with");
            foreach (var (input, secret) in secrets)
            {
                if (Node(with, input) is YamlScalarNode value && value.Value == $"${{{{ secrets.{secret} }}}}")
                {
                    placed.Add(value);
                }
                else
                {
                    problems.Add($"the {rid} package call's {input} is not secrets.{secret}");
                }
            }
        }

        // Every other mention of a secret, anywhere in the workflow, is the tap's token, once, in one job. A mention is
        // the secrets context named inside an expression, however it is read: by a name after a dot, by index, or whole.
        // A placed node is its place once: the parser hands an alias back as the node it names, so a second sight of
        // one is that secret standing somewhere else as well.
        var holders = new HashSet<string>(StringComparer.Ordinal);
        var taps = 0;
        var seen = new HashSet<YamlNode>(ReferenceEqualityComparer.Instance);
        foreach (var (where, scalar) in release.Children.SelectMany(pair => ScalarNodes(pair.Value).Select(node =>
            (Where: pair.Key.ToString() == "jobs" ? null : pair.Key.ToString(), Node: node))))
        {
            if (scalar.Value is null || !NamesSecrets().IsMatch(scalar.Value))
            {
                continue;
            }

            if (placed.Contains(scalar))
            {
                if (!seen.Add(scalar))
                {
                    problems.Add($"'{scalar.Value}' stands a second time, through an alias of its place");
                }

                continue;
            }

            var standing = jobs.Children.Where(pair => ScalarNodes(pair.Value).Any(node => ReferenceEquals(node, scalar))).Select(pair => pair.Key.ToString()).ToList();
            var job = where ?? string.Join(" and ", standing);
            if (where is null && scalar.Value == TapToken && ++taps == 1)
            {
                holders.UnionWith(standing);
            }
            else
            {
                problems.Add($"'{scalar.Value}' stands in {(where is null ? $"the {job} job" : $"the workflow's {where}")}, outside its place");
            }
        }

        if (holders.Count != 1)
        {
            problems.Add($"the tap's token stands in {holders.Count} jobs");
        }

        return problems;
    }

    /// <summary>Each signed platform and the secrets its package call alone holds, each under the input of its own name.</summary>
    /// <remarks>osx-x64 is signed in the macOS job as well, for the desktop's Intel package, and holds the same six.</remarks>
    private static (string Rid, (string Input, string Secret)[] Secrets)[] SigningSecrets =>
        [("osx-arm64", AppleSecrets), ("osx-x64", AppleSecrets), ("win-x64", AzureSecrets)];

    /// <summary>The package calls that name a platform literally, in a job none of whose steps runs a test, with the job's name.</summary>
    private static IEnumerable<(string Job, YamlMappingNode Step)> PackageCallsWithoutTests(YamlMappingNode jobs, string rid) =>
        jobs.Children
            .Where(job => !JobSteps((YamlMappingNode)job.Value).Any(step => Scalar(step, "run") is { } run && RunsTests().IsMatch(run)))
            .SelectMany(job => JobSteps((YamlMappingNode)job.Value)
                .Where(step => Scalar(step, "uses") == "./.github/actions/package")
                .Where(step => Node(step, "with") is YamlMappingNode with && Scalar(with, "rid") == rid)
                .Select(step => (job.Key.ToString(), step)));

    /// <summary>Every scalar node under a node, keys and values alike.</summary>
    private static IEnumerable<YamlScalarNode> ScalarNodes(YamlNode node) => node switch
    {
        YamlScalarNode scalar => [scalar],
        YamlSequenceNode sequence => sequence.Children.SelectMany(ScalarNodes),
        YamlMappingNode mapping => mapping.Children.SelectMany(pair => ScalarNodes(pair.Key).Concat(ScalarNodes(pair.Value))),
        _ => [],
    };

    /// <summary>A job's steps, none for a job that calls a workflow instead.</summary>
    private static IEnumerable<YamlMappingNode> JobSteps(YamlMappingNode job) =>
        Node(job, "steps") is YamlSequenceNode steps ? steps.Children.Cast<YamlMappingNode>() : [];

    /// <summary>A script that runs tests.</summary>
    [GeneratedRegex(@"\bdotnet\s+test\b")]
    private static partial Regex RunsTests();

    /// <summary>An expression that names the secrets context, as a word: <c>secrets.X</c>, <c>secrets['X']</c>, <c>toJSON(secrets)</c>.</summary>
    [GeneratedRegex(@"\$\{\{(?:(?!\}\}).)*\bsecrets\b", RegexOptions.Singleline)]
    private static partial Regex NamesSecrets();

    /// <summary>
    /// The place of each secret, held against release.yml as it is and against copies of it that move one: a seventh
    /// name beside the six, two of them swapped, a secret that is not Apple's in the package call, the six in another
    /// step of the macOS job, in another job's environment, in another job's package call or in the matrix's package
    /// call, which builds the other platforms beside the tests, a test run in the macOS job, the tap's token in the
    /// macOS job; and a secret read elsewhere without its name after a dot, the whole context or by index, and one
    /// of the six reached through a YAML alias of the node in the call, which the parser hands back as that node itself.
    /// The macOS job's second call, which builds osx-x64 for the desktop, holds the same six in the same way: a copy
    /// whose osx-x64 call lacks them, or that calls for osx-x64 twice, is refused as well. Each copy is refused, and
    /// release.yml itself is not.
    /// </summary>
    [Theory]
    [InlineData("as it is", false)]
    [InlineData("a seventh name", true)]
    [InlineData("two swapped", true)]
    [InlineData("a secret not Apple's in the call", true)]
    [InlineData("another step of the macOS job", true)]
    [InlineData("another job's environment", true)]
    [InlineData("another job's package call", true)]
    [InlineData("the matrix's package call", true)]
    [InlineData("a test in the macOS job", true)]
    [InlineData("the token in the macOS job", true)]
    [InlineData("the whole context in another job", true)]
    [InlineData("a secret by index in another job", true)]
    [InlineData("an alias of the call's team id", true)]
    [InlineData("the osx-x64 call's six dropped", true)]
    [InlineData("a second osx-x64 call", true)]
    public void Only_the_package_calls_that_build_a_macos_executable_hold_the_apple_secrets(string copy, bool refused)
    {
        var text = Read(Release).ReplaceLineEndings("\n");
        // The osx-arm64 call's team id, told from the osx-x64 call's by the step that follows it.
        const string ArmTail = "\n      # The archive is what a person downloads";
        const string TeamId = "          apple-team-id: ${{ secrets.APPLE_TEAM_ID }}\n";
        const string X64Call = "          rid: osx-x64\n";
        const string Archive = "      - name: Read the signature of the archived macOS executable\n        shell: bash\n        env:\n          ARCHIVE: ${{ steps.package.outputs.archive }}\n";
        const string PluginSteps = "  plugin:\n    name: The plugins' packages\n    needs: version\n    runs-on: ubuntu-latest\n";
        var six = string.Concat(AppleSecrets.Select(secret => $"          {secret.Input}: ${{{{ secrets.{secret.Secret} }}}}\n"));

        const string MacosPackage = "      - name: Package\n        id: package\n        uses: ./.github/actions/package\n        with:\n          rid: osx-arm64\n";
        const string MatrixPackage = "          rid: ${{ matrix.rid }}\n          version: ${{ needs.version.outputs.version }}\n";

        string Moved(string from, string to, string? within = null)
        {
            within ??= text;
            Assert.Equal(1, Regex.Count(within, Regex.Escape(from)));
            return within.Replace(from, to, StringComparison.Ordinal);
        }

        var moved = copy switch
        {
            "as it is" => text,
            "a seventh name" => Moved(TeamId + ArmTail, TeamId + "          apple-extra-key: ${{ secrets.APPLE_EXTRA_KEY }}\n" + ArmTail),
            "two swapped" => Moved(TeamId + ArmTail, "          apple-team-id: ${{ secrets.APPLE_NOTARY_KEY_ID }}\n" + ArmTail),
            "a secret not Apple's in the call" => Moved(TeamId + ArmTail, TeamId + "          extra: ${{ secrets.SOME_OTHER_TOKEN }}\n" + ArmTail),
            "another step of the macOS job" => Moved(Archive, Archive + "          KEY: ${{ secrets.APPLE_NOTARY_KEY_P8 }}\n"),
            "another job's environment" => Moved(PluginSteps, PluginSteps + "    env:\n      KEY: ${{ secrets.APPLE_NOTARY_KEY_P8 }}\n"),
            "another job's package call" => Moved(
                "    steps:\n      - uses: actions/checkout@v7\n\n      - name: Archive plugins/reply\n",
                "    steps:\n      - uses: actions/checkout@v7\n\n      - uses: ./.github/actions/package\n        with:\n          rid: linux-x64\n" + six +
                "\n      - name: Archive plugins/reply\n"),
            "the matrix's package call" => Moved(MatrixPackage, MatrixPackage + six, Moved(six + ArmTail, ArmTail)),
            "a test in the macOS job" => Moved(MacosPackage, "      - run: dotnet test --solution dotnet/Jason.slnx\n\n" + MacosPackage),
            "the token in the macOS job" => Moved(Archive, Archive + $"          TOKEN: {TapToken}\n"),
            "the whole context in another job" => Moved(PluginSteps, PluginSteps + "    env:\n      ALL: ${{ toJSON(secrets) }}\n"),
            "a secret by index in another job" => Moved(PluginSteps, PluginSteps + "    env:\n      KEY: ${{ secrets['APPLE_NOTARY_KEY_P8'] }}\n"),
            "an alias of the call's team id" => Moved(
                PluginSteps,
                PluginSteps + "    env:\n      TEAM: *team\n",
                Moved(TeamId + ArmTail, "          apple-team-id: &team ${{ secrets.APPLE_TEAM_ID }}\n" + ArmTail)),
            "the osx-x64 call's six dropped" => Moved(six + "\n      # The osx-x64 executable started once", "\n      # The osx-x64 executable started once"),
            "a second osx-x64 call" => Moved(
                "\n  windows:\n",
                "\n      - uses: ./.github/actions/package\n        with:\n" + X64Call + "          version: ${{ needs.version.outputs.version }}\n" + six + "\n  windows:\n"),
            _ => throw new ArgumentOutOfRangeException(nameof(copy)),
        };

        var problems = SecretsOutOfPlace(moved);
        Assert.True(refused == (problems.Count > 0), $"{copy}: {string.Join("; ", problems)}");
    }

    /// <summary>Every scalar under a node, keys and values alike.</summary>
    private static IEnumerable<string> Scalars(YamlNode node) => node switch
    {
        YamlScalarNode scalar => [scalar.Value ?? string.Empty],
        YamlSequenceNode sequence => sequence.Children.SelectMany(Scalars),
        YamlMappingNode mapping => mapping.Children.SelectMany(pair => Scalars(pair.Key).Concat(Scalars(pair.Value))),
        _ => [],
    };

    /// <summary>
    /// The notes address a running Jason shows is the release's page at the product's own address, written as a
    /// constant: the address redirects it to the public repository's release page, so it never depends on which
    /// repository a run is in. That repository is read in conditions alone, never in what a step writes.
    /// </summary>
    [Fact]
    public void The_notes_address_is_the_release_page_at_the_products_address()
    {
        var release = Read(Release);

        Assert.All(
            release.Split('\n').Where(line => line.Contains("github.repository", StringComparison.Ordinal)),
            line => Assert.StartsWith("if: ", line.Trim(), StringComparison.Ordinal));
        Assert.Contains(
            "-ReleaseNotesUrl \"https://get.jasonharness.ai/releases/tag/v$env:VERSION\"",
            release,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A tag publishes its release at once, in the repository the run is in, on the tag that was pushed: the step asks
    /// <c>gh</c> to verify the tag rather than make one. A draft an earlier button left behind holds the name, so it is
    /// deleted first — and only a draft: a published release of that version is somebody's installed software, and the
    /// step stops before it creates anything.
    /// </summary>
    /// <remarks>
    /// The step is run, not read: its own PowerShell, with <c>gh</c> replaced by a function that records what it was
    /// asked to do (a function wins over an external command, so what runs is the workflow's own line).
    /// </remarks>
    [Theory]
    [InlineData("none")]
    [InlineData("draft")]
    [InlineData("published")]
    public void A_tag_publishes_its_release_at_once_here_and_replaces_only_a_draft(string existing)
    {
        var (exit, _, stderr, calls) = RunThePublishStep("push", existing, "0.2.0");

        // Every call is about the repository the run is in.
        Assert.NotEmpty(calls);
        Assert.All(calls, call => Assert.Contains("-R reply-team/jason-ai", call, StringComparison.Ordinal));
        Assert.Contains(calls, call => call.StartsWith("release view v0.2.0", StringComparison.Ordinal));
        var deletions = calls.Index().Where(call => call.Item.StartsWith("release delete", StringComparison.Ordinal)).ToList();
        var creations = calls.Index().Where(call => call.Item.StartsWith("release create", StringComparison.Ordinal)).ToList();

        if (existing == "published")
        {
            Assert.NotEqual(0, exit);
            Assert.Contains("v0.2.0 is already published in reply-team/jason-ai", stderr, StringComparison.Ordinal);
            Assert.Empty(deletions);
            Assert.Empty(creations);
            return;
        }

        Assert.True(exit == 0, $"The step exited {exit}: {stderr}");
        var created = Assert.Single(creations);

        // Published, on the tag that was pushed, and the latest release when none is newer. And every flag reaches gh
        // whole: PowerShell unrolls a one-element array assigned from an `if` into a scalar string, and splatting a
        // string spells it out one character per argument.
        Assert.StartsWith("release create v0.2.0 -R reply-team/jason-ai --verify-tag --latest --title Jason 0.2.0 --notes-file artifacts/release/notes.md ", created.Item, StringComparison.Ordinal);
        Assert.DoesNotContain("--draft", created.Item, StringComparison.Ordinal);
        Assert.DoesNotContain("--target", created.Item, StringComparison.Ordinal);
        Assert.DoesNotContain("--prerelease", created.Item, StringComparison.Ordinal);
        Assert.DoesNotContain("- -", created.Item, StringComparison.Ordinal);

        // And every file a release publishes reaches gh as an asset, each a path of its own: the arguments after the notes
        // file are exactly the published files, once each, in the order the workflow attaches them, from the folder each is
        // taken from - the install scripts are the tag's own, the rest were assembled under artifacts/release.
        const string notesFile = "--notes-file artifacts/release/notes.md ";
        var assets = created.Item[(created.Item.IndexOf(notesFile, StringComparison.Ordinal) + notesFile.Length)..]
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var expected = ReleaseAssets.Published
            .Select(name => (ReleaseAssets.InstallScripts.Contains(name) ? "install/" : "artifacts/release/") + name)
            .ToArray();
        Assert.Equal(expected, assets);

        if (existing == "none")
        {
            Assert.Empty(deletions);
            return;
        }

        var deletion = Assert.Single(deletions);
        Assert.StartsWith("release delete v0.2.0", deletion.Item, StringComparison.Ordinal);
        Assert.True(deletion.Index < created.Index, $"The draft is deleted after the release is created:\n{string.Join('\n', calls)}");

        // A draft has no tag to clean up, and asking for one is how a published tag gets deleted by accident.
        Assert.DoesNotContain("--cleanup-tag", deletion.Item, StringComparison.Ordinal);
    }

    /// <summary>
    /// A release becomes the latest unless a newer one already is: a fix to an older line, published after a newer
    /// release, must not take latest from it — latest is what every install one-liner and the default feed download.
    /// </summary>
    [Theory]
    [InlineData("v0.7.0", "--latest=false")]
    [InlineData("v0.10.0", "--latest=false")]
    [InlineData("v0.5.3", "--latest")]
    [InlineData("v0.6.0", "--latest")]
    [InlineData("", "--latest")]
    public void A_release_older_than_the_latest_is_published_beside_it(string latest, string flag)
    {
        var (exit, stdout, stderr, calls) = RunThePublishStep("push", "none", "0.6.0", latest);

        Assert.True(exit == 0, stderr);
        Assert.Contains(calls, call => call == "release view -R reply-team/jason-ai --json tagName --jq .tagName");
        var created = Assert.Single(calls, call => call.StartsWith("release create", StringComparison.Ordinal));
        Assert.StartsWith($"release create v0.6.0 -R reply-team/jason-ai --verify-tag {flag} --title ", created, StringComparison.Ordinal);
        if (flag == "--latest=false")
        {
            Assert.Contains($"{latest} is newer and stays the latest release; v0.6.0 is published beside it.", stdout, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Only a release that is not there is taken as none: gh says <c>release not found</c> for that, and the step goes
    /// on — a tag's release becomes the latest, a version with no draft is created. Any other failure of a read stops the
    /// step before anything is deleted or created, saying what gh said, so that the run is re-run: taken as none, a failed
    /// read of the latest would let a fix to an older line take latest from a newer release, and a failed read of this
    /// version would create a release beside a draft of the same name.
    /// </summary>
    [Theory]
    [InlineData("push", "none", "", true)]
    [InlineData("push", "none", "unanswered", false)]
    [InlineData("push", "unanswered", "v0.5.3", false)]
    [InlineData("workflow_dispatch", "none", "", true)]
    [InlineData("workflow_dispatch", "unanswered", "", false)]
    public void Only_a_release_that_is_not_there_is_taken_as_none(string eventName, string existing, string latest, bool created)
    {
        var (exit, _, stderr, calls) = RunThePublishStep(eventName, existing, "0.6.0", latest);

        var creations = calls.Where(call => call.StartsWith("release create", StringComparison.Ordinal)).ToList();
        if (created)
        {
            Assert.True(exit == 0, stderr);
            var creation = Assert.Single(creations);
            if (eventName == "push")
            {
                Assert.StartsWith("release create v0.6.0 -R reply-team/jason-ai --verify-tag --latest --title ", creation, StringComparison.Ordinal);
            }

            return;
        }

        Assert.NotEqual(0, exit);
        Assert.Empty(creations);
        Assert.DoesNotContain(calls, call => call.StartsWith("release delete", StringComparison.Ordinal));
        var asked = latest == "unanswered" ? "-R reply-team/jason-ai --json tagName --jq .tagName" : "v0.6.0 -R reply-team/jason-ai --json isDraft --jq .isDraft";
        Assert.Contains($"gh release view {asked} exited with 1: HTTP 502: Server Error", stderr, StringComparison.Ordinal);
    }

    /// <summary>
    /// A candidate is a pre-release from the start, whichever event made it, and never the latest release: a tag's
    /// candidate is published as one, the button's is a draft of one, and neither asks which release is the latest.
    /// </summary>
    [Theory]
    [InlineData("push", "release create v1.0.0-rc.1 -R reply-team/jason-ai --verify-tag --prerelease --title ")]
    [InlineData("workflow_dispatch", "release create v1.0.0-rc.1 -R reply-team/jason-ai --draft --target " + BuiltCommit + " --prerelease --title ")]
    public void A_candidate_is_a_pre_release_and_never_the_latest(string eventName, string expected)
    {
        var (exit, _, stderr, calls) = RunThePublishStep(eventName, "none", "1.0.0-rc.1", "v0.9.0");

        Assert.True(exit == 0, stderr);
        var created = Assert.Single(calls, call => call.StartsWith("release create", StringComparison.Ordinal));
        Assert.StartsWith(expected, created, StringComparison.Ordinal);
        Assert.DoesNotContain("--latest", created, StringComparison.Ordinal);
        Assert.DoesNotContain(calls, call => call.Contains("tagName", StringComparison.Ordinal));
    }

    /// <summary>
    /// The button makes a draft and nothing else: no tag exists, so the draft names the commit the run built. The draft
    /// is for reading and is deleted afterwards, since a release is published only by its tag; never <c>--latest</c>,
    /// which is decided when a release is published. A draft an earlier button left is replaced, a published release of that version is never touched.
    /// </summary>
    [Theory]
    [InlineData("none")]
    [InlineData("draft")]
    [InlineData("published")]
    public void The_button_makes_a_draft_at_the_commit_it_built_and_never_the_latest(string existing)
    {
        var (exit, _, stderr, calls) = RunThePublishStep("workflow_dispatch", existing, "0.2.0", "v0.1.0");

        Assert.All(calls, call => Assert.Contains("-R reply-team/jason-ai", call, StringComparison.Ordinal));
        if (existing == "published")
        {
            Assert.NotEqual(0, exit);
            Assert.Contains("v0.2.0 is already published in reply-team/jason-ai", stderr, StringComparison.Ordinal);
            Assert.DoesNotContain(calls, call => call.StartsWith("release create", StringComparison.Ordinal) || call.StartsWith("release delete", StringComparison.Ordinal));
            return;
        }

        Assert.True(exit == 0, stderr);
        var created = Assert.Single(calls, call => call.StartsWith("release create", StringComparison.Ordinal));
        Assert.StartsWith($"release create v0.2.0 -R reply-team/jason-ai --draft --target {BuiltCommit} --title Jason 0.2.0 ", created, StringComparison.Ordinal);
        Assert.DoesNotContain("--latest", created, StringComparison.Ordinal);
        Assert.DoesNotContain("--verify-tag", created, StringComparison.Ordinal);
        Assert.DoesNotContain(calls, call => call.Contains("tagName", StringComparison.Ordinal));
        Assert.Equal(existing == "draft" ? 1 : 0, calls.Count(call => call.StartsWith("release delete v0.2.0", StringComparison.Ordinal)));
    }

    /// <summary>The commit a run built, as the runner names it in <c>GITHUB_SHA</c>.</summary>
    private const string BuiltCommit = "3333333333333333333333333333333333333333";

    /// <summary>One step of the publishing job, by its name.</summary>
    private static YamlMappingNode PublishingStep(string name) => Assert.Single(PublishingSteps(), step => Scalar(step, "name") == name);

    /// <summary>The steps of the job that publishes the release, in order.</summary>
    private static IReadOnlyList<YamlMappingNode> PublishingSteps() =>
        [.. Assert.IsType<YamlSequenceNode>(Node(PublishingJob(), "steps")).Children.Cast<YamlMappingNode>()];

    /// <summary>The job that publishes the release: the one whose steps create it.</summary>
    private static YamlMappingNode PublishingJob()
    {
        var jobs = Mapping(Parse(Read(Release)), "jobs");
        return Mapping(jobs, PublishingJobName(jobs));
    }

    /// <summary>The name of the job, among these, whose steps create the release.</summary>
    private static string PublishingJobName(YamlMappingNode jobs) =>
        jobs.Children
            .Single(job => JobSteps((YamlMappingNode)job.Value).Any(step => Scalar(step, "run") is { } run && Regex.IsMatch(Code(run), @"\bgh\s+release\s+create\b")))
            .Key.ToString();

    /// <summary>The job that asks the tap to bump its formula: the one whose step sends the tap's token.</summary>
    private static YamlMappingNode TapJob()
    {
        var jobs = Mapping(Parse(Read(Release)), "jobs");
        return (YamlMappingNode)jobs.Children.Single(job => Scalars(job.Value).Contains(TapToken)).Value;
    }

    /// <summary>
    /// The publishing job's "Publish the release" step, run with <c>gh</c> standing in, in the public repository at the
    /// commit <see cref="BuiltCommit"/>: its exit code, what it wrote, and what it asked <c>gh</c> to do — one call per
    /// line, in the order they were made. <paramref name="latest"/> is the tag of the latest release, or empty for none.
    /// </summary>
    private static (int Exit, string Stdout, string Stderr, IReadOnlyList<string> Calls) RunThePublishStep(string eventName, string existing, string version, string latest = "")
    {
        var step = PublishingStep(PublishStep);
        Assert.Equal("${{ github.event_name }}", Scalar(Mapping(step, "env"), "EVENT"));
        Assert.Equal("${{ needs.version.outputs.version }}", Scalar(Mapping(step, "env"), "VERSION"));

        using var tree = new TempTree();
        var script = Path.Combine(tree.Root, "publish.ps1");
        File.WriteAllText(script, Scalar(step, "run"));
        var calls = Path.Combine(tree.Root, "gh-calls.txt");
        File.WriteAllText(calls, string.Empty);
        var harness = Path.Combine(tree.Root, "harness.ps1");
        File.WriteAllText(harness, StandInForGh.Replace("@STEP@", script, StringComparison.Ordinal));

        var (exit, stdout, stderr) = Pwsh(
            new Dictionary<string, string>
            {
                ["EVENT"] = eventName,
                ["VERSION"] = version,
                ["GH_TOKEN"] = "not-a-token",
                ["GITHUB_REPOSITORY"] = "reply-team/jason-ai",
                ["GITHUB_SHA"] = BuiltCommit,
                ["EXISTING"] = existing,
                ["LATEST"] = latest,
                ["CALLS"] = calls,
            },
            "-File", harness);

        return (exit, stdout, stderr, File.ReadAllLines(calls));
    }

    /// <summary>
    /// <c>gh</c> for the length of one step: every call recorded, and <c>release view</c> answering the way the step
    /// asks it. Named, with <c>--json isDraft --jq .isDraft</c>, it answers <c>true</c>, <c>false</c>, or nothing and a
    /// non-zero exit code when there is no release of that name at all; unnamed, with <c>--json tagName --jq .tagName</c>,
    /// it answers the latest release's tag, or nothing and a non-zero exit code when there is none. A release that is not
    /// there is said on the error stream as gh 2.86 says it, <c>release not found</c>; <c>unanswered</c>, for either, is
    /// a read that failed for another reason, said as gh says a server error.
    /// </summary>
    private const string StandInForGh = """
        $ErrorActionPreference = 'Stop'

        function gh {
            Add-Content -LiteralPath $env:CALLS -Value ($args -join ' ')
            if ($args[0] -eq 'release' -and $args[1] -eq 'view') {
                $said = if ($args[2].StartsWith('-')) { if ($env:LATEST) { $env:LATEST } else { 'none' } } else { $env:EXISTING }
                switch ($said) {
                    'draft'      { $global:LASTEXITCODE = 0; 'true'; return }
                    'published'  { $global:LASTEXITCODE = 0; 'false'; return }
                    'none'       { Write-Error 'release not found' -ErrorAction Continue; $global:LASTEXITCODE = 1; return }
                    'unanswered' { Write-Error 'HTTP 502: Server Error (https://api.github.com/repos/reply-team/jason-ai/releases)' -ErrorAction Continue; $global:LASTEXITCODE = 1; return }
                    default      { $global:LASTEXITCODE = 0; $said; return }
                }
            }

            $global:LASTEXITCODE = 0
        }

        try { & '@STEP@'; if ((Test-Path -LiteralPath variable:\LASTEXITCODE)) { exit $LASTEXITCODE } } catch { [Console]::Error.WriteLine($_.Exception.Message); exit 1 }
        """;

    /// <summary>
    /// PB2 and PA7: the tests run at the tag before the leg that runs them packages, and one <c>-p:Version=</c> reaches
    /// build, test and publish alike. <c>VersionPrefix</c> is never stamped — it would leave the suffix behind
    /// and print <c>0.2.0-dev</c> on a release. The tier is the Linux leg's alone, so it precedes that leg's packaging; the
    /// other legs package beside it, and the publishing job waits for every leg, so nothing is published before it passes.
    /// </summary>
    [Fact]
    [Critical(Family.B7, "a release is tested first")]
    public void The_tests_run_at_the_tag_before_the_leg_that_runs_them_packages()
    {
        var release = Read(Release);
        Assert.DoesNotContain("VersionPrefix", release, StringComparison.Ordinal);

        var jobs = Mapping(Parse(release), "jobs");
        var building = (YamlMappingNode)jobs.Children.Single(job => Node((YamlMappingNode)job.Value, "strategy") is not null).Value;
        var steps = Assert.IsType<YamlSequenceNode>(Node(building, "steps")).Children.Cast<YamlMappingNode>().ToList();

        var tested = steps.FindIndex(step => Scalar(step, "run")?.StartsWith("dotnet test", StringComparison.Ordinal) == true);
        var packaged = steps.FindIndex(step => Scalar(step, "uses") == "./.github/actions/package");
        Assert.True(tested >= 0, "No step runs dotnet test.");
        Assert.True(packaged > tested, "The packaging step does not come after the tests.");

        foreach (var step in steps.Where(step => Scalar(step, "run") is { } run && (run.StartsWith("dotnet build", StringComparison.Ordinal) || run.StartsWith("dotnet test", StringComparison.Ordinal))))
        {
            Assert.Contains("-p:Version=", Scalar(step, "run"), StringComparison.Ordinal);
        }

        Assert.NotNull(Scalar(Mapping(steps[packaged], "with"), "version"));
    }

    /// <summary>
    /// The critical tier runs at the tag on Linux alone. The tag is the tree the release's pull request tested whole on every
    /// platform, and what differs at the tag is the version string and, when the channel has released an SDK since, the SDK
    /// the tag resolves again, which the Linux run stamps, builds on and tests: the same tier on Windows and macOS would
    /// repeat a run that tree already passed there, on the dearest runners. The build the tier
    /// tests is the Linux leg's alone for the same reason, since the packaging step publishes on its own; every platform
    /// still packages and is smoke-tested.
    /// </summary>
    [Fact]
    public void The_critical_tier_and_the_build_it_tests_run_on_linux_alone_while_every_platform_packages()
    {
        var jobs = Mapping(Parse(Read(Release)), "jobs");
        var building = (YamlMappingNode)jobs.Children.Single(job => Node((YamlMappingNode)job.Value, "strategy") is not null).Value;
        var steps = Assert.IsType<YamlSequenceNode>(Node(building, "steps")).Children.Cast<YamlMappingNode>().ToList();

        const string OnLinux = "matrix.rid == 'linux-x64'";
        var ranOnLinuxAlone = steps
            .Where(step => Scalar(step, "run") is { } run && (run.StartsWith("dotnet test", StringComparison.Ordinal) || run.StartsWith("dotnet build", StringComparison.Ordinal) || run.StartsWith("dotnet restore", StringComparison.Ordinal)))
            .ToList();
        Assert.Equal(3, ranOnLinuxAlone.Count);
        // The package action's publish builds on its own; only a signing release publishes a second time, and that one
        // bundles the first one's build.
        var publish = Assert.Single(
            Scalar(ActionSteps().Single(step => Scalar(step, "id") == "publish"), "run")!.Split('\n'),
            line => line.TrimStart().StartsWith("dotnet publish ", StringComparison.Ordinal));
        Assert.DoesNotContain("--no-build", publish, StringComparison.Ordinal);
        Assert.DoesNotContain("--no-restore", publish, StringComparison.Ordinal);
        Assert.All(ranOnLinuxAlone, step => Assert.True(Scalar(step, "if") == OnLinux, $"'{Scalar(step, "name")}' has the condition '{Scalar(step, "if")}', not '{OnLinux}'"));

        // macOS and Windows each package in a job of their own, which builds and tests nothing else; every job packages
        // and smoke-tests every platform it publishes, the three smoke tests one text. The macOS job packages osx-x64 as
        // well, for the desktop's Intel package alone, and starts that executable under Rosetta where it can.
        var macos = JobSteps(MacosJob()).ToList();
        var windows = JobSteps(WindowsJob()).ToList();
        foreach (var platform in new[] { steps, macos, windows })
        {
            var everywhere = platform.Where(step => Scalar(step, "uses") == "./.github/actions/package" || Scalar(step, "name")?.StartsWith("Smoke-test", StringComparison.Ordinal) == true).ToList();
            Assert.Equal(platform == macos ? 3 : 2, everywhere.Count);
            Assert.All(everywhere, step => Assert.True(Scalar(step, "if") is null, $"'{Scalar(step, "name") ?? Scalar(step, "uses")}' runs only when '{Scalar(step, "if")}'"));
        }

        static YamlMappingNode Smoke(IEnumerable<YamlMappingNode> steps) => steps.Single(step => Scalar(step, "name")?.StartsWith("Smoke-test", StringComparison.Ordinal) == true);
        foreach (var job in new[] { macos, windows })
        {
            Assert.Equal(Scalar(Smoke(steps), "run"), Scalar(Smoke(job), "run"));
            Assert.Equal(
                Mapping(Smoke(steps), "env").Children.Select(pair => $"{pair.Key}={pair.Value}"),
                Mapping(Smoke(job), "env").Children.Select(pair => $"{pair.Key}={pair.Value}"));
        }
    }

    [Fact]
    public void The_smoke_step_compares_what_the_executable_says_to_the_tag()
    {
        var jobs = Mapping(Parse(Read(Release)), "jobs");
        var building = (YamlMappingNode)jobs.Children.Single(job => Node((YamlMappingNode)job.Value, "strategy") is not null).Value;
        var smoke = Assert.IsType<YamlSequenceNode>(Node(building, "steps")).Children.Cast<YamlMappingNode>()
            .Single(step => Scalar(step, "name")?.StartsWith("Smoke-test", StringComparison.Ordinal) == true);

        var run = Scalar(smoke, "run")!;
        Assert.Contains("--version", run, StringComparison.Ordinal);
        Assert.Contains("$env:VERSION", run, StringComparison.Ordinal);
        Assert.Contains("throw", run, StringComparison.Ordinal);
    }

    [Fact]
    public void The_release_names_every_platform_the_code_publishes()
    {
        var release = Read(Release);
        foreach (var rid in ReleaseAssets.Rids)
        {
            Assert.Contains(rid, release, StringComparison.Ordinal);
        }

        foreach (var name in ReleaseAssets.Published)
        {
            Assert.Contains(name, release, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Each plugin's package is written once per release, not once per platform: by one job of its own, from the tag's
    /// checkout, after the version was decided and holding no token, a step per package. Each is uploaded under a name of its
    /// own that the publishing job's download takes, into the directory the checksums are written in, and the publishing job
    /// waits for that job, so nothing is published without every package. What the script writes is
    /// <see cref="PluginAssetTests"/>' subject.
    /// </summary>
    [Fact]
    public void Each_plugin_s_package_is_written_once_by_the_plugins_job_and_reaches_the_release()
    {
        var packages = ReleasedPackages();
        var jobs = Mapping(Parse(Read(Release)), "jobs");
        var writing = jobs.Children
            .Select(job => (Name: job.Key.ToString(), Job: (YamlMappingNode)job.Value))
            .SelectMany(found => (Node(found.Job, "steps") as YamlSequenceNode)?.Children.Cast<YamlMappingNode>().Select(step => (found.Name, found.Job, Step: step)) ?? [])
            .Where(found => Scalar(found.Step, "run")?.Contains("Write-PluginArchive.ps1", StringComparison.Ordinal) == true)
            .ToList();
        var name = Assert.Single(writing.Select(found => found.Name).Distinct());
        var job = writing[0].Job;

        Assert.Null(Node(job, "strategy"));
        Assert.Equal("version", Scalar(job, "needs"));
        Assert.Equal(
            packages.Select(package => $"./.github/scripts/Write-PluginArchive.ps1 -Package plugins/{package.Id} -Archive artifacts/release/{package.Asset}"),
            writing.Select(found => Scalar(found.Step, "run")!.Trim()));
        Assert.All(writing, found => Assert.Null(Node(found.Step, "continue-on-error")));

        var steps = Assert.IsType<YamlSequenceNode>(Node(job, "steps")).Children.Cast<YamlMappingNode>().ToList();
        Assert.StartsWith("actions/checkout@", Scalar(steps[0], "uses"), StringComparison.Ordinal);
        var uploads = steps.Where(one => Scalar(one, "uses")?.StartsWith("actions/upload-artifact@", StringComparison.Ordinal) == true).ToList();
        Assert.Equal(
            packages.Select(package => $"artifacts/release/{package.Asset}"),
            uploads.Select(upload => Scalar(Mapping(upload, "with"), "path")));

        var publishing = PublishingJob();
        var download = Mapping(Assert.Single(PublishingSteps(), one => Scalar(one, "uses")?.StartsWith("actions/download-artifact@", StringComparison.Ordinal) == true), "with");
        var pattern = Scalar(download, "pattern")!;
        var artifacts = uploads.Select(upload => Scalar(Mapping(upload, "with"), "name")!).ToList();
        Assert.Equal(artifacts.Count, artifacts.Distinct(StringComparer.Ordinal).Count());
        foreach (var (upload, artifact) in uploads.Zip(artifacts))
        {
            Assert.Equal("error", Scalar(Mapping(upload, "with"), "if-no-files-found"));
            Assert.True(
                Regex.IsMatch(artifact, "^" + Regex.Escape(pattern).Replace(@"\*", ".*", StringComparison.Ordinal) + "$"),
                $"A plugin's package is uploaded as '{artifact}', which the publishing job's download of '{pattern}' does not take.");
        }

        // Each package is uploaded after the step that wrote it.
        foreach (var (found, upload) in writing.Zip(uploads))
        {
            Assert.True(steps.IndexOf(found.Step) < steps.IndexOf(upload), $"'{Scalar(Mapping(upload, "with"), "path")}' is uploaded before it is written.");
        }

        Assert.Equal("artifacts/release", Scalar(download, "path"));
        Assert.Contains(name, Assert.IsType<YamlSequenceNode>(Node(publishing, "needs")).Children.Select(need => need.ToString()));
    }

    /// <summary>
    /// The plugin packages a release publishes, read from the names the code publishes: every file named
    /// <c>jason-plugin-&lt;id&gt;.zip</c>, the name the manifest's writer requires of a package it lists under
    /// <c>plugins</c>. Five, each a directory of the tree's <c>plugins/</c>; a sixth is a decision of
    /// <see cref="ReleaseAssets"/>, and every test reading this follows it.
    /// </summary>
    private static IReadOnlyList<(string Id, string Asset)> ReleasedPackages()
    {
        const string Prefix = "jason-plugin-";
        const string Suffix = ".zip";
        var packages = ReleaseAssets.Published
            .Where(name => name.StartsWith(Prefix, StringComparison.Ordinal) && name.EndsWith(Suffix, StringComparison.Ordinal))
            .Select(name => (Id: name[Prefix.Length..^Suffix.Length], Asset: name))
            .ToList();
        Assert.Equal(
            [
                (ReplyId, ReleaseAssets.ReplyPlugin),
                (ForwarderId, ReleaseAssets.JournalForwarderPlugin),
                (NtfyId, ReleaseAssets.NtfyPlugin),
                (SlackId, ReleaseAssets.SlackPlugin),
                (TelegramId, ReleaseAssets.TelegramPlugin),
            ],
            packages);
        Assert.All(packages, package => Assert.True(
            File.Exists(Path.Combine(RepositoryRoot(), "plugins", package.Id, "plugin.yaml")),
            $"{package.Asset} is published, and plugins/{package.Id} is not a package of the tree."));
        return packages;
    }

    /// <summary>
    /// <c>jason status</c> exits 1 whenever the installation is not ready, and GitHub's <c>pwsh</c> ends a step with the
    /// last native command's exit code. A CI step that asks it, in a job that never makes its installation ready — no role
    /// skills, no PATH entry — has to say that it succeeded itself, as the release's smoke does after its last command,
    /// or it fails after every assertion in it passed.
    /// </summary>
    [Fact]
    public void A_ci_step_that_asks_jason_status_ends_by_saying_it_succeeded()
    {
        var asking = Mapping(Parse(Read(Ci)), "jobs").Children
            .SelectMany(job => (Node((YamlMappingNode)job.Value, "steps") as YamlSequenceNode)?.Children.Cast<YamlMappingNode>() ?? [])
            .Select(step => Scalar(step, "run"))
            .OfType<string>()
            .Where(run => Regex.IsMatch(run, @"&\s*\$env:install\s+status\b"))
            .ToList();

        Assert.NotEmpty(asking);
        Assert.All(asking, run => Assert.Equal("exit 0", run.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd().Split('\n')[^1].Trim()));
    }

    /// <summary>
    /// The UI's package is built once per release, like the plugin's: by a job of its own, from the tag's checkout, with
    /// the release's version stamped in its manifest — CI's zip says <c>-dev</c> and is gone in fourteen days, so the
    /// release builds its own, after the UI's checks and tests have passed on that tree. It is uploaded under a name the
    /// publishing job's download takes, and the publishing job waits for it.
    /// </summary>
    [Fact]
    public void The_ui_s_package_is_built_at_the_tag_by_a_job_of_its_own_and_reaches_the_release()
    {
        var jobs = Mapping(Parse(Read(Release)), "jobs");
        var (name, job) = Assert.Single(
            jobs.Children.Select(found => (Name: found.Key.ToString(), Job: (YamlMappingNode)found.Value)),
            found => (Node(found.Job, "steps") as YamlSequenceNode)?.Children.Cast<YamlMappingNode>().Any(step => Scalar(step, "run")?.Contains("npm run package", StringComparison.Ordinal) == true) == true);

        Assert.Null(Node(job, "strategy"));
        Assert.Equal("version", Scalar(job, "needs"));
        var steps = Assert.IsType<YamlSequenceNode>(Node(job, "steps")).Children.Cast<YamlMappingNode>().ToList();
        Assert.StartsWith("actions/checkout@", Scalar(steps[0], "uses"), StringComparison.Ordinal);
        var node = Mapping(Assert.Single(steps, one => Scalar(one, "uses")?.StartsWith("actions/setup-node@", StringComparison.Ordinal) == true), "with");
        Assert.Equal("24", Scalar(node, "node-version"));

        var runs = string.Join('\n', steps.Select(one => Scalar(one, "run")).OfType<string>());
        var order = new[] { "npm ci", "npm run check", "npm test", "npm run build", "npm run package" }.Select(command => runs.IndexOf(command, StringComparison.Ordinal)).ToList();
        Assert.DoesNotContain(-1, order);
        Assert.Equal(order.Order(), order);
        var packaging = Assert.Single(steps, one => Scalar(one, "run")?.Contains("npm run package", StringComparison.Ordinal) == true);
        Assert.Equal("${{ needs.version.outputs.version }}", Scalar(Mapping(packaging, "env"), "JASON_VERSION"));
        Assert.All(steps.Where(one => Scalar(one, "run") is not null), one => Assert.Null(Node(one, "continue-on-error")));

        var upload = Mapping(Assert.Single(steps, one => Scalar(one, "uses")?.StartsWith("actions/upload-artifact@", StringComparison.Ordinal) == true), "with");
        Assert.Equal($"ui/artifacts/{ReleaseAssets.Ui}", Scalar(upload, "path"));
        Assert.Equal("error", Scalar(upload, "if-no-files-found"));

        var download = Mapping(Assert.Single(PublishingSteps(), one => Scalar(one, "uses")?.StartsWith("actions/download-artifact@", StringComparison.Ordinal) == true), "with");
        var pattern = Scalar(download, "pattern")!;
        var artifact = Scalar(upload, "name")!;
        Assert.True(
            Regex.IsMatch(artifact, "^" + Regex.Escape(pattern).Replace(@"\*", ".*", StringComparison.Ordinal) + "$"),
            $"The UI's package is uploaded as '{artifact}', which the publishing job's download of '{pattern}' does not take.");
        Assert.Contains(name, Assert.IsType<YamlSequenceNode>(Node(PublishingJob(), "needs")).Children.Select(need => need.ToString()));
    }

    /// <summary>
    /// The skills' package is written once per release, like the plugins': by one job of its own, from the tag's
    /// checkout, after the version was decided and holding no token. Its <c>source.json</c> names the public repository,
    /// as a constant like the notes address, the release's tag and the commit the checkout is, so an installation that deploys from it records the tag it was
    /// published at. It is uploaded under a name the publishing job's download takes, and the publishing job waits for it.
    /// What the script writes is <see cref="SkillsAssetTests"/>' subject.
    /// </summary>
    [Fact]
    public void The_skills_package_is_written_at_the_tag_by_a_job_of_its_own_and_reaches_the_release()
    {
        var jobs = Mapping(Parse(Read(Release)), "jobs");
        var (name, job) = Assert.Single(
            jobs.Children.Select(found => (Name: found.Key.ToString(), Job: (YamlMappingNode)found.Value)),
            found => JobSteps(found.Job).Any(step => Scalar(step, "run")?.Contains("Write-SkillsArchive.ps1", StringComparison.Ordinal) == true));

        Assert.Null(Node(job, "strategy"));
        Assert.Equal("version", Scalar(job, "needs"));
        Assert.Equal(["contents=read"], Mapping(job, "permissions").Children.Select(pair => $"{pair.Key}={pair.Value}"));
        var steps = JobSteps(job).ToList();
        Assert.StartsWith("actions/checkout@", Scalar(steps[0], "uses"), StringComparison.Ordinal);
        var writing = Assert.Single(steps, one => Scalar(one, "run")?.Contains("Write-SkillsArchive.ps1", StringComparison.Ordinal) == true);
        Assert.Equal("pwsh", Scalar(writing, "shell"));
        Assert.Null(Node(writing, "continue-on-error"));
        Assert.Equal(
            $"./.github/scripts/Write-SkillsArchive.ps1 -Tree . -Archive artifacts/release/{ReleaseAssets.Skills} -Repository {Jason.Cli.PublicRepository.Url} -Ref \"v${{{{ needs.version.outputs.version }}}}\" -Commit \"${{{{ github.sha }}}}\"",
            Scalar(writing, "run")!.Trim());

        var upload = Mapping(Assert.Single(steps, one => Scalar(one, "uses")?.StartsWith("actions/upload-artifact@", StringComparison.Ordinal) == true), "with");
        Assert.True(steps.IndexOf(writing) < steps.FindIndex(one => Scalar(one, "uses")?.StartsWith("actions/upload-artifact@", StringComparison.Ordinal) == true), "The skills' package is uploaded before it is written.");
        Assert.Equal($"artifacts/release/{ReleaseAssets.Skills}", Scalar(upload, "path"));
        Assert.Equal("error", Scalar(upload, "if-no-files-found"));

        var download = Mapping(Assert.Single(PublishingSteps(), one => Scalar(one, "uses")?.StartsWith("actions/download-artifact@", StringComparison.Ordinal) == true), "with");
        var pattern = Scalar(download, "pattern")!;
        var artifact = Scalar(upload, "name")!;
        Assert.True(
            Regex.IsMatch(artifact, "^" + Regex.Escape(pattern).Replace(@"\*", ".*", StringComparison.Ordinal) + "$"),
            $"The skills' package is uploaded as '{artifact}', which the publishing job's download of '{pattern}' does not take.");
        Assert.Contains(name, Assert.IsType<YamlSequenceNode>(Node(PublishingJob(), "needs")).Children.Select(need => need.ToString()));
    }

    /// <summary>
    /// The step that decides the version, run as the workflow would run it, with the same environment GitHub
    /// gives it. A tag is <c>v</c> and a version; the button gives the version itself; anything else stops the
    /// workflow before a runner has built anything. The accepted rows are the ones <see cref="SemanticVersion"/>
    /// reads too, so the workflow's rule and the code's cannot part.
    /// </summary>
    [Theory]
    [InlineData("push", "v1.2.3", "", "1.2.3")]
    [InlineData("push", "v1.0.0-rc.1", "", "1.0.0-rc.1")]
    [InlineData("push", "v0.2.0", "", "0.2.0")]
    [InlineData("workflow_dispatch", "main", "0.2.0", "0.2.0")]
    [InlineData("push", "v1.2", "", null)]
    [InlineData("push", "1.2.3", "", null)]
    [InlineData("push", "v01.2.3", "", null)]
    [InlineData("push", "v1.2.3-", "", null)]
    [InlineData("push", "v", "", null)]
    [InlineData("workflow_dispatch", "main", "v0.2.0", null)]
    [InlineData("workflow_dispatch", "main", "", null)]
    public void A_tag_that_is_not_a_version_is_refused_before_anything_is_built(string eventName, string tag, string requested, string? expected)
    {
        var jobs = Mapping(Parse(Read(Release)), "jobs");
        var building = (YamlMappingNode)jobs.Children.Single(job => Node((YamlMappingNode)job.Value, "strategy") is not null).Value;
        Assert.Equal(["version", DemoJob], Needs(building));

        var decided = Assert.IsType<YamlSequenceNode>(Node(Mapping(jobs, "version"), "steps")).Children.Cast<YamlMappingNode>()
            .Single(step => Scalar(step, "id") == "version");

        using var tree = new TempTree();
        var output = Path.Combine(tree.Root, "output.txt");
        File.WriteAllText(output, string.Empty);
        var step = Path.Combine(tree.Root, "version.ps1");
        File.WriteAllText(step, Scalar(decided, "run"));

        var (exit, _, stderr) = Pwsh(
            new Dictionary<string, string> { ["EVENT"] = eventName, ["TAG"] = tag, ["REQUESTED"] = requested, ["GITHUB_OUTPUT"] = output },
            "-File", step);

        if (expected is null)
        {
            Assert.NotEqual(0, exit);
            Assert.Equal(string.Empty, File.ReadAllText(output));
            return;
        }

        Assert.True(exit == 0, stderr);
        Assert.Equal($"version={expected}", File.ReadAllText(output).Trim());
        Assert.True(SemanticVersion.TryParse(expected, out _));
    }

    /// <summary>
    /// <see cref="SemanticVersion"/> reads <c>1.2.3+build</c> and discards the metadata; a tag carrying it is
    /// refused all the same. The build stamps its own metadata — the commit — and a tag that names some other
    /// would be a version string nobody could reproduce from the tag alone.
    /// </summary>
    [Fact]
    public void A_tag_with_build_metadata_is_refused_though_the_code_would_read_it()
    {
        Assert.True(SemanticVersion.TryParse("1.2.3+build", out _));
        A_tag_that_is_not_a_version_is_refused_before_anything_is_built("push", "v1.2.3+build", "", null);
    }

    // --- the macOS signature ---------------------------------------------------------------------------

    /// <summary>The six Apple secrets, each under the package action's input of its own name.</summary>
    private static readonly (string Input, string Secret)[] AppleSecrets =
    [
        ("apple-developer-id-p12-base64", "APPLE_DEVELOPER_ID_P12_BASE64"),
        ("apple-developer-id-p12-password", "APPLE_DEVELOPER_ID_P12_PASSWORD"),
        ("apple-notary-key-p8", "APPLE_NOTARY_KEY_P8"),
        ("apple-notary-key-id", "APPLE_NOTARY_KEY_ID"),
        ("apple-notary-issuer-id", "APPLE_NOTARY_ISSUER_ID"),
        ("apple-team-id", "APPLE_TEAM_ID"),
    ];

    /// <summary>
    /// What every step that signs, notarizes or assesses an executable runs on: a macOS platform, osx-arm64 or the
    /// desktop's osx-x64, and only when it is asked to.
    /// </summary>
    private const string Signing = "startsWith(inputs.rid, 'osx-') && inputs.sign-macos == 'true'";

    private const string Entitlements = "dotnet/src/Jason.App/jason.entitlements";

    private static List<YamlMappingNode> ActionSteps() =>
        [.. Assert.IsType<YamlSequenceNode>(Node(Mapping(Parse(Read(Action)), "runs"), "steps")).Children.Cast<YamlMappingNode>()];

    private static YamlMappingNode ActionStep(string name) => Assert.Single(ActionSteps(), step => Scalar(step, "name") == name);

    /// <summary>The release's job that builds osx-arm64: the one whose package call names it.</summary>
    private static YamlMappingNode MacosJob() =>
        Assert.Single(
            Mapping(Parse(Read(Release)), "jobs").Children.Values.Cast<YamlMappingNode>(),
            job => JobSteps(job).Any(step => Scalar(step, "uses") == "./.github/actions/package" && Scalar(Mapping(step, "with"), "rid") == "osx-arm64"));

    /// <summary>The release's job that builds the other platforms: its matrix.</summary>
    private static YamlMappingNode MatrixJob() =>
        Assert.Single(Mapping(Parse(Read(Release)), "jobs").Children.Values.Cast<YamlMappingNode>(), job => Node(job, "strategy") is not null);

    private static YamlMappingNode MacosStep(Func<YamlMappingNode, bool> which) => Assert.Single(JobSteps(MacosJob()), step => which(step));

    /// <summary>
    /// A release's macOS executable carries Reply's Developer ID; a pull request's carries the ad hoc signature the SDK
    /// writes at publish. The package action signs only when it is told to, and <c>release.yml</c> tells it so in the
    /// two calls of a job of its own, the one that names osx-arm64 and the one that names osx-x64 for the desktop's
    /// Intel package, each with the six Apple secrets under the input of its own name; its other calls, the matrix's and
    /// the Windows job's, tell it nothing of macOS. That job runs on macOS and runs no test, so no test code shares a
    /// runner with the identity. <c>ci.yml</c> tells the action nothing and
    /// names no Apple secret at all: a pull request's build never holds the identity.
    /// </summary>
    [Fact]
    public void A_release_signs_the_macos_executable_and_ci_never_does()
    {
        var inputs = Mapping(Parse(Read(Action)), "inputs");
        Assert.Equal("false", Scalar(Mapping(inputs, "sign-macos"), "default"));
        foreach (var (input, _) in AppleSecrets)
        {
            Assert.Equal("false", Scalar(Mapping(inputs, input), "required"));
            Assert.Equal(string.Empty, Scalar(Mapping(inputs, input), "default"));
        }

        var release = PackagingCalls(Release);
        Assert.Equal(4, release.Count);
        var signing = release.Where(call => Node(Mapping(call, "with"), "sign-macos") is not null).ToList();
        Assert.Equal(["osx-arm64", "osx-x64"], signing.Select(call => Scalar(Mapping(call, "with"), "rid")));
        foreach (var call in signing)
        {
            var with = Mapping(call, "with");
            Assert.Equal("true", Scalar(with, "sign-macos"));
            foreach (var (input, secret) in AppleSecrets)
            {
                Assert.Equal($"${{{{ secrets.{secret} }}}}", Scalar(with, input));
            }

            Assert.Null(Node(call, "continue-on-error"));
        }

        Assert.Equal(
            ["osx-arm64", "osx-x64"],
            JobSteps(MacosJob()).Where(step => Scalar(step, "uses") == "./.github/actions/package").Select(step => Scalar(Mapping(step, "with"), "rid")));

        Assert.All(release.Where(call => !signing.Contains(call)), call => Assert.DoesNotContain(
            Mapping(call, "with").Children.Keys.Select(key => key.ToString()),
            key => key == "sign-macos" || key.StartsWith("apple-", StringComparison.Ordinal)));

        var macos = MacosJob();
        Assert.Equal("macos-latest", Scalar(macos, "runs-on"));
        Assert.Null(Node(macos, "strategy"));
        Assert.DoesNotContain(JobSteps(macos), step => Scalar(step, "run") is { } run && Regex.IsMatch(run, @"\bdotnet\s+(test|build|restore)\b"));
        Assert.DoesNotContain("osx-arm64", Scalars(Mapping(MatrixJob(), "strategy")));

        var ci = PackagingCalls(Ci);
        Assert.NotEmpty(ci);
        Assert.All(ci, call => Assert.DoesNotContain(
            Mapping(call, "with").Children.Keys.Select(key => key.ToString()),
            key => key == "sign-macos" || key.StartsWith("apple-", StringComparison.Ordinal)));
        Assert.DoesNotContain("APPLE_", Read(Ci), StringComparison.Ordinal);
        Assert.DoesNotContain("codesign", Read(Ci), StringComparison.Ordinal);
    }

    /// <summary>
    /// The executable is signed after the publish and before the staging, so that the staged copy, the archive and the
    /// caller's smoke test all carry the one signed file: the identity first, then the signature, the notary, Gatekeeper's
    /// assessment and the identity forgotten. The signature replaces the SDK's own (<c>--force</c>), carries the hardened
    /// runtime notarization requires, a timestamp that outlives the certificate and the entitlements file, and is verified
    /// and read back into the log before anything else is done with the file.
    /// </summary>
    [Fact]
    public void The_macos_executable_is_signed_between_the_publish_and_the_staging_with_the_hardened_runtime()
    {
        var steps = ActionSteps();
        int At(Func<YamlMappingNode, bool> which) => steps.FindIndex(step => which(step));

        var published = At(step => Scalar(step, "id") == "publish");
        var imported = At(step => Scalar(step, "id") == "identity");
        var signed = At(step => Scalar(step, "name") == "Sign the macOS executable with the hardened runtime");
        var notarized = At(step => Scalar(step, "name") == "Notarize the macOS executable");
        var assessed = At(step => Scalar(step, "name") == "Assess the macOS executable as Gatekeeper does");
        var forgotten = At(step => Scalar(step, "name") == "Forget the Developer ID identity");
        var staged = At(step => Scalar(step, "name") == "Put LICENSE and THIRD-PARTY-NOTICES beside the executable");

        Assert.True(published >= 0 && imported > published, "The identity is not imported after the publish.");
        Assert.True(signed > imported, "The executable is not signed after the identity is imported.");
        Assert.True(notarized > signed, "The executable is not notarized after it is signed.");
        Assert.True(assessed > notarized, "The executable is not assessed after it is notarized.");
        Assert.True(forgotten > assessed, "The identity is not forgotten after the last step that needs it.");
        Assert.True(staged > forgotten, "The executable is staged before it is signed.");

        foreach (var step in new[] { steps[imported], steps[signed], steps[notarized], steps[assessed] })
        {
            Assert.Equal(Signing, Scalar(step, "if"));
            Assert.Null(Node(step, "continue-on-error"));
        }

        var sign = steps[signed];
        Assert.Equal("${{ steps.publish.outputs.executable }}", Scalar(Mapping(sign, "env"), "EXECUTABLE"));
        Assert.Equal("${{ steps.identity.outputs.identity }}", Scalar(Mapping(sign, "env"), "IDENTITY"));
        var run = Scalar(sign, "run")!;
        Assert.Contains($"entitlements={Entitlements}", run, StringComparison.Ordinal);
        var codesign = Assert.Single(run.Split('\n'), line => line.TrimStart().StartsWith("codesign --force ", StringComparison.Ordinal));
        foreach (var flag in (string[])["--sign \"$IDENTITY\"", "--options runtime", "--timestamp", "--entitlements \"$entitlements\"", "\"$EXECUTABLE\""])
        {
            Assert.Contains(flag, codesign, StringComparison.Ordinal);
        }

        Assert.Contains("codesign --verify --strict --verbose=2 \"$EXECUTABLE\"", run, StringComparison.Ordinal);
        Assert.Contains("codesign --display --entitlements - --xml \"$EXECUTABLE\" > \"$signed\"", run, StringComparison.Ordinal);
        Assert.True(
            run.IndexOf("codesign --force ", StringComparison.Ordinal) < run.IndexOf("codesign --verify ", StringComparison.Ordinal),
            "The signature is verified before it is made.");
        Assert.True(
            run.IndexOf("codesign --force ", StringComparison.Ordinal) < run.IndexOf("codesign --display --entitlements ", StringComparison.Ordinal),
            "The entitlements are read back before the file is signed.");

        // The entitlements read back are held to the file they were signed with, both in the one form plutil writes.
        foreach (var part in (string[])
                 [
                     "plutil -convert xml1 -o \"$carried\" \"$signed\"", "plutil -convert xml1 -o \"$expected\" \"$entitlements\"",
                     "if ! diff \"$expected\" \"$carried\"; then",
                     "echo \"::error title=Signing::The signed executable carries entitlements other than $entitlements: the difference is above.\"",
                 ])
        {
            Assert.Contains(part, run, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The signing step itself, run as GitHub runs a bash step, against a codesign that signs nothing and reads back the
    /// entitlements this row says the signed file carries, and a plutil that writes its input unchanged. The step passes
    /// on exactly the entitlements of <c>jason.entitlements</c> and fails, saying so, on one key more, one key fewer and
    /// none at all.
    /// </summary>
    [Theory]
    [InlineData("the file's own", 0)]
    [InlineData("one key more", 1)]
    [InlineData("one key fewer", 1)]
    [InlineData("none", 1)]
    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    public void The_signing_step_fails_on_entitlements_other_than_the_file_s_own(string carried, int exit)
    {
        Assert.SkipUnless(OperatingSystem.IsLinux() || OperatingSystem.IsMacOS(), "The step is bash; CI's Linux and macOS legs run this row.");

        var own = File.ReadAllText(Path.Combine([RepositoryRoot(), .. Entitlements.Split('/')]));
        const string Jit = "  <key>com.apple.security.cs.allow-jit</key>\n  <true/>\n";
        Assert.Contains(Jit, own, StringComparison.Ordinal);
        var readBack = carried switch
        {
            "the file's own" => own,
            "one key more" => own.Replace("</dict>", "  <key>com.apple.security.get-task-allow</key>\n  <true/>\n</dict>", StringComparison.Ordinal),
            "one key fewer" => own.Replace(Jit, string.Empty, StringComparison.Ordinal),
            _ => string.Empty,
        };

        using var tree = new TempTree();
        var bin = Path.Combine(tree.Root, "bin");
        Directory.CreateDirectory(bin);
        Directory.CreateDirectory(Path.Combine([tree.Root, .. Entitlements.Split('/')[..^1]]));
        File.WriteAllText(Path.Combine([tree.Root, .. Entitlements.Split('/')]), own);
        File.WriteAllText(Path.Combine(tree.Root, "carried.txt"), readBack);

        // macOS's plutil writes a property list in one form; this one writes its input as it is, so the two files are
        // told apart by what they hold and nothing else.
        File.WriteAllText(Path.Combine(bin, "codesign"), $$"""
            #!/bin/bash
            if [ "$1 $2 $3 $4" = "--display --entitlements - --xml" ]; then cat '{{Path.Combine(tree.Root, "carried.txt")}}'; fi
            exit 0
            """.ReplaceLineEndings("\n"));
        File.WriteAllText(Path.Combine(bin, "plutil"), """
            #!/bin/bash
            case "$1" in
              -convert) cp "$5" "$4" ;;
              -p) cat "$2" ;;
            esac
            exit 0
            """.ReplaceLineEndings("\n"));
        foreach (var stub in (string[])["codesign", "plutil"])
        {
            File.SetUnixFileMode(Path.Combine(bin, stub), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        var environment = new Dictionary<string, string>
        {
            ["PATH"] = bin + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH"),
            ["RUNNER_TEMP"] = tree.Root,
            ["EXECUTABLE"] = Path.Combine(tree.Root, "jason"),
            ["IDENTITY"] = TeamIdentity,
            ["KEYCHAIN"] = Path.Combine(tree.Root, "jason-signing.keychain-db"),
        };

        var ran = Bash(environment, tree.Root, Scalar(ActionStep("Sign the macOS executable with the hardened runtime"), "run")!);

        Assert.True(ran.Exit == exit, $"exit {ran.Exit}\n{ran.Stdout}\n{ran.Stderr}");
        Assert.Equal(
            exit,
            ran.Stdout.Split('\n').Count(line => line == "::error title=Signing::The signed executable carries entitlements other than " + Entitlements + ": the difference is above."));
    }

    /// <summary>
    /// The entitlements are what .NET needs under the hardened runtime, and nothing else: the JIT and the executable
    /// memory it writes. Library validation stays on, since the native libraries the bundle unpacks are signed by the
    /// executable's own team, and the <c>DYLD_</c> variables stay dropped, since nothing the runtime loads is found
    /// through one.
    /// Each key says why it is there, in a comment just above it. The file sits beside the Windows manifest, and the
    /// stray-file check of the publish keeps it out of the archive.
    /// </summary>
    [Fact]
    public void The_entitlements_file_holds_exactly_its_keys_and_says_why_each_is_there()
    {
        var path = Path.Combine([RepositoryRoot(), .. Entitlements.Split('/')]);
        Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(path)!, "jason.manifest")), "The entitlements file is not beside jason.manifest.");

        var plist = System.Xml.Linq.XDocument.Load(path, System.Xml.Linq.LoadOptions.None).Root!;
        Assert.Equal("plist", plist.Name.LocalName);
        var dict = Assert.Single(plist.Elements());
        Assert.Equal("dict", dict.Name.LocalName);

        var keys = dict.Elements("key").ToList();
        Assert.Equal(
            [
                "com.apple.security.cs.allow-jit",
                "com.apple.security.cs.allow-unsigned-executable-memory",
            ],
            keys.Select(key => key.Value).Order(StringComparer.Ordinal));
        Assert.Equal(keys.Count * 2, dict.Elements().Count());
        Assert.All(keys, key =>
        {
            Assert.Equal("true", (key.NextNode as System.Xml.Linq.XElement)?.Name.LocalName);
            var why = key.PreviousNode as System.Xml.Linq.XComment;
            Assert.True(why is not null && why.Value.Trim().Length > 0, $"{key.Value} has no comment above it saying why it is there.");
        });

        Assert.DoesNotContain("jason.entitlements", Read(["dotnet", "src", "Jason.App", "Jason.App.csproj"]), StringComparison.Ordinal);
    }

    /// <summary>
    /// The six secrets reach the package action by name, as inputs, and inside it only the steps that use them, through
    /// their <c>env</c>: never interpolated into a script, which GitHub prints into the log before it runs it. No step that
    /// holds one prints its environment or traces its commands, and none echoes a secret's variable anywhere but into a
    /// pipe or a file that is not one of the runner's own. Those steps are the identity's import, the executable's
    /// notarization and the desktop's Mac check, which is given the team id alone. <c>release.yml</c> names them only
    /// where it calls the action.
    /// </summary>
    [Fact]
    public void The_apple_secrets_reach_only_the_steps_that_use_them_and_none_of_those_prints_them()
    {
        var action = Read(Action);
        var inputs = Mapping(Parse(action), "inputs").Children.Keys.Select(key => key.ToString()).ToList();
        Assert.All(AppleSecrets, secret => Assert.Contains(secret.Input, inputs));

        var holding = new List<string>();
        foreach (var step in ActionSteps())
        {
            var run = Scalar(step, "run") ?? string.Empty;
            Assert.DoesNotContain("inputs.apple-", run, StringComparison.Ordinal);
            Assert.DoesNotContain("secrets.", run, StringComparison.Ordinal);
            if (Node(step, "with") is YamlMappingNode with)
            {
                Assert.DoesNotContain(with.Children.Values.SelectMany(Scalars), value => value.Contains("inputs.apple-", StringComparison.Ordinal));
            }

            var secrets = Node(step, "env") is YamlMappingNode env
                ? env.Children.Where(pair => pair.Value.ToString().Contains("inputs.apple-", StringComparison.Ordinal)).Select(pair => pair.Key.ToString()).ToList()
                : [];
            if (secrets.Count == 0)
            {
                continue;
            }

            var name = Scalar(step, "name");
            holding.Add(name!);
            var lines = run.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0 && !line.StartsWith('#')).ToList();
            Assert.DoesNotContain(lines, line => PrintsTheEnvironment().IsMatch(line));
            foreach (var line in lines.Where(line => Regex.IsMatch(line, @"^(echo|printf|cat)\b")))
            {
                var named = secrets.Where(secret => line.Contains("$" + secret, StringComparison.Ordinal) || line.Contains("${" + secret, StringComparison.Ordinal)).ToList();
                if (named.Count == 0)
                {
                    continue;
                }

                var piped = line.Contains('|', StringComparison.Ordinal);
                var redirected = Regex.IsMatch(line, @">\s*""\$[a-z]") && !line.Contains("GITHUB_", StringComparison.Ordinal);
                Assert.True(piped || redirected, $"'{name}' prints {string.Join(", ", named)}: {line}");
            }
        }

        // The team id alone reaches the desktop's Mac check, which holds each package's signatures to that team.
        Assert.Equal(
            ["Import the Developer ID identity into a keychain of this job's own", "Notarize the macOS executable", MacDesktopVerify],
            holding);
        Assert.Equal(["TEAM_ID"], Mapping(ActionStep(MacDesktopVerify), "env").Children.Where(pair => pair.Value.ToString().Contains("inputs.apple-", StringComparison.Ordinal)).Select(pair => pair.Key.ToString()));

        // Once in each of the macOS job's two package calls, osx-arm64's and osx-x64's.
        var release = Read(Release);
        foreach (var (_, secret) in AppleSecrets)
        {
            Assert.Equal(2, Regex.Count(release, $@"\bsecrets\.{secret}\b"));
        }

        var runs = Mapping(Parse(release), "jobs").Children.Values.Cast<YamlMappingNode>()
            .SelectMany(job => Node(job, "steps") is YamlSequenceNode sequence ? sequence.Children.Cast<YamlMappingNode>() : [])
            .Select(step => Scalar(step, "run") ?? string.Empty);
        Assert.All(runs, run => Assert.DoesNotContain("APPLE_", run, StringComparison.Ordinal));
        Assert.DoesNotContain("set -x", action + release, StringComparison.Ordinal);
    }

    /// <summary>A line of a script that prints its environment or traces its commands, whatever it does with the output.</summary>
    [GeneratedRegex(@"^(set\s+-[a-z]*x|set\s+-o\s+xtrace|env\s*($|[|>])|printenv\b|export\s+-p|declare\s+-p|set\s*($|[|>]))")]
    private static partial Regex PrintsTheEnvironment();

    /// <summary>
    /// The lines that print a step's environment, read as a step holding a secret is read: bare, or into a pipe or a
    /// file, which the log shows as surely, and the lines that only look like one.
    /// </summary>
    [Theory]
    [InlineData("env", true)]
    [InlineData("env | sort", true)]
    [InlineData("env|grep APPLE", true)]
    [InlineData("env > \"$RUNNER_TEMP/environment\"", true)]
    [InlineData("env >> environment.txt", true)]
    [InlineData("printenv", true)]
    [InlineData("printenv P12_PASSWORD", true)]
    [InlineData("set", true)]
    [InlineData("set | grep P12", true)]
    [InlineData("set -x", true)]
    [InlineData("set -eux", true)]
    [InlineData("set -o xtrace", true)]
    [InlineData("export -p", true)]
    [InlineData("declare -p", true)]
    [InlineData("env -i PATH=/usr/bin codesign --version", false)]
    [InlineData("envsubst < template", false)]
    [InlineData("environment=\"$RUNNER_TEMP/x\"", false)]
    [InlineData("set -e", false)]
    [InlineData("set -- \"$keychain\"", false)]
    [InlineData("settings=1", false)]
    public void A_line_that_prints_the_environment_is_told_apart(string line, bool prints) =>
        Assert.Equal(prints, PrintsTheEnvironment().IsMatch(line));

    /// <summary>
    /// The identity lives in a keychain of this job's own under <c>RUNNER_TEMP</c>, unlocked by a password made in the step
    /// and kept nowhere, imported as GitHub documents for Apple certificates on a macOS runner, and found by the team the
    /// secret names, never by a name typed here. The runner's own keychains carry Apple's Developer ID intermediate, so
    /// nothing is fetched to build the chain: an identity that is not valid stops the step, said plainly. Whatever
    /// happened, the last step takes the keychain off the search list and the disk and removes every file a secret was
    /// written to.
    /// </summary>
    [Fact]
    public void The_identity_lives_in_a_keychain_of_the_job_s_own_and_is_forgotten_whatever_happened()
    {
        var import = Scalar(ActionStep("Import the Developer ID identity into a keychain of this job's own"), "run")!;
        foreach (var part in (string[])
                 [
                     "keychain=\"$RUNNER_TEMP/", "p12=\"$RUNNER_TEMP/", "openssl rand", "security create-keychain -p \"$password\" \"$keychain\"",
                     "security unlock-keychain", "-A -t cert -f pkcs12 -k \"$keychain\"", "security set-key-partition-list -S apple-tool:,apple:",
                     "security list-keychains -d user -s", "security find-identity -v -p codesigning \"$keychain\"", "$TEAM_ID",
                     "if [ -z \"$sha\" ]; then", "echo \"::error title=Signing::The keychain holds no valid Developer ID Application identity of the team the secret names: what find-identity listed is above.\"",
                 ])
        {
            Assert.Contains(part, import, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("Developer ID Application: Reply", import, StringComparison.Ordinal);
        foreach (var fetch in (string[])["curl", "wget", "https://", "DeveloperIDG2CA", "-t cert -k"])
        {
            Assert.DoesNotContain(fetch, import, StringComparison.Ordinal);
        }

        Assert.Single(Regex.Matches(import, "sha=\"\\$\\(identity\\)\""));

        var forget = ActionStep("Forget the Developer ID identity");
        Assert.Equal($"always() && {Signing}", Scalar(forget, "if"));
        var run = Scalar(forget, "run")!;
        Assert.Contains("security delete-keychain", run, StringComparison.Ordinal);
        Assert.Contains("security list-keychains -d user -s", run, StringComparison.Ordinal);

        // Every file the steps write a secret to, by the path they write it to.
        var notarize = Scalar(ActionStep("Notarize the macOS executable"), "run")!;
        foreach (var (step, variable) in new[] { (import, "keychain"), (import, "p12"), (import, "opened"), (notarize, "key") })
        {
            var written = Regex.Match(step, $@"^\s*{variable}=""\$RUNNER_TEMP/([^""]+)""", RegexOptions.Multiline);
            Assert.True(written.Success, $"No {variable}=\"$RUNNER_TEMP/...\" line.");
            Assert.Contains("$RUNNER_TEMP/" + written.Groups[1].Value, run, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The two steps that write a secret to a file write it for the runner's own user alone: each begins with
    /// <c>umask 077</c>. The keychain's password, made in the import step, is masked in the log the moment it exists, as
    /// a secret is.
    /// </summary>
    [Fact]
    public void The_steps_holding_a_secret_write_it_for_the_runner_s_user_alone_and_mask_the_keychain_password()
    {
        foreach (var name in (string[])["Import the Developer ID identity into a keychain of this job's own", "Notarize the macOS executable"])
        {
            var commands = Scalar(ActionStep(name), "run")!.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0 && !line.StartsWith('#'));
            Assert.Equal("umask 077", commands.First());
        }

        var import = Scalar(ActionStep("Import the Developer ID identity into a keychain of this job's own"), "run")!.Split('\n').Select(line => line.Trim()).ToList();
        var made = import.IndexOf("password=\"$(openssl rand -base64 32)\"");
        Assert.True(made >= 0, "The keychain's password is not made in the import step.");
        Assert.Equal("echo \"::add-mask::$password\"", import[made + 1]);
    }

    /// <summary>
    /// The step that forgets the identity, run as GitHub runs a bash step, against a security that records whether a
    /// file holding a secret was still on the disk when it was called, and that succeeds or fails as the row says. Every
    /// such file is removed before any keychain command runs, so a keychain command that fails cannot leave one behind;
    /// such a failure is warned of, and the step still succeeds.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    public void The_forget_step_removes_every_secret_file_first_and_only_warns_of_a_keychain_command_that_fails(bool fails)
    {
        Assert.SkipUnless(OperatingSystem.IsLinux() || OperatingSystem.IsMacOS(), "The step is bash; CI's Linux and macOS legs run this row.");

        using var tree = new TempTree();
        var bin = Path.Combine(tree.Root, "bin");
        Directory.CreateDirectory(bin);
        var calls = Path.Combine(tree.Root, "calls.txt");
        string[] secrets = ["jason-signing.p12", "jason-signing.opened", "jason-notary.p8", "jason-notarize.zip"];
        foreach (var file in secrets)
        {
            File.WriteAllText(Path.Combine(tree.Root, file), "made-up");
        }

        File.WriteAllText(Path.Combine(tree.Root, "jason-signing.keychain-db"), "made-up");
        File.WriteAllText(Path.Combine(tree.Root, "jason-signing.search-list"), "/made/up/login.keychain-db\n/made/up/other.keychain-db\n");

        File.WriteAllText(Path.Combine(bin, "security"), $$"""
            #!/bin/bash
            if {{string.Join(" || ", secrets.Select(file => $"[ -e '{Path.Combine(tree.Root, file)}' ]"))}}; then
              echo "security $1 with a secret on the disk" >> '{{calls}}'
            else
              echo "security $1 $*" >> '{{calls}}'
            fi
            exit {{(fails ? 1 : 0)}}
            """.ReplaceLineEndings("\n"));
        File.SetUnixFileMode(Path.Combine(bin, "security"), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        var environment = new Dictionary<string, string>
        {
            ["PATH"] = bin + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH"),
            ["RUNNER_TEMP"] = tree.Root,
        };

        var ran = Bash(environment, tree.Root, Scalar(ActionStep("Forget the Developer ID identity"), "run")!);

        Assert.True(ran.Exit == 0, $"exit {ran.Exit}\n{ran.Stdout}\n{ran.Stderr}");
        Assert.Equal(
            [
                "security list-keychains list-keychains -d user -s /made/up/login.keychain-db /made/up/other.keychain-db",
                "security delete-keychain delete-keychain " + Path.Combine(tree.Root, "jason-signing.keychain-db"),
            ],
            File.ReadAllLines(calls));
        foreach (var file in secrets.Append("jason-signing.search-list"))
        {
            Assert.False(File.Exists(Path.Combine(tree.Root, file)), $"{file} was left on the disk.");
        }

        Assert.Equal(fails ? 2 : 0, ran.Stdout.Split('\n').Count(line => line.StartsWith("::warning title=Signing::", StringComparison.Ordinal)));
    }

    /// <summary>
    /// The .p12 is opened with the password secret before the keychain sees it, by macOS's own openssl and with the
    /// password read from its environment variable. Two cases stop the step before the keychain is made: a secret that
    /// is not a PKCS#12 file, and a password that does not open it. A .p12 that opens is described in a notice — the
    /// MAC's digest and each bag's PBE, with the rebuild line when they are not sha1 and 3DES — and the keychain's
    /// import decides; nothing is refused by name, and a refusal there carries the rebuild line too. What openssl
    /// reports is kept in a file and only its lines of names and iteration counts are kept; its stdout, where the
    /// bags' attributes go, is dropped, and no line prints either secret. The description is printed only just before
    /// a refusal, the step's own or the keychain's: a release's log is public.
    /// </summary>
    [Fact]
    public void The_p12_is_opened_with_the_password_before_it_is_imported_and_neither_secret_is_printed()
    {
        var import = Scalar(ActionStep("Import the Developer ID identity into a keychain of this job's own"), "run")!;
        const string Opens = "/usr/bin/openssl pkcs12 -in \"$p12\" -info -noout -passin env:P12_PASSWORD > /dev/null 2> \"$opened\"";
        var opens = import.IndexOf(Opens, StringComparison.Ordinal);
        Assert.True(opens >= 0, "The .p12 is not opened with the password secret, its stdout dropped.");
        Assert.True(opens < import.IndexOf("security create-keychain", StringComparison.Ordinal), "The .p12 is opened after the keychain is made.");
        Assert.True(opens < import.IndexOf("security import \"$p12\"", StringComparison.Ordinal), "The .p12 is opened after it is imported.");
        Assert.Contains("/usr/bin/openssl asn1parse -inform DER -in \"$p12\" 2> /dev/null", import, StringComparison.Ordinal);
        Assert.Contains("grep -E '^(MAC |MAC:|PKCS7 Encrypted data:|Shrouded Keybag:)' \"$opened\"", import, StringComparison.Ordinal);
        foreach (var said in (string[])
                 [
                     "The decoded secret is not a PKCS#12 file.",
                     "The password secret does not open the .p12.",
                     "advice=\"rebuild it with -legacy -macalg sha1 -keypbe PBE-SHA1-3DES -certpbe PBE-SHA1-3DES\"",
                     "echo \"::notice title=Signing::$described\"",
                     "::error title=Signing::The keychain refused to import the .p12, which the password secret opens: what security said is above. If the notice above names an encoding other than sha1 and 3DES, $advice.",
                 ])
        {
            Assert.Contains(said, import, StringComparison.Ordinal);
        }

        // The encoding is said, never refused, and said only just before a refusal: the description is a function, and
        // each of its two calls is followed by the line that prints a refusal. No refusal names the encoding.
        var lines = import.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0 && !line.StartsWith('#')).ToList();
        var notice = lines.FindIndex(line => line.Contains("::notice title=Signing::$described", StringComparison.Ordinal));
        Assert.True(notice > lines.IndexOf("describe() {") && notice < lines.IndexOf("}", lines.IndexOf("describe() {")), "The notice is printed outside the description.");
        var calls = lines.Index().Where(line => line.Item == "describe").Select(line => line.Index).ToList();
        Assert.Equal(2, calls.Count);
        Assert.All(calls, call => Assert.StartsWith("echo \"::error title=Signing::", lines[call + 1], StringComparison.Ordinal));
        Assert.DoesNotContain("encoding macOS refuses", import, StringComparison.Ordinal);

        // The secrets reach a command only as an argument it does not print, or through the environment: never in an
        // echo, never as pass:, never after cat.
        Assert.DoesNotContain("pass:", import, StringComparison.Ordinal);
        Assert.DoesNotContain("cat \"$opened\"", import, StringComparison.Ordinal);
        foreach (var line in import.Split('\n').Where(line => Regex.IsMatch(line, @"\b(echo|printf)\b")))
        {
            Assert.True(
                !Regex.IsMatch(line, @"\$\{?P12_(BASE64|PASSWORD)\b") || line.Trim() == "printf '%s' \"$P12_BASE64\" | base64 --decode > \"$p12\"",
                $"A line prints a secret: {line.Trim()}");
        }
    }

    // The rebuild line the import step gives, and its last word when the keychain refuses a .p12 the password opens.
    private const string Rebuild = "rebuild it with -legacy -macalg sha1 -keypbe PBE-SHA1-3DES -certpbe PBE-SHA1-3DES";

    private const string Refused =
        "The keychain refused to import the .p12, which the password secret opens: what security said is above. If the notice above names an encoding other than sha1 and 3DES, " + Rebuild + ".";

    /// <summary>
    /// The import step itself, run as GitHub runs a bash step, up to the keychain's import, against an openssl that
    /// answers <c>pkcs12 -info</c> and <c>asn1parse</c> as LibreSSL does — the report on stderr, the bags' attributes and
    /// a certificate on stdout, a hex dump among the objects — and a security that refuses every import. A secret that
    /// is not a PKCS#12 file and a password that does not open it are said in their own words and stop the step before
    /// anything is imported. A .p12 that opens goes on to the keychain whatever its encoding, after a notice that names
    /// its MAC's digest and each bag's PBE, carrying the rebuild line only when they are not sha1 and 3DES; a
    /// certificate bag under RC2-40 is said to be one macOS reads. The keychain's refusal carries the rebuild line. The
    /// report's lines of names and iteration counts are printed, nothing else openssl wrote, and none of the made-up
    /// secret values.
    /// </summary>
    [Theory]
    [InlineData("not a p12", "The decoded secret is not a PKCS#12 file.", null, false)]
    [InlineData("wrong password", "The password secret does not open the .p12.", null, false)]
    [InlineData("wrong password ending in a space", "The password secret does not open the .p12.", null, false)]
    [InlineData("aes under pbes2", Refused, "Its MAC's digest: sha256. Its bags: PKCS7 Encrypted data under PBES2, Shrouded Keybag under PBES2.", true)]
    [InlineData("a sha256 mac", Refused, "Its MAC's digest: sha256. Its bags: PKCS7 Encrypted data under pbeWithSHA1And3-KeyTripleDES-CBC, Shrouded Keybag under pbeWithSHA1And3-KeyTripleDES-CBC.", true)]
    [InlineData("an rc2 certificate bag", Refused, "Its MAC's digest: sha1. Its bags: PKCS7 Encrypted data under pbeWithSHA1And40BitRC2-CBC, Shrouded Keybag under pbeWithSHA1And3-KeyTripleDES-CBC. A certificate bag under pbeWithSHA1And40BitRC2-CBC is one macOS reads.", false)]
    [InlineData("bags libressl cannot read", Refused, "Its MAC's digest: sha1. Its bags: none read. LibreSSL could not read every bag.", true)]
    [InlineData("sha1 and 3des", Refused, "Its MAC's digest: sha1. Its bags: PKCS7 Encrypted data under pbeWithSHA1And3-KeyTripleDES-CBC, Shrouded Keybag under pbeWithSHA1And3-KeyTripleDES-CBC.", false)]
    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    public void The_import_step_tells_apart_why_the_p12_cannot_be_imported(string answer, string said, string? described, bool rebuild)
    {
        Assert.SkipUnless(OperatingSystem.IsLinux() || OperatingSystem.IsMacOS(), "The step is bash; CI's Linux and macOS legs run this row.");

        const string TripleDes = "pbeWithSHA1And3-KeyTripleDES-CBC, Iteration 2048";
        const string Verified = "MAC Iteration 2048\nMAC verified OK\n";
        var (report, exit, mac) = answer switch
        {
            "not a p12" => ("error:0D07207B:asn1 encoding routines:ASN1_get_object:header too long\n", 1, ""),
            "aes under pbes2" => (Verified + "PKCS7 Encrypted data: PBES2, Iteration 2048\nShrouded Keybag: PBES2, Iteration 2048\n", 0, "sha256"),
            "a sha256 mac" => (Verified + $"PKCS7 Encrypted data: {TripleDes}\nShrouded Keybag: {TripleDes}\n", 0, "sha256"),
            "an rc2 certificate bag" => (Verified + $"PKCS7 Encrypted data: pbeWithSHA1And40BitRC2-CBC, Iteration 2048\nShrouded Keybag: {TripleDes}\n", 0, "sha1"),
            "bags libressl cannot read" => (Verified + "error:06074079:digital envelope routines:EVP_PBE_CipherInit:unknown pbe algorithm\n", 1, "sha1"),
            "sha1 and 3des" => (Verified + $"PKCS7 Encrypted data: {TripleDes}\nShrouded Keybag: {TripleDes}\n", 0, "sha1"),
            _ => ("MAC Iteration 2048\nMac verify error: invalid password?\n", 1, "sha1"),
        };

        using var tree = new TempTree();
        var bin = Path.Combine(tree.Root, "bin");
        Directory.CreateDirectory(bin);
        var calls = Path.Combine(tree.Root, "calls.txt");
        File.WriteAllText(Path.Combine(tree.Root, "report.txt"), report);
        File.WriteAllText(Path.Combine(tree.Root, "objects.txt"), mac.Length == 0 ? string.Empty : $"""
                0:d=0  hl=4 l=1000 cons: SEQUENCE
               19:d=2  hl=2 l=   9 prim: OBJECT            :pkcs7-data
               32:d=3  hl=4 l= 900 prim: OCTET STRING      [HEX DUMP]:MADEUPHEXOFAKEY
              960:d=4  hl=2 l=   5 prim: OBJECT            :{mac}

            """.ReplaceLineEndings("\n"));

        // An openssl and a security that record what they are asked and answer as this row says; openssl writes the bags'
        // attributes and a certificate on stdout, as LibreSSL would, which the step must never print.
        File.WriteAllText(Path.Combine(bin, "openssl"), $$"""
            #!/bin/bash
            echo "openssl $*" >> '{{calls}}'
            if [ "$1" = rand ]; then echo made-up-keychain-password; exit 0; fi
            if [ "$1" = pkcs12 ]; then
              printf 'Bag Attributes\n    friendlyName: Developer ID Application: Made Up (MADEUPTEAM)\n-----BEGIN CERTIFICATE-----\nMADEUPCERTIFICATETEXT\n'
              cat '{{Path.Combine(tree.Root, "report.txt")}}' >&2
              exit {{exit}}
            fi
            if [ "$1" = asn1parse ]; then
              if [ -s '{{Path.Combine(tree.Root, "objects.txt")}}' ]; then cat '{{Path.Combine(tree.Root, "objects.txt")}}'; exit 0; fi
              echo 'Error: offset too large' >&2; exit 1
            fi
            exit 2
            """.ReplaceLineEndings("\n"));
        File.WriteAllText(Path.Combine(bin, "security"), $$"""
            #!/bin/bash
            echo "security $1" >> '{{calls}}'
            if [ "$1" = import ]; then echo 'security: SecKeychainItemImport: The user name or passphrase you entered is not correct.' >&2; exit 1; fi
            exit 0
            """.ReplaceLineEndings("\n"));
        foreach (var stub in (string[])["openssl", "security"])
        {
            File.SetUnixFileMode(Path.Combine(bin, stub), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        var password = answer.EndsWith("space", StringComparison.Ordinal) ? "made-up-p12-password " : "made-up-p12-password";
        var environment = new Dictionary<string, string>
        {
            ["PATH"] = bin + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH"),
            ["RUNNER_TEMP"] = tree.Root,
            ["P12_BASE64"] = Convert.ToBase64String(Encoding.ASCII.GetBytes("made-up-p12-bytes")),
            ["P12_PASSWORD"] = password,
            ["TEAM_ID"] = "MADEUPTEAM",
        };

        // macOS's openssl is named by its path; the stub stands there for this run alone.
        var step = Scalar(ActionStep("Import the Developer ID identity into a keychain of this job's own"), "run")!;
        var ran = Bash(environment, tree.Root, step.Replace("/usr/bin/openssl ", "openssl ", StringComparison.Ordinal));
        var printed = ran.Stdout + ran.Stderr;

        Assert.True(ran.Exit == 1, $"exit {ran.Exit}\n{ran.Stdout}\n{ran.Stderr}");
        Assert.Contains("The decoded .p12 is 17 bytes.", ran.Stdout, StringComparison.Ordinal);
        Assert.Contains("::error title=Signing::" + said, ran.Stdout, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(ran.Stdout, "::error "));
        Assert.Equal(answer.EndsWith("space", StringComparison.Ordinal), ran.Stdout.Contains("ends with whitespace", StringComparison.Ordinal));

        // A .p12 that opens is described just before the keychain's refusal, and the keychain decides.
        var notices = ran.Stdout.Split('\n').Where(line => line.StartsWith("::notice title=Signing::The .p12 opens", StringComparison.Ordinal)).ToArray();
        if (described is null)
        {
            Assert.Empty(notices);
        }
        else
        {
            var notice = Assert.Single(notices);
            Assert.Equal("::notice title=Signing::The .p12 opens with the password secret. " + described + (rebuild ? " If the keychain refuses it, " + Rebuild + "." : string.Empty), notice);
            Assert.True(
                ran.Stdout.IndexOf(notice, StringComparison.Ordinal) < ran.Stdout.IndexOf("::error ", StringComparison.Ordinal),
                "The notice is printed after the keychain's refusal.");
        }
        Assert.Contains($"The MAC's digest, by the file's structure: {(mac.Length == 0 ? "(none)" : mac)}.", ran.Stdout, StringComparison.Ordinal);
        foreach (var line in report.Split('\n').Where(line => Regex.IsMatch(line, "^(MAC |PKCS7 Encrypted data:|Shrouded Keybag:)")))
        {
            Assert.Contains(line, ran.Stdout, StringComparison.Ordinal);
        }

        var asked = File.ReadAllLines(calls);
        Assert.Contains("openssl pkcs12 -in " + Path.Combine(tree.Root, "jason-signing.p12") + " -info -noout -passin env:P12_PASSWORD", asked);
        Assert.Equal(described is not null, asked.Contains("security import"));
        Assert.False(File.Exists(Path.Combine(tree.Root, "jason-signing.p12")), "The decoded .p12 was left on the disk.");
        Assert.False(File.Exists(Path.Combine(tree.Root, "jason-signing.opened")), "What openssl reported was left on the disk.");

        foreach (var never in (string[])["made-up-p12-password", environment["P12_BASE64"], "made-up-p12-bytes", "MADEUPTEAM", "friendlyName", "BEGIN CERTIFICATE", "MADEUPCERTIFICATETEXT", "MADEUPHEXOFAKEY"])
        {
            Assert.DoesNotContain(never, printed, StringComparison.Ordinal);
        }
    }

    // The made-up SHA-1 of the Developer ID identity of the team the rows' secret names.
    private const string TeamIdentity = "0A1B2C3D4E5F60718293A4B5C6D7E8F901234567";

    /// <summary>
    /// The identity the import step hands to the signing step, by the import step itself run as GitHub runs a bash step,
    /// against a keychain that imports the .p12 and a find-identity that lists as this row says: the first valid
    /// Developer ID Application identity of the team the secret names, by its SHA-1, and nothing else. No identity,
    /// another team's alone and an identity of the team that is not Developer ID Application stop the step, said plainly;
    /// a team id secret that ends with whitespace matches no identity, and the step says it ends so, as it says of the
    /// password.
    /// </summary>
    [Theory]
    [InlineData("none", null)]
    [InlineData("another team's", null)]
    [InlineData("an apple development identity of the team", null)]
    [InlineData("another team's, then the team's", TeamIdentity)]
    [InlineData("the team's", TeamIdentity)]
    [InlineData("two of the team's", TeamIdentity)]
    [InlineData("the team's, with a team id ending in a space", null)]
    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    public void The_import_step_hands_on_the_first_developer_id_identity_of_the_team(string listed, string? identity)
    {
        Assert.SkipUnless(OperatingSystem.IsLinux() || OperatingSystem.IsMacOS(), "The step is bash; CI's Linux and macOS legs run this row.");

        const string Theirs = "FEDCBA9876543210FEDCBA9876543210FEDCBA98";
        const string Second = "1111111111111111111111111111111111111111";
        var ours = $"  1) {TeamIdentity} \"Developer ID Application: Made Up Ltd (MADEUPTEAM)\"\n";
        var theirs = $"  1) {Theirs} \"Developer ID Application: Someone Else Ltd (OTHERTEAM1)\"\n";
        var listing = listed switch
        {
            "none" => "     0 valid identities found\n",
            "another team's" => theirs + "     1 valid identities found\n",
            "an apple development identity of the team" => $"  1) {Theirs} \"Apple Development: Made Up Ltd (MADEUPTEAM)\"\n     1 valid identities found\n",
            "another team's, then the team's" => theirs + ours.Replace("  1) ", "  2) ", StringComparison.Ordinal) + "     2 valid identities found\n",
            "two of the team's" => ours + $"  2) {Second} \"Developer ID Application: Made Up Ltd (MADEUPTEAM)\"\n     2 valid identities found\n",
            _ => ours + "     1 valid identities found\n",
        };

        using var tree = new TempTree();
        var bin = Path.Combine(tree.Root, "bin");
        Directory.CreateDirectory(bin);
        var calls = Path.Combine(tree.Root, "calls.txt");
        var output = Path.Combine(tree.Root, "github-output.txt");
        File.WriteAllText(output, string.Empty);
        File.WriteAllText(Path.Combine(tree.Root, "listing.txt"), listing);

        // An openssl that opens the .p12 as sha1 and 3DES, and a security that imports it and lists as this row says.
        File.WriteAllText(Path.Combine(bin, "openssl"), $$"""
            #!/bin/bash
            if [ "$1" = rand ]; then echo made-up-keychain-password; exit 0; fi
            if [ "$1" = pkcs12 ]; then
              printf 'MAC Iteration 2048\nMAC verified OK\nPKCS7 Encrypted data: pbeWithSHA1And3-KeyTripleDES-CBC, Iteration 2048\nShrouded Keybag: pbeWithSHA1And3-KeyTripleDES-CBC, Iteration 2048\n' >&2
              exit 0
            fi
            if [ "$1" = asn1parse ]; then echo '  960:d=4  hl=2 l=   5 prim: OBJECT            :sha1'; exit 0; fi
            exit 2
            """.ReplaceLineEndings("\n"));
        File.WriteAllText(Path.Combine(bin, "security"), $$"""
            #!/bin/bash
            echo "security $1" >> '{{calls}}'
            if [ "$1" = find-identity ]; then cat '{{Path.Combine(tree.Root, "listing.txt")}}'; fi
            exit 0
            """.ReplaceLineEndings("\n"));
        foreach (var stub in (string[])["openssl", "security"])
        {
            File.SetUnixFileMode(Path.Combine(bin, stub), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        var environment = new Dictionary<string, string>
        {
            ["PATH"] = bin + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH"),
            ["RUNNER_TEMP"] = tree.Root,
            ["GITHUB_OUTPUT"] = output,
            ["P12_BASE64"] = Convert.ToBase64String(Encoding.ASCII.GetBytes("made-up-p12-bytes")),
            ["P12_PASSWORD"] = "made-up-p12-password",
            ["TEAM_ID"] = listed.EndsWith("space", StringComparison.Ordinal) ? "MADEUPTEAM " : "MADEUPTEAM",
        };

        var step = Scalar(ActionStep("Import the Developer ID identity into a keychain of this job's own"), "run")!;
        var ran = Bash(environment, tree.Root, step.Replace("/usr/bin/openssl ", "openssl ", StringComparison.Ordinal));

        Assert.True(ran.Exit == (identity is null ? 1 : 0), $"exit {ran.Exit}\n{ran.Stdout}\n{ran.Stderr}");
        Assert.Contains("security import", File.ReadAllLines(calls));
        var handed = File.ReadAllLines(output);
        if (identity is null)
        {
            Assert.Empty(handed);
            Assert.Equal(
                "::error title=Signing::The keychain holds no valid Developer ID Application identity of the team the secret names: what find-identity listed is above.",
                Assert.Single(ran.Stdout.Split('\n'), line => line.StartsWith("::error ", StringComparison.Ordinal)));
        }
        else
        {
            Assert.Equal(["identity=" + identity, "keychain=" + Path.Combine(tree.Root, "jason-signing.keychain-db")], handed);
            Assert.DoesNotContain("::error ", ran.Stdout, StringComparison.Ordinal);
        }

        Assert.Equal(
            listed.EndsWith("space", StringComparison.Ordinal),
            ran.Stdout.Contains("::notice title=Signing::The team id secret ends with whitespace", StringComparison.Ordinal));
        foreach (var never in (string[])["made-up-p12-password", environment["P12_BASE64"], "made-up-p12-bytes"])
        {
            Assert.DoesNotContain(never, ran.Stdout + ran.Stderr, StringComparison.Ordinal);
        }

        // The keychain's password reaches the log once, as the command that masks it.
        Assert.Equal(
            "::add-mask::made-up-keychain-password",
            Assert.Single((ran.Stdout + ran.Stderr).Split('\n'), line => line.Contains("made-up-keychain-password", StringComparison.Ordinal)));
    }

    /// <summary>
    /// A release's log is public, so a run that signs says nothing of the .p12 itself: its size, the lines of openssl's
    /// report, the MAC's digest and the notice that names its encoding are printed only when the .p12 is refused, by the
    /// step or by the keychain, where they say what to rebuild. The import step itself, run as GitHub runs a bash step,
    /// against a keychain that imports the .p12 and holds the team's identity; one row per line that stays unsaid.
    /// </summary>
    [Theory]
    [InlineData("The decoded .p12 is ")]
    [InlineData("MAC Iteration")]
    [InlineData("MAC verified OK")]
    [InlineData("PKCS7 Encrypted data:")]
    [InlineData("Shrouded Keybag:")]
    [InlineData("The MAC's digest")]
    [InlineData("::notice title=Signing::The .p12 opens")]
    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    public void The_import_step_says_nothing_of_the_p12_when_the_keychain_takes_it(string unsaid)
    {
        Assert.SkipUnless(OperatingSystem.IsLinux() || OperatingSystem.IsMacOS(), "The step is bash; CI's Linux and macOS legs run this row.");

        using var tree = new TempTree();
        var bin = Path.Combine(tree.Root, "bin");
        Directory.CreateDirectory(bin);
        var output = Path.Combine(tree.Root, "github-output.txt");
        File.WriteAllText(output, string.Empty);

        // An openssl that opens the .p12 as sha1 and 3DES, and a security that imports it and lists the team's identity.
        File.WriteAllText(Path.Combine(bin, "openssl"), """
            #!/bin/bash
            if [ "$1" = rand ]; then echo made-up-keychain-password; exit 0; fi
            if [ "$1" = pkcs12 ]; then
              printf 'MAC Iteration 2048\nMAC verified OK\nPKCS7 Encrypted data: pbeWithSHA1And3-KeyTripleDES-CBC, Iteration 2048\nShrouded Keybag: pbeWithSHA1And3-KeyTripleDES-CBC, Iteration 2048\n' >&2
              exit 0
            fi
            if [ "$1" = asn1parse ]; then echo '  960:d=4  hl=2 l=   5 prim: OBJECT            :sha1'; exit 0; fi
            exit 2
            """.ReplaceLineEndings("\n"));
        File.WriteAllText(Path.Combine(bin, "security"), $$"""
            #!/bin/bash
            if [ "$1" = find-identity ]; then printf '  1) {{TeamIdentity}} "Developer ID Application: Made Up Ltd (MADEUPTEAM)"\n     1 valid identities found\n'; fi
            exit 0
            """.ReplaceLineEndings("\n"));
        foreach (var stub in (string[])["openssl", "security"])
        {
            File.SetUnixFileMode(Path.Combine(bin, stub), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        var environment = new Dictionary<string, string>
        {
            ["PATH"] = bin + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH"),
            ["RUNNER_TEMP"] = tree.Root,
            ["GITHUB_OUTPUT"] = output,
            ["P12_BASE64"] = Convert.ToBase64String(Encoding.ASCII.GetBytes("made-up-p12-bytes")),
            ["P12_PASSWORD"] = "made-up-p12-password",
            ["TEAM_ID"] = "MADEUPTEAM",
        };

        var step = Scalar(ActionStep("Import the Developer ID identity into a keychain of this job's own"), "run")!;
        var ran = Bash(environment, tree.Root, step.Replace("/usr/bin/openssl ", "openssl ", StringComparison.Ordinal));

        Assert.True(ran.Exit == 0, $"exit {ran.Exit}\n{ran.Stdout}\n{ran.Stderr}");
        Assert.Contains("identity=" + TeamIdentity, File.ReadAllLines(output));
        Assert.DoesNotContain(unsaid, ran.Stdout + ran.Stderr, StringComparison.Ordinal);
    }

    /// <summary>
    /// The notary is asked with the executable zipped as Apple documents, and waited for, for thirty minutes at most:
    /// anything but <c>Accepted</c> prints the service's own log of the submission and fails the step. The executable is
    /// never stapled: a bare executable has no place for the ticket, which Apple documents Gatekeeper finding online
    /// instead. Only the desktop's application and dmg are, which have one.
    /// </summary>
    [Fact]
    public void The_notarization_waits_for_the_notary_and_fails_on_anything_but_accepted()
    {
        var notarize = ActionStep("Notarize the macOS executable");
        Assert.Equal("${{ steps.publish.outputs.executable }}", Scalar(Mapping(notarize, "env"), "EXECUTABLE"));
        var run = Scalar(notarize, "run")!;

        Assert.Contains("ditto -c -k --keepParent \"$EXECUTABLE\" \"$zip\"", run, StringComparison.Ordinal);
        var submit = Assert.Single(run.Split('\n'), line => line.Contains("notarytool submit", StringComparison.Ordinal));
        foreach (var flag in (string[])["\"$zip\"", "--key \"$key\"", "--key-id \"$NOTARY_KEY_ID\"", "--issuer \"$NOTARY_ISSUER\"", "--wait", "--timeout 30m"])
        {
            Assert.Contains(flag, submit, StringComparison.Ordinal);
        }

        Assert.Contains("notarytool log \"$id\"", run, StringComparison.Ordinal);
        Assert.Contains("if [ \"$status\" = \"Accepted\" ]; then", run, StringComparison.Ordinal);
        Assert.DoesNotContain("stapler", Read(Release), StringComparison.Ordinal);

        // The one step that staples is the desktop's, whose application and dmg have a place for the ticket.
        Assert.Equal(
            ["Verify the desktop's Mac packages and what they carry"],
            ActionSteps().Where(step => (Scalar(step, "run") ?? string.Empty).Contains("stapler", StringComparison.Ordinal)).Select(step => Scalar(step, "name")));
    }

    /// <summary>
    /// The notarization step itself, run as GitHub runs a bash step, against a notary that answers as told: only an
    /// accepted submission passes; a refused one has its log asked for and fails, as does a wait that ran out and an answer
    /// that is not one. And none of the made-up secret values reaches what the step printed, nor, for an accepted
    /// submission, its id: a release's log is public, and only a submission that failed needs its id read. On the call
    /// that builds the desktop, the key is kept in the job's keychain for the packages, while its file is still there, and
    /// the file is gone when the step ends either way.
    /// </summary>
    [Theory]
    [InlineData("Accepted", 0, 0, false, false)]
    [InlineData("Accepted", 0, 0, false, true)]
    [InlineData("Invalid", 1, 1, true, false)]
    [InlineData("Invalid", 1, 1, true, true)]
    [InlineData("In Progress", 0, 1, true, false)]
    [InlineData(null, 1, 1, false, false)]
    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    public void The_notarization_step_passes_an_accepted_submission_alone(string? status, int notary, int exit, bool logAsked, bool desktop)
    {
        Assert.SkipUnless(OperatingSystem.IsLinux() || OperatingSystem.IsMacOS(), "The step is bash; CI's Linux and macOS legs run this row.");

        using var tree = new TempTree();
        var bin = Path.Combine(tree.Root, "bin");
        Directory.CreateDirectory(bin);
        var calls = Path.Combine(tree.Root, "calls.txt");
        var answer = status is null ? "notarytool could not reach the service" : $$"""{"id":"0f1e2d3c-made-up","message":"Processing complete","status":"{{status}}"}""";

        // A notary that records what it is asked, never on stdout, keeps a copy of the key file it was handed with its
        // mode, and answers as this row says.
        var witness = Path.Combine(tree.Root, "key-as-handed");
        File.WriteAllText(Path.Combine(bin, "xcrun"), $$"""
            #!/bin/bash
            echo "$*" >> '{{calls}}'
            if [ "$2" = submit ]; then cp -p "$5" '{{witness}}'; printf '%s\n' '{{answer}}'; exit {{notary}}; fi
            if [ "$2" = log ]; then echo "the notary's log of $3"; fi
            if [ "$2" = store-credentials ] && [ -f "$5" ]; then echo "the key's file was there" >> '{{calls}}'; fi
            """.ReplaceLineEndings("\n"));
        File.WriteAllText(Path.Combine(bin, "ditto"), "#!/bin/bash\necho \"$*\" >> '" + calls + "'\n: > \"$5\"\n");
        File.SetUnixFileMode(Path.Combine(bin, "xcrun"), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        File.SetUnixFileMode(Path.Combine(bin, "ditto"), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        var executable = Path.Combine(tree.Root, "jason");
        File.WriteAllText(executable, "not really an executable");
        var environment = new Dictionary<string, string>
        {
            ["PATH"] = bin + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH"),
            ["RUNNER_TEMP"] = tree.Root,
            ["EXECUTABLE"] = executable,
            ["NOTARY_KEY"] = "-----BEGIN PRIVATE KEY-----\nmade-up-key-material\n-----END PRIVATE KEY-----",
            ["NOTARY_KEY_ID"] = "MADEUPKEYID",
            ["NOTARY_ISSUER"] = "00000000-made-up-issuer",
            ["KEYCHAIN"] = Path.Combine(tree.Root, "jason-signing.keychain-db"),
            ["DESKTOP"] = desktop ? "true" : "false",
        };

        var ran = Bash(environment, tree.Root, Scalar(ActionStep("Notarize the macOS executable"), "run")!);

        Assert.True(ran.Exit == exit, $"exit {ran.Exit}\n{ran.Stdout}\n{ran.Stderr}");
        var asked = File.ReadAllLines(calls);
        Assert.Contains(asked, line => line.StartsWith("notarytool submit ", StringComparison.Ordinal) && line.Contains("--wait", StringComparison.Ordinal));
        Assert.Equal(logAsked, asked.Any(line => line.StartsWith("notarytool log 0f1e2d3c-made-up ", StringComparison.Ordinal)));
        var kept = desktop && status == "Accepted";
        Assert.Equal(kept, asked.Any(line => line.StartsWith("notarytool store-credentials jason-notary --key ", StringComparison.Ordinal)));
        Assert.Equal(kept, asked.Contains("the key's file was there"));
        Assert.Equal(environment["NOTARY_KEY"] + "\n", File.ReadAllText(witness));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(witness));

        // The key's file is gone when the step ends, whatever it ended with: what comes after it in the job, the
        // desktop's build tooling among it, never finds the key on disk.
        Assert.False(File.Exists(Path.Combine(tree.Root, "jason-notary.p8")), "The notary key's file outlives the notarization step.");

        foreach (var secret in (string[])["made-up-key-material", "MADEUPKEYID", "00000000-made-up-issuer"])
        {
            Assert.DoesNotContain(secret, ran.Stdout + ran.Stderr, StringComparison.Ordinal);
        }

        // A release's log is public: an accepted submission is said without its id, and a refused one keeps it, beside
        // the notary's answer, which is what reading Apple's log of it needs.
        if (status == "Accepted")
        {
            Assert.DoesNotContain("0f1e2d3c-made-up", ran.Stdout + ran.Stderr, StringComparison.Ordinal);
            Assert.Contains("The notary accepted the executable.", ran.Stdout, StringComparison.Ordinal);
        }
        else if (status is not null)
        {
            Assert.Contains($"submission 0f1e2d3c-made-up, status {status}", ran.Stdout, StringComparison.Ordinal);
            Assert.Contains(answer, ran.Stdout, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Gatekeeper's own question, asked of Apple's service once the notary holds the ticket: the file must be accepted
    /// from the source "Notarized Developer ID". A command-line tool is not an app, and the execute assessment may refuse
    /// it as one ("does not seem to be an app"); the assessment Gatekeeper applies to a tool's own signature is asked then,
    /// and must be accepted from the same source. Any other answer fails the step.
    /// </summary>
    [Fact]
    public void The_assessment_wants_the_executable_accepted_as_notarized_developer_id()
    {
        var assess = ActionStep("Assess the macOS executable as Gatekeeper does");
        Assert.Equal("${{ steps.publish.outputs.executable }}", Scalar(Mapping(assess, "env"), "EXECUTABLE"));
        var run = Scalar(assess, "run")!;

        Assert.Contains("spctl --assess --type execute --verbose=2 \"$EXECUTABLE\"", run, StringComparison.Ordinal);
        Assert.Contains("spctl --assess --type open --context context:primary-signature --verbose=2 \"$EXECUTABLE\"", run, StringComparison.Ordinal);
        Assert.Contains("does not seem to be an app", run, StringComparison.Ordinal);
        Assert.Contains("source=Notarized Developer ID", run, StringComparison.Ordinal);
        Assert.EndsWith("exit 1", run.TrimEnd(), StringComparison.Ordinal);
        Assert.Null(Scalar(assess, "continue-on-error"));
    }

    /// <summary>
    /// The assessment step itself, run as GitHub runs a bash step, against an spctl that answers as this row says and a
    /// sleep that only records how long it was asked to wait. Accepted from the source "Notarized Developer ID" passes,
    /// on the execute assessment or, for a tool the execute assessment calls no app, on the assessment of its own
    /// signature. Apple's service may not yet answer for a ticket it has just issued, so anything else is asked again,
    /// three times in all and twenty seconds apart, before the step fails: accepted from the source "Developer ID" alone,
    /// which is a signature without a ticket, and rejected.
    /// </summary>
    [Theory]
    [InlineData("accepted on execute", 0, "spctl execute")]
    [InlineData("not an app, then accepted on open", 0, "spctl execute, spctl open")]
    [InlineData("rejected, then accepted on execute", 0, "spctl execute, sleep 20, spctl execute")]
    [InlineData("accepted from developer id alone", 1, "spctl execute, sleep 20, spctl execute, sleep 20, spctl execute")]
    [InlineData("not an app, then developer id alone on open", 1, "spctl execute, spctl open, sleep 20, spctl execute, spctl open, sleep 20, spctl execute, spctl open")]
    [InlineData("rejected", 1, "spctl execute, sleep 20, spctl execute, sleep 20, spctl execute")]
    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    public void The_assessment_step_asks_three_times_and_passes_notarized_developer_id_alone(string answers, int exit, string asked)
    {
        Assert.SkipUnless(OperatingSystem.IsLinux() || OperatingSystem.IsMacOS(), "The step is bash; CI's Linux and macOS legs run this row.");

        const string Notarized = "printf 'jason: accepted\\nsource=Notarized Developer ID\\n'; exit 0";
        const string Signed = "printf 'jason: accepted\\nsource=Developer ID\\n'; exit 0";
        const string Rejected = "printf 'jason: rejected\\nsource=Unnotarized Developer ID\\n' >&2; exit 3";
        const string NotAnApp = "printf 'jason: rejected (the code is valid but does not seem to be an app)\\n' >&2; exit 3";
        var said = answers switch
        {
            "accepted on execute" => $"execute:*) {Notarized} ;;",
            "not an app, then accepted on open" => $"execute:*) {NotAnApp} ;;\n  open:*) {Notarized} ;;",
            "rejected, then accepted on execute" => $"execute:1) {Rejected} ;;\n  execute:*) {Notarized} ;;",
            "accepted from developer id alone" => $"execute:*) {Signed} ;;",
            "not an app, then developer id alone on open" => $"execute:*) {NotAnApp} ;;\n  open:*) {Signed} ;;",
            _ => $"execute:*) {Rejected} ;;",
        };

        using var tree = new TempTree();
        var bin = Path.Combine(tree.Root, "bin");
        Directory.CreateDirectory(bin);
        var calls = Path.Combine(tree.Root, "calls.txt");
        File.WriteAllText(calls, string.Empty);

        // The assessment's kind is its third argument; the answer is this row's for that kind and that try.
        File.WriteAllText(Path.Combine(bin, "spctl"), $$"""
            #!/bin/bash
            echo "spctl $3" >> '{{calls}}'
            case "$3:$(grep -c "^spctl $3\$" '{{calls}}')" in
              {{said}}
            esac
            exit 9
            """.ReplaceLineEndings("\n"));
        File.WriteAllText(Path.Combine(bin, "sleep"), "#!/bin/bash\necho \"sleep $*\" >> '" + calls + "'\n");
        foreach (var stub in (string[])["spctl", "sleep"])
        {
            File.SetUnixFileMode(Path.Combine(bin, stub), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        var environment = new Dictionary<string, string>
        {
            ["PATH"] = bin + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH"),
            ["RUNNER_TEMP"] = tree.Root,
            ["EXECUTABLE"] = Path.Combine(tree.Root, "jason"),
        };

        var ran = Bash(environment, tree.Root, Scalar(ActionStep("Assess the macOS executable as Gatekeeper does"), "run")!);

        Assert.True(ran.Exit == exit, $"exit {ran.Exit}\n{ran.Stdout}\n{ran.Stderr}");
        Assert.Equal(asked, string.Join(", ", File.ReadAllLines(calls)));
        Assert.Equal(exit, ran.Stdout.Split('\n').Count(line => line.StartsWith("::error title=Gatekeeper::", StringComparison.Ordinal)));
    }

    /// <summary>
    /// The file the smoke test runs is the file the action signed, and the archive's copy is read for its signature on
    /// the leg: unpacked from the archive a person downloads, verified, and its Developer ID authority required, rather
    /// than trusted to be the same file. And after the smoke test, a step lists who signed each native library the bundle
    /// unpacked, which shows each one signed by our team: a reading, which never fails the job.
    /// </summary>
    [Fact]
    public void The_smoke_test_runs_the_signed_file_and_the_archived_copy_is_read_for_its_signature()
    {
        Assert.Equal("${{ steps.publish.outputs.executable }}", Scalar(Mapping(Mapping(Parse(Read(Action)), "outputs"), "executable"), "value"));

        var steps = JobSteps(MacosJob()).ToList();
        var smoke = MacosStep(step => Scalar(step, "name")?.StartsWith("Smoke-test", StringComparison.Ordinal) == true);
        Assert.Equal("${{ steps.package.outputs.executable }}", Scalar(Mapping(smoke, "env"), "EXECUTABLE"));
        Assert.EndsWith("exit 0", Scalar(smoke, "run")!.TrimEnd(), StringComparison.Ordinal);

        var libraries = MacosStep(step => Scalar(step, "name") == "Read who signed each native library the bundle unpacked");
        Assert.True(steps.IndexOf(libraries) > steps.IndexOf(smoke), "The native libraries are read before the smoke test unpacks them.");
        var listed = Scalar(libraries, "run")!;
        Assert.Contains("'.net/jason'", listed, StringComparison.Ordinal);
        Assert.Contains("codesign -dv", listed, StringComparison.Ordinal);
        Assert.Contains("catch", listed, StringComparison.Ordinal);

        var archived = MacosStep(step => Scalar(step, "name") == "Read the signature of the archived macOS executable");
        Assert.Null(Node(archived, "if"));
        Assert.Null(Node(archived, "continue-on-error"));
        Assert.Equal("${{ steps.package.outputs.archive }}", Scalar(Mapping(archived, "env"), "ARCHIVE"));
        var run = Scalar(archived, "run")!;
        foreach (var part in (string[])["tar -xzf \"$ARCHIVE\"", "codesign --verify --strict --verbose=2", "codesign --display --verbose=4", "Authority=Developer ID Application: "])
        {
            Assert.Contains(part, run, StringComparison.Ordinal);
        }
    }

    // --- the notes --------------------------------------------------------------------------------------

    /// <summary>
    /// Which executables a release signs, said where a person downloading it reads: the macOS executable carries Reply's
    /// Developer ID and is notarized, the release's own assessment accepts its signature as Notarized Developer ID, and a
    /// quarantined copy is started on the build machine; of a person's first run the notes say only what Apple documents.
    /// What a reader checks to know the signature is Reply's is named, the Authority line and the team id, since spctl
    /// accepts any notarized Developer ID's signature. The one native library is signed with the same identity, so the
    /// executable keeps library validation and carries only the two
    /// entitlements the JIT needs, and the notes say where a reader finds the library to check it. The Windows
    /// executable is signed with Reply's certificate through Azure Artifact Signing and carries an RFC 3161 timestamp, and
    /// what a reader checks is named there too: the subject the certificate is issued to, and signtool's verdict. SmartScreen
    /// still looks at a downloaded copy, and its reputation for a new publisher builds over downloads, so the notes promise
    /// no silent first run in the first weeks: its dialog is still possible, and they no longer say it is a certain stop.
    /// The Linux executable is not signed, and the checksum is its only provenance. The sentences that nothing, or the
    /// Windows executable, is unsigned or not yet signed are gone from every page that said them, and so are the claims
    /// that Gatekeeper accepts the executable or lets a first start through, and that a first run needs the network, the
    /// release's own notes included.
    /// </summary>
    [Fact]
    public void The_notes_say_which_executables_a_release_signs()
    {
        var notes = Regex.Replace(Read(Notes), @"\s+", " ");
        Assert.Contains("The macOS executable is signed with Reply's Developer ID and notarized by Apple.", notes, StringComparison.Ordinal);
        // Of a downloaded copy the notes claim only what a release shows on its build machine, and Apple's own word
        // for the rest: the assessment of the signature that holds the release back, the quarantined copy the job
        // starts, and that an unstapled ticket is found online as Apple documents it, not as any run saw it. spctl's
        // execute assessment refuses a command-line tool as not an app, so nothing says the executable itself is
        // accepted, and that refusal is not quoted.
        Assert.Contains("Before anything is published, the release's macOS job has `spctl` assess the executable's signature and stops unless that signature is accepted from the source `Notarized Developer ID`; the same job starts a copy carrying the quarantine flag a browser puts on a download, and its log says whether that copy started and opened its database.", notes, StringComparison.Ordinal);
        Assert.Contains("The ticket is not stapled to the file, which a bare executable has no place for; for a file without a stapled ticket, Apple's documentation says Gatekeeper finds the ticket online.", notes, StringComparison.Ordinal);
        Assert.Contains("an archive fetched with a browser and unpacked by hand keeps the flag.", notes, StringComparison.Ordinal);
        Assert.Contains("`spctl` accepts the signature of any notarized Developer ID from the source `Notarized Developer ID`, so it is that name and that team id, not the acceptance, that say the executable is Reply's.", notes, StringComparison.Ordinal);
        foreach (var unshown in (string[])["checks the notarization with Apple, online, the first time", "that first run needs the network", "assess the executable and stops unless it is accepted", "Gatekeeper accepts any notarized Developer ID"])
        {
            Assert.DoesNotContain(unshown, notes, StringComparison.Ordinal);
        }
        Assert.Contains("The Windows executable is signed with Reply's certificate through Azure Artifact Signing, and timestamped under RFC 3161, so the signature stays valid after the certificate expires.", notes, StringComparison.Ordinal);
        Assert.Contains("SmartScreen's reputation for a new publisher builds over downloads, so in the first weeks of signed releases a download may still show its dialog", notes, StringComparison.Ordinal);
        Assert.Contains("The Linux executable is not signed, and nothing else a release carries is either, apart from the desktop's installer and Mac packages, as \"Desktop\" above says.", notes, StringComparison.Ordinal);
        // The desktop's carried jason is signed and notarized like the archive's, and, a bare executable, never stapled.
        Assert.Contains("the application, the `jason` it carries and the dmg are signed with Reply's Developer ID and notarized, and the application and the dmg are stapled.", notes, StringComparison.Ordinal);
        Assert.DoesNotContain("Developer ID, notarized and stapled", notes, StringComparison.Ordinal);
        Assert.Contains("On Linux that check is the only provenance there is; on macOS and Windows the signature says who built the executable as well.", notes, StringComparison.Ordinal);
        foreach (var checkable in (string[])["`codesign -dv --verbose=2`", "`Authority=Developer ID Application:`", "`TeamIdentifier=`", "`Get-AuthenticodeSignature`", "`Valid`", "`CN=ReplyApp Inc.`", "`signtool verify /pa /v`"])
        {
            Assert.Contains(checkable, notes, StringComparison.Ordinal);
        }

        // The signer is a line a reader can type and a value they can compare, the same under Windows PowerShell and
        // PowerShell: the certificate's subject, whose first part is the common name.
        Assert.Contains(@"`(Get-AuthenticodeSignature .\jason.exe).SignerCertificate.Subject` prints the subject the certificate is issued to, which begins `CN=ReplyApp Inc.`", notes, StringComparison.Ordinal);

        // The one native library is signed as the executable is, which is what lets the executable keep library
        // validation and drop the DYLD key; the notes name the library, where it is unpacked, and the two entitlements
        // left, so a reader can check all three.
        Assert.Contains("The one native library the executable carries, `libe_sqlite3.dylib`, is signed with the same Developer ID and timestamped before it is bundled; the executable unpacks it under `~/.net/jason/` the first time it runs, and `codesign -dv --verbose=2` on it prints the same `Authority=` and `TeamIdentifier=` lines.", notes, StringComparison.Ordinal);
        Assert.Contains("So the executable keeps the hardened runtime's library validation, which loads only a library signed by Apple or by the executable's own team, and it ignores `DYLD_` environment variables: the only entitlements it carries, `com.apple.security.cs.allow-jit` and `com.apple.security.cs.allow-unsigned-executable-memory`, let the .NET runtime compile code as it runs.", notes, StringComparison.Ordinal);
        foreach (var dropped in (string[])["disable-library-validation", "allow-dyld-environment-variables"])
        {
            Assert.DoesNotContain(dropped, notes, StringComparison.Ordinal);
        }

        // What no run has shown: a quarantined copy started on a release runner, but nothing there showed that
        // Gatekeeper assessed it, so the notes do not say how a downloaded copy's first run ends; and a signed
        // executable's first run is SmartScreen's to decide, so they no longer say it is stopped.
        Assert.DoesNotContain("lets it start", notes, StringComparison.Ordinal);
        Assert.DoesNotContain("On Windows that check is the only provenance", notes, StringComparison.Ordinal);
        Assert.DoesNotContain("On Linux and Windows that check", notes, StringComparison.Ordinal);
        var architecture = Regex.Replace(Read(["docs", "architecture.md"]), @"\s+", " ");
        Assert.Contains("the Linux executable is not signed", architecture, StringComparison.Ordinal);
        Assert.Contains("the Windows executable is signed through Azure Artifact Signing with a certificate issued to Reply and timestamped", architecture, StringComparison.Ordinal);
        Assert.Contains("The Windows signing identity is reached only from the release's Windows job, in the GitHub environment `signing`, through that job's own OIDC token; no client secret is stored.", architecture, StringComparison.Ordinal);

        // A release's own notes are the other half of its body, so they are held to the same words whenever the file is
        // there; the next release rewrites it, and a sentence carried from the last one is caught before it ships again.
        string[][] pages = [Notes, ["README.md"], ["docs", "handbook.md"], ["docs", "INSTALL.md"], ["docs", "release-and-update.md"], ["docs", "architecture.md"]];
        if (File.Exists(Path.Combine(RepositoryRoot(), "RELEASE-NOTES.md")))
        {
            pages = [.. pages, ["RELEASE-NOTES.md"]];
        }

        foreach (var page in pages)
        {
            var text = Regex.Replace(Read(page), @"\s+", " ");
            Assert.DoesNotMatch(@"(?i)without Gatekeeper refusing", text);
            Assert.DoesNotMatch(@"(?i)first run needs the network", text);
            Assert.DoesNotMatch(@"(?i)nothing (in (this|a) release )?is signed", text);
            Assert.DoesNotMatch(@"(?i)\bWindows executables? (is|are) not signed", text);
            Assert.DoesNotMatch(@"(?i)\bnot yet signed", text);
            Assert.DoesNotMatch(UnsignedClaim, text);
            Assert.DoesNotMatch(@"(?i)Microsoft's Artifact Signing", text);
            Assert.DoesNotMatch(@"(?i)signing the Windows executable", text);
            Assert.DoesNotMatch(@"(?i)stopped by SmartScreen", text);
            Assert.DoesNotMatch(@"(?i)Gatekeeper accepts|accepts? the executable|executable (is|was) accepted|seem to be an app", text);
        }
    }

    /// <summary>
    /// Any page saying something is unsigned, except for the one entitlement whose name holds the word.
    /// </summary>
    private const string UnsignedClaim = @"(?i)(?<!cs\.allow-)\bunsigned\b(?!-executable-memory)";

    /// <summary>
    /// The ban on "unsigned" reads past the entitlement <c>com.apple.security.cs.allow-unsigned-executable-memory</c>,
    /// which is a name rather than a claim, and past nothing else: the word is still caught on its own, and after a
    /// prefix that only looks like the entitlement's.
    /// </summary>
    [Theory]
    [InlineData("the Linux executable is unsigned", true)]
    [InlineData("the executable is allow-unsigned", true)]
    [InlineData("an allow-unsigned build", true)]
    [InlineData("its entitlements are `com.apple.security.cs.allow-jit` and `com.apple.security.cs.allow-unsigned-executable-memory`", false)]
    public void The_ban_on_unsigned_reads_past_only_the_entitlements_name(string text, bool caught)
    {
        Assert.Equal(caught, Regex.IsMatch(text, UnsignedClaim));
    }

    /// <summary>
    /// PB7: the fact a dry run's reader needs, where they will read it — the template, which is where whoever
    /// presses the button is already looking. That it does not reach the release page is the next test.
    /// </summary>
    [Fact]
    public void The_notes_say_that_a_drafts_tag_does_not_exist_until_it_is_published()
    {
        var notes = Read(Notes);
        Assert.StartsWith("<!--", notes, StringComparison.Ordinal);
        Assert.Contains("does not exist until the draft is published", notes, StringComparison.Ordinal);
    }

    /// <summary>
    /// The comment at the top of the template is addressed to whoever edits the template: edit this file rather
    /// than the release page, here is what the button does with a draft. It was shipping inside the release
    /// body. Markdown hides an HTML comment when a page is rendered, but the body is read raw as well — by the
    /// API, by <c>gh release view</c>, in the notification mail — so the step that writes the notes strips every
    /// comment, and what is asserted here is what the step wrote, not what the template says.
    /// </summary>
    [Fact]
    public void The_notes_that_ship_carry_no_instructions_meant_for_their_author()
    {
        var shipped = WriteTheNotes(Read(Notes));

        Assert.DoesNotContain("<!--", shipped, StringComparison.Ordinal);
        Assert.DoesNotContain("-->", shipped, StringComparison.Ordinal);
        Assert.DoesNotContain("edit this file rather than the release page", shipped, StringComparison.Ordinal);
        Assert.DoesNotContain("does not exist until the draft is published", shipped, StringComparison.Ordinal);
        Assert.StartsWith("Jason 0.2.0", shipped, StringComparison.Ordinal);
        Assert.DoesNotContain("{{version}}", shipped, StringComparison.Ordinal);

        // Every comment, not only the one at the top: a note left further down is a note to the same author.
        var later = WriteTheNotes("Jason {{version}} is here.\n\n<!-- remember to mention the migration -->\n\nEnjoy.\n");
        Assert.DoesNotContain("remember to mention", later, StringComparison.Ordinal);
        Assert.Contains("Jason 0.2.0 is here.", later, StringComparison.Ordinal);
        Assert.Contains("Enjoy.", later, StringComparison.Ordinal);
    }

    /// <summary>
    /// The notes told every reader that "the install scripts clear that mark", and one of the two does not.
    /// <c>install.ps1</c> clears the mark of the web with <c>Unblock-File</c>; <c>install.sh</c> clears nothing,
    /// and needs to clear nothing, because macOS's quarantine attribute is set by the browser that downloaded a
    /// file and not by <c>curl</c>. A sentence that is true of one script and not the other is worth guarding
    /// against both scripts rather than against itself.
    /// </summary>
    [Fact]
    public void The_notes_say_which_script_clears_the_mark_of_the_web()
    {
        var notes = Regex.Replace(Read(Notes), @"\s+", " ");

        Assert.Contains("Unblock-File", Read(["install", "install.ps1"]), StringComparison.Ordinal);
        Assert.Contains("`install.ps1` clears", notes, StringComparison.Ordinal);

        // If install.sh ever does clear it, this goes red and the sentence gets rewritten rather than quietly
        // becoming true again by accident.
        var sh = Read(["install", "install.sh"]);
        Assert.DoesNotContain("xattr", sh, StringComparison.Ordinal);
        Assert.DoesNotContain("quarantine", sh, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("the install scripts clear", notes, StringComparison.Ordinal);
        Assert.Contains("not by `curl`", notes, StringComparison.Ordinal);
    }

    /// <summary>
    /// The release's "Write the notes" step, run the way the publishing job runs it: this template, this
    /// version, in a tree of the test's own — and what it wrote. With <paramref name="own"/>, the tree holds that
    /// <c>RELEASE-NOTES.md</c> and the version job has said it belongs to this version; without, it has said there are
    /// none to carry.
    /// </summary>
    private static string WriteTheNotes(string template, string? own = null)
    {
        var writing = PublishingStep("Write the notes");
        Assert.Equal("${{ needs.version.outputs.notes }}", Scalar(Mapping(writing, "env"), "NOTES"));

        using var tree = new TempTree();
        Directory.CreateDirectory(Path.Combine(tree.Root, ".github"));
        Directory.CreateDirectory(Path.Combine(tree.Root, "artifacts", "release"));
        File.WriteAllText(Path.Combine(tree.Root, ".github", "release-notes-template.md"), template);
        if (own is not null)
        {
            File.WriteAllText(Path.Combine(tree.Root, "RELEASE-NOTES.md"), own);
        }

        var step = Path.Combine(tree.Root, "write-the-notes.ps1");
        File.WriteAllText(step, Scalar(writing, "run"));

        var (exit, _, stderr) = Pwsh(
            new Dictionary<string, string> { ["VERSION"] = "0.2.0", ["NOTES"] = own is null ? "false" : "true" },
            "-Command", $"$ErrorActionPreference = 'Stop'; Set-Location -LiteralPath '{tree.Root}'; & '{step}'");

        Assert.True(exit == 0, stderr);
        return File.ReadAllText(Path.Combine(tree.Root, "artifacts", "release", "notes.md"));
    }

    /// <summary>
    /// The upgrade note is conditional, and its condition is held: only a machine that ran 0.1.0 with autostart
    /// enabled has the runtime its logon task started still holding the data directory. The notes a release
    /// really writes carry it with the version filled in.
    /// </summary>
    [Fact]
    public void The_notes_say_what_an_upgrade_from_0_1_0_with_autostart_needs_and_only_then()
    {
        var notes = WriteTheNotes(Read(Notes));
        var start = notes.IndexOf("## Upgrading from 0.1.0\n", StringComparison.Ordinal);
        Assert.True(start >= 0, "the notes have no section 'Upgrading from 0.1.0'.");
        var end = notes.IndexOf("\n## ", start + 1, StringComparison.Ordinal);
        var note = Regex.Replace(notes[(start + "## Upgrading from 0.1.0".Length)..(end < 0 ? notes.Length : end)], @"\s+", " ").Trim();

        Assert.StartsWith("If this machine ran 0.1.0 with autostart enabled,", note, StringComparison.Ordinal);
        Assert.Contains("`jason runtime autostart enable` or `disable` once from an ordinary prompt", note, StringComparison.Ordinal);
        var enable = note.IndexOf("`jason runtime autostart enable`", StringComparison.Ordinal);
        var denied = note.IndexOf("if that answers Access is denied, follow its message first", StringComparison.Ordinal);
        var stop = note.IndexOf("`jason runtime stop` once", StringComparison.Ordinal);
        var started = note.IndexOf("then `jason runtime start` (or sign in again, with autostart enabled)", StringComparison.Ordinal);
        Assert.True(enable >= 0 && denied > enable && stop > denied && started > stop, $"the acts are not named in order: {note}");
        Assert.DoesNotContain("release-and-update", note, StringComparison.Ordinal);
        Assert.DoesNotContain("{{", note, StringComparison.Ordinal);
    }

    [Fact]
    public void The_notes_name_every_asset_the_release_carries()
    {
        var notes = Read(Notes);
        foreach (var name in ReleaseAssets.Published)
        {
            Assert.Contains(name, notes, StringComparison.Ordinal);
        }

        Assert.Contains("{{version}}", notes, StringComparison.Ordinal);
    }

    // --- the release's own notes ------------------------------------------------------------------------

    /// <summary>
    /// A release's own notes, <c>RELEASE-NOTES.md</c>, go into its body after the template's opening paragraph,
    /// where the template says <c>{{notes}}</c>: without their header comment, which is addressed to whoever writes
    /// them, and without their first line, <c># Jason &lt;version&gt;</c>, which the release's title already says.
    /// </summary>
    [Fact]
    public void A_release_s_own_notes_follow_the_opening_paragraph_without_their_header_or_their_title()
    {
        const string Own = "<!--\nA note to whoever writes the next one.\n-->\n\n# Jason 0.2.0\n\n## What is new since 0.1.0\n\nA thing that is new.\n\n## Known issues\n\nNone.\n";

        var shipped = WriteTheNotes(Read(Notes), Own);

        Assert.StartsWith("Jason 0.2.0: ", shipped, StringComparison.Ordinal);
        Assert.Contains("passed.\n\n## What is new since 0.1.0\n\nA thing that is new.\n\n## Known issues\n\nNone.\n\n## Assets\n", shipped, StringComparison.Ordinal);
        Assert.DoesNotContain("# Jason 0.2.0", shipped, StringComparison.Ordinal);
        Assert.DoesNotContain("A note to whoever writes", shipped, StringComparison.Ordinal);
        Assert.DoesNotContain("<!--", shipped, StringComparison.Ordinal);
        Assert.DoesNotContain("{{notes}}", shipped, StringComparison.Ordinal);
    }

    /// <summary>
    /// Without notes of its own — only the button gets this far without them — the body is the template alone, with
    /// no placeholder and no gap where the notes would have stood.
    /// </summary>
    [Fact]
    public void Without_notes_of_its_own_the_body_is_the_template_alone()
    {
        Assert.Contains("\n\n{{notes}}\n\n## Assets\n", Read(Notes).ReplaceLineEndings("\n"), StringComparison.Ordinal);

        var shipped = WriteTheNotes(Read(Notes));

        Assert.DoesNotContain("{{notes}}", shipped, StringComparison.Ordinal);
        Assert.Contains("passed.\n\n## Assets\n", shipped, StringComparison.Ordinal);
        Assert.DoesNotContain("\n\n\n", shipped, StringComparison.Ordinal);
    }

    /// <summary>
    /// The release's own notes fill in <c>{{version}}</c> as the template does, so an address copied from the template
    /// into them names this release rather than shipping with the braces in it.
    /// </summary>
    [Fact]
    public void The_release_s_own_notes_fill_in_the_version_as_the_template_does()
    {
        const string Own = "# Jason 0.2.0\n\n## Upgrading from 0.1.0\n\nRead https://get.jasonharness.ai/releases/tag/v{{version}} first.\n";

        var shipped = WriteTheNotes(Read(Notes), Own);

        Assert.Contains("Read https://get.jasonharness.ai/releases/tag/v0.2.0 first.\n", shipped, StringComparison.Ordinal);
        Assert.DoesNotContain("{{version}}", shipped, StringComparison.Ordinal);
    }

    /// <summary>
    /// The version job reads <c>RELEASE-NOTES.md</c> before anything is built. A tag ships its own notes, so on a tag a
    /// file that is not there, or that names another version — the last release's, which the version after a release
    /// leaves in place until this one rewrites it — stops the release there. The button's draft carries the template
    /// alone in either case, and says so in a warning. Run, not read: the step's own PowerShell in a tree of the test's own.
    /// </summary>
    [Theory]
    [InlineData("push", "0.21.0", null, 1, "There is no RELEASE-NOTES.md; this tag is v0.21.0, and a tag ships its own notes.")]
    [InlineData("push", "0.21.0", "# Jason 0.20.0\n\n## Known issues\n\nNone.\n", 1, "RELEASE-NOTES.md names 0.20.0; this tag is v0.21.0")]
    [InlineData("push", "0.21.0", "# Jason 0.21.0-rc.1\n\n## Known issues\n\nNone.\n", 1, "RELEASE-NOTES.md names 0.21.0-rc.1; this tag is v0.21.0")]
    [InlineData("push", "0.21.0", "Jason 0.21.0\n\n## Known issues\n\nNone.\n", 1, "RELEASE-NOTES.md does not open with '# Jason <version>'; this tag is v0.21.0")]
    [InlineData("push", "0.21.0", "# Jason 0.21.0\n\n## Known issues\n\nNone.\n", 0, "notes=true")]
    [InlineData("push", "0.21.0", "<!--\nThe header.\n# Jason 0.20.0\n-->\n\n# Jason 0.21.0\n\n## Known issues\n\nNone.\n", 0, "notes=true")]
    [InlineData("push", "1.0.0-rc.1", "# Jason 1.0.0-rc.1\n\n## Known issues\n\nNone.\n", 0, "notes=true")]
    [InlineData("workflow_dispatch", "0.21.0", null, 0, "notes=false")]
    [InlineData("workflow_dispatch", "0.21.0", "# Jason 0.20.0\n\n## Known issues\n\nNone.\n", 0, "notes=false")]
    [InlineData("workflow_dispatch", "0.21.0", "# Jason 0.21.0\n\n## Known issues\n\nNone.\n", 0, "notes=true")]
    [InlineData("push", "0.21.0", "# Jason 0.21.0-unwritten\n\n## What is new since 0.20.0\n\nNot yet.\n", 1, "RELEASE-NOTES.md names 0.21.0-unwritten; this tag is v0.21.0")]
    [InlineData("workflow_dispatch", "0.21.0", "# Jason 0.21.0-unwritten\n\n## What is new since 0.20.0\n\nNot yet.\n", 0, "notes=false")]
    [InlineData("push", "0.21.0-rc.1", "# Jason 0.21.0\n\n## Known issues\n\nNone.\n", 1, "RELEASE-NOTES.md names 0.21.0; this tag is v0.21.0-rc.1")]
    [InlineData("push", "0.21.0", "# Jason 0.21.0\r\n\r\n## Known issues\r\n\r\nNone.\r\n", 0, "notes=true")]
    [InlineData("push", "0.21.0", "\uFEFF# Jason 0.21.0\n\n## Known issues\n\nNone.\n", 0, "notes=true")]
    [InlineData("push", "0.21.0", "# Jason 0.21.0 \n\n## Known issues\n\nNone.\n", 1, "RELEASE-NOTES.md does not open with '# Jason <version>'; this tag is v0.21.0")]
    [InlineData("push", "0.21.0", "\n\n# Jason 0.21.0\n\n## Known issues\n\nNone.\n", 0, "notes=true")]
    [InlineData("workflow_dispatch", "0.21.0", "Jason 0.21.0\n\n## Known issues\n\nNone.\n", 0, "notes=false")]
    [InlineData("push", "0.21.0", "", 1, "RELEASE-NOTES.md does not open with '# Jason <version>'; this tag is v0.21.0")]
    [InlineData("workflow_dispatch", "0.21.0", "", 0, "notes=false")]
    public void A_tag_without_its_own_notes_stops_before_anything_is_built(string eventName, string version, string? own, int exit, string said)
    {
        var jobs = Mapping(Parse(Read(Release)), "jobs");
        var steps = Assert.IsType<YamlSequenceNode>(Node(Mapping(jobs, "version"), "steps")).Children.Cast<YamlMappingNode>().ToList();
        var check = Assert.Single(steps, step => Scalar(step, "id") == "notes");
        Assert.Equal("${{ steps.notes.outputs.notes }}", Scalar(Mapping(Mapping(jobs, "version"), "outputs"), "notes"));
        Assert.Equal("${{ steps.version.outputs.version }}", Scalar(Mapping(check, "env"), "VERSION"));
        Assert.True(
            steps.IndexOf(check) < steps.FindIndex(step => Scalar(step, "id") == "dotnet"),
            "The notes are read after .NET is set up; saying no to a tag needs nothing that comes after them.");
        Assert.Equal("${{ needs.version.outputs.notes }}", Scalar(Mapping(PublishingStep("Write the notes"), "env"), "NOTES"));

        using var tree = new TempTree();
        var output = Path.Combine(tree.Root, "output.txt");
        File.WriteAllText(output, string.Empty);
        if (own is not null)
        {
            File.WriteAllText(Path.Combine(tree.Root, "RELEASE-NOTES.md"), own);
        }

        var script = Path.Combine(tree.Root, "check-the-notes.ps1");
        File.WriteAllText(script, Scalar(check, "run"));

        // Under the harness every script runs in, so that a refusal reaches stderr as the exception's message, whole: the
        // error view wraps it at the console's width, which splits a long one on a runner without a terminal.
        var (code, stdout, stderr) = Pwsh(
            new Dictionary<string, string> { ["EVENT"] = eventName, ["VERSION"] = version, ["GITHUB_OUTPUT"] = output },
            "-Command",
            $"Set-Location -LiteralPath '{tree.Root}'\n"
                + Workflows.PlainErrors.Replace("@SCRIPT@", script.Replace("'", "''", StringComparison.Ordinal), StringComparison.Ordinal));

        if (exit != 0)
        {
            Assert.NotEqual(0, code);
            Assert.Contains(said, stderr, StringComparison.Ordinal);
            Assert.Equal(string.Empty, File.ReadAllText(output));
            return;
        }

        Assert.True(code == 0, stderr);
        Assert.Equal(said, File.ReadAllText(output).Trim());

        // The button says why this run carries the template alone, as a warning on the run; otherwise the log names
        // the version the notes were written for. `$env:VERSION:` would be read as a variable of its own, so braces.
        Assert.Equal(said == "notes=false", stdout.Contains("::warning", StringComparison.Ordinal));
        if (said == "notes=false")
        {
            Assert.Contains($"; this run is v{version}, so it carries the template alone.", stdout, StringComparison.Ordinal);
        }
        else
        {
            Assert.Contains("${env:VERSION}", Scalar(check, "run"), StringComparison.Ordinal);
            Assert.Contains($"RELEASE-NOTES.md names {version}: the release carries its own notes.", stdout, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The shape the release reads its own notes in: an optional header comment, addressed to whoever writes them and
    /// dropped; a first line <c># Jason &lt;version&gt;</c>, which the version job compares with the tag; then only the
    /// release's own sections, each once — what is new, what is fixed, how to upgrade, what is known not to work — and no
    /// comment in what ships. Read only when the file is there, and never against the version this tree builds: the
    /// version after a release leaves the file naming the release it was written for, and the next release rewrites it.
    /// </summary>
    [Fact]
    [Critical(Family.B4, "a release's own notes")]
    public void The_release_s_own_notes_have_the_shape_the_release_reads()
    {
        // The template is the other half of a public release's body, so it is read for the same pointers.
        var pointers = PublicPointers(Read(Notes));
        Assert.True(pointers.Count == 0, ".github/release-notes-template.md:\n" + string.Join('\n', pointers));

        var path = Path.Combine(RepositoryRoot(), "RELEASE-NOTES.md");
        if (!File.Exists(path))
        {
            return;
        }

        var problems = OwnNotesProblems(File.ReadAllText(path));
        Assert.True(problems.Count == 0, "RELEASE-NOTES.md:\n" + string.Join('\n', problems));
    }

    /// <summary>
    /// What ships in a release's body is read publicly, so what the public repository's check refuses in a pull request
    /// is refused in the notes and in the template too: each kind of pointer, spelled here in pieces so that this file
    /// is not one.
    /// </summary>
    [Theory]
    [InlineData("A fix (" + "REPLY" + "-4242).", "a tracker key")]
    [InlineData("See https://example." + "Atlassian" + ".net/browse/X-1.", "an Atlassian address")]
    [InlineData("Co-Authored-" + "By: Someone <someone@example.invalid>", "an attribution trailer")]
    [InlineData("Claude-" + "Session: https://example.invalid/x", "an attribution trailer")]
    [InlineData("Generated with [" + "Claude Code](https://example.invalid)", "the generated-with footer")]
    public void A_pointer_is_named_in_the_notes_and_in_the_template(string pointer, string expected)
    {
        Assert.Contains(expected, OwnNotesProblems("# Jason 0.21.0\n\n## Fixed since 0.20.0\n\n" + pointer + "\n"));
        Assert.Contains(expected, PublicPointers(Read(Notes) + "\n" + pointer + "\n"));
    }

    /// <summary>The shape's reader, on texts before the file: each way out of it is named.</summary>
    [Theory]
    [InlineData("# Notes for 0.21.0\n\n## Known issues\n\nNone.\n", "the first line is '# Notes for 0.21.0', not '# Jason <version>'")]
    [InlineData("<!--\nThe header.\n-->\nJason 0.21.0\n\n## Known issues\n\nNone.\n", "the first line is 'Jason 0.21.0', not '# Jason <version>'")]
    [InlineData("# Jason 0.21.0\n\n## Highlights\n\nA thing.\n", "'## Highlights' is not one of the release's own sections")]
    [InlineData("# Jason 0.21.0\n\n## Known issues\n\n### On Windows\n\nA thing.\n", "'### On Windows' is not one of the release's own sections")]
    [InlineData("# Jason 0.21.0\n\n## What is new since the last one\n\nA thing.\n", "'## What is new since the last one' is not one of the release's own sections")]
    [InlineData("# Jason 0.21.0\n\n## Known issues\n\n<!-- remember the migration -->\nNone.\n", "an HTML comment in the body, which never ships")]
    [InlineData("# Jason 0.21.0\n\n## Known issues\n\nNone.\n\n## Known issues\n\nStill none.\n", "'## Known issues' twice")]
    [InlineData("# Jason 0.21.0\n\nA paragraph and no section.\n", "no section")]
    [InlineData("# Jason 0.21.0 \n\n## Known issues\n\nNone.\n", "the first line is '# Jason 0.21.0 ', not '# Jason <version>'")]
    [InlineData("<!--\nThe header, never closed.\n\n# Jason 0.21.0\n\n## Known issues\n\nNone.\n", "the header comment is never closed")]
    [InlineData("# Jason 0.21.0\n\n## Known issues\n\nOne step --> the next.\n", "an HTML comment in the body, which never ships")]
    [InlineData("# Jason 0.21.0\n\n## Known issues\n\nHighlights\n----------\n\nNone.\n", "'Highlights' underlined with '-' is a heading, not one of the release's own sections")]
    [InlineData("# Jason 0.21.0\n\n## Known issues\n\nHighlights\n===\n\nNone.\n", "'Highlights' underlined with '=' is a heading, not one of the release's own sections")]
    [InlineData("# Jason 0.21.0\n\n## Known issues\n\n  ## Highlights\n\nNone.\n", "'  ## Highlights' is an indented heading, not one of the release's own sections")]
    public void Notes_out_of_that_shape_are_named(string text, string expected)
    {
        var problems = OwnNotesProblems(text);

        Assert.Contains(expected, problems);
    }

    [Theory]
    [InlineData("# Jason 0.21.0\n\n## Known issues\n\nNone.\n")]
    [InlineData("<!--\nThe header, which may say # Jason 0.20.0 and ## Anything.\n-->\n\n# Jason 0.21.0\n\n## What is new since 0.20.0\n\nA.\n\n## Fixed since 0.20.0\n\nB.\n\n## Upgrading from 0.20.0\n\nC.\n\n## Known issues\n\nD.\n")]
    [InlineData("# Jason 1.0.0-rc.1\n\n## Upgrading from 0.1.0\n\nRun this:\n\n```sh\n# a comment, not a heading\njason runtime stop\n```\n")]
    [InlineData("# Jason 0.21.0\r\n\r\n## Known issues\r\n\r\nNone.\r\n")]
    [InlineData("\uFEFF# Jason 0.21.0\n\n## Known issues\n\nNone.\n")]
    [InlineData("# Jason 0.21.0-unwritten\n\n## What is new since 0.20.0\n\nA list:\n\n- one\n- two\n\nA rule after a blank line:\n\n---\n\nNone.\n")]
    public void Notes_in_that_shape_are_not(string text) =>
        Assert.Empty(OwnNotesProblems(text));

    /// <summary>What is out of shape in a release's own notes; nothing when they are in it.</summary>
    private static List<string> OwnNotesProblems(string text)
    {
        var problems = PublicPointers(text);
        var body = text.TrimStart('\uFEFF').ReplaceLineEndings("\n").TrimStart();
        if (body.StartsWith("<!--", StringComparison.Ordinal))
        {
            var end = body.IndexOf("-->", StringComparison.Ordinal);
            if (end < 0)
            {
                problems.Add("the header comment is never closed");
            }

            body = end < 0 ? string.Empty : body[(end + "-->".Length)..].TrimStart();
        }

        var lines = body.Split('\n');
        if (!OwnNotesTitle().IsMatch(lines[0]))
        {
            problems.Add($"the first line is '{lines[0]}', not '# Jason <version>'");
        }

        if (body.Contains("<!--", StringComparison.Ordinal) || body.Contains("-->", StringComparison.Ordinal))
        {
            problems.Add("an HTML comment in the body, which never ships");
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var fenced = false;
        for (var at = 1; at < lines.Length; at++)
        {
            var line = lines[at];
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal) || line.TrimStart().StartsWith("~~~", StringComparison.Ordinal))
            {
                fenced = !fenced;
                continue;
            }

            if (fenced)
            {
                continue;
            }

            // A heading the release page renders that is not written as `## ` at the start of a line: a text line
            // underlined with `=` or `-`, or a `#` heading indented by up to three spaces.
            var before = lines[at - 1];
            if (SetextUnderline().IsMatch(line) && before.Trim().Length > 0 && !before.TrimStart().StartsWith('#'))
            {
                problems.Add($"'{before.Trim()}' underlined with '{line.Trim()[0]}' is a heading, not one of the release's own sections");
                continue;
            }

            if (IndentedHeading().IsMatch(line))
            {
                problems.Add($"'{line}' is an indented heading, not one of the release's own sections");
                continue;
            }

            if (!line.StartsWith('#'))
            {
                continue;
            }

            if (!OwnNotesSection().IsMatch(line))
            {
                problems.Add($"'{line}' is not one of the release's own sections");
            }
            else if (!seen.Add(line))
            {
                problems.Add($"'{line}' twice");
            }
        }

        if (seen.Count == 0)
        {
            problems.Add("no section");
        }

        return problems;
    }

    /// <summary>
    /// What the public repository's check refuses in a pull request, read in a text that ships in a release's body:
    /// a tracker key, an Atlassian address in any case, an attribution trailer and the generated-with footer.
    /// </summary>
    private static List<string> PublicPointers(string text)
    {
        var pointers = new List<string>();
        if (TrackerKey().IsMatch(text))
        {
            pointers.Add("a tracker key");
        }

        if (text.Contains("atlas" + "sian", StringComparison.OrdinalIgnoreCase))
        {
            pointers.Add("an Atlassian address");
        }

        if (AttributionTrailer().IsMatch(text))
        {
            pointers.Add("an attribution trailer");
        }

        if (GeneratedWithFooter().IsMatch(text))
        {
            pointers.Add("the generated-with footer");
        }

        return pointers;
    }

    [GeneratedRegex(@"\b" + "REP" + "LY" + "-[0-9]+")]
    private static partial Regex TrackerKey();

    [GeneratedRegex("Co-Authored-" + "By:|Claude-" + "Session:", RegexOptions.IgnoreCase)]
    private static partial Regex AttributionTrailer();

    [GeneratedRegex(@"Generated with \[" + "Claude", RegexOptions.IgnoreCase)]
    private static partial Regex GeneratedWithFooter();

    [GeneratedRegex(@"^ {0,3}(?:=+|-+)[ \t]*$")]
    private static partial Regex SetextUnderline();

    [GeneratedRegex(@"^ {1,3}#{1,6}(?:[ \t]|$)")]
    private static partial Regex IndentedHeading();

    private const string AVersion = @"(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?";

    [GeneratedRegex(@"^# Jason " + AVersion + "$")]
    private static partial Regex OwnNotesTitle();

    [GeneratedRegex(@"^## (?:(?:What is new since|Fixed since|Upgrading from) " + AVersion + "|Known issues)$")]
    private static partial Regex OwnNotesSection();

    // --- Write-Manifest.ps1 ----------------------------------------------------------------------------

    /// <summary>
    /// The fragments the action writes, merged by the script the release runs, parse as a manifest: the shape
    /// the workflow emits and the shape the runtime reads cannot drift apart without this going red. The script
    /// is executed, not imitated.
    /// </summary>
    [Fact]
    public void The_manifest_the_release_writes_is_one_this_build_can_read()
    {
        using var tree = new TempTree();
        var directory = Staged(tree);

        var (exit, _, stderr) = Pwsh(
            NoEnvironment,
            "-File", Path.Combine(RepositoryRoot(), Path.Combine(Script)),
            "-Version", "0.2.0",
            "-Directory", directory,
            "-Require", string.Join(',', ReleaseAssets.Rids),
            "-Plugins", ReleaseAssets.ReplyPlugin,
            "-ReleaseNotesUrl", "https://example.invalid/releases/tag/v0.2.0");
        Assert.True(exit == 0, stderr);

        var manifest = UpdateManifest.Read(File.ReadAllText(Path.Combine(directory, ReleaseAssets.Manifest)));
        Assert.Equal(UpdateManifest.CurrentSchema, manifest.Schema);
        Assert.Equal("0.2.0", manifest.Version.ToString());
        Assert.Equal("https://example.invalid/releases/tag/v0.2.0", manifest.ReleaseNotesUrl);
        Assert.Null(manifest.MinUpgradeFrom);
        Assert.Equal(TimeSpan.Zero, manifest.PublishedAt.Offset);
        Assert.Equal(ReleaseAssets.Rids.Count, manifest.Artifacts.Count);
        foreach (var rid in ReleaseAssets.Rids)
        {
            Assert.Equal(ReleaseAssets.For(rid), manifest.Artifacts[rid].Asset);
            Assert.Equal(new FileInfo(Path.Combine(directory, ReleaseAssets.For(rid))).Length, manifest.Artifacts[rid].Size);
        }

        // The plugin's package is not a platform, and the manifest's artifacts are platforms: a running Jason reads the
        // manifest to learn which executable to take, and the package is not one.
        Assert.DoesNotContain(manifest.Artifacts.Values, artifact => artifact.Asset == ReleaseAssets.ReplyPlugin);

        // Given no digest list, the script names no package: the field is there, last, and null.
        Assert.Empty(manifest.Plugins);
        using (var written = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, ReleaseAssets.Manifest))))
        {
            var last = written.RootElement.EnumerateObject().Last();
            Assert.Equal("plugins", last.Name);
            Assert.Equal(JsonValueKind.Null, last.Value.ValueKind);
        }

        // sha256sum's own format — the digest, two spaces, the name — one per line, Unix newlines, so that
        // `sha256sum -c checksums.txt` works wherever the archives were downloaded to. The plugin's package has its
        // line after the archives', which is where a person checks it from.
        var checksums = File.ReadAllText(Path.Combine(directory, ReleaseAssets.Checksums));
        Assert.DoesNotContain('\r', checksums);
        var lines = checksums.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(ReleaseAssets.Rids.Count + 1, lines.Length);
        foreach (var rid in ReleaseAssets.Rids)
        {
            Assert.Contains($"{manifest.Artifacts[rid].Sha256}  {ReleaseAssets.For(rid)}", lines);
        }

        var package = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(directory, ReleaseAssets.ReplyPlugin))));
        Assert.Equal($"{package}  {ReleaseAssets.ReplyPlugin}", lines[^1]);
    }

    /// <summary>
    /// A plugin's package the release promises and the download did not bring would be a line naming a file that is not
    /// there, so nothing is written: neither the checksums nor the manifest.
    /// </summary>
    [Fact]
    public void A_release_missing_the_plugin_s_package_is_not_assembled()
    {
        using var tree = new TempTree();
        var directory = Staged(tree);
        File.Delete(Path.Combine(directory, ReleaseAssets.ReplyPlugin));

        // Run so that the refusal reaches stderr whole, as the exception's message, rather than wrapped by PowerShell's view.
        var (exit, _, stderr) = Workflows.Script(NoEnvironment, Path.Combine(RepositoryRoot(), Path.Combine(Script)), "-Version", "0.2.0", "-Directory", directory, "-Require", string.Join(',', ReleaseAssets.Rids), "-Plugins", ReleaseAssets.ReplyPlugin);

        // The script's own refusal, which says what the release promised, rather than whatever a later line would say
        // about a file it cannot find.
        Assert.NotEqual(0, exit);
        Assert.Contains($"The release carries the package {ReleaseAssets.ReplyPlugin}, and it is not in", stderr, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(directory, ReleaseAssets.Manifest)));
        Assert.False(File.Exists(Path.Combine(directory, ReleaseAssets.Checksums)));
    }

    /// <summary>A manifest that named a platform the release does not carry would send that platform's users a 404.</summary>
    [Fact]
    public void A_release_missing_a_platform_is_not_assembled()
    {
        using var tree = new TempTree();
        var directory = Staged(tree);
        File.Delete(Path.Combine(directory, $"jason-{ReleaseAssets.Rids[0]}.fragment.json"));

        var (exit, _, stderr) = Pwsh(NoEnvironment,"-File", Path.Combine(RepositoryRoot(), Path.Combine(Script)), "-Version", "0.2.0", "-Directory", directory, "-Require", string.Join(',', ReleaseAssets.Rids));

        Assert.NotEqual(0, exit);
        Assert.Contains(ReleaseAssets.Rids[0], stderr, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(directory, ReleaseAssets.Manifest)));
    }

    /// <summary>
    /// The fragment says what the archive hashes to; the script hashes the archive again before it repeats the
    /// claim in the manifest. An archive that changed between the packaging and the assembling — or a fragment
    /// that never described it — is refused, not published.
    /// </summary>
    [Fact]
    public void A_fragment_that_does_not_describe_its_archive_is_refused()
    {
        using var tree = new TempTree();
        var directory = Staged(tree);
        File.AppendAllText(Path.Combine(directory, ReleaseAssets.For(ReleaseAssets.Rids[1])), "one more byte");

        var (exit, _, stderr) = Pwsh(NoEnvironment,"-File", Path.Combine(RepositoryRoot(), Path.Combine(Script)), "-Version", "0.2.0", "-Directory", directory, "-Require", string.Join(',', ReleaseAssets.Rids));

        Assert.NotEqual(0, exit);
        Assert.Contains(ReleaseAssets.For(ReleaseAssets.Rids[1]), stderr, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(directory, ReleaseAssets.Manifest)));
    }

    /// <summary>
    /// A bare version, X.Y.Z, the version of a published release: a pre-release, build metadata, or anything that does
    /// not parse is refused before either file is written, and the message names what it was given.
    /// </summary>
    [Theory]
    [InlineData("0.19")]
    [InlineData("v0.19.0")]
    [InlineData("0.19.0-rc.1")]
    [InlineData("01.19.0")]
    public void A_floor_that_is_not_a_bare_version_is_refused_before_anything_is_written(string floor)
    {
        using var tree = new TempTree();
        var directory = Staged(tree);

        var (exit, _, stderr) = Workflows.Script(NoEnvironment, Path.Combine(RepositoryRoot(), Path.Combine(Script)), "-Version", "0.2.0", "-Directory", directory, "-Require", string.Join(',', ReleaseAssets.Rids), "-MinUpgradeFrom", floor);

        Assert.NotEqual(0, exit);
        Assert.Contains($"-MinUpgradeFrom must be a version like 0.19.0, not '{floor}'", stderr, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(directory, ReleaseAssets.Manifest)));
        Assert.False(File.Exists(Path.Combine(directory, ReleaseAssets.Checksums)));
    }

    /// <summary>
    /// A digest list names its package in the manifest: the last line's version and digest, the asset the release
    /// carries it as, and every line as recorded — under <c>plugins</c>, the manifest's last field, so the first
    /// <c>"version":</c> line of the document is still the release's own, which is the line install.sh reads.
    /// </summary>
    [Fact]
    public void A_digest_list_names_its_last_line_as_the_package_the_release_publishes()
    {
        using var tree = new TempTree();
        var directory = Staged(tree);
        var list = DigestList(tree, ("0.8.0", OlderPackage), ("0.9.0", NewerPackage));

        var (exit, _, stderr) = Workflows.Script(NoEnvironment, Path.Combine(RepositoryRoot(), Path.Combine(Script)), "-Version", "0.2.0", "-Directory", directory, "-Require", string.Join(',', ReleaseAssets.Rids), "-Plugins", ReleaseAssets.ReplyPlugin, "-PluginDigests", list);
        Assert.True(exit == 0, stderr);

        var text = File.ReadAllText(Path.Combine(directory, ReleaseAssets.Manifest));
        var reply = UpdateManifest.Read(text).Plugins["reply"];
        Assert.Equal(SemanticVersion.Parse("0.9.0"), reply.Version);
        Assert.Equal(ReleaseAssets.ReplyPlugin, reply.Asset);
        Assert.Equal(NewerPackage, reply.Digest);
        Assert.Equal(OlderPackage, reply.Recorded[SemanticVersion.Parse("0.8.0")]);
        Assert.Equal(2, reply.Recorded.Count);

        Assert.Equal("\"version\": \"0.2.0\",", text.Split('\n').Select(line => line.Trim()).First(line => line.StartsWith("\"version\":", StringComparison.Ordinal)));
        using var written = JsonDocument.Parse(text);
        Assert.Equal("plugins", written.RootElement.EnumerateObject().Last().Name);
    }

    /// <summary>
    /// The script knows no package by name: two lists name two packages, each under the id its list's file name gives, with
    /// its own asset, version, digest and record, in the order the lists were given, and the checksums carry both after
    /// the archives.
    /// </summary>
    [Fact]
    public void Two_digest_lists_name_two_packages_each_under_its_own_id()
    {
        using var tree = new TempTree();
        var directory = Staged(tree);
        var reply = DigestListOf(tree, ReplyId, ("0.8.0", OlderPackage), ("0.9.0", NewerPackage));
        var forwarder = DigestListOf(tree, ForwarderId, ("1.0.0", OtherOlderPackage), ("1.0.1", OtherNewerPackage));

        var (exit, _, stderr) = Workflows.Script(
            NoEnvironment,
            Path.Combine(RepositoryRoot(), Path.Combine(Script)),
            "-Version", "0.2.0",
            "-Directory", directory,
            "-Require", string.Join(',', ReleaseAssets.Rids),
            "-Plugins", string.Join(',', ReleaseAssets.ReplyPlugin, ReleaseAssets.JournalForwarderPlugin, ReleaseAssets.Ui),
            "-PluginDigests", string.Join(',', reply, forwarder));
        Assert.True(exit == 0, stderr);

        var manifest = UpdateManifest.Read(File.ReadAllText(Path.Combine(directory, ReleaseAssets.Manifest)));
        Assert.Equal([ReplyId, ForwarderId], manifest.Plugins.Keys);

        var first = manifest.Plugins[ReplyId];
        Assert.Equal(ReleaseAssets.ReplyPlugin, first.Asset);
        Assert.Equal(SemanticVersion.Parse("0.9.0"), first.Version);
        Assert.Equal(NewerPackage, first.Digest);
        Assert.Equal([OlderPackage, NewerPackage], first.Recorded.OrderBy(entry => entry.Key).Select(entry => entry.Value));

        var second = manifest.Plugins[ForwarderId];
        Assert.Equal(ReleaseAssets.JournalForwarderPlugin, second.Asset);
        Assert.Equal(SemanticVersion.Parse("1.0.1"), second.Version);
        Assert.Equal(OtherNewerPackage, second.Digest);
        Assert.Equal([OtherOlderPackage, OtherNewerPackage], second.Recorded.OrderBy(entry => entry.Key).Select(entry => entry.Value));

        var lines = File.ReadAllText(Path.Combine(directory, ReleaseAssets.Checksums)).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(ReleaseAssets.Rids.Count + 3, lines.Length);
        Assert.Equal(
            [ReleaseAssets.ReplyPlugin, ReleaseAssets.JournalForwarderPlugin, ReleaseAssets.Ui],
            lines[ReleaseAssets.Rids.Count..].Select(line => line.Split("  ")[1]));
    }

    /// <summary>
    /// A list the script cannot read as the package's record is refused, and so is one for a package the release does not
    /// carry: either would put in the manifest a package nobody can fetch or a record no reader can trust, and a reader
    /// refuses a manifest with either whole. Nothing is written.
    /// </summary>
    [Theory]
    [InlineData("no such file", "is not a file")]
    [InlineData("a line that is not a version and a digest", "which is not a version like 0.9.0")]
    [InlineData("a version twice", "names 0.9.0 twice")]
    [InlineData("a digest twice", "under another version too")]
    [InlineData("no line", "names no version")]
    [InlineData("a package the release does not carry", "which is not among -Plugins")]
    public void A_digest_list_that_is_not_the_package_s_record_is_refused_before_anything_is_written(string what, string expected)
    {
        using var tree = new TempTree();
        var directory = Staged(tree);
        var list = what switch
        {
            "no such file" => Path.Combine(tree.Root, "reply.digests"),
            "a line that is not a version and a digest" => DigestList(tree, ("0.9", NewerPackage)),
            "a version twice" => DigestList(tree, ("0.9.0", OlderPackage), ("0.9.0", NewerPackage)),
            "a digest twice" => DigestList(tree, ("0.8.0", NewerPackage), ("0.9.0", NewerPackage)),
            "no line" => DigestList(tree),
            _ => DigestList(tree, ("0.9.0", NewerPackage)),
        };
        var plugins = what == "a package the release does not carry" ? ReleaseAssets.Ui : ReleaseAssets.ReplyPlugin;

        var (exit, _, stderr) = Workflows.Script(NoEnvironment, Path.Combine(RepositoryRoot(), Path.Combine(Script)), "-Version", "0.2.0", "-Directory", directory, "-Require", string.Join(',', ReleaseAssets.Rids), "-Plugins", plugins, "-PluginDigests", list);

        Assert.True(exit != 0, $"{what} was not refused");
        Assert.Contains(expected, stderr, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(directory, ReleaseAssets.Manifest)));
        Assert.False(File.Exists(Path.Combine(directory, ReleaseAssets.Checksums)));
    }

    private static readonly string OlderPackage = "sha256:" + new string('a', 64);

    private static readonly string NewerPackage = "sha256:" + new string('b', 64);

    private static readonly string OtherOlderPackage = "sha256:" + new string('c', 64);

    private static readonly string OtherNewerPackage = "sha256:" + new string('d', 64);

    /// <summary>A Reply digest list as the repository keeps one: a comment, a blank line, then a line per version.</summary>
    private static string DigestList(TempTree tree, params (string Version, string Digest)[] lines) =>
        DigestListOf(tree, ReplyId, lines);

    /// <summary>A package's digest list as the repository keeps one, named by the package's id.</summary>
    private static string DigestListOf(TempTree tree, string id, params (string Version, string Digest)[] lines)
    {
        var path = Path.Combine(tree.Root, $"{id}.digests");
        File.WriteAllText(path, string.Join('\n', ["# The digest of each version of the package.", string.Empty, .. lines.Select(line => $"{line.Version} {line.Digest}")]) + "\n");
        return path;
    }

    /// <summary>
    /// A directory the way the assembling job sees it after the download: one stand-in archive per platform and
    /// the fragment the packaging action wrote for it, hashed the way the action hashes, the plugins' packages and the UI's.
    /// </summary>
    private static string Staged(TempTree tree)
    {
        var directory = tree.NewDirectory("release");
        foreach (var rid in ReleaseAssets.Rids)
        {
            var asset = ReleaseAssets.For(rid);
            var bytes = Encoding.UTF8.GetBytes($"not really {asset}");
            File.WriteAllBytes(Path.Combine(directory, asset), bytes);

            var digest = Convert.ToHexStringLower(SHA256.HashData(bytes));
            var fragment = new Dictionary<string, object> { [rid] = new { asset, sha256 = digest, size = bytes.Length } };
            File.WriteAllText(Path.Combine(directory, $"jason-{rid}.fragment.json"), JsonSerializer.Serialize(fragment) + "\n");
        }

        foreach (var carried in (string[])[.. ReleasedPackages().Select(package => package.Asset), ReleaseAssets.Ui, ReleaseAssets.Skills, .. ReleaseAssets.Desktop])
        {
            File.WriteAllText(Path.Combine(directory, carried), $"not really {carried}");
        }

        return directory;
    }

    /// <summary>
    /// <c>pwsh</c>, found on this machine's PATH, run to completion. A machine without it skips the test rather
    /// than failing it: the workflows run on runners that have it, and this suite has nothing to install.
    /// </summary>
    /// <summary>
    /// Every place that checks a published or installed executable starts a runtime with it, not only asks it
    /// for its version.
    /// </summary>
    /// <remarks>
    /// A single-file publish leaves SQLite's native library beside the executable unless it is told to bundle
    /// it, and the archive carries the executable alone. Such a build answers <c>--version</c> with 0 and
    /// <c>runtime status</c> with 3 and <c>no_descriptor</c> — both of the assertions this smoke test used to
    /// make — and then dies on <c>runtime start</c> with a missing DLL. The one build a release exists to
    /// deliver would have gone out green, which is why opening the database is now part of every one of these.
    /// </remarks>
    [Theory]
    [InlineData("ci.yml")]
    [InlineData("release.yml")]
    public void Every_executable_check_opens_the_database(string workflow)
    {
        var text = Read(workflow == "ci.yml" ? Ci : Release);
        // Steps that *ask* an executable its version, not steps that mention the question. A comment explaining
        // why something cannot be proved by --version is not an executable check, and reading it as one would
        // have this guard demand a runtime start inside a step that starts nothing.
        var steps = text.Split("- name:").Where(Checks).ToList();

        Assert.NotEmpty(steps);
        foreach (var step in steps)
        {
            Assert.True(Runs(step, "runtime start"), $"{workflow} checks an executable without starting a runtime with it:\n{step}");
            Assert.True(Runs(step, "runtime status"), $"{workflow} starts a runtime and never asks it anything:\n{step}");
            Assert.Contains("applied_migrations", step, StringComparison.Ordinal);
            Assert.True(Runs(step, "runtime stop"), $"{workflow} leaves the runtime it started running:\n{step}");
        }
    }

    /// <summary>
    /// Whether a step really asks an executable what version it is, rather than talking about it, or, in the demo job's one
    /// publish, telling the desktop's prepare script the version to publish the sample's jason at, which asks no executable
    /// anything (the sample is made with that jason, its runtime started and stopped, right after).
    /// </summary>
    private static bool Checks(string step) =>
        step.Split('\n')
            .Select(line => line.Trim())
            .Any(line => line.Contains("--version", StringComparison.Ordinal) && !line.StartsWith('#') && line != DemoPublish);

    /// <summary>The demo job's publish of the release's jason for Linux, the one line the check above passes over.</summary>
    private const string DemoPublish = "node scripts/prepare.mjs --rid linux-x64 --output bundles/demo --version \"$VERSION\"";

    /// <summary>
    /// Where a step really runs a command, by the same rules <see cref="Runs"/> uses — which is not where it
    /// first mentions one. This job's rollback step opens with a comment naming <c>jason update rollback</c>,
    /// so a guard that split the step at the first mention was splitting it at the comment, and a
    /// <c>runtime stop</c> inserted between that comment and the invocation fell on the wrong side of it.
    /// </summary>
    private static int At(string text, string command)
    {
        var position = 0;
        foreach (var line in text.Split('\n'))
        {
            if (Runs(line, command))
            {
                return position;
            }

            position += line.Length + 1;
        }

        return -1;
    }

    /// <summary>
    /// Whether the step really runs the command, rather than merely mentioning it.
    /// </summary>
    /// <remarks>
    /// A plain substring search passes on a step whose command has been deleted, because two other lines still
    /// carry the words: the throw below it (<c>throw "jason runtime start exited with ..."</c>) and the line
    /// that reports success (<c>"the runtime started, migrated and answered"</c> — "started" contains "start").
    /// So the mention must end on a word boundary, and must not be a throw, a condition or a comment: what is
    /// left is the invocation.
    /// </remarks>
    private static bool Runs(string step, string command) =>
        step.Split('\n')
            .Select(line => line.Trim())
            .Any(line => Regex.IsMatch(line, @"\b" + Regex.Escape(command) + @"\b(\s|\||$)")
                && !line.Contains("throw", StringComparison.Ordinal)
                && !line.StartsWith('#')
                && !line.StartsWith("if", StringComparison.Ordinal)
                && !line.StartsWith("Write-Host", StringComparison.Ordinal));

    /// <summary>
    /// A release candidate is published as a pre-release, so that it does not become the latest release.
    /// </summary>
    /// <remarks>
    /// This is the one place where a missing flag reaches everybody at once. `latest` is what the default feed
    /// advertises and what both install one-liners download, so pushing <c>v1.0.0-rc.1</c> without
    /// <c>--prerelease</c> would offer a release candidate to every runtime that checks and install it for
    /// anybody who typed the one-liner that day — the opposite of what the page promises, which is that 0.x
    /// releases are ordinary releases and the pre-release flag is kept for candidates.
    /// </remarks>
    [Fact]
    public void A_version_with_a_pre_release_part_is_published_as_a_pre_release()
    {
        var release = Read(Release);

        Assert.Contains("--prerelease", release, StringComparison.Ordinal);

        // And the flag is decided from the version rather than from the event: a candidate is a candidate
        // whether it arrived as a tag or from the button.
        var deciding = release
            .Split('\n')
            .FirstOrDefault(line => line.Contains("--prerelease", StringComparison.Ordinal));

        Assert.NotNull(deciding);
        Assert.Contains("VERSION", deciding, StringComparison.Ordinal);
        Assert.Contains("-", deciding, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every line a workflow really types to run the manifest script, typed — through the PowerShell parser it
    /// will meet there, and not as a list of argv strings.
    /// </summary>
    /// <remarks>
    /// The difference is the whole of it. A workflow writes its invocation inline, so PowerShell parses the
    /// line: <c>-Require win-x64,linux-x64,osx-arm64</c> is an <em>array</em> there, while the identical text
    /// handed over as one argv element is a string. A test that only ever passes argv proves the script and
    /// says nothing about the line — which is how a run reached CI with both install jobs failing at
    /// "Cannot convert value to type System.String" while every test here was green.
    /// </remarks>
    [Theory]
    [MemberData(nameof(ManifestInvocations))]
    public void Every_line_a_workflow_runs_the_manifest_script_with_is_one_powershell_accepts(string workflow, string invocation)
    {
        using var tree = new TempTree();
        var directory = Staged(tree);

        // The workflow's own line, with the two things only a runner can supply put in: the version it would
        // have computed, and the directory the artifacts would have been downloaded to. Everything else — above
        // all how -Require is written — is left exactly as the workflow writes it.
        var line = ExpandForThisMachine(invocation, directory);

        var (exit, _, stderr) = Pwsh(NoEnvironment, "-Command", line);

        Assert.True(exit == 0, $"{workflow} runs `{invocation}`, and pwsh answered: {stderr}");
        // What it must carry is what the line asked it to carry. A release assembles all three platforms; the
        // end-to-end job assembles the one it is updating, which is a feed of one release for one machine.
        var manifest = UpdateManifest.Read(File.ReadAllText(Path.Combine(directory, ReleaseAssets.Manifest)));
        var required = System.Text.RegularExpressions.Regex.Match(line, @"-Require (\S+)").Groups[1].Value
            .Trim('\'', '"')
            .Split(',', StringSplitOptions.RemoveEmptyEntries);
        Assert.NotEmpty(required);
        foreach (var rid in required)
        {
            Assert.True(manifest.Artifacts.ContainsKey(rid), $"the manifest {invocation} wrote carries nothing for {rid}");
        }

        // The release's notes are read at the product's own address, which redirects to where the release is
        // published, the public repository. And its checksums carry the packages, which the manifest's artifacts do not.
        if (workflow == Release[^1])
        {
            Assert.Equal("https://get.jasonharness.ai/releases/tag/v0.2.0", manifest.ReleaseNotesUrl);

            // Every release from 0.20.0 names 0.19.0, the first applier that hands over to the new executable; it moves
            // only when an applier change raises it.
            Assert.Equal(SemanticVersion.Parse("0.19.0"), manifest.MinUpgradeFrom);

            var checksums = File.ReadAllText(Path.Combine(directory, ReleaseAssets.Checksums));
            foreach (var carried in (string[])[.. ReleasedPackages().Select(package => package.Asset), ReleaseAssets.Ui, ReleaseAssets.Skills, .. ReleaseAssets.Desktop])
            {
                var package = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(directory, carried))));
                Assert.True(
                    checksums.Split('\n').Contains($"{package}  {carried}"),
                    $"{workflow} runs `{invocation}`, and the checksums it wrote carry no line for {carried}:\n{checksums}");
            }

            // Each plugin's package is named from the tag's digest list: its last line is the package the release publishes,
            // the one a runtime built from the same tag carries, and every line is recorded for a reader holding an
            // installed copy's digest. The release's version stays the first "version" line, the one install.sh reads.
            Assert.Equal(ReleasedPackages().Select(package => package.Id), manifest.Plugins.Keys);
            foreach (var (id, asset) in ReleasedPackages())
            {
                var named = manifest.Plugins[id];
                var released = Assert.Single(ReleasedPlugins.Embedded().Packages, plugin => plugin.Id == id);
                Assert.Equal(asset, named.Asset);
                Assert.Equal(PackageDigest.Compute(Path.Combine(RepositoryRoot(), "plugins", id)).Digest, named.Digest);
                Assert.Equal(released.Digest, named.Digest);
                Assert.Equal(SemanticVersion.Parse(released.Version), named.Version);
                Assert.Equal(
                    File.ReadAllLines(Path.Combine(RepositoryRoot(), Digests(id))).Where(line => line.Length > 0 && !line.StartsWith('#')),
                    named.Recorded.OrderBy(entry => entry.Key).Select(entry => $"{entry.Key} {entry.Value}"));
            }

            Assert.Equal(
                "\"version\": \"0.2.0\",",
                File.ReadAllLines(Path.Combine(directory, ReleaseAssets.Manifest)).Select(line => line.Trim()).First(line => line.StartsWith("\"version\":", StringComparison.Ordinal)));
        }
        else
        {
            // The floor is a published release's word about the appliers below it. A run's feed is one machine's, and
            // the side it updates from is this tree, so a floor there would say nothing about any applier.
            Assert.Null(manifest.MinUpgradeFrom);

            // Nor does a run's feed carry a plugin's package, so it names none.
            Assert.Empty(manifest.Plugins);
        }
    }

    /// <summary>
    /// The release names each plugin's package from the tag's own digest list, the file the package's test holds to the
    /// package, and carries every package it names: the job that writes the manifest builds nothing and hashes nothing to
    /// say which packages it publishes. The UI's, the skills' and the desktop's files follow them into checksums.txt.
    /// </summary>
    [Fact]
    public void The_release_names_each_plugin_s_package_from_the_tag_s_digest_list()
    {
        var line = Assert.Single(Read(Release).Split('\n'), line => line.Contains("./.github/scripts/Write-Manifest.ps1", StringComparison.Ordinal));
        var packages = ReleasedPackages();

        Assert.Contains($"-PluginDigests {string.Join(',', packages.Select(package => Digests(package.Id)))} ", line + " ", StringComparison.Ordinal);
        Assert.Contains($"-Plugins {string.Join(',', [.. packages.Select(package => package.Asset), ReleaseAssets.Ui, ReleaseAssets.Skills, .. ReleaseAssets.Desktop])} ", line, StringComparison.Ordinal);
        Assert.All(packages, package => Assert.True(File.Exists(Path.Combine(RepositoryRoot(), Digests(package.Id))), $"{Digests(package.Id)} is not in the repository."));
    }

    /// <summary>The id of each package a release publishes.</summary>
    private const string ReplyId = "reply";

    private const string ForwarderId = "journal-forwarder";

    private const string NtfyId = "ntfy";

    private const string SlackId = "slack";

    private const string TelegramId = "telegram";

    /// <summary>A package's digest list, beside the package and outside it.</summary>
    private static string Digests(string id) => $"plugins/{id}.digests";

    public static TheoryData<string, string> ManifestInvocations()
    {
        var data = new TheoryData<string, string>();
        foreach (var workflow in (string[][])[Ci, Release])
        {
            foreach (var line in Read(workflow).Split('\n'))
            {
                var trimmed = line.Trim();
                var start = trimmed.IndexOf("./.github/scripts/Write-Manifest.ps1", StringComparison.Ordinal);
                if (start >= 0)
                {
                    data.Add(workflow[^1], trimmed[start..]);
                }
            }
        }

        return data;
    }

    /// <summary>
    /// What the version job of a run that runs every check hands on as <c>rids</c>: every platform its deciding step is
    /// given, comma-separated, as the runner puts it into a line — which PowerShell then reads as an array, as it reads a
    /// list written out.
    /// </summary>
    private static string FullRunRids()
    {
        var deciding = Assert.IsType<YamlSequenceNode>(Node(Mapping(Mapping(Parse(Read(Ci)), "jobs"), "version"), "steps")).Children.Cast<YamlMappingNode>()
            .Single(step => Scalar(step, "id") == "legs");
        using var platforms = JsonDocument.Parse(Scalar(Mapping(deciding, "env"), "PLATFORMS")!);
        return string.Join(',', platforms.RootElement.EnumerateArray().Select(leg => leg.GetProperty("rid").GetString()));
    }

    /// <summary>
    /// The runner's own substitutions, and nothing else: the workflow expressions that carry the version and a full
    /// run's platforms, the environment variables the jobs export, the directory the artifacts arrive in, and the
    /// checkout a path in the repository is read from.
    /// </summary>
    private static string ExpandForThisMachine(string invocation, string directory)
    {
        var script = Path.Combine(RepositoryRoot(), Path.Combine(Script));
        var expanded = invocation
            .Replace("./.github/scripts/Write-Manifest.ps1", $"& '{script}'", StringComparison.Ordinal)
            .Replace("${{ needs.version.outputs.version }}", "0.2.0", StringComparison.Ordinal)
            .Replace("${{ needs.version.outputs.rids }}", FullRunRids(), StringComparison.Ordinal)
            .Replace("$env:VERSION", "0.2.0", StringComparison.Ordinal)
            .Replace("$env:NEXT", "0.2.1", StringComparison.Ordinal)
            .Replace("$env:RID", ReleaseAssets.Rids[0], StringComparison.Ordinal);

        // -PluginDigests names paths in the checkout the job runs in, a list PowerShell parses as an array; here they are in
        // this repository, and the list stays an array.
        expanded = System.Text.RegularExpressions.Regex.Replace(
            expanded,
            @"-PluginDigests (\S+)",
            list => "-PluginDigests " + string.Join(',', list.Groups[1].Value.Split(',').Select(path => $"'{Path.Combine(RepositoryRoot(), path)}'")));

        // -Directory names where a runner downloaded the artifacts to; here it is the staged tree this test made.
        return System.Text.RegularExpressions.Regex.Replace(
            expanded,
            @"-Directory \S+",
            $"-Directory '{directory}'");
    }

    /// <summary>
    /// The end-to-end job updates a real installation between two real versions, and asserts the one that came
    /// up. Everything about it that could quietly stop proving anything is read here.
    /// </summary>
    /// <remarks>
    /// <para>
    /// With one version on both sides, <c>--version</c> cannot tell a completed swap from an applier that did
    /// nothing: the health check passes either way, which makes the whole job theatre. So the job publishes the
    /// <em>next</em> patch version and updates to that — and the version it takes for the installed side is the
    /// artifact the platform job already published, so only one extra publish is paid for.
    /// </para>
    /// <para>
    /// The other three things that would make it theatre: a database prepared by copying a file instead of
    /// migrating one (the triggers live in the migrations, so a copied file is not an older database), a feed
    /// served from anywhere but this machine, and a runtime started before the update — which would migrate the
    /// prepared database itself and leave the new version nothing to back up, so the acceptance criterion this
    /// job exists for would never be exercised.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_end_to_end_job_updates_between_two_versions_and_asserts_the_one_that_ran()
    {
        var job = Job(Read(Ci), "update-end-to-end");

        // Two versions, and the installed one is the artifact the platform job already made: one extra
        // publish, through the same action a release packages with.
        Assert.Contains("download-artifact", job, StringComparison.Ordinal);
        Assert.Contains("./.github/actions/package", job, StringComparison.Ordinal);
        Assert.Contains("steps.next.outputs.next", job, StringComparison.Ordinal);

        // A real older database, made by EF's own migrator, with the connection given so that nothing is
        // written into the working directory instead.
        Assert.Contains("dotnet ef database update", job, StringComparison.Ordinal);
        Assert.Contains("--connection", job, StringComparison.Ordinal);

        // A feed that is this machine and nothing else.
        Assert.Contains("127.0.0.1", job, StringComparison.Ordinal);
        Assert.DoesNotContain("github.com", job, StringComparison.Ordinal);

        // The update is applied by the installed executable, and every stage is asserted by name.
        Assert.Contains("update apply", job, StringComparison.Ordinal);
        foreach (var step in new[] { "staged", "drained", "stopped", "kept", "swapped", "started", "healthy" })
        {
            Assert.Contains($"'{step}'", job, StringComparison.Ordinal);
        }

        // And the assertions a no-op cannot produce: the old file kept, the new file installed by identity, and
        // the staged copy gone.
        Assert.Contains("previous", job, StringComparison.Ordinal);
        Assert.Contains("Get-FileHash", job, StringComparison.Ordinal);
    }

    /// <summary>
    /// The install job runs the script the way the one-liner does — as text, in the caller's own session — and
    /// checks that it leaves nothing of itself behind there.
    /// </summary>
    /// <remarks>
    /// <c>irm … | iex</c> runs the file's text in the scope that typed it, so a function the script defines is
    /// still defined in that shell afterwards. Running the script as a file, which is what every other check in
    /// this job does, gives it a scope of its own and cannot see that at all.
    /// </remarks>
    [Fact]
    public void The_install_job_runs_the_script_the_way_the_one_liner_does()
    {
        var job = Job(Read(Ci), "install-ps1");

        Assert.True(Runs(job, "Invoke-Expression"), $"the install job never runs the script as text, so it cannot see what the one-liner leaves behind:\n{job}");
        Assert.Contains("Get-Command Install-Jason", job, StringComparison.Ordinal);
    }

    /// <summary>
    /// The end-to-end job serves its own feed and waits for it, and a wait that runs out of tries says so.
    /// </summary>
    /// <remarks>
    /// The wait fell through in silence: fifty tries, then on to the next step whatever the answer. A feed that
    /// never came up then surfaced three steps later as an update that could not reach it, which is a failure
    /// somebody has to diagnose twice — once to find out that the update was fine and once to find out that
    /// nothing was listening.
    /// </remarks>
    [Fact]
    public void The_feed_the_end_to_end_serves_is_waited_for_and_the_wait_can_run_out()
    {
        var job = Job(Read(Ci), "update-end-to-end");

        var waiting = job.IndexOf("Invoke-WebRequest", StringComparison.Ordinal);
        Assert.True(waiting > 0, $"the end-to-end job no longer waits for the feed it serves:\n{job}");

        // The statement that gives up, not a word about giving up: what follows the wait has to be a throw that
        // happens only when the wait ran out.
        var after = job[waiting..];
        Assert.Matches(@"if \(-not \$ready\) \{[^}\n]*throw", after);
    }

    /// <summary>
    /// And it puts the update back again, from the executable a person would type — which is the one thing about
    /// a rollback no test in this repository can reach.
    /// </summary>
    /// <remarks>
    /// `jason update rollback`, typed from the PATH the way every refusal message says to, runs the executable
    /// the update installed: the file the rollback has to replace is the process doing the replacing. Windows
    /// will not let a running image be written over — only renamed — and the unit tests cannot see that, because
    /// a fake installation is a file and not a process. Here it is a process.
    /// </remarks>
    [Fact]
    public void The_end_to_end_job_rolls_the_update_back_from_the_installed_executable()
    {
        var job = Job(Read(Ci), "update-end-to-end");

        // Where it is run, not where it is named: this step's own comment says `jason update rollback` four
        // lines above the invocation.
        var rollback = At(job, "update rollback");
        Assert.True(rollback > 0, "the end-to-end job does not run `update rollback`");

        // Rolled back by the installed executable, not by anything built beside it.
        var invocation = job[rollback..job.IndexOf('\n', rollback)];
        Assert.Contains("$env:install update rollback", invocation, StringComparison.Ordinal);

        // Against the runtime the update left running. A rollback decides whether restoring a database is safe
        // by asking that runtime what has been recorded since; a job that stopped it first would be asserting
        // the refusal rather than the rollback — which is what happened the first time this step ran.
        var before = job[..rollback];
        Assert.False(
            Runs(before, "runtime stop"),
            "the end-to-end job stops the runtime before it rolls back, so it would assert the refusal rather than the rollback");

        // And what it must leave behind: the old version serving, the kept executable consumed, and the
        // database from before the update.
        // Each of these is the expression the check is made of rather than a word the step also uses about
        // itself: a guard that matched the prose beside a check would survive the check being deleted.
        var after = job[rollback..];
        Assert.Contains("Join-Path $update 'previous'", after, StringComparison.Ordinal);
        Assert.Contains("runtime_version", after, StringComparison.Ordinal);

        // That last one is asked of what the restored runtime had to do, because the file cannot be inspected
        // - the rollback starts a runtime, which opens the database at once - and because nothing can be
        // written into the migrated database first: work recorded after an update is exactly what makes a
        // rollback refuse to restore one. So: the restored version found the database a migration behind and
        // migrated it again, and the backup it took is not the backup the update took.
        Assert.Contains("newly_applied", after, StringComparison.Ordinal);
        Assert.Contains("$env:backup", after, StringComparison.Ordinal);

        // And on Windows, that the image which ran the rollback was moved aside rather than written over.
        Assert.Contains("Join-Path $update 'replaced'", after, StringComparison.Ordinal);
        Assert.True(
            job.IndexOf("\"backup=", StringComparison.Ordinal) is var kept && kept > 0 && kept < rollback,
            "the end-to-end job never remembers the backup the update took, so it cannot tell it from the one the rollback's own start takes");
    }

    /// <summary>
    /// The runtime is not started before the update. This is the one thing about the job that would pass every
    /// other assertion here and still prove nothing: a runtime started first migrates the prepared database
    /// itself, so the new version's first start has nothing to back up and nothing to migrate.
    /// </summary>
    [Fact]
    public void The_end_to_end_job_leaves_the_installed_runtime_stopped_until_the_update()
    {
        var job = Job(Read(Ci), "update-end-to-end");
        var apply = job.IndexOf("update apply", StringComparison.Ordinal);
        Assert.True(apply > 0, "the end-to-end job does not apply an update");

        var before = job[..apply];
        Assert.DoesNotContain("runtime start", before, StringComparison.Ordinal);
        Assert.DoesNotContain("runtime run", before, StringComparison.Ordinal);

        // And what it asserts afterwards is that this update's own start migrated: newly_applied and a backup.
        var after = job[apply..];
        Assert.Contains("newly_applied", after, StringComparison.Ordinal);
        Assert.Contains("backup_file", after, StringComparison.Ordinal);
    }

    /// <summary>
    /// The update job installs one version and updates to the next, built on another .NET runtime than the installed one,
    /// every run, and says so in the files: what the installed executable carries is read out of it, the next version is
    /// published on a runtime one patch away, and the executable that publish made is read again before anything is
    /// installed.
    /// </summary>
    /// <remarks>
    /// The executable is a single file that reads its .NET libraries out of itself as it first needs them, and an apply
    /// runs from the file it replaces. Built on one runtime, both files kept every library at the same offset, and an
    /// apply that read one out of the new file after the swap passed here; a release built on another runtime than the
    /// installation it updated failed on Linux. A release and every platform job still publish on the SDK's own runtime.
    /// </remarks>
    [Fact]
    public void The_end_to_end_job_updates_to_a_build_on_another_dotnet_runtime()
    {
        var jobs = Mapping(Parse(Read(Ci)), "jobs");
        var steps = Assert.IsType<YamlSequenceNode>(Node(Mapping(jobs, "update-end-to-end"), "steps")).Children.Cast<YamlMappingNode>().ToList();

        var decided = steps.FindIndex(step => Scalar(step, "id") == "next");
        var packaged = steps.FindIndex(step => Scalar(step, "uses") == "./.github/actions/package");
        var held = steps.FindIndex(step => Scalar(step, "run") is { } run && run.Contains("$env:EXECUTABLE", StringComparison.Ordinal) && run.Contains(RuntimeScript, StringComparison.Ordinal));
        Assert.True(decided >= 0 && packaged > decided && held > packaged, $"the job does not read the installed runtime, publish on another and read the result, in that order: {decided}, {packaged}, {held}");

        // The installed side is read, and the other runtime is its own output; the publish is told it.
        var decide = Scalar(steps[decided], "run")!;
        Assert.Contains(RuntimeScript, decide, StringComparison.Ordinal);
        Assert.Contains("\"runtime=", decide, StringComparison.Ordinal);
        Assert.Contains("\"installed-runtime=", decide, StringComparison.Ordinal);
        Assert.Equal("${{ steps.next.outputs.runtime }}", Scalar(Mapping(steps[packaged], "with"), "runtime-version"));

        // The executable that publish made is read before it is served, and refused on the installed side's runtime.
        var env = Mapping(steps[held], "env");
        Assert.Equal("${{ steps.package.outputs.executable }}", Scalar(env, "EXECUTABLE"));
        Assert.Equal("${{ steps.next.outputs.installed-runtime }}", Scalar(env, "INSTALLED"));
        Assert.Matches(@"if \(\$next -ne \$env:RUNTIME\) \{ throw", Scalar(steps[held], "run")!);
        Assert.Matches(@"if \(\$next -eq \$env:INSTALLED\) \{ throw", Scalar(steps[held], "run")!);
        Assert.True(held < steps.FindIndex(step => Scalar(step, "name") == "Serve that version from a feed on this machine"), "the next version is served before its runtime is read");

        // And only there: everything else that packages publishes on the SDK's own runtime.
        foreach (var workflow in (string[][])[Ci, Release])
        {
            var others = Mapping(Parse(Read(workflow)), "jobs").Children
                .Where(job => job.Key.ToString() != "update-end-to-end")
                .SelectMany(job => Node((YamlMappingNode)job.Value, "steps") is YamlSequenceNode sequence ? sequence.Children.Cast<YamlMappingNode>() : [])
                .Where(step => Scalar(step, "uses") == "./.github/actions/package");
            Assert.All(others, step => Assert.Null(Scalar(Mapping(step, "with"), "runtime-version")));
        }

        // The action hands the input to the publish as the runtime to build on, and nothing when it is not given.
        var action = Read(Action);
        Assert.Contains("RUNTIME_VERSION: ${{ inputs.runtime-version }}", action, StringComparison.Ordinal);
        Assert.Contains("$runtime = @(if ($env:RUNTIME_VERSION) { \"-p:RuntimeFrameworkVersion=$env:RUNTIME_VERSION\" })", action, StringComparison.Ordinal);
        var publishes = action.Split('\n').Where(line => line.TrimStart().StartsWith("dotnet publish ", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(publishes);
        Assert.All(publishes, publish => Assert.Contains(" @runtime ", publish, StringComparison.Ordinal));
    }

    /// <summary>
    /// The runtime an executable carries is read out of the file, from the dependency manifest a single-file publish
    /// bundles uncompressed, and a file that names none, or two, is refused rather than guessed at.
    /// </summary>
    [Fact]
    public void The_runtime_an_executable_carries_is_read_out_of_the_file()
    {
        using var tree = new TempTree();
        static byte[] Bytes(string text) => [0x7F, 0x45, 0x4C, 0x46, 0x00, .. Encoding.ASCII.GetBytes(text), 0x00, 0xFF];
        var script = Path.Combine([RepositoryRoot(), .. RuntimeScriptPath]);

        var one = Path.Combine(tree.Root, "one");
        File.WriteAllBytes(one, Bytes("\"runtimepack.Microsoft.NETCore.App.Runtime.linux-x64/10.0.13\": {} \"runtimepack.Microsoft.NETCore.App.Runtime.linux-x64/10.0.13\": {\"type\": \"runtimepack\"} \"runtimepack.Microsoft.AspNetCore.App.Runtime.linux-x64/10.0.12\""));
        var read = Pwsh(NoEnvironment, "-File", script, "-Executable", one);
        Assert.True(read.Exit == 0, read.Stderr);
        Assert.Equal("10.0.13", read.Stdout.Trim());

        var none = Path.Combine(tree.Root, "none");
        File.WriteAllBytes(none, Bytes("a file that names no runtime"));
        Assert.NotEqual(0, Pwsh(NoEnvironment, "-File", script, "-Executable", none).Exit);

        var two = Path.Combine(tree.Root, "two");
        File.WriteAllBytes(two, Bytes("runtimepack.Microsoft.NETCore.App.Runtime.linux-x64/10.0.13 runtimepack.Microsoft.NETCore.App.Runtime.linux-x64/10.0.12"));
        var refused = Pwsh(NoEnvironment, "-File", script, "-Executable", two);
        Assert.NotEqual(0, refused.Exit);
        Assert.Contains("10.0.12, 10.0.13", refused.Stderr, StringComparison.Ordinal);
    }

    private const string RuntimeScript = "./.github/scripts/Read-RuntimePack.ps1";

    private static readonly string[] RuntimeScriptPath = [".github", "scripts", "Read-RuntimePack.ps1"];

    /// <summary>
    /// Every job of either workflow installs .NET through this repository's own action, never through
    /// <c>actions/setup-dotnet</c>, and only once its checkout has put that action on the runner.
    /// </summary>
    /// <remarks>
    /// The marketplace action reads Microsoft's older download origin, which refused for a day and took every job with it;
    /// the repository's action installs from the CDN. A job that still named the marketplace action would go back to that
    /// origin in silence, and a local action used before the checkout is a path to nothing.
    /// </remarks>
    [Fact]
    public void Every_job_installs_dotnet_through_the_repository_s_action_after_its_checkout()
    {
        var uses = 0;
        foreach (var workflow in (string[][])[Ci, Release])
        {
            foreach (var (name, job) in Mapping(Parse(Read(workflow)), "jobs").Children.Select(pair => (pair.Key.ToString(), (YamlMappingNode)pair.Value)))
            {
                if (Node(job, "steps") is not YamlSequenceNode sequence)
                {
                    continue;
                }

                var steps = sequence.Children.Cast<YamlMappingNode>().ToList();
                Assert.DoesNotContain(steps, step => Scalar(step, "uses")?.StartsWith("actions/setup-dotnet@", StringComparison.Ordinal) == true);

                var checkout = steps.FindIndex(step => Scalar(step, "uses")?.StartsWith("actions/checkout@", StringComparison.Ordinal) == true);
                for (var at = 0; at < steps.Count; at++)
                {
                    if (Scalar(steps[at], "uses") == "./.github/actions/setup-dotnet")
                    {
                        uses++;
                        Assert.True(checkout >= 0 && checkout < at, $"{workflow[^1]}: the job '{name}' sets up .NET at step {at} and checks the repository out at step {checkout}");
                    }
                }
            }
        }

        Assert.True(uses >= 7, $"only {uses} job(s) set up .NET through the repository's action");
    }

    /// <summary>
    /// The action pins no SDK: its default is empty, which it refuses, and given the word <c>latest</c> it installs the
    /// newest SDK the 10.0 channel has released, read from the release metadata on Microsoft's CDN, through the install
    /// scripts at the addresses Microsoft publishes; given an exact version it installs that one from the same CDN; and
    /// whichever it is, a version that is not a 10.0 release stops the step before anything is installed.
    /// </summary>
    /// <remarks>
    /// A pin once held every build to one runtime patch, while the update into the release whose applier survives a
    /// change of patch was still performed by a build without that applier. Updates performed by 0.19.0 or a later build
    /// survive it, and the update job proves that on every run that builds a platform, the version pull request aside, by
    /// building its two sides on two patches, so no SDK is written down here: each run resolves the channel's newest once,
    /// in its version job.
    /// </remarks>
    [Fact]
    public void The_dotnet_action_pins_no_sdk_and_installs_the_channel_s_newest_from_the_cdn()
    {
        var action = Parse(Read(SetupDotnet));
        var asked = Scalar(Mapping(Mapping(action, "inputs"), "sdk-version"), "default") ?? string.Empty;
        Assert.True(asked.Length == 0, $"the action's sdk-version defaults to '{asked}', rather than to nothing, which the action refuses");

        var installs = Assert.IsType<YamlSequenceNode>(Node(Mapping(action, "runs"), "steps")).Children.Cast<YamlMappingNode>()
            .Where(step => Scalar(step, "name") == "Install the .NET SDK")
            .ToDictionary(step => Scalar(step, "shell")!, step => Scalar(step, "run")!, StringComparer.Ordinal);
        Assert.Equal(["bash", "pwsh"], installs.Keys.Order(StringComparer.Ordinal));

        foreach (var run in installs.Values)
        {
            Assert.Contains("https://builds.dotnet.microsoft.com/dotnet", run, StringComparison.Ordinal);
            Assert.Contains("$feed/release-metadata/10.0/releases.json", run, StringComparison.Ordinal);
            Assert.Contains("latest-sdk", run, StringComparison.Ordinal);
        }

        var bash = installs["bash"];
        AssertResolvedOnLatest(bash, "if [[ \"$SDK_VERSION\" == latest ]]; then");
        Assert.Contains("https://dot.net/v1/dotnet-install.sh", bash, StringComparison.Ordinal);
        Assert.Contains("--version \"$SDK_VERSION\" --azure-feed \"$feed\"", bash, StringComparison.Ordinal);
        AssertCheckedBetween(bash, Regex.Match(bash, @"\[\[ ""\$SDK_VERSION"" =~ \^10\\\.0\\\.\[0-9\]\+\$ \]\] \|\| \{[^}\n]*exit 1; \}"), "dotnet-install.sh");

        var pwsh = installs["pwsh"];
        AssertResolvedOnLatest(pwsh, "if ($env:SDK_VERSION -ceq 'latest') {");
        Assert.Contains("https://dot.net/v1/dotnet-install.ps1", pwsh, StringComparison.Ordinal);
        Assert.Contains("-Version $env:SDK_VERSION -AzureFeed $feed", pwsh, StringComparison.Ordinal);
        AssertCheckedBetween(pwsh, Regex.Match(pwsh, @"if \(\$env:SDK_VERSION -cnotmatch '\^10\\\.0\\\.\[0-9\]\+\\z'\) \{ throw"), "dotnet-install.ps1");

        // The release metadata is read only when the step was asked for latest.
        static void AssertResolvedOnLatest(string run, string asked)
        {
            var at = run.IndexOf(asked, StringComparison.Ordinal);
            Assert.True(at >= 0, $"the step does not read the release metadata on '{asked}':\n{run}");
            Assert.True(at < run.IndexOf("$feed/release-metadata/10.0/releases.json", StringComparison.Ordinal), $"the step reads the release metadata before it asks whether it was given latest:\n{run}");
        }

        // The version is checked after the metadata may have named it and before the script that installs it is fetched.
        static void AssertCheckedBetween(string run, Match check, string script)
        {
            Assert.True(check.Success, $"the step does not refuse a version that is not a 10.0 release:\n{run}");
            Assert.True(
                run.IndexOf("latest-sdk", StringComparison.Ordinal) < check.Index && check.Index < run.IndexOf(script, StringComparison.Ordinal),
                $"the step checks the version outside the metadata read and the install:\n{run}");
        }
    }

    /// <summary>
    /// The action refuses an empty <c>sdk-version</c>, in both install steps, before it fetches anything: before the
    /// release metadata is read and before the install script is fetched. Both steps stop with the same sentence, which
    /// names the input and says where the SDK comes from: a workflow's version job resolves it with <c>latest</c>, and
    /// every other job passes that job's <c>sdk</c> output.
    /// </summary>
    /// <remarks>
    /// Empty is also what a job reads from an output that is not there. Were it read as the channel's newest, a job handed
    /// no SDK would resolve one of its own at its own start, a second SDK in one run with nothing to say so; refused, the
    /// job fails where it is.
    /// </remarks>
    [Fact]
    public void The_dotnet_action_refuses_an_empty_sdk_version_before_it_fetches_anything()
    {
        var installs = Assert.IsType<YamlSequenceNode>(Node(Mapping(Parse(Read(SetupDotnet)), "runs"), "steps")).Children.Cast<YamlMappingNode>()
            .Where(step => Scalar(step, "name") == "Install the .NET SDK")
            .ToDictionary(step => Scalar(step, "shell")!, step => Scalar(step, "run")!, StringComparer.Ordinal);
        Assert.Equal(["bash", "pwsh"], installs.Keys.Order(StringComparer.Ordinal));

        var bash = installs["bash"];
        var saidByBash = AssertRefusedBeforeAnyFetch(
            bash,
            Regex.Match(bash, @"\[\[ -n ""\$\{SDK_VERSION:-\}"" \]\] \|\| \{ echo ""(?<said>[^""\n]+)"" >&2; exit 1; \}"),
            ["curl ", "dotnet-install.sh"]);
        var pwsh = installs["pwsh"];
        var saidByPwsh = AssertRefusedBeforeAnyFetch(
            pwsh,
            Regex.Match(pwsh, @"if \(-not \$env:SDK_VERSION\) \{ throw ""(?<said>[^""\n]+)"" \}"),
            ["Invoke-RestMethod", "Invoke-WebRequest", "dotnet-install.ps1"]);

        Assert.Equal(saidByBash, saidByPwsh);
        foreach (var named in (string[])["sdk-version", "version job", "sdk output"])
        {
            Assert.Contains(named, saidByBash, StringComparison.Ordinal);
        }

        // The empty value is tested before the step first names a command that fetches, or the script it fetches.
        static string AssertRefusedBeforeAnyFetch(string run, Match refusal, string[] fetching)
        {
            Assert.True(refusal.Success, $"the step does not refuse an empty sdk-version:\n{run}");
            foreach (var fetch in fetching)
            {
                var at = run.IndexOf(fetch, StringComparison.Ordinal);
                Assert.True(at >= 0, $"the step never names '{fetch}':\n{run}");
                Assert.True(refusal.Index < at, $"the step reaches '{fetch}' before it refuses an empty sdk-version:\n{run}");
            }

            return refusal.Groups["said"].Value;
        }
    }

    /// <summary>
    /// The action says which SDK it installed, the exact version, as its <c>sdk-version</c> output: each install step,
    /// the bash one and the pwsh one, writes the version once its install script has run, and the output reads
    /// whichever of the two ran on the runner.
    /// </summary>
    /// <remarks>
    /// Given latest the action decides the SDK itself, from the release metadata, and nothing else in the job
    /// knows what it decided. The version job hands the output on, so that every other job of the run installs that SDK.
    /// </remarks>
    [Fact]
    public void The_dotnet_action_says_which_sdk_it_installed()
    {
        var action = Parse(Read(SetupDotnet));
        var installs = Assert.IsType<YamlSequenceNode>(Node(Mapping(action, "runs"), "steps")).Children.Cast<YamlMappingNode>()
            .Where(step => Scalar(step, "name") == "Install the .NET SDK")
            .ToList();
        Assert.Equal(["bash", "pwsh"], installs.Select(step => Scalar(step, "shell")));

        var ids = installs.Select(step => Scalar(step, "id")).ToList();
        Assert.All(ids, id => Assert.False(string.IsNullOrEmpty(id), "an install step has no id its output can be read by"));
        var outputs = Node(action, "outputs") as YamlMappingNode;
        var said = outputs is null || Node(outputs, "sdk-version") is not YamlMappingNode output ? null : Scalar(output, "value");
        Assert.Equal($"${{{{ steps.{ids[0]}.outputs.sdk-version || steps.{ids[1]}.outputs.sdk-version }}}}", said);

        AssertWrittenAfterTheInstall(Scalar(installs[0], "run")!, "echo \"sdk-version=$SDK_VERSION\" >> \"$GITHUB_OUTPUT\"", "bash \"$script\" --version");
        AssertWrittenAfterTheInstall(Scalar(installs[1], "run")!, "\"sdk-version=$env:SDK_VERSION\" >> $env:GITHUB_OUTPUT", "& $script -Version");

        // The version written is the one the install script was given, so it is written once that script has run.
        static void AssertWrittenAfterTheInstall(string run, string written, string install)
        {
            var at = run.IndexOf(written, StringComparison.Ordinal);
            Assert.True(at >= 0, $"the step does not write the SDK it installed as '{written}':\n{run}");
            var installed = run.IndexOf(install, StringComparison.Ordinal);
            Assert.True(installed >= 0 && installed < at, $"the step writes the SDK before its install script has run:\n{run}");
        }
    }

    /// <summary>
    /// One run, one SDK. Each workflow's version job installs .NET through the action asking for <c>latest</c>, the
    /// channel's newest, and hands on the exact SDK it installed as its <c>sdk</c> output; every other use of the action
    /// passes that output, so no two jobs of one run build on two SDKs, even when the channel releases one while the run
    /// is going.
    /// </summary>
    [Fact]
    public void Every_job_of_a_run_builds_on_the_sdk_its_version_job_resolved()
    {
        var passing = 0;
        foreach (var workflow in (string[][])[Ci, Release])
        {
            var jobs = Mapping(Parse(Read(workflow)), "jobs");

            var version = Mapping(jobs, "version");
            var resolving = Assert.Single(SettingUpDotnet(version));
            var asked = Node(resolving, "with") is YamlMappingNode given ? Scalar(given, "sdk-version") : null;
            Assert.True(asked == "latest", $"{workflow[^1]}: the version job asks the action for '{asked}' rather than latest, the channel's newest");
            var id = Scalar(resolving, "id");
            Assert.False(string.IsNullOrEmpty(id), $"{workflow[^1]}: the version job's use of the action has no id its output can be read by");
            var outputs = Node(version, "outputs") as YamlMappingNode;
            Assert.Equal($"${{{{ steps.{id}.outputs.sdk-version }}}}", outputs is null ? null : Scalar(outputs, "sdk"));

            foreach (var (name, job) in jobs.Children.Select(pair => (pair.Key.ToString(), (YamlMappingNode)pair.Value)).Where(pair => pair.Item1 != "version"))
            {
                foreach (var use in SettingUpDotnet(job))
                {
                    passing++;
                    var passed = Node(use, "with") is YamlMappingNode told ? Scalar(told, "sdk-version") : null;
                    Assert.True(
                        passed == "${{ needs.version.outputs.sdk }}",
                        $"{workflow[^1]}: the job '{name}' sets up .NET with '{passed}', not the SDK its version job resolved");
                }
            }
        }

        Assert.True(passing >= 5, $"only {passing} use(s) of the action outside a version job");
    }

    /// <summary>
    /// Every job that sets up .NET needs its workflow's version job directly: a job reads the outputs of the jobs it
    /// needs and of no other, so one that reached the version job only through another job would read no SDK there, and
    /// its install would stop on the empty version.
    /// </summary>
    [Fact]
    public void Every_job_that_sets_up_dotnet_needs_its_version_job_directly()
    {
        var checkedJobs = 0;
        foreach (var workflow in (string[][])[Ci, Release])
        {
            foreach (var (name, job) in Mapping(Parse(Read(workflow)), "jobs").Children.Select(pair => (pair.Key.ToString(), (YamlMappingNode)pair.Value)))
            {
                if (name == "version" || !SettingUpDotnet(job).Any())
                {
                    continue;
                }

                checkedJobs++;
                List<string> needs = Node(job, "needs") switch
                {
                    YamlScalarNode one => [one.Value!],
                    YamlSequenceNode many => [.. many.Children.Select(need => need.ToString())],
                    _ => [],
                };
                Assert.True(needs.Contains("version"), $"{workflow[^1]}: the job '{name}' sets up .NET and needs {(needs.Count == 0 ? "no job" : string.Join(", ", needs))}, not the version job");
            }
        }

        Assert.True(checkedJobs >= 5, $"only {checkedJobs} job(s) outside a version job set up .NET");
    }

    private static IEnumerable<YamlMappingNode> SettingUpDotnet(YamlMappingNode job) =>
        Node(job, "steps") is YamlSequenceNode steps
            ? steps.Children.Cast<YamlMappingNode>().Where(step => Scalar(step, "uses") == "./.github/actions/setup-dotnet")
            : [];

    private static readonly string[] SetupDotnet = [".github", "actions", "setup-dotnet", "action.yml"];
}
