using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
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
    /// A draft's tag does not exist, but a draft holds the name: <c>gh release create v0.2.0</c> fails while a
    /// draft of v0.2.0 is sitting there, and it fails complaining about a tag that does not exist. So the
    /// second dry run of a version — the ordinary case, because a dry run is inspected and then pressed again —
    /// died at the last step, with every runner's work already done.
    /// </summary>
    /// <remarks>
    /// The step is run, not read: its own PowerShell, with <c>gh</c> replaced by a function that records what
    /// it was asked to do (a function wins over an external command, so what runs is the workflow's own line).
    /// What the rows assert is the safety of the repair as much as the repair: a <em>published</em> release of
    /// that version is somebody's installed software, and nothing here may delete it — the workflow stops
    /// instead, which is what <c>gh release create</c> failing on an existing release does.
    /// </remarks>
    [Theory]
    [InlineData("workflow_dispatch", "draft", true)]
    [InlineData("push", "draft", true)]
    [InlineData("workflow_dispatch", "published", false)]
    [InlineData("workflow_dispatch", "none", false)]
    public void A_second_dry_run_of_the_same_version_replaces_its_draft_rather_than_failing(string eventName, string existing, bool deleted)
    {
        var calls = RunTheReleaseStep(eventName, existing);

        Assert.Contains(calls, call => call.StartsWith("release view v0.2.0", StringComparison.Ordinal));
        var created = calls.Index().Single(call => call.Item.StartsWith("release create", StringComparison.Ordinal));

        // And every flag reaches gh whole. PowerShell unrolls a one-element array assigned from an `if` into a
        // scalar string, `+=` on a string concatenates, and splatting a string spells it out one character per
        // argument — which is what the tag path did: `gh release create v0.2.0 - - v e r i f y - t a g …`.
        Assert.Contains(eventName == "push" ? "--verify-tag" : "--draft", created.Item, StringComparison.Ordinal);
        Assert.Contains("--latest", created.Item, StringComparison.Ordinal);
        Assert.DoesNotContain("- -", created.Item, StringComparison.Ordinal);
        var deletions = calls.Index().Where(call => call.Item.StartsWith("release delete", StringComparison.Ordinal)).ToList();

        if (!deleted)
        {
            Assert.Empty(deletions);
            return;
        }

        var deletion = Assert.Single(deletions);
        Assert.Contains("v0.2.0", deletion.Item, StringComparison.Ordinal);
        Assert.True(deletion.Index < created.Index, $"The draft is deleted after the release is created:\n{string.Join('\n', calls)}");

        // A draft has no tag to clean up, and asking for one is how a published tag gets deleted by accident.
        Assert.DoesNotContain("--cleanup-tag", deletion.Item, StringComparison.Ordinal);
    }

    /// <summary>
    /// The assembling job's "Create the release" step, run with <c>gh</c> standing in, and what it asked
    /// <c>gh</c> to do — one call per line, in the order they were made.
    /// </summary>
    private static IReadOnlyList<string> RunTheReleaseStep(string eventName, string existing)
    {
        var jobs = Mapping(Parse(Read(Release)), "jobs");
        var assembling = (YamlMappingNode)jobs.Children
            .Single(job => Node((YamlMappingNode)job.Value, "permissions") is YamlMappingNode permissions && Scalar(permissions, "contents") == "write")
            .Value;
        var creating = Assert.IsType<YamlSequenceNode>(Node(assembling, "steps")).Children.Cast<YamlMappingNode>()
            .Single(step => Scalar(step, "name") == "Create the release");

        using var tree = new TempTree();
        var step = Path.Combine(tree.Root, "create-the-release.ps1");
        File.WriteAllText(step, Scalar(creating, "run"));
        var calls = Path.Combine(tree.Root, "gh-calls.txt");
        File.WriteAllText(calls, string.Empty);
        var harness = Path.Combine(tree.Root, "harness.ps1");
        File.WriteAllText(harness, StandInForGh.Replace("@STEP@", step, StringComparison.Ordinal));

        var (exit, _, stderr) = Pwsh(
            new Dictionary<string, string>
            {
                ["EVENT"] = eventName,
                ["SHA"] = "1111111111111111111111111111111111111111",
                ["VERSION"] = "0.2.0",
                ["GH_TOKEN"] = "not-a-token",
                ["EXISTING"] = existing,
                ["CALLS"] = calls,
            },
            "-File", harness);

        Assert.True(exit == 0, $"The step exited {exit}: {stderr}");
        return File.ReadAllLines(calls);
    }

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

        & '@STEP@'
        """;

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
    /// The release's "Write the notes" step, run the way the assembling job runs it: this template, this
    /// version, in a tree of the test's own — and what it wrote.
    /// </summary>
    private static string WriteTheNotes(string template)
    {
        var jobs = Mapping(Parse(Read(Release)), "jobs");
        var assembling = (YamlMappingNode)jobs.Children
            .Single(job => Node((YamlMappingNode)job.Value, "permissions") is YamlMappingNode permissions && Scalar(permissions, "contents") == "write")
            .Value;
        var writing = Assert.IsType<YamlSequenceNode>(Node(assembling, "steps")).Children.Cast<YamlMappingNode>()
            .Single(step => Scalar(step, "name") == "Write the notes");

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
    /// <summary>
    /// Every place that checks a published or installed executable starts a runtime with it, not only asks it
    /// for its version.
    /// </summary>
    /// <remarks>
    /// A single-file publish leaves SQLite's native library beside the executable unless it is told to bundle
    /// it, and the archive carries the executable alone. Such a build answers <c>--version</c> with 0 and
    /// <c>runtime status</c> with 3 and <c>no_descriptor</c> — both of the assertions this smoke test used to
    /// make — and then dies on <c>runtime start</c> with a missing DLL. The one build this wave exists to
    /// deliver would have gone out green, which is why opening the database is now part of every one of these.
    /// </remarks>
    [Theory]
    [InlineData("ci.yml")]
    [InlineData("release.yml")]
    public void Every_executable_check_opens_the_database(string workflow)
    {
        var text = Read(workflow == "ci.yml" ? Ci : Release);
        var steps = text.Split("- name:").Where(step => step.Contains("--version", StringComparison.Ordinal)).ToList();

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
        var manifest = UpdateManifest.Read(File.ReadAllText(Path.Combine(directory, ReleaseAssets.Manifest)));
        Assert.Equal(ReleaseAssets.Rids.Count, manifest.Artifacts.Count);
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
    /// The runner's own substitutions, and nothing else: the workflow expression that carries the version, the
    /// environment variables the release job exports, and the directory the artifacts arrive in.
    /// </summary>
    private static string ExpandForThisMachine(string invocation, string directory)
    {
        var script = Path.Combine(RepositoryRoot(), Path.Combine(Script));
        var expanded = invocation
            .Replace("./.github/scripts/Write-Manifest.ps1", $"& '{script}'", StringComparison.Ordinal)
            .Replace("${{ needs.version.outputs.version }}", "0.2.0", StringComparison.Ordinal)
            .Replace("$env:VERSION", "0.2.0", StringComparison.Ordinal)
            .Replace("$env:REPOSITORY", "reply-team/jason-ai", StringComparison.Ordinal);

        // -Directory names where a runner downloaded the artifacts to; here it is the staged tree this test made.
        return System.Text.RegularExpressions.Regex.Replace(
            expanded,
            @"-Directory \S+",
            $"-Directory '{directory}'");
    }

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
