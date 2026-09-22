using Jason.Cli;
using Jason.Cli.Process;
using Jason.Cli.Skills;
using Jason.Contracts.Skills;
using Jason.Cli.Tests.Autostart;
using Jason.Cli.Tests.Process;

namespace Jason.Cli.Tests.Skills;

/// <summary>
/// <c>jason skills install</c>: the verb that closes the hole, and the validation that keeps it from opening a
/// worse one.
/// </summary>
public class SkillsInstallTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// The whole tree is judged before a byte is written. A role whose skill the launcher would refuse is not
    /// written at all: a half-deployed role root refuses every launch of that role, which is strictly worse
    /// than the role having no skill and running untaught.
    /// </summary>
    [Fact]
    public async Task A_tree_the_launcher_would_refuse_is_not_written_at_all()
    {
        using var dir = new TempPaths();
        var source = Source(dir);
        Rename(source, "researcher", "researcher-v2");
        var (env, output, error) = Machine(dir);

        var exit = await CliApp.RunAsync(["skills", "install", "--source", source, "--root", Harness(dir)], env, Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.False(Directory.Exists(dir.Paths.RoleSkillsDirectory), "The role root was written although the deployment was refused.");
        Assert.Empty(Directory.EnumerateFileSystemEntries(Harness(dir)));
        Assert.Contains("researcher-v2", error.ToString(), StringComparison.Ordinal);
        Assert.Contains("Nothing was written.", error.ToString(), StringComparison.Ordinal);
        Assert.Contains(Harness(dir), output.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// A pack directory with no skill file. The launcher does not refuse it — it copies what is there and a
    /// host loads nothing — so nothing downstream would report it and the role would run untaught with a
    /// deployment on disk saying otherwise. It is refused here, where somebody is still watching.
    /// </summary>
    [Fact]
    public async Task A_role_directory_with_no_skill_file_is_refused_before_it_is_written()
    {
        using var dir = new TempPaths();
        var source = Source(dir);
        File.Delete(Path.Combine(source, "skills", "runtime", "roles", "researcher", "SKILL.md"));
        var (env, _, error) = Machine(dir);

        var exit = await CliApp.RunAsync(["skills", "install", "--source", source, "--root", Harness(dir)], env, Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Contains("has no SKILL.md", error.ToString(), StringComparison.Ordinal);
        Assert.False(Directory.Exists(dir.Paths.RoleSkillsDirectory));
    }

    /// <summary>
    /// The cap is the runtime's, because it is a live setting: an installer that guessed it would validate
    /// against the wrong number, and be wrong in the direction that refuses every launch of a role.
    /// </summary>
    [Fact]
    public async Task Without_a_runtime_it_validates_against_the_documented_default_and_names_it()
    {
        using var dir = new TempPaths();
        var source = Source(dir);
        var (env, output, _) = Machine(dir);

        var exit = await CliApp.RunAsync(["skills", "install", "--source", source, "--root", Harness(dir)], env, Ct);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains("1048576 bytes, the documented default", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("this runtime did not answer", output.ToString(), StringComparison.Ordinal);
    }

    /// <summary>The roots are printed before anything is written, in every mode and not only under --dry-run.</summary>
    [Fact]
    public async Task The_target_roots_are_printed_before_the_first_byte_is_written()
    {
        using var dir = new TempPaths();
        var source = Source(dir);
        var (env, output, _) = Machine(dir);

        await CliApp.RunAsync(["skills", "install", "--source", source, "--root", Harness(dir)], env, Ct);

        var text = output.ToString();
        Assert.True(
            text.IndexOf(Harness(dir), StringComparison.Ordinal) < text.IndexOf("Written:", StringComparison.Ordinal),
            $"The roots were not printed before the write:{Environment.NewLine}{text}");
        Assert.Contains(dir.Paths.RoleSkillsDirectory, text, StringComparison.Ordinal);
    }

    /// <summary>A dry run changes nothing anywhere, and says what it would have changed.</summary>
    [Fact]
    public async Task A_dry_run_writes_nothing_anywhere()
    {
        using var dir = new TempPaths();
        var source = Source(dir);
        var (env, output, _) = Machine(dir);

        var exit = await CliApp.RunAsync(["skills", "install", "--source", source, "--root", Harness(dir), "--dry-run"], env, Ct);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.False(Directory.Exists(dir.Paths.RoleSkillsDirectory));
        Assert.Empty(Directory.EnumerateFileSystemEntries(Harness(dir)));
        Assert.Contains("Would write", output.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// The role halves go to the runtime's own directory and the interactive ones to the harness. Two
    /// destinations, because there are two readers, and the first is not optional.
    /// </summary>
    [Fact]
    public async Task The_role_halves_go_to_the_runtime_and_the_rest_to_the_harness()
    {
        using var dir = new TempPaths();
        var source = Source(dir);
        var (env, _, _) = Machine(dir);

        var exit = await CliApp.RunAsync(["skills", "install", "--source", source, "--root", Harness(dir)], env, Ct);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.True(File.Exists(Path.Combine(dir.Paths.RoleSkillsDirectory, "researcher", "SKILL.md")));
        Assert.True(File.Exists(Path.Combine(Harness(dir), "operating", "SKILL.md")));
        Assert.True(File.Exists(Path.Combine(Harness(dir), "campaign-planning", "SKILL.md")));

        // Role skills are a level deeper than a host discovers, and they are the runtime's to read.
        Assert.False(Directory.Exists(Path.Combine(Harness(dir), "roles")));
        Assert.False(Directory.Exists(Path.Combine(Harness(dir), "researcher")));
    }

    /// <summary>
    /// Every <em>directory</em> under the role root is a role. That is the property letting the runtime
    /// enumerate that root with no skip list, and a skip list is a thing that goes stale.
    /// </summary>
    /// <remarks>
    /// Directories and not entries, because the runtime enumerates directories: the record file this
    /// deployment leaves beside them is a file, and nothing can mistake it for a role. What must never appear
    /// there is a staging or set-aside directory, which would be reported as a deployed role — almost
    /// certainly a refused one — by the operation an operator asks whether this installation is ready.
    /// </remarks>
    [Fact]
    public async Task The_role_root_is_left_holding_role_directories_only()
    {
        using var dir = new TempPaths();
        var source = Source(dir);
        var (env, _, _) = Machine(dir);

        await CliApp.RunAsync(["skills", "install", "--source", source, "--root", Harness(dir)], env, Ct);

        Assert.All(
            Directory.EnumerateDirectories(dir.Paths.RoleSkillsDirectory),
            entry => Assert.True(
                File.Exists(Path.Combine(entry, "SKILL.md")),
                $"'{entry}' is a directory under the role root and is not a role. Every directory under that "
                + "root is reported as a deployed role, so the installer keeps its workings elsewhere."));
    }

    /// <summary>A record is left in the harness root naming every path written.</summary>
    [Fact]
    public async Task A_record_is_left_naming_every_path_that_was_written()
    {
        using var dir = new TempPaths();
        var source = Source(dir);
        var (env, _, _) = Machine(dir);

        await CliApp.RunAsync(["skills", "install", "--source", source, "--root", Harness(dir)], env, Ct);

        var record = SkillsRecord.Read(Harness(dir));
        Assert.NotNull(record);
        Assert.Equal(2, record.Packs.Count);
        foreach (var file in record.Packs.SelectMany(pack => pack.Files))
        {
            Assert.True(File.Exists(Path.Combine(Harness(dir), file.Path)), $"The record names '{file.Path}', which is not there.");
        }
    }

    /// <summary>--pack deploys only that one.</summary>
    [Fact]
    public async Task A_named_pack_is_the_only_one_deployed()
    {
        using var dir = new TempPaths();
        var source = Source(dir);
        var (env, _, _) = Machine(dir);

        await CliApp.RunAsync(["skills", "install", "--source", source, "--root", Harness(dir), "--pack", SkillPacks.Business], env, Ct);

        Assert.True(File.Exists(Path.Combine(Harness(dir), "campaign-planning", "SKILL.md")));
        Assert.False(Directory.Exists(Path.Combine(Harness(dir), "operating")));
        Assert.False(Directory.Exists(dir.Paths.RoleSkillsDirectory));
    }

    /// <summary>
    /// <c>--root</c> replaces detection rather than narrowing it: nothing on this machine is consulted at all.
    /// That makes containment one flag instead of a careful combination.
    /// </summary>
    [Fact]
    public async Task Root_replaces_detection_rather_than_narrowing_it()
    {
        using var dir = new TempPaths();
        var source = Source(dir);
        var locator = new CountingLocator(Harness(dir));
        var (env, _, _) = Machine(dir, locator);

        await CliApp.RunAsync(["skills", "install", "--source", source, "--root", Harness(dir)], env, Ct);

        Assert.False(locator.WasAsked, "Detection ran although --root named the root.");
    }

    /// <summary>An environment with no locator refuses rather than guessing where a person keeps skills.</summary>
    [Fact]
    public async Task Without_a_locator_and_without_a_root_the_verb_refuses()
    {
        using var dir = new TempPaths();
        var source = Source(dir);
        var error = new StringWriter();
        var env = new CliEnvironment(new StringWriter(), error, dir.Paths, Programs: ProgramRunners.ForThisMachine());

        var exit = await CliApp.RunAsync(["skills", "install", "--source", source], env, Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Contains("harness_detection_unavailable", error.ToString(), StringComparison.Ordinal);
        Assert.False(Directory.Exists(dir.Paths.RoleSkillsDirectory));
    }

    private static string Harness(TempPaths dir) =>
        Directory.CreateDirectory(Path.Combine(dir.Paths.Root, "harness")).FullName;

    private static (CliEnvironment Env, StringWriter Out, StringWriter Error) Machine(TempPaths dir, IHarnessLocator? harnesses = null)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        return (
            new CliEnvironment(
                output,
                error,
                dir.Paths,
                Processes: new FakeProcessControl(),
                Autostart: new RecordingRegistrar(),
                Harnesses: harnesses,
                Programs: new FakeProgramRunner()),
            output,
            error);
    }

    /// <summary>A source shaped the way this repository is: two packs, one of them with roles under it.</summary>
    private static string Source(TempPaths dir)
    {
        var root = Directory.CreateDirectory(Path.Combine(dir.Paths.Root, "source")).FullName;
        Skill(Path.Combine(root, "skills", "runtime", "operating"), "operating");
        Skill(Path.Combine(root, "skills", "runtime", "roles", "researcher"), "researcher");
        Skill(Path.Combine(root, "skills", "runtime", "roles", "planner"), "planner");
        Skill(Path.Combine(root, "skills", "business", "campaign-planning"), "campaign-planning");
        return root;
    }

    private static void Skill(string directory, string name)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            Path.Combine(directory, "SKILL.md"),
            $"---\nname: {name}\ndescription: one line\n---\n\nbody\n");
        File.WriteAllText(Path.Combine(directory, "reference.md"), "where to look");
    }

    private static void Rename(string source, string role, string declared) =>
        File.WriteAllText(
            Path.Combine(source, "skills", "runtime", "roles", role, "SKILL.md"),
            $"---\nname: {declared}\ndescription: one line\n---\n\nbody\n");

    private sealed class CountingLocator(string directory) : IHarnessLocator
    {
        public bool WasAsked { get; private set; }

        public IReadOnlyList<HarnessRoot> Detect()
        {
            WasAsked = true;
            return HarnessLocators.At(directory).Detect();
        }
    }
}
