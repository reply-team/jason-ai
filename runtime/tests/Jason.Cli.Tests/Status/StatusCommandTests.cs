using System.Collections.Generic;
using System.Net;
using System.Text.Json;
using Jason.Cli;
using Jason.Cli.Process;
using Jason.Cli.Skills;
using Jason.Contracts.Skills;
using Jason.Cli.Status;
using Jason.Cli.Tests.Autostart;
using Jason.Cli.Tests.Commands;
using Jason.Cli.Tests.Process;
using Jason.Contracts.Api;
using Jason.Contracts.Json;
using Jason.Cli.Tests.Uninstall;
using Jason.Cli.Uninstall;

namespace Jason.Cli.Tests.Status;

/// <summary>
/// <c>jason status</c>: can this installation start work? The one command here that is not one-to-one with an
/// API operation, and the one that never exits 3.
/// </summary>
public class StatusCommandTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// A runtime that is not answering, and that is the answer rather than the reason there is none. Exit 3
    /// means "I could not ask", and this is the verb whose whole job is to answer when the runtime cannot be
    /// asked — so a person whose runtime is down gets a report, not an error envelope.
    /// </summary>
    [Fact]
    public async Task A_runtime_that_does_not_answer_is_a_failed_check_and_never_exit_three()
    {
        using var dir = new TempPaths();
        var output = new StringWriter();

        var exit = await CliApp.RunAsync(["status"], Machine(dir, output), Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.NotEqual(ExitCodes.RuntimeUnavailable, exit);

        var report = Read(output);
        Assert.False(report.Ready);
        var runtime = Assert.Single(report.Checks, check => check.Name == "runtime");
        Assert.Equal(CheckState.Failed, runtime.State);
        Assert.Equal("jason runtime start", runtime.Fix);
    }

    /// <summary>A ready installation exits 0, and says so in the body as well as in the code.</summary>
    [Fact]
    public async Task An_installation_with_every_required_check_passing_is_ready()
    {
        using var dir = new TempPaths();
        var output = new StringWriter();
        Running(dir);

        var exit = await CliApp.RunAsync(["status"], Machine(dir, output), Ct);

        var report = Read(output);
        Assert.True(report.Ready, string.Join(Environment.NewLine, report.Checks.Where(check => check.Required && check.State != CheckState.Ok).Select(check => check.Fact)));
        Assert.Equal(ExitCodes.Success, exit);
    }

    /// <summary>
    /// Nothing optional can fail the run. That is what optional means, and a person whose provider is not
    /// configured yet has a working installation rather than a broken one.
    /// </summary>
    [Fact]
    public async Task Nothing_optional_can_fail_the_run()
    {
        using var dir = new TempPaths();
        var output = new StringWriter();
        Running(dir);

        var exit = await CliApp.RunAsync(["status"], Machine(dir, output), Ct);

        var report = Read(output);
        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains(report.Checks, check => !check.Required && check.State == CheckState.Absent);
        Assert.DoesNotContain(report.Checks, check => !check.Required && check.State == CheckState.Failed);
    }

    /// <summary>
    /// The hole this wave exists to close. A role whose skill the launcher would refuse is worse than one with
    /// no skill at all — it refuses every launch of that role rather than running it untaught — and the check
    /// re-reads the cap from the runtime rather than trusting the number an installer validated against.
    /// </summary>
    [Fact]
    public async Task Role_skills_the_launcher_would_refuse_are_a_required_failure_naming_the_cap()
    {
        using var dir = new TempPaths();
        var output = new StringWriter();
        Running(dir, skills: new SkillsInfo(
            "/data/skills/roles",
            4096,
            [new DeployedRoleSkill("researcher", 5000, "The skill in '/data/skills/roles/researcher' is 5000 bytes and Roles:MaxSkillBytes allows 4096.")]));

        var exit = await CliApp.RunAsync(["status"], Machine(dir, output), Ct);

        var skills = Assert.Single(Read(output).Checks, check => check.Name == "role_skills");
        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.True(skills.Required);
        Assert.Equal(CheckState.Failed, skills.State);
        Assert.Contains("Roles:MaxSkillBytes", skills.Fact, StringComparison.Ordinal);
    }

    /// <summary>And a seeded role nothing has taught is the same failure by omission.</summary>
    [Fact]
    public async Task A_seeded_role_with_no_skill_at_all_is_a_required_failure()
    {
        using var dir = new TempPaths();
        var output = new StringWriter();
        Running(dir, roles: ["researcher", "planner"], skills: new SkillsInfo("/data/skills/roles", 1048576, [Taught("researcher")]));

        var exit = await CliApp.RunAsync(["status"], Machine(dir, output), Ct);

        var skills = Assert.Single(Read(output).Checks, check => check.Name == "role_skills");
        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Equal(CheckState.Failed, skills.State);
        Assert.Contains("1 of 2 seeded roles have no skill", skills.Fact, StringComparison.Ordinal);
        Assert.Contains("planner", skills.Fact, StringComparison.Ordinal);

        // The role half alone. A required check whose one repair also wrote the interactive and business packs
        // into the agent's own configuration could not be repaired by somebody who would not allow that — an
        // agent told to change nothing outside the install and data directories stopped here, twice.
        Assert.Equal("jason skills install --roles-only", skills.Fix);
    }

    /// <summary>
    /// A deployed role with no skill file. The launcher does not refuse it — it copies what is there and the
    /// host loads nothing — so this is the only place it is ever reported: M3's hole wearing a different coat,
    /// the role running untaught while a deployment on disk says it was taught.
    /// </summary>
    [Fact]
    public async Task A_deployed_role_with_no_skill_file_is_a_required_failure()
    {
        using var dir = new TempPaths();
        var output = new StringWriter();
        Running(dir, skills: new SkillsInfo(
            "/data/skills/roles",
            1048576,
            [new DeployedRoleSkill("researcher", 12, "'/data/skills/roles/researcher' holds no SKILL.md, so a launch copies what is there and the host loads no skill from it.")]));

        var exit = await CliApp.RunAsync(["status"], Machine(dir, output), Ct);

        var skills = Assert.Single(Read(output).Checks, check => check.Name == "role_skills");
        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Equal(CheckState.Failed, skills.State);
        Assert.Contains("holds no SKILL.md", skills.Fact, StringComparison.Ordinal);

        // A plain install, not --force. Overwriting is not what repairs a directory missing its SKILL.md,
        // and --force overwrites every edited file in every root of the plan — so printing it here would
        // cost an operator unrelated work for a problem that never needed it. And the role half alone, for
        // the reason the check above gives.
        Assert.Equal("jason skills install --roles-only", skills.Fix);
    }

    /// <summary>
    /// The other half of the same comparison: a directory that is not a seeded role. It is inert — the
    /// launcher looks a skill up by role name, so nothing will ever read it — and saying so is worth a line
    /// and is not worth failing over.
    /// </summary>
    [Fact]
    public async Task A_deployed_directory_that_is_not_a_seeded_role_is_reported_and_does_not_fail_the_run()
    {
        using var dir = new TempPaths();
        var output = new StringWriter();
        Running(dir, roles: ["researcher"], skills: new SkillsInfo("/data/skills/roles", 1048576, [Taught("researcher"), Taught("leftovers")]));

        var exit = await CliApp.RunAsync(["status"], Machine(dir, output), Ct);

        var report = Read(output);
        var skills = Assert.Single(report.Checks, check => check.Name == "role_skills");
        Assert.Equal(ExitCodes.Success, exit);
        Assert.True(report.Ready);
        Assert.NotEqual(CheckState.Failed, skills.State);
        Assert.Contains("'leftovers' is deployed and is not a role this runtime seeds", skills.Fact, StringComparison.Ordinal);
    }

    /// <summary>
    /// The provider check names the command it runs, bounds it, and prints nothing that is a secret — not the
    /// key, not a prefix of it, not its length.
    /// </summary>
    [Fact]
    public async Task The_provider_check_names_its_command_bounds_it_and_prints_no_credential()
    {
        const string Secret = "rk_live_do_not_print_me";
        using var dir = new TempPaths();
        var output = new StringWriter();
        Running(dir);
        var runner = new FakeProgramRunner(_ => new ProgramResult(-1, Secret, Secret, TimedOut: true));

        var exit = await CliApp.RunAsync(["status", "--provider-cli", "acme-sdr"], Machine(dir, output, runner), Ct);

        var provider = Assert.Single(Read(output).Checks, check => check.Name == "provider_cli");
        Assert.Equal(ExitCodes.Success, exit);
        Assert.False(provider.Required);
        Assert.Equal(CheckState.Absent, provider.State);
        Assert.Contains("acme-sdr --version", provider.Fact, StringComparison.Ordinal);
        Assert.Contains("did not answer in 10 seconds", provider.Fact, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, output.ToString(), StringComparison.OrdinalIgnoreCase);

        var asked = Assert.Single(runner.Requested);
        Assert.Equal(TimeSpan.FromSeconds(10), asked.Timeout);
    }

    /// <summary>
    /// A harness root whose record cannot be read is unknown, never absent. Absent would say "nothing is
    /// installed here", and an uninstall reading that would remove nothing and report success.
    /// </summary>
    [Fact]
    public async Task A_harness_root_with_an_unreadable_record_is_unknown_rather_than_absent()
    {
        using var dir = new TempPaths();
        var output = new StringWriter();
        Running(dir);
        var harness = Directory.CreateDirectory(Path.Combine(dir.Paths.Root, "harness")).FullName;
        await File.WriteAllTextAsync(Path.Combine(harness, SkillsRecord.FileName), "{ not json", Ct);

        await CliApp.RunAsync(["status"], Machine(dir, output, harnesses: HarnessLocators.At(harness)), Ct);

        var check = Assert.Single(Read(output).Checks, check => check.Name == "harness_skills");
        Assert.Equal(CheckState.Unknown, check.State);
    }

    /// <summary>
    /// One check, one subject. With the packs deployed into the runtime's own role root and a detected
    /// harness holding nothing, the harness check is <c>absent</c> — because nothing is deployed into a
    /// harness.
    /// </summary>
    /// <remarks>
    /// It used to answer <c>ok</c> here, on the strength of the role root's record, naming a directory that
    /// is not a harness while the harness it had detected went unmentioned. The README prompt tells an agent
    /// to branch on this body, and that is a body it would read wrongly.
    /// </remarks>
    [Fact]
    public async Task The_harness_check_is_about_harnesses_and_not_about_the_role_root()
    {
        using var dir = new TempPaths();
        var output = new StringWriter();
        Running(dir);
        Deploy(dir.Paths.RoleSkillsDirectory);
        var harness = Directory.CreateDirectory(Path.Combine(dir.Paths.Root, "harness")).FullName;

        await CliApp.RunAsync(["status"], Machine(dir, output, harnesses: HarnessLocators.At(harness)), Ct);

        var check = Assert.Single(Read(output).Checks, check => check.Name == "harness_skills");
        Assert.Equal(CheckState.Absent, check.State);
        Assert.DoesNotContain("skills", check.Fact.Replace(harness, string.Empty, StringComparison.Ordinal).Replace("skill packs", string.Empty, StringComparison.Ordinal), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>And what the record in the role root says is reported by the check whose subject it is.</summary>
    [Fact]
    public async Task The_role_check_names_the_pack_and_ref_its_own_record_carries()
    {
        using var dir = new TempPaths();
        var output = new StringWriter();

        // The root the *runtime* reports, not the one this CLI would compose. They are the same on an
        // ordinary machine and the runtime's is the truth where they differ: it is the directory it reads at
        // every launch, whatever data directory this client thinks it is talking about.
        Running(dir, skills: new SkillsInfo(dir.Paths.RoleSkillsDirectory, 1048576, [Taught("researcher")]));
        Deploy(dir.Paths.RoleSkillsDirectory);

        await CliApp.RunAsync(["status"], Machine(dir, output), Ct);

        var check = Assert.Single(Read(output).Checks, check => check.Name == "role_skills");
        Assert.Equal(CheckState.Ok, check.State);
        Assert.Contains("jason-runtime-skills at v0.1.0", check.Fact, StringComparison.Ordinal);
    }

    /// <summary>
    /// A required check that failed prints the line that repairs it — and that line is the one this
    /// product's own installer writes, so the repair and the installer cannot drift apart.
    /// </summary>
    /// <remarks>
    /// This is the check a from-source installation fails, and it failed with no repair at all: the verdict
    /// was "Not ready." and the whole repair list was an optional check's line. A required failure with
    /// nothing to do about it tells a person the tool is broken at the moment they most need it not to be.
    /// </remarks>
    [Fact]
    public async Task A_failing_path_check_carries_the_line_this_products_own_installer_writes()
    {
        using var dir = new TempPaths();
        var output = new StringWriter();
        Running(dir);

        // Named, because this suite is not an installation: only a published single file is, and the test host
        // is one file of many.
        var directory = Directory.CreateDirectory(Path.Combine(dir.Paths.Root, "install")).FullName;
        var exit = await CliApp.RunAsync(["status"], Machine(dir, output, onPath: false, installPath: Path.Combine(directory, "jason")), Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        var check = Assert.Single(Read(output).Checks, check => check.Name == "path");
        Assert.Equal(CheckState.Failed, check.State);
        Assert.NotNull(check.Fix);

        Assert.Contains(
            OperatingSystem.IsWindows() ? "CreateSubKey('Environment')" : Jason.Cli.Uninstall.PathEntry.ExportLine(directory),
            check.Fix,
            StringComparison.Ordinal);
        Assert.Contains(directory, check.Fix, StringComparison.Ordinal);
    }

    /// <summary>
    /// A shell older than the installation is not a broken installation: when this account's PATH carries the
    /// directory and this shell's does not, the check is ok and says which case it is — and how to reach the
    /// executable from here.
    /// </summary>
    /// <remarks>
    /// The shell an installer ran in is exactly this case, and it is the one an agent is standing in. The check
    /// read only the PATH this process inherited, so it said <c>failed</c> there although every new shell
    /// found <c>jason</c>, and its repair, typed as told, appended the directory to the account's Path a second
    /// time.
    /// </remarks>
    [Fact]
    public async Task A_path_this_account_carries_and_this_shell_does_not_is_ok_and_says_so()
    {
        using var dir = new TempPaths();
        var output = new StringWriter();
        Running(dir);
        var installed = Path.Combine(dir.Paths.Root, "programs", "jason", OperatingSystem.IsWindows() ? "jason.exe" : "jason");
        var carried = new RecordingRemover
        {
            PathEntry = new PathEntryPlan(Path.GetDirectoryName(installed)!, ["/home/a/.profile"], "carried", Ours: true, Persisted: "/home/a/.profile"),
        };

        await CliApp.RunAsync(["status"], Machine(dir, output, onPath: false, machine: carried, installPath: installed), Ct);

        var check = Assert.Single(Read(output).Checks, check => check.Name == "path");
        Assert.Equal(CheckState.Ok, check.State);
        Assert.Null(check.Fix);
        Assert.Contains("started before it was put there", check.Fact, StringComparison.Ordinal);
        Assert.Contains(installed, check.Fact, StringComparison.Ordinal);
        Assert.Equal([Path.GetDirectoryName(installed)!], carried.Reads);
        Assert.Empty(carried.Calls);
    }

    /// <summary>
    /// What a new shell finds is the question, not who put it there: a directory on this account's PATH by
    /// somebody else's hand is found all the same.
    /// </summary>
    [Fact]
    public async Task A_path_somebody_else_put_on_the_account_is_found_all_the_same()
    {
        using var dir = new TempPaths();
        var output = new StringWriter();
        Running(dir);
        var installed = Path.Combine(dir.Paths.Root, "programs", "jason", OperatingSystem.IsWindows() ? "jason.exe" : "jason");
        var carried = new RecordingRemover
        {
            PathEntry = new PathEntryPlan(Path.GetDirectoryName(installed)!, [], "carried", Ours: false, Persisted: "/home/a/.zprofile"),
        };

        await CliApp.RunAsync(["status"], Machine(dir, output, onPath: false, machine: carried, installPath: installed), Ct);

        var check = Assert.Single(Read(output).Checks, check => check.Name == "path");
        Assert.Equal(CheckState.Ok, check.State);
        Assert.Contains("/home/a/.zprofile", check.Fact, StringComparison.Ordinal);
    }

    /// <summary>
    /// And a block this installer wrote into a profile no login shell of this account reads answers nothing: bash
    /// does not open <c>~/.profile</c> once <c>~/.bash_profile</c> exists, and the check said <c>ok</c> there.
    /// </summary>
    [Fact]
    public async Task A_block_in_a_profile_no_login_shell_reads_is_not_a_path_a_new_shell_finds()
    {
        using var dir = new TempPaths();
        var output = new StringWriter();
        Running(dir);
        var installed = Path.Combine(dir.Paths.Root, "programs", "jason", OperatingSystem.IsWindows() ? "jason.exe" : "jason");
        var unread = new RecordingRemover
        {
            PathEntry = new PathEntryPlan(Path.GetDirectoryName(installed)!, ["/home/a/.profile"], null, Ours: true, Persisted: null),
        };

        var exit = await CliApp.RunAsync(["status"], Machine(dir, output, onPath: false, machine: unread, installPath: installed), Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        var check = Assert.Single(Read(output).Checks, check => check.Name == "path");
        Assert.Equal(CheckState.Failed, check.State);
        Assert.NotNull(check.Fix);
    }

    /// <summary>And where the account does not carry it either, it is the failure it always was, with the repair.</summary>
    [Fact]
    public async Task A_path_neither_this_shell_nor_this_account_carries_still_fails_with_its_repair()
    {
        using var dir = new TempPaths();
        var output = new StringWriter();
        Running(dir);
        var installed = Path.Combine(dir.Paths.Root, "programs", "jason", OperatingSystem.IsWindows() ? "jason.exe" : "jason");

        var exit = await CliApp.RunAsync(["status"], Machine(dir, output, onPath: false, machine: new RecordingRemover(), installPath: installed), Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        var check = Assert.Single(Read(output).Checks, check => check.Name == "path");
        Assert.Equal(CheckState.Failed, check.State);
        Assert.Contains(Path.GetDirectoryName(installed)!, check.Fix, StringComparison.Ordinal);
    }

    /// <summary>
    /// Each repair once, in the order it was first called for.
    /// </summary>
    /// <remarks>
    /// With no runtime, four required checks all want one started, and this printed
    /// <c>jason runtime start</c> four times — the first thing a new installation said to whoever had just
    /// installed it. The old guard asserted only that the heading was there, which is why it never noticed.
    /// </remarks>
    [Fact]
    public async Task Each_repair_is_printed_once_in_the_order_it_was_first_called_for()
    {
        using var dir = new TempPaths();
        var output = new StringWriter();

        await CliApp.RunAsync(["status", "--human"], Machine(dir, output), Ct);

        var lines = output.ToString().Split(Environment.NewLine, StringSplitOptions.None);
        var heading = Array.FindIndex(lines, line => line.Trim() == "To repair:");
        Assert.True(heading >= 0, output.ToString());

        var repairs = lines.Skip(heading + 1).Where(line => line.StartsWith("  ", StringComparison.Ordinal)).Select(line => line.Trim()).ToList();

        Assert.NotEmpty(repairs);
        Assert.Equal(repairs.Distinct(StringComparer.Ordinal), repairs);
        Assert.Contains("jason runtime start", repairs);

        // And the checks really did ask for it more than once, so this is not passing because there was
        // nothing to de-duplicate. Asked of the machine shape, since the prose above is not a document.
        var machine = new StringWriter();
        await CliApp.RunAsync(["status"], Machine(dir, machine), Ct);
        var asked = Read(machine).Checks.Count(check => check.Fix == "jason runtime start");
        Assert.True(asked > 1, $"only {asked} check asked for a runtime to be started, so nothing was de-duplicated.");
    }

    /// <summary>A deployment record in a root, as `jason skills install` leaves one.</summary>
    private static void Deploy(string root)
    {
        var skill = Path.Combine(root, "researcher", "SKILL.md");
        Directory.CreateDirectory(Path.GetDirectoryName(skill)!);
        File.WriteAllText(skill, "---" + Environment.NewLine + "name: researcher" + Environment.NewLine + "---" + Environment.NewLine);
        SkillsRecord.Write(root, new SkillsRecord(
            SkillsRecord.CurrentVersion,
            [new SkillsDeployment("jason-runtime-skills", "/somewhere", "v0.1.0", false, null, DateTimeOffset.UnixEpoch,
                [new DeployedFile("researcher/SKILL.md", SkillsRecord.Digest(skill))])]));
    }

    /// <summary>The human shape says the same thing, and prints the repairs under their own heading.</summary>
    [Fact]
    public async Task The_human_shape_says_whether_it_is_ready_and_what_would_repair_it()
    {
        using var dir = new TempPaths();
        var output = new StringWriter();

        var exit = await CliApp.RunAsync(["status", "--human"], Machine(dir, output), Ct);

        var text = output.ToString();
        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Contains("Not ready.", text, StringComparison.Ordinal);
        Assert.Contains("To repair:", text, StringComparison.Ordinal);
        Assert.Contains("jason runtime start", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// A roster that could not be read is unknown, never ok.
    /// </summary>
    /// <remarks>
    /// Turning a failed <c>role.list</c> into "zero seeded roles" made this check answer "0 roles taught, all
    /// within the cap" and <c>ready: true</c> on a machine where every role launches untaught — the hole this
    /// increment exists to close, reported as closed, by the one verb whose whole job is to be trusted about
    /// readiness. Everything else unreadable in this verb answers unknown, and so does this.
    /// </remarks>
    [Fact]
    public async Task A_roster_that_could_not_be_read_is_unknown_and_never_ready()
    {
        using var dir = new TempPaths();
        var output = new StringWriter();
        Running(dir, rosterFails: true);

        var exit = await CliApp.RunAsync(["status"], Machine(dir, output), Ct);

        var report = Read(output);
        var skills = Assert.Single(report.Checks, check => check.Name == "role_skills");
        Assert.Equal(CheckState.Unknown, skills.State);
        Assert.False(report.Ready);
        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.DoesNotContain("all within", skills.Fact, StringComparison.Ordinal);
    }

    /// <summary>
    /// Named by the operator and by nobody else. Which provider somebody uses is theirs, and a build that knew
    /// one vendor's command by heart would be this product naming a vendor in its own sources, which A7(a)
    /// forbids and a guard reads off them.
    /// </summary>
    [Fact]
    public async Task With_no_provider_named_nothing_is_run_and_the_check_says_how_to_name_one()
    {
        using var dir = new TempPaths();
        var output = new StringWriter();
        Running(dir);
        var runner = new FakeProgramRunner();

        await CliApp.RunAsync(["status"], Machine(dir, output, runner), Ct);

        var provider = Assert.Single(Read(output).Checks, check => check.Name == "provider_cli");
        Assert.Equal(CheckState.Absent, provider.State);
        Assert.Contains("--provider-cli", provider.Fact, StringComparison.Ordinal);
        Assert.Empty(runner.Requested);
    }

    /// <summary>And the partition is published in help, because an exit code means nothing until it is.</summary>
    [Fact]
    public async Task Help_publishes_which_checks_are_required_and_that_this_verb_never_exits_three()
    {
        using var dir = new TempPaths();
        var output = new StringWriter();

        await CliApp.RunAsync(["status", "--help"], new CliEnvironment(output, new StringWriter(), dir.Paths), Ct);

        var text = output.ToString();
        Assert.Contains("Required", text, StringComparison.Ordinal);
        Assert.Contains("Optional", text, StringComparison.Ordinal);
        Assert.Contains("Never 3", text, StringComparison.Ordinal);
        Assert.Contains("--provider-cli", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// And the path check's rule as it now is, in the verb's own help: the old-shell <c>ok</c>, which profile a
    /// login shell reads, and — the part a caller has to act on — that an agent host started before the install
    /// keeps failing a bare <c>jason</c> while <c>ready</c> is true.
    /// </summary>
    /// <remarks>
    /// The help still said only "'jason' resolves on PATH" after the check had learned to answer <c>ok</c> for a
    /// shell older than the installation. An agent reading it would take <c>ready: true</c> to mean its own next
    /// <c>jason</c> works.
    /// </remarks>
    [Fact]
    public async Task Help_publishes_the_path_rule_as_it_is()
    {
        using var dir = new TempPaths();
        var output = new StringWriter();

        await CliApp.RunAsync(["status", "--help"], new CliEnvironment(output, new StringWriter(), dir.Paths), Ct);

        var text = string.Join(' ', output.ToString().Split((char[])[' ', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries));
        Assert.Contains("in every shell started from now on", text, StringComparison.Ordinal);
        Assert.Contains("~/.bash_profile", text, StringComparison.Ordinal);
        Assert.Contains("an agent host started before the install", text, StringComparison.Ordinal);
        Assert.Contains("while ready is true", text, StringComparison.Ordinal);
        Assert.Contains("PowerShell on Windows", text, StringComparison.Ordinal);
    }

    private static DeployedRoleSkill Taught(string role) => new(role, 4096, null);

    private static StatusReport Read(StringWriter output) =>
        JsonSerializer.Deserialize<StatusReport>(output.ToString(), JasonJson.Options)!;

    /// <summary>A descriptor on disk and a runtime behind it answering the three operations status asks.</summary>
    /// <summary>A descriptor and a runtime behind it. <paramref name="roles"/> null makes role.list fail.</summary>
    private static void Running(TempPaths dir, IReadOnlyList<string>? roles = null, SkillsInfo? skills = null, bool rosterFails = false)
    {
        dir.WriteDescriptor(RuntimeVerbs.Descriptor("rt_01J"));
        Answers[dir.Paths.Root] = (
            rosterFails ? null : roles ?? ["researcher"],
            skills ?? new SkillsInfo("/data/skills/roles", 1048576, [Taught("researcher")]));
    }

    private static readonly Dictionary<string, (IReadOnlyList<string>? Roles, SkillsInfo Skills)> Answers = [];

    /// <summary>
    /// The machine these run against: this test's own data directory, a runtime that exists only in a handler,
    /// a process table that launches nothing, a recording registrar, a harness root inside the test's tree, a
    /// runner that runs nothing -- and a PATH of this test's own.
    /// </summary>
    /// <remarks>
    /// The last one is not a nicety. Whether `jason` resolves by name is a fact about the machine, and reading
    /// the real PATH would make this suite report the developer's machine rather than the installation under
    /// test: green where somebody has Jason installed and red on every CI runner, for the same code.
    /// </remarks>
    private static CliEnvironment Machine(
        TempPaths dir,
        StringWriter output,
        IProgramRunner? programs = null,
        IHarnessLocator? harnesses = null,
        bool onPath = true,
        IInstallationRemover? machine = null,
        string? installPath = null) =>
        new(
            output,
            new StringWriter(),
            dir.Paths,
            new FakeHandler(request => Answer(dir, request)),
            Processes: new FakeProcessControl(),
            InstallPath: installPath,
            Autostart: new RecordingRegistrar(),
            Harnesses: harnesses ?? HarnessLocators.At(Path.Combine(dir.Paths.Root, "no-harness-here")),
            Programs: programs ?? new FakeProgramRunner(_ => new ProgramResult(-1, string.Empty, "not found", false)),
            SearchPath: onPath ? Installed(dir) : Path.Combine(dir.Paths.Root, "nowhere"),
            Removes: machine);

    /// <summary>A directory on this test's PATH with a file in it that a bare `jason` would resolve to.</summary>
    private static string Installed(TempPaths dir)
    {
        var bin = Directory.CreateDirectory(Path.Combine(dir.Paths.Root, "bin")).FullName;
        File.WriteAllText(Path.Combine(bin, OperatingSystem.IsWindows() ? "jason.exe" : "jason"), string.Empty);
        return bin;
    }

    private static HttpResponseMessage Answer(TempPaths dir, HttpRequestMessage request)
    {
        if (!Answers.TryGetValue(dir.Paths.Root, out var answers))
        {
            throw new HttpRequestException("connection refused");
        }

        var route = request.RequestUri!.AbsolutePath;
        if (route.EndsWith(Operations.SystemInfo, StringComparison.Ordinal))
        {
            return RuntimeVerbs.Response(HttpStatusCode.OK, JsonSerializer.Serialize(Info(answers.Skills), JasonJson.Options));
        }

        if (route.EndsWith(Operations.RoleList, StringComparison.Ordinal))
        {
            if (answers.Roles is null)
            {
                return RuntimeVerbs.Response(HttpStatusCode.InternalServerError, "{}");
            }

            var page = new Page<RoleDto>(
                [.. answers.Roles.Select(name => new RoleDto($"rol_{name}", name, true, null, [], new System.Text.Json.Nodes.JsonObject(), null, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch))],
                null);
            return RuntimeVerbs.Response(HttpStatusCode.OK, JsonSerializer.Serialize(page, JasonJson.Options));
        }

        if (route.EndsWith(Operations.RouteList, StringComparison.Ordinal))
        {
            var routes = new RoutesDto("rts_EMPTY", "snp_EMPTY", new RouteSetDto(null, new Dictionary<string, RouteDto>()), []);
            return RuntimeVerbs.Response(HttpStatusCode.OK, JsonSerializer.Serialize(routes, JasonJson.Options));
        }

        throw new HttpRequestException($"nothing answers {route} here");
    }

    private static SystemInfoResponse Info(SkillsInfo skills) =>
        new(
            "0.1.0-dev",
            "v1",
            "rt_01J",
            77,
            DateTimeOffset.UnixEpoch,
            "/data",
            new DatabaseInfo(["20260913225419_InitialCreate"], [], null),
            new DispatcherInfo(DispatcherState.Running, 10, 4, 0, null, 0, 0),
            new PluginsInfo(0, "snp_EMPTY", DateTimeOffset.UnixEpoch, true),
            new RoutesInfo("rts_EMPTY", DateTimeOffset.UnixEpoch, null, 0, 0),
            null,
            skills);
}
