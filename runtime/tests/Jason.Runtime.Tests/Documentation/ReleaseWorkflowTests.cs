using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using Jason.Contracts.Update;
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
public class ReleaseWorkflowTests
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

        // Q5: every archive carries a licence beside the executable, and it is the runtime's own (the test below).
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
        var project = Read(["runtime", "src", "Jason.App", "Jason.App.csproj"]);
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
    /// The runtime's licence is <c>LICENSE-RUNTIME.md</c> at the root, the place a release publishes it from, and
    /// the repository's <c>LICENSE</c> names that file in the paragraph that scopes the MIT licence. That licence
    /// covers the binaries and grants no right to the source, so the paragraph says the source is licensed to no one.
    /// </summary>
    [Fact]
    public void The_runtime_s_licence_is_at_the_root_and_the_scope_paragraph_names_it()
    {
        var root = RepositoryRoot();
        Assert.True(File.Exists(Path.Combine(root, "LICENSE-RUNTIME.md")), "LICENSE-RUNTIME.md is not at the root.");
        Assert.False(File.Exists(Path.Combine(root, "runtime", "LICENSE")), "runtime/LICENSE is still there beside it.");

        var licence = File.ReadAllText(Path.Combine(root, "LICENSE"));
        var scope = licence[..Assert.Single(Regex.Matches(licence, "^MIT License$", RegexOptions.Multiline)).Index];
        Assert.Contains("(see LICENSE-RUNTIME.md)", scope, StringComparison.Ordinal);
        Assert.Contains("whose source is proprietary to ReplyApp Inc.", scope.ReplaceLineEndings(" "), StringComparison.Ordinal);
        Assert.DoesNotContain("runtime/LICENSE", scope, StringComparison.Ordinal);
    }

    /// <summary>
    /// The archive carries the runtime's own licence under the name <c>LICENSE</c>, not the repository's: the
    /// repository's MIT licence covers what lies outside <c>runtime/</c>, and the executable is the runtime.
    /// </summary>
    /// <remarks>
    /// The action's own step is read out of the action and run against a tree of this test's own that holds both
    /// files, so the test sees which of the two lands beside the executable rather than what the step mentions.
    /// </remarks>
    [Fact]
    public void The_archive_carries_the_runtime_s_licence_rather_than_the_repository_s()
    {
        var staging = Assert.IsType<YamlSequenceNode>(Node(Mapping(Parse(Read(Action)), "runs"), "steps")).Children.Cast<YamlMappingNode>()
            .Single(step => Scalar(step, "name") == "Put LICENSE and THIRD-PARTY-NOTICES beside the executable");

        using var tree = new TempTree();
        File.WriteAllText(Path.Combine(tree.Root, "LICENSE"), "the repository's licence");
        File.WriteAllText(Path.Combine(tree.Root, "LICENSE-RUNTIME.md"), "the runtime's licence");
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
        Assert.Equal("the runtime's licence", File.ReadAllText(Path.Combine(package, "LICENSE")));
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
        File.WriteAllText(Path.Combine(tree.Root, "LICENSE-RUNTIME.md"), "the runtime's licence");
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
        Assert.Equal("the runtime's licence", entries["LICENSE"]);
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

        var project = File.ReadAllText(Path.Combine(RepositoryRoot(), "runtime", "src", "Jason.App", "Jason.App.csproj"));
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
        Assert.Contains("dotnet msbuild runtime/src/Jason.App/Jason.App.csproj -getProperty:VersionPrefix", asking, StringComparison.Ordinal);
        Assert.Contains("\"release=$release\" >> $env:GITHUB_OUTPUT", asking, StringComparison.Ordinal);

        // One job, on Linux and nowhere else, that is stamped with it for the build and the tests alike.
        var stamped = Mapping(jobs, "release-stamp");
        Assert.Equal("ubuntu-latest", Scalar(stamped, "runs-on"));
        Assert.Null(Node(stamped, "strategy"));
        Assert.Equal("version", Scalar(stamped, "needs"));
        var steps = Steps(stamped);
        var runs = steps.Select(step => Scalar(step, "run")).OfType<string>().ToList();
        Assert.Single(runs, run => run.StartsWith("dotnet build runtime/Jason.slnx --configuration Release", StringComparison.Ordinal)
            && run.Contains("-p:Version=${{ needs.version.outputs.release }}", StringComparison.Ordinal));
        var test = Assert.Single(runs, run => run.StartsWith("dotnet test", StringComparison.Ordinal));
        Assert.StartsWith("dotnet test --solution runtime/Jason.slnx --configuration Release --no-build", test, StringComparison.Ordinal);
        Assert.Contains("-p:Version=${{ needs.version.outputs.release }}", test, StringComparison.Ordinal);

        // The whole suite, not a part of it: the three tests that failed were not where anybody would have looked.
        Assert.DoesNotContain("filter", test, StringComparison.Ordinal);

        // And it proves it was stamped, from what was built: a job that quietly lost the stamp would pass as -dev.
        var check = Assert.Single(steps, step => Scalar(step, "run")?.Contains("FileVersionInfo", StringComparison.Ordinal) == true);
        Assert.Equal("${{ needs.version.outputs.release }}", Scalar(Mapping(check, "env"), "RELEASE"));
        Assert.Contains("throw", Scalar(check, "run"), StringComparison.Ordinal);
        Assert.True(
            steps.IndexOf(check) < steps.FindIndex(step => Scalar(step, "run") == test),
            "The stamp is checked after the tests have already run under it.");

        // Every other job keeps the -dev version, and nothing stamps VersionPrefix itself: that would leave the suffix behind.
        foreach (var name in jobs.Children.Keys.Select(key => key.ToString()).Where(name => name is not "version" and not "release-stamp"))
        {
            Assert.DoesNotContain("outputs.release", Job(ci, name), StringComparison.Ordinal);
        }

        Assert.DoesNotContain("-p:VersionPrefix", ci, StringComparison.Ordinal);
    }

    /// <summary>
    /// The checks ci.yml reports, its matrix legs expanded, are exactly the eleven this repository's ruleset requires
    /// before a pull request merges. A renamed job is a required check that never reports, and every pull request
    /// would wait for it: a job is renamed here only together with the ruleset. The job that reports a check a rule
    /// skipped reports it under a name another job's leg has, which CiRunsTests holds, so it adds no name here.
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
        ];

        // Reported and not required by the ruleset: none today. Making one required is a ruleset change, made together
        // with moving its name up.
        string[] optional = [];

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
    /// whole list that job's deciding step is given, which is what a full run runs.
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
        using var all = JsonDocument.Parse(Scalar(Mapping(deciding, "env"), decided.Groups[1].Value.ToUpperInvariant())!);
        return [.. all.RootElement.EnumerateArray()
            .Select(leg => leg.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.GetString()!, StringComparer.Ordinal))];
    }

    // --- release.yml -------------------------------------------------------------------------------------

    [Fact]
    public void A_release_is_built_from_a_tag_and_a_draft_from_a_button()
    {
        var triggers = Mapping(Parse(Read(Release)), "on");

        var tags = Assert.IsType<YamlSequenceNode>(Node(Mapping(triggers, "push"), "tags"));
        Assert.Contains("v*", tags.Children.Select(tag => tag.ToString()));
        Assert.Contains("version", Mapping(Mapping(triggers, "workflow_dispatch"), "inputs").Children.Keys.Select(key => key.ToString()));

        // Both build the draft in the public repository; only a tag goes on to open the pull request whose merge
        // publishes it, so the button is a dry run that leaves no branch and no pull request behind.
        Assert.Equal("github.event_name == 'push'", Scalar(PublishingStep("Open the pull request"), "if"));
        Assert.Null(Scalar(PublishingStep("Create the draft release"), "if"));
    }

    /// <summary>
    /// The token that can publish is handed to the one job that publishes, and to nothing that builds or tests: a
    /// compromised test on a matrix runner must not be able to create a release or push to the public repository.
    /// That job writes nothing here either — the draft, the branch and the pull request are all in the public
    /// repository, reached with that token alone — so no permission anywhere grants a write.
    /// </summary>
    [Fact]
    [Critical(Family.B4, "the publication token's scope")]
    public void Only_the_publishing_job_holds_the_publication_token()
    {
        var text = Read(Release);
        var release = Parse(text);
        Assert.Equal("read", Scalar(Mapping(release, "permissions"), "contents"));
        Assert.DoesNotContain("github.token", text, StringComparison.Ordinal);

        var jobs = Mapping(release, "jobs");
        foreach (var (name, job) in jobs.Children.Select(job => (job.Key.ToString(), (YamlMappingNode)job.Value)))
        {
            if (Node(job, "permissions") is YamlMappingNode permissions)
            {
                Assert.True(Scalar(permissions, "contents") != "write", $"The '{name}' job may write to this repository.");
            }
        }

        // Every mention of a secret is the publication token's, and every one of them lies inside the publishing job.
        var everywhere = Scalars(release).Where(scalar => scalar.Contains("secrets.", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(everywhere);
        Assert.All(everywhere, scalar => Assert.Contains("secrets.PUBLISH_TOKEN", scalar, StringComparison.Ordinal));
        var holders = jobs.Children.Where(job => Scalars(job.Value).Any(scalar => scalar.Contains("secrets.", StringComparison.Ordinal))).ToList();
        var holder = Assert.Single(holders);
        Assert.Equal(everywhere.Count, Scalars(holder.Value).Count(scalar => scalar.Contains("secrets.", StringComparison.Ordinal)));

        // It runs after every matrix leg, and nothing is published when one of them failed: `needs` is exactly that.
        var building = jobs.Children.Single(job => Node((YamlMappingNode)job.Value, "strategy") is not null).Key.ToString();
        Assert.Contains(building, Assert.IsType<YamlSequenceNode>(Node((YamlMappingNode)holder.Value, "needs")).Children.Select(need => need.ToString()));
        Assert.Contains("gh release create", text, StringComparison.Ordinal);
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
    /// constant: the address redirects it to the public repository's release page, where the release is published
    /// whichever repository built it, so the repository running the workflow is not it.
    /// </summary>
    [Fact]
    public void The_notes_address_is_the_release_page_at_the_products_address()
    {
        var release = Read(Release);

        Assert.DoesNotContain("github.repository", release, StringComparison.Ordinal);
        Assert.Contains(
            "-ReleaseNotesUrl \"https://jasonharness.ai/releases/tag/v$env:VERSION\"",
            release,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The release is created in the public repository as a draft, by a tag and by the button alike, before anything
    /// announces it. A draft an earlier run left behind holds the name, so it is deleted first — and only a draft: a
    /// published release of that version is somebody's installed software, and the step stops before it creates
    /// anything.
    /// </summary>
    /// <remarks>
    /// The step is run, not read: its own PowerShell, with <c>gh</c> replaced by a function that records what it was
    /// asked to do (a function wins over an external command, so what runs is the workflow's own line).
    /// </remarks>
    [Theory]
    [InlineData("workflow_dispatch", "none")]
    [InlineData("workflow_dispatch", "draft")]
    [InlineData("workflow_dispatch", "published")]
    [InlineData("push", "none")]
    [InlineData("push", "draft")]
    [InlineData("push", "published")]
    public void The_draft_is_made_in_the_public_repository_and_replaces_only_a_draft(string eventName, string existing)
    {
        var (exit, stderr, calls) = RunTheReleaseStep(eventName, existing, "0.2.0");

        // Every call is about the public repository, and none is about this one.
        Assert.NotEmpty(calls);
        Assert.All(calls, call => Assert.Contains("-R reply-team/jason-ai", call, StringComparison.Ordinal));
        Assert.Contains(calls, call => call.StartsWith("release view v0.2.0", StringComparison.Ordinal));
        var deletions = calls.Index().Where(call => call.Item.StartsWith("release delete", StringComparison.Ordinal)).ToList();
        var creations = calls.Index().Where(call => call.Item.StartsWith("release create", StringComparison.Ordinal)).ToList();

        if (existing == "published")
        {
            Assert.NotEqual(0, exit);
            Assert.Contains("v0.2.0 is already published", stderr, StringComparison.Ordinal);
            Assert.Empty(deletions);
            Assert.Empty(creations);
            return;
        }

        Assert.True(exit == 0, $"The step exited {exit}: {stderr}");
        var created = Assert.Single(creations);

        // A draft aimed at the public repository's main, whatever started the run; never --latest, which is decided
        // when it is published. And every flag reaches gh whole: PowerShell unrolls a one-element array assigned from
        // an `if` into a scalar string, and splatting a string spells it out one character per argument.
        Assert.Contains("--draft --target main", created.Item, StringComparison.Ordinal);
        Assert.DoesNotContain("--latest", created.Item, StringComparison.Ordinal);
        Assert.DoesNotContain("--prerelease", created.Item, StringComparison.Ordinal);
        Assert.DoesNotContain("--verify-tag", created.Item, StringComparison.Ordinal);
        Assert.DoesNotContain("- -", created.Item, StringComparison.Ordinal);

        // And every file a release publishes reaches gh as an asset, each a path of its own.
        foreach (var name in ReleaseAssets.Published)
        {
            Assert.Matches($@"(^| )\S*/{Regex.Escape(name)}( |$)", created.Item);
        }

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

    /// <summary>A candidate's draft is a pre-release from the start, whichever event made it.</summary>
    [Theory]
    [InlineData("workflow_dispatch")]
    [InlineData("push")]
    public void A_candidate_s_draft_is_a_pre_release(string eventName)
    {
        var (exit, stderr, calls) = RunTheReleaseStep(eventName, "none", "1.0.0-rc.1");

        Assert.True(exit == 0, stderr);
        var created = Assert.Single(calls, call => call.StartsWith("release create", StringComparison.Ordinal));
        Assert.Contains("--draft --target main --prerelease", created, StringComparison.Ordinal);
        Assert.DoesNotContain("--latest", created, StringComparison.Ordinal);
    }

    /// <summary>One step of the publishing job, by its name.</summary>
    private static YamlMappingNode PublishingStep(string name) => Assert.Single(PublishingSteps(), step => Scalar(step, "name") == name);

    /// <summary>The steps of the job that holds the publication token, in order.</summary>
    private static IReadOnlyList<YamlMappingNode> PublishingSteps() =>
        [.. Assert.IsType<YamlSequenceNode>(Node(PublishingJob(), "steps")).Children.Cast<YamlMappingNode>()];

    /// <summary>The job that holds the publication token.</summary>
    private static YamlMappingNode PublishingJob()
    {
        var jobs = Mapping(Parse(Read(Release)), "jobs");
        return (YamlMappingNode)jobs.Children
            .Single(job => Node((YamlMappingNode)job.Value, "env") is YamlMappingNode env && Scalar(env, "GH_TOKEN")?.Contains("secrets.PUBLISH_TOKEN", StringComparison.Ordinal) == true)
            .Value;
    }

    /// <summary>
    /// The publishing job's "Create the draft release" step, run with <c>gh</c> standing in: its exit code, what it
    /// wrote to stderr, and what it asked <c>gh</c> to do — one call per line, in the order they were made.
    /// </summary>
    private static (int Exit, string Stderr, IReadOnlyList<string> Calls) RunTheReleaseStep(string eventName, string existing, string version)
    {
        using var tree = new TempTree();
        var step = Path.Combine(tree.Root, "create-the-draft.ps1");
        File.WriteAllText(step, Scalar(PublishingStep("Create the draft release"), "run"));
        var calls = Path.Combine(tree.Root, "gh-calls.txt");
        File.WriteAllText(calls, string.Empty);
        var harness = Path.Combine(tree.Root, "harness.ps1");
        File.WriteAllText(harness, StandInForGh.Replace("@STEP@", step, StringComparison.Ordinal));

        var (exit, _, stderr) = Pwsh(
            new Dictionary<string, string>
            {
                ["EVENT"] = eventName,
                ["VERSION"] = version,
                ["GH_TOKEN"] = "not-a-token",
                ["EXISTING"] = existing,
                ["CALLS"] = calls,
            },
            "-File", harness);

        return (exit, stderr, File.ReadAllLines(calls));
    }

    /// <summary>
    /// The draft exists before the pull request that announces it, so a person never reads a pull request whose merge
    /// would have nothing to publish.
    /// </summary>
    [Fact]
    public void The_draft_is_made_before_the_pull_request()
    {
        var names = PublishingSteps().Select(step => Scalar(step, "name")).ToList();
        var draft = names.IndexOf("Create the draft release");
        var opening = names.IndexOf("Open the pull request");

        Assert.True(draft >= 0 && draft < opening, $"The steps run in this order: {string.Join(", ", names)}");
    }

    /// <summary>
    /// What a tag does in the public repository once the draft exists: it commits the projection as the person the
    /// release process acts for, pushes it over this version's branch, and opens that branch's pull request — or, when
    /// one is open already, gives it the new body. Labelled for the people who read contract changes when the body
    /// says a contract changed. Run with <c>git</c> and <c>gh</c> standing in.
    /// </summary>
    [Theory]
    [InlineData("contracts", "[]", "create --label contracts")]
    [InlineData("", "[]", "create")]
    [InlineData("", "[{\"number\":3,\"headRefName\":\"fix-a-typo\"}]", "create")]
    [InlineData("contracts", "[{\"number\":7,\"headRefName\":\"release/v0.6.0\"}]", "edit 7")]
    public void A_tag_commits_the_projection_pushes_its_branch_and_opens_or_updates_its_pull_request(string label, string open, string expected)
    {
        var (exit, _, stderr, calls) = RunThePullRequestStep(staged: true, label, open);

        Assert.True(exit == 0, $"The step exited {exit}: {stderr}");
        var commit = Assert.Single(calls, call => call.StartsWith("git ", StringComparison.Ordinal) && call.Contains(" commit ", StringComparison.Ordinal));
        Assert.Equal("git -C public -c user.name=Artem Kosolap -c user.email=art@reply.io commit --quiet -m Jason 0.6.0", commit);
        Assert.Contains("git -C public push --force origin HEAD:refs/heads/release/v0.6.0", calls);
        Assert.All(calls.Where(call => call.StartsWith("gh ", StringComparison.Ordinal)), call => Assert.Contains("-R reply-team/jason-ai", call, StringComparison.Ordinal));

        var pulls = calls.Where(call => call.StartsWith("gh pr create", StringComparison.Ordinal) || call.StartsWith("gh pr edit", StringComparison.Ordinal)).ToList();
        var pull = Assert.Single(pulls);
        if (expected.StartsWith("edit", StringComparison.Ordinal))
        {
            Assert.Equal("gh pr edit 7 -R reply-team/jason-ai --body-file artifacts/publish/body.md", pull);
            return;
        }

        var labelled = expected.EndsWith("--label contracts", StringComparison.Ordinal) ? " --label contracts" : string.Empty;
        Assert.Equal($"gh pr create -R reply-team/jason-ai --base main --head release/v0.6.0 --title Jason 0.6.0 --body-file artifacts/publish/body.md{labelled}", pull);
    }

    /// <summary>
    /// One release's pull request at a time: merging one tags its version, so two open at once would be tagged in
    /// whatever order a person merged them. The other one is named, and the step stops after the draft and the push,
    /// so running the job again once it is merged or closed has only the pull request left to open.
    /// </summary>
    [Fact]
    public void Another_release_s_open_pull_request_stops_the_step_before_this_one_is_opened()
    {
        var (exit, _, stderr, calls) = RunThePullRequestStep(staged: true, "", "[{\"number\":5,\"headRefName\":\"release/v0.5.1\"}]");

        Assert.NotEqual(0, exit);
        Assert.Contains("release/v0.5.1's pull request is open; merge or close it, then re-run this job.", stderr, StringComparison.Ordinal);
        Assert.Contains("git -C public push --force origin HEAD:refs/heads/release/v0.6.0", calls);
        Assert.DoesNotContain(calls, call => call.StartsWith("gh pr create", StringComparison.Ordinal) || call.StartsWith("gh pr edit", StringComparison.Ordinal));
    }

    /// <summary>A projection that changes nothing — a run after the release was merged — commits nothing and opens nothing, and says so.</summary>
    [Fact]
    public void A_projection_that_changes_nothing_commits_nothing_and_opens_nothing()
    {
        var (exit, stdout, stderr, calls) = RunThePullRequestStep(staged: false, "", "[]");

        Assert.True(exit == 0, stderr);
        Assert.Contains("nothing to commit", stdout, StringComparison.Ordinal);
        Assert.DoesNotContain(calls, call => call.Contains(" commit ", StringComparison.Ordinal) || call.Contains(" push ", StringComparison.Ordinal) || call.StartsWith("gh ", StringComparison.Ordinal));
    }

    /// <summary>
    /// The publishing job's "Open the pull request" step, run with <c>git</c> and <c>gh</c> standing in: <c>git diff
    /// --cached --quiet</c> answers whether anything is staged, <c>gh pr list</c> answers <paramref name="open"/>, and
    /// every call of either is recorded as one line.
    /// </summary>
    private static (int Exit, string Stdout, string Stderr, IReadOnlyList<string> Calls) RunThePullRequestStep(bool staged, string label, string open)
    {
        using var tree = new TempTree();
        var step = Path.Combine(tree.Root, "open-the-pull-request.ps1");
        File.WriteAllText(step, Scalar(PublishingStep("Open the pull request"), "run"));
        var calls = Path.Combine(tree.Root, "calls.txt");
        File.WriteAllText(calls, string.Empty);
        var harness = Path.Combine(tree.Root, "harness.ps1");
        File.WriteAllText(harness, StandInsForThePullRequest.Replace("@STEP@", step, StringComparison.Ordinal));

        var (exit, stdout, stderr) = Pwsh(
            new Dictionary<string, string>
            {
                ["VERSION"] = "0.6.0",
                ["LABEL"] = label,
                ["GH_TOKEN"] = "not-a-token",
                ["STAGED"] = staged ? "yes" : "no",
                ["OPEN"] = open,
                ["CALLS"] = calls,
            },
            "-File", harness);

        return (exit, stdout, stderr, File.ReadAllLines(calls));
    }

    private const string StandInsForThePullRequest = """
        $ErrorActionPreference = 'Stop'

        function git {
            Add-Content -LiteralPath $env:CALLS -Value ('git ' + ($args -join ' '))
            $global:LASTEXITCODE = 0
            if ($args -contains 'diff' -and $args -contains '--quiet' -and $env:STAGED -eq 'yes') { $global:LASTEXITCODE = 1 }
        }

        function gh {
            Add-Content -LiteralPath $env:CALLS -Value ('gh ' + ($args -join ' '))
            $global:LASTEXITCODE = 0
            if ($args[0] -eq 'pr' -and $args[1] -eq 'list') { $env:OPEN }
        }

        try { & '@STEP@'; if ((Test-Path -LiteralPath variable:\LASTEXITCODE)) { exit $LASTEXITCODE } } catch { [Console]::Error.WriteLine($_.Exception.Message); exit 1 }
        """;

    /// <summary>
    /// <c>gh</c> for the length of one step: every call recorded, and <c>release view</c> answering the way
    /// <c>--json isDraft --jq .isDraft</c> answers — <c>true</c>, <c>false</c>, or nothing and a non-zero exit
    /// code when there is no release of that name at all.
    /// </summary>
    private const string StandInForGh = """
        $ErrorActionPreference = 'Stop'

        function gh {
            Add-Content -LiteralPath $env:CALLS -Value ($args -join ' ')
            if ($args[0] -eq 'release' -and $args[1] -eq 'view') {
                switch ($env:EXISTING) {
                    'draft'     { $global:LASTEXITCODE = 0; 'true'; return }
                    'published' { $global:LASTEXITCODE = 0; 'false'; return }
                    default     { [Console]::Error.WriteLine('release not found'); $global:LASTEXITCODE = 1; return }
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
    /// platform, and what differs at the tag is only the version string, which the Linux run stamps and tests: the same tier
    /// on Windows and macOS would repeat a run that tree already passed there, on the dearest runners. The build the tier
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
        var action = Read(Action);
        var publish = Assert.Single(action.Split('\n'), line => line.TrimStart().StartsWith("dotnet publish ", StringComparison.Ordinal));
        Assert.DoesNotContain("--no-build", publish, StringComparison.Ordinal);
        Assert.DoesNotContain("--no-restore", publish, StringComparison.Ordinal);
        Assert.All(ranOnLinuxAlone, step => Assert.True(Scalar(step, "if") == OnLinux, $"'{Scalar(step, "name")}' has the condition '{Scalar(step, "if")}', not '{OnLinux}'"));

        var everywhere = steps.Where(step => Scalar(step, "uses") == "./.github/actions/package" || Scalar(step, "name")?.StartsWith("Smoke-test", StringComparison.Ordinal) == true).ToList();
        Assert.Equal(2, everywhere.Count);
        Assert.All(everywhere, step => Assert.True(Scalar(step, "if") is null, $"'{Scalar(step, "name") ?? Scalar(step, "uses")}' runs only when '{Scalar(step, "if")}'"));
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
    /// The Reply plugin's package is written once per release, not once per platform: by a job of its own, from the tag's
    /// checkout, after the version was decided and holding no token. It is uploaded under a name the publishing job's
    /// download takes, into the directory the checksums are written in, and the publishing job waits for that job, so
    /// nothing is published without it. What the script writes is <see cref="PluginAssetTests"/>' subject.
    /// </summary>
    [Fact]
    public void The_plugin_s_package_is_written_once_by_a_job_of_its_own_and_reaches_the_release()
    {
        var jobs = Mapping(Parse(Read(Release)), "jobs");
        var writing = jobs.Children
            .Select(job => (Name: job.Key.ToString(), Job: (YamlMappingNode)job.Value))
            .SelectMany(found => (Node(found.Job, "steps") as YamlSequenceNode)?.Children.Cast<YamlMappingNode>().Select(step => (found.Name, found.Job, Step: step)) ?? [])
            .Where(found => Scalar(found.Step, "run")?.Contains("Write-PluginArchive.ps1", StringComparison.Ordinal) == true)
            .ToList();
        var (name, job, step) = Assert.Single(writing);

        Assert.Null(Node(job, "strategy"));
        Assert.Equal("version", Scalar(job, "needs"));
        Assert.Equal(
            $"./.github/scripts/Write-PluginArchive.ps1 -Package plugins/reply -Archive artifacts/release/{ReleaseAssets.ReplyPlugin}",
            Scalar(step, "run")!.Trim());
        Assert.Null(Node(step, "continue-on-error"));

        var steps = Assert.IsType<YamlSequenceNode>(Node(job, "steps")).Children.Cast<YamlMappingNode>().ToList();
        Assert.StartsWith("actions/checkout@", Scalar(steps[0], "uses"), StringComparison.Ordinal);
        var upload = Mapping(Assert.Single(steps, one => Scalar(one, "uses")?.StartsWith("actions/upload-artifact@", StringComparison.Ordinal) == true), "with");
        Assert.Equal($"artifacts/release/{ReleaseAssets.ReplyPlugin}", Scalar(upload, "path"));
        Assert.Equal("error", Scalar(upload, "if-no-files-found"));

        var publishing = PublishingJob();
        var download = Mapping(Assert.Single(PublishingSteps(), one => Scalar(one, "uses")?.StartsWith("actions/download-artifact@", StringComparison.Ordinal) == true), "with");
        var pattern = Scalar(download, "pattern")!;
        var artifact = Scalar(upload, "name")!;
        Assert.True(
            Regex.IsMatch(artifact, "^" + Regex.Escape(pattern).Replace(@"\*", ".*", StringComparison.Ordinal) + "$"),
            $"The plugin's package is uploaded as '{artifact}', which the publishing job's download of '{pattern}' does not take.");
        Assert.Equal("artifacts/release", Scalar(download, "path"));
        Assert.Contains(name, Assert.IsType<YamlSequenceNode>(Node(publishing, "needs")).Children.Select(need => need.ToString()));
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
        Assert.Equal("version", Scalar(building, "needs"));

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

    // --- the notes --------------------------------------------------------------------------------------

    [Fact]
    public void The_notes_say_plainly_that_nothing_is_signed() =>
        Assert.Contains("Nothing in this release is signed or notarized.", Read(Notes), StringComparison.Ordinal);

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
    /// version, in a tree of the test's own — and what it wrote.
    /// </summary>
    private static string WriteTheNotes(string template)
    {
        var writing = PublishingStep("Write the notes");

        using var tree = new TempTree();
        Directory.CreateDirectory(Path.Combine(tree.Root, ".github"));
        Directory.CreateDirectory(Path.Combine(tree.Root, "artifacts", "release"));
        File.WriteAllText(Path.Combine(tree.Root, ".github", "release-notes-template.md"), template);

        var step = Path.Combine(tree.Root, "write-the-notes.ps1");
        File.WriteAllText(step, Scalar(writing, "run"));

        var (exit, _, stderr) = Pwsh(
            new Dictionary<string, string> { ["VERSION"] = "0.2.0" },
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
    /// A directory the way the assembling job sees it after the download: one stand-in archive per platform and
    /// the fragment the packaging action wrote for it, hashed the way the action hashes, and the plugin's package.
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

        File.WriteAllText(Path.Combine(directory, ReleaseAssets.ReplyPlugin), $"not really {ReleaseAssets.ReplyPlugin}");
        File.WriteAllText(Path.Combine(directory, ReleaseAssets.Ui), $"not really {ReleaseAssets.Ui}");
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

    /// <summary>Whether a step really asks an executable what version it is, rather than talking about it.</summary>
    private static bool Checks(string step) =>
        step.Split('\n')
            .Select(line => line.Trim())
            .Any(line => line.Contains("--version", StringComparison.Ordinal) && !line.StartsWith('#'));

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
        // published, the public repository. And its checksums carry the plugin's package, which the manifest does not.
        if (workflow == Release[^1])
        {
            Assert.Equal("https://jasonharness.ai/releases/tag/v0.2.0", manifest.ReleaseNotesUrl);

            var checksums = File.ReadAllText(Path.Combine(directory, ReleaseAssets.Checksums));
            foreach (var carried in (string[])[ReleaseAssets.ReplyPlugin, ReleaseAssets.Ui])
            {
                var package = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(directory, carried))));
                Assert.True(
                    checksums.Split('\n').Contains($"{package}  {carried}"),
                    $"{workflow} runs `{invocation}`, and the checksums it wrote carry no line for {carried}:\n{checksums}");
            }
        }
    }

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
    /// run's platforms, the environment variables the jobs export, and the directory the artifacts arrive in.
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
}
