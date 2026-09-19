using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Jason.Contracts.Update;
using YamlDotNet.RepresentationModel;

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

        // Q5: MIT's own condition is that the notice accompanies every copy of the software.
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
    /// The archive holds the executable and <c>LICENSE</c> and nothing else, which is only true if the
    /// executable is one file. A self-contained single-file publish leaves native libraries beside it — SQLite's
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

    // --- release.yml -------------------------------------------------------------------------------------

    [Fact]
    public void A_release_is_built_from_a_tag_and_a_draft_from_a_button()
    {
        var release = Read(Release);
        var triggers = Mapping(Parse(release), "on");

        var tags = Assert.IsType<YamlSequenceNode>(Node(Mapping(triggers, "push"), "tags"));
        Assert.Contains("v*", tags.Children.Select(tag => tag.ToString()));
        Assert.Contains("version", Mapping(Mapping(triggers, "workflow_dispatch"), "inputs").Children.Keys.Select(key => key.ToString()));

        // The button's release is a draft, so a dry run can be inspected and deleted — and the two things are
        // decided on one line, so nobody can read the condition without the consequence.
        Assert.Contains(release.Split('\n'), line => line.Contains("workflow_dispatch", StringComparison.Ordinal) && line.Contains("--draft", StringComparison.Ordinal));
    }

    /// <summary>
    /// The token that can write to the repository is granted to the one job that publishes, and to nothing that
    /// builds or tests: a compromised test on a matrix runner must not be able to create a release.
    /// </summary>
    [Fact]
    public void Only_the_assembling_job_may_write()
    {
        var release = Parse(Read(Release));
        Assert.Equal("read", Scalar(Mapping(release, "permissions"), "contents"));

        var jobs = Mapping(release, "jobs");
        var writers = jobs.Children
            .Where(job => Node((YamlMappingNode)job.Value, "permissions") is YamlMappingNode permissions && Scalar(permissions, "contents") == "write")
            .Select(job => job.Key.ToString())
            .ToList();

        var writer = Assert.Single(writers);
        var assembling = (YamlMappingNode)jobs.Children[new YamlScalarNode(writer)];
        var building = jobs.Children.Single(job => Node((YamlMappingNode)job.Value, "strategy") is not null).Key.ToString();

        // It runs after every matrix leg, and nothing is created when one of them failed: `needs` is exactly that.
        Assert.Contains(building, Assert.IsType<YamlSequenceNode>(Node(assembling, "needs")).Children.Select(need => need.ToString()));
        Assert.Contains("gh release create", Read(Release), StringComparison.Ordinal);
    }

    /// <summary>
    /// PB2 and PA7: the tests run at the tag before anything is packaged, and one <c>-p:Version=</c> reaches
    /// build, test and publish alike. <c>VersionPrefix</c> is never stamped — it would leave the suffix behind
    /// and print <c>0.2.0-dev</c> on a release.
    /// </summary>
    [Fact]
    public void The_tests_run_at_the_tag_before_anything_is_packaged()
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
            Assert.Contains(ReleaseAssets.For(rid), release, StringComparison.Ordinal);
        }

        Assert.Contains(ReleaseAssets.Manifest, release, StringComparison.Ordinal);
        Assert.Contains(ReleaseAssets.Checksums, release, StringComparison.Ordinal);
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

    /// <summary>PB7: the fact a dry run's reader needs, where they will read it.</summary>
    [Fact]
    public void The_notes_say_that_a_drafts_tag_does_not_exist_until_it_is_published()
    {
        var notes = Read(Notes);
        Assert.StartsWith("<!--", notes, StringComparison.Ordinal);
        Assert.Contains("does not exist until the draft is published", notes, StringComparison.Ordinal);
    }

    [Fact]
    public void The_notes_name_every_asset_the_release_carries()
    {
        var notes = Read(Notes);
        foreach (var rid in ReleaseAssets.Rids)
        {
            Assert.Contains(ReleaseAssets.For(rid), notes, StringComparison.Ordinal);
        }

        Assert.Contains(ReleaseAssets.Manifest, notes, StringComparison.Ordinal);
        Assert.Contains(ReleaseAssets.Checksums, notes, StringComparison.Ordinal);
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

        // sha256sum's own format — the digest, two spaces, the name — one per line, Unix newlines, so that
        // `sha256sum -c checksums.txt` works wherever the archives were downloaded to.
        var checksums = File.ReadAllText(Path.Combine(directory, ReleaseAssets.Checksums));
        Assert.DoesNotContain('\r', checksums);
        var lines = checksums.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(ReleaseAssets.Rids.Count, lines.Length);
        foreach (var rid in ReleaseAssets.Rids)
        {
            Assert.Contains($"{manifest.Artifacts[rid].Sha256}  {ReleaseAssets.For(rid)}", lines);
        }
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
    /// the fragment the packaging action wrote for it, hashed the way the action hashes.
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

        return directory;
    }

    /// <summary>
    /// <c>pwsh</c>, found on this machine's PATH, run to completion. A machine without it skips the test rather
    /// than failing it: the workflows run on runners that have it, and this suite has nothing to install.
    /// </summary>
    private static (int Exit, string Stdout, string Stderr) Pwsh(IReadOnlyDictionary<string, string> environment, params string[] args)
    {
        var pwsh = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .SelectMany(directory => new[] { Path.Combine(directory, "pwsh.exe"), Path.Combine(directory, "pwsh") })
            .FirstOrDefault(File.Exists);
        Assert.SkipWhen(pwsh is null, "pwsh is not on this machine's PATH; every CI runner has it, and the release is only ever built there.");

        var start = new ProcessStartInfo(pwsh)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        foreach (var argument in args)
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
            Assert.Fail("pwsh did not finish in two minutes.");
        }

        return (process.ExitCode, stdout.Result, stderr.Result);
    }

    private static string Read(string[] relative) => File.ReadAllText(Path.Combine([RepositoryRoot(), .. relative]));

    private static YamlMappingNode Parse(string yaml)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(yaml));
        return (YamlMappingNode)stream.Documents[0].RootNode;
    }

    private static YamlNode? Node(YamlMappingNode node, string key) =>
        node.Children.TryGetValue(new YamlScalarNode(key), out var child) ? child : null;

    private static string? Scalar(YamlMappingNode node, string key) => (Node(node, key) as YamlScalarNode)?.Value;

    private static YamlMappingNode Mapping(YamlMappingNode node, string key)
    {
        var child = Node(node, key);
        Assert.True(child is not null, $"There is no '{key}' mapping.");
        return Assert.IsType<YamlMappingNode>(child);
    }

    /// <summary>The repository root, found by walking up from the test binaries to the directory holding <c>global.json</c>.</summary>
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
