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

    /// <summary>
    /// A descriptor no client can be made from — an address that is not one, or none at all — is a runtime that did not
    /// answer, as one that is not listening is. It reached the verb's last-resort handler instead, so an install exited 1
    /// over a question it asks only to pick a cap, and <c>jason update apply</c>, which runs the same code after its
    /// update is complete, reported a finished update as refused.
    /// </summary>
    [Theory]
    [InlineData("not an address")]
    [InlineData(null)]
    [InlineData("ftp://127.0.0.1:1/")]
    public async Task A_descriptor_no_client_can_be_made_from_is_a_runtime_that_did_not_answer(string? address)
    {
        using var dir = new TempPaths();
        var source = Source(dir);
        dir.WriteDescriptor(new Jason.Contracts.Discovery.RuntimeDescriptor("v1", "0.1.0", "rt_X", 1, address!, "tok", DateTimeOffset.UnixEpoch));
        var (env, output, error) = Machine(dir);

        var exit = await CliApp.RunAsync(["skills", "install", "--source", source, "--root", Harness(dir)], env, Ct);

        Assert.True(exit == ExitCodes.Success, $"The install exited {exit} over a descriptor naming '{address}': {error}");
        Assert.Contains("1048576 bytes, the documented default", output.ToString(), StringComparison.Ordinal);
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
        // The plan is stated in the same words either way -- what differs between a dry run and a real one
        // is what happens next -- and the dry run says so on a line of its own. It says deployed, not written:
        // a git source not here yet is fetched into staging first, which is a write, and the line said none.
        var printed = output.ToString();
        Assert.Contains("Target:", printed, StringComparison.Ordinal);
        Assert.Contains("Dry run: nothing will be deployed.", printed, StringComparison.Ordinal);
        Assert.DoesNotContain("nothing will be written", printed, StringComparison.Ordinal);
        Assert.DoesNotContain("Writing", printed, StringComparison.Ordinal);
    }

    /// <summary>
    /// The first look on a new installation shows the plan: <c>--roles-only --dry-run</c> fetches the source into
    /// staging, prints the one root it would write, exits 0, and deploys nothing.
    /// </summary>
    /// <remarks>
    /// It exited 1 with "not on this machine yet", so the command the install page puts first answered a person
    /// following it with a refusal and an instruction to skip it.
    /// </remarks>
    [Fact]
    public async Task A_first_roles_only_dry_run_fetches_into_staging_prints_the_plan_and_deploys_nothing()
    {
        using var dir = new TempPaths();
        var origin = await TaggedSource.CreateAsync(dir, Ct, "v1.2.3");
        var output = new StringWriter();
        var error = new StringWriter();
        var env = new CliEnvironment(
            output,
            error,
            dir.Paths,
            Processes: new FakeProcessControl(),
            Autostart: new RecordingRegistrar(),
            Programs: ProgramRunners.ForThisMachine(),
            BuildVersion: "1.2.3");

        var exit = await CliApp.RunAsync(["skills", "install", "--source", origin, "--roles-only", "--dry-run"], env, Ct);

        Assert.True(exit == ExitCodes.Success, error.ToString());
        var target = Assert.Single(output.ToString().Split('\n'), line => line.StartsWith("Target:", StringComparison.Ordinal));
        Assert.Contains(dir.Paths.RoleSkillsDirectory, target, StringComparison.Ordinal);
        Assert.Contains("Dry run: nothing will be deployed.", output.ToString(), StringComparison.Ordinal);
        Assert.False(Directory.Exists(dir.Paths.RoleSkillsDirectory), "The dry run deployed into the role root.");
        Assert.True(
            File.Exists(Path.Combine(dir.Paths.SkillsStagedSourceDirectory("v1.2.3"), "skills", "runtime", "roles", "researcher", "SKILL.md")),
            "The dry run left nothing in staging to show.");
    }

    /// <summary>The flag's own help says what a dry run writes, on both verbs that take it.</summary>
    [Theory]
    [InlineData("install")]
    [InlineData("update")]
    public async Task The_dry_run_help_says_a_source_not_here_yet_is_fetched_into_staging_first(string verb)
    {
        using var dir = new TempPaths();
        var output = new StringWriter();

        await CliApp.RunAsync(["skills", verb, "--help"], new CliEnvironment(output, new StringWriter(), dir.Paths), Ct);

        var text = string.Join(' ', output.ToString().Split((char[])[' ', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries));
        Assert.Contains(
            "Print exactly what would be written where, and deploy nothing. A source not on this machine yet is fetched into Jason's staging directory first, so there is something to show.",
            text,
            StringComparison.Ordinal);
        Assert.DoesNotContain("refused rather than fetched", text, StringComparison.Ordinal);
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

        var exit = await CliApp.RunAsync(["skills", "install", "--source", source, "--root", Harness(dir)], env, Ct);
        Assert.Equal(ExitCodes.Success, exit);

        // Over something, and after a deployment that succeeded. Asserting over an enumeration passes on an
        // empty one, and the state this is meant to catch -- a role root with nothing in it -- is exactly
        // what a failed install leaves.
        var deployed = Directory.GetDirectories(dir.Paths.RoleSkillsDirectory);
        Assert.NotEmpty(deployed);
        Assert.All(
            deployed,
            entry => Assert.True(
                File.Exists(Path.Combine(entry, "SKILL.md")),
                $"'{entry}' is a directory under the role root and is not a role. Every directory under that "
                + "root is reported as a deployed role, so the installer keeps its workings elsewhere."));
    }

    /// <summary>
    /// And nothing of this deployment's own is left in the person's own directory. The role root's version of
    /// this property was asserted from the start; the harness root's was not, and it is the one that is in
    /// somebody's home rather than in Jason's.
    /// </summary>
    [Fact]
    public async Task The_harness_root_is_left_holding_skill_directories_only()
    {
        using var dir = new TempPaths();
        var source = Source(dir);
        var (env, _, _) = Machine(dir);

        var exit = await CliApp.RunAsync(["skills", "install", "--source", source, "--root", Harness(dir)], env, Ct);

        Assert.Equal(ExitCodes.Success, exit);
        var deployed = Directory.GetDirectories(Harness(dir));
        Assert.NotEmpty(deployed);
        Assert.All(
            deployed,
            entry => Assert.True(
                File.Exists(Path.Combine(entry, "SKILL.md")),
                $"'{entry}' is a directory this deployment left in the person's own skills directory."));
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

    /// <summary>
    /// <c>--roles-only</c> deploys what a runtime cannot work without, into its own directory, and writes into
    /// no agent harness — detection is not even asked.
    /// </summary>
    /// <remarks>
    /// <c>role_skills</c> is required and the harness half is not, but the one command that repaired the first
    /// also wrote the interactive and business packs into the agent's own configuration. An agent told to
    /// change nothing outside the install and data directories stopped there rather than break the rule, in
    /// both runs of the fresh-account check: right for an agent, and no way to reach ready.
    /// </remarks>
    [Fact]
    public async Task Roles_only_deploys_the_role_skills_and_writes_into_no_harness()
    {
        using var dir = new TempPaths();
        var source = Source(dir);
        var locator = new CountingLocator(Harness(dir));
        var (env, output, _) = Machine(dir, locator);

        var exit = await CliApp.RunAsync(["skills", "install", "--source", source, "--roles-only"], env, Ct);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.True(File.Exists(Path.Combine(dir.Paths.RoleSkillsDirectory, "researcher", "SKILL.md")));
        Assert.True(File.Exists(Path.Combine(dir.Paths.RoleSkillsDirectory, "planner", "SKILL.md")));
        Assert.Empty(Directory.EnumerateFileSystemEntries(Harness(dir)));
        Assert.False(locator.WasAsked, "--roles-only asked where the agent harnesses are, which it has no use for.");

        // The plan names the one root, and it is the runtime's own.
        var targets = output.ToString().Split('\n').Where(line => line.StartsWith("Target:", StringComparison.Ordinal)).ToList();
        var target = Assert.Single(targets);
        Assert.Contains(dir.Paths.RoleSkillsDirectory, target, StringComparison.Ordinal);
    }

    /// <summary>
    /// And it refuses what would contradict it, before anything is read: a flag that quietly ignored
    /// <c>--root</c> would deploy somewhere other than where it was told.
    /// </summary>
    [Theory]
    [InlineData("--root")]
    [InlineData("--host")]
    [InlineData("--pack")]
    public async Task Roles_only_refuses_a_harness_or_another_pack_as_a_usage_error(string contradiction)
    {
        using var dir = new TempPaths();
        var source = Source(dir);
        var (env, _, error) = Machine(dir, HarnessLocators.At(Harness(dir)));
        var value = contradiction switch
        {
            "--root" => Harness(dir),
            "--host" => "claude_code",
            _ => SkillPacks.Business,
        };

        var exit = await CliApp.RunAsync(["skills", "install", "--source", source, "--roles-only", contradiction, value], env, Ct);

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Contains("--roles-only", error.ToString(), StringComparison.Ordinal);
        Assert.False(Directory.Exists(dir.Paths.RoleSkillsDirectory));
        Assert.Empty(Directory.EnumerateFileSystemEntries(Harness(dir)));
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
