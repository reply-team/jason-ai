using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Contracts.Execution;
using Jason.Contracts.Json;
using Jason.Runtime.Configuration;
using Jason.Runtime.Execution;
using Jason.Runtime.Execution.Hosts;
using Jason.Runtime.Hosting;
using Jason.Runtime.WorkItems;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jason.Runtime.Tests.Execution;

/// <summary>
/// The launcher against a real child process. Nothing here needs a runtime: the point is what the operating
/// system sees — the envelope on stdin, the environment, the working directory, the pipes, the kill — and what
/// is left on disk afterwards.
/// </summary>
public class AiRoleCommandTests
{
    private const string Token = "tok-4f3a-canary";
    private const string WorkItemId = "wi_one";
    private const string AttemptId = "att_one";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_host_that_exits_quietly_is_reported_with_its_exit_code_and_its_output_kept()
    {
        using var directory = new TempDataDir();
        var workDir = directory.Paths.AttemptWorkDirectory(WorkItemId, AttemptId);

        var outcome = await RunAsync(directory.Paths, workDir, FakeAgentHost.EntryCommand("silent"));

        var exited = Assert.IsType<CommandOutcome.Exited>(outcome);
        Assert.Equal(0, exited.ExitCode);
        Assert.NotNull(exited.Launch.Pid);
        Assert.Equal(0, exited.Launch.ExitCode);
        Assert.Equal(workDir, exited.Launch.WorkDir);

        var stderr = await File.ReadAllTextAsync(Path.Combine(workDir, "stderr.log"), Ct);
        Assert.Contains("behaviour=silent", stderr, StringComparison.Ordinal);
        Assert.Contains("data-dir=set", stderr, StringComparison.Ordinal);

        // The host reports the directory it really runs in. On macOS the temporary directory is reached through
        // a symbolic link, so the spelling can differ from the path the test built; the directory is the same
        // one when the launcher's own files are visible through it.
        var cwd = CurrentDirectoryReportedIn(stderr);
        Assert.True(File.Exists(Path.Combine(cwd, "stderr.log")), $"The host ran in '{cwd}', not in the attempt's work directory '{workDir}'.");
        Assert.EndsWith(Path.Combine(WorkItemId, AttemptId), cwd.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(Path.Combine(workDir, "stdout.log")));
    }

    [Fact]
    public async Task A_host_that_dies_hands_back_the_exit_code_and_the_tail_of_what_it_said()
    {
        using var directory = new TempDataDir();
        var workDir = directory.Paths.AttemptWorkDirectory(WorkItemId, AttemptId);

        var outcome = await RunAsync(directory.Paths, workDir, FakeAgentHost.EntryCommand("crash"));

        var exited = Assert.IsType<CommandOutcome.Exited>(outcome);
        Assert.Equal(3, exited.ExitCode);
        Assert.Equal(3, exited.Launch.ExitCode);
        Assert.Contains("behaviour=crash", exited.StderrTail!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_kill_signal_ends_the_child_and_the_outcome_says_so()
    {
        using var directory = new TempDataDir();
        var workDir = directory.Paths.AttemptWorkDirectory(WorkItemId, AttemptId);
        using var kill = new CancellationTokenSource();
        kill.CancelAfter(TimeSpan.FromMilliseconds(500));

        var outcome = await RunAsync(directory.Paths, workDir, FakeAgentHost.EntryCommand("mute"), kill);

        var killed = Assert.IsType<CommandOutcome.Killed>(outcome);
        Assert.NotNull(killed.Launch);
        await AssertGoneAsync(killed.Launch!.Pid!.Value);
    }

    [Fact]
    public async Task An_entry_command_that_names_nothing_startable_is_a_launch_failure()
    {
        using var directory = new TempDataDir();
        var workDir = directory.Paths.AttemptWorkDirectory(WorkItemId, AttemptId);

        var outcome = await RunAsync(directory.Paths, workDir, ["definitely-not-a-program-7f3a"]);

        var failed = Assert.IsType<CommandOutcome.LaunchFailed>(outcome);
        Assert.NotEmpty(failed.Message);
        Assert.Null(failed.Launch.Pid);
        Assert.Equal(["definitely-not-a-program-7f3a"], failed.Launch.EntryCommand);
    }

    [Fact]
    public async Task A_role_with_no_entry_command_is_a_launch_failure_before_any_process_exists()
    {
        using var directory = new TempDataDir();
        var workDir = directory.Paths.AttemptWorkDirectory(WorkItemId, AttemptId);

        var outcome = await RunAsync(directory.Paths, workDir, []);

        var failed = Assert.IsType<CommandOutcome.LaunchFailed>(outcome);
        Assert.Equal("The role has no entry command.", failed.Message);
        Assert.Null(failed.Launch.Pid);
    }

    [Fact]
    public async Task The_capability_token_is_nowhere_in_the_child_environment()
    {
        using var directory = new TempDataDir();
        WriteDescriptor(directory.Paths);
        var workDir = directory.Paths.AttemptWorkDirectory(WorkItemId, AttemptId);

        await RunAsync(directory.Paths, workDir, FakeAgentHost.EntryCommand("silent"));

        var stderr = await File.ReadAllTextAsync(Path.Combine(workDir, "stderr.log"), Ct);
        Assert.Contains("token-in-env=false", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_host_that_spills_the_token_leaves_none_of_it_on_disk_or_in_the_outcome()
    {
        using var directory = new TempDataDir();
        WriteDescriptor(directory.Paths);
        var workDir = directory.Paths.AttemptWorkDirectory(WorkItemId, AttemptId);

        var outcome = await RunAsync(directory.Paths, workDir, FakeAgentHost.EntryCommand("leak"));

        var exited = Assert.IsType<CommandOutcome.Exited>(outcome);
        var stdout = await File.ReadAllTextAsync(Path.Combine(workDir, "stdout.log"), Ct);
        var stderr = await File.ReadAllTextAsync(Path.Combine(workDir, "stderr.log"), Ct);

        Assert.Contains($"token={TokenRedactor.Mask}", stdout, StringComparison.Ordinal);
        Assert.Contains($"token={TokenRedactor.Mask}", stderr, StringComparison.Ordinal);
        Assert.DoesNotContain(Token, stdout, StringComparison.Ordinal);
        Assert.DoesNotContain(Token, stderr, StringComparison.Ordinal);
        Assert.Contains(TokenRedactor.Mask, exited.StderrTail!, StringComparison.Ordinal);
        Assert.DoesNotContain(Token, exited.StderrTail!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_envelope_the_child_reads_is_the_one_the_contract_describes()
    {
        using var directory = new TempDataDir();
        var workDir = directory.Paths.AttemptWorkDirectory(WorkItemId, AttemptId);

        await RunAsync(directory.Paths, workDir, FakeAgentHost.EntryCommand("echo-envelope"));

        var echoed = await File.ReadAllTextAsync(Path.Combine(workDir, "stdout.log"), Ct);
        Assert.Contains("\"envelope_version\":3", echoed, StringComparison.Ordinal);
        Assert.Contains("\"work_dir\":", echoed, StringComparison.Ordinal);

        var envelope = JsonSerializer.Deserialize<LaunchEnvelope>(echoed, JasonJson.Options)!;
        Assert.Equal(LaunchEnvelope.CurrentVersion, envelope.EnvelopeVersion);
        Assert.Equal(AttemptId, envelope.AttemptId);
        Assert.Equal(2, envelope.AttemptNumber);
        Assert.Equal(WorkItemId, envelope.WorkItemId);
        Assert.Equal("cmp_one", envelope.CampaignId);
        Assert.Equal("cnt_one", envelope.ContactId);
        Assert.Equal(WorkItemKind.AiRole, envelope.Kind);
        Assert.Equal("researcher", envelope.Role);
        Assert.Equal("find the decision maker", (string?)envelope.Context["goal"]);
        Assert.Equal(1800, envelope.TimeoutSeconds);
        Assert.Equal(120, envelope.HeartbeatSeconds);
        Assert.Equal(workDir, envelope.WorkDir);
        Assert.Equal(directory.Paths.DescriptorFile, envelope.Runtime.DescriptorFile);
        Assert.Equal(ApiVersion.Current, envelope.Runtime.ApiVersion);

        // The word the child calls home with, so it never has to guess one.
        Assert.Equal(ProgramResolver.DefaultCliCommand, envelope.Runtime.CliCommand);
    }

    [Fact]
    public async Task The_child_is_told_which_attempt_it_is_and_finds_the_cli_first_on_its_path()
    {
        using var directory = new TempDataDir();
        var workDir = directory.Paths.AttemptWorkDirectory(WorkItemId, AttemptId);

        await RunAsync(directory.Paths, workDir, FakeAgentHost.EntryCommand("silent"));

        var stderr = await File.ReadAllTextAsync(Path.Combine(workDir, "stderr.log"), Ct);
        Assert.Contains($"attempt-in-env={AttemptId}", stderr, StringComparison.Ordinal);
        Assert.Contains($"work-item-in-env={WorkItemId}", stderr, StringComparison.Ordinal);

        // Named first, so that the bare command word an agent is allowed to run reaches this build of Jason.
        var directoryOfThisBuild = ProgramResolver.ExecutableDirectory;
        Assert.NotNull(directoryOfThisBuild);
        Assert.Contains($"path-head={directoryOfThisBuild}", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_child_starts_in_a_directory_that_already_says_what_it_may_not_do()
    {
        using var directory = new TempDataDir();
        var workDir = directory.Paths.AttemptWorkDirectory(WorkItemId, AttemptId);

        await RunAsync(directory.Paths, workDir, FakeAgentHost.EntryCommand("silent"), deny: ["Bash(rm *)"]);

        var settings = await File.ReadAllTextAsync(Path.Combine(workDir, ".claude", "settings.json"), Ct);
        Assert.Contains("Bash(rm *)", settings, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_role_whose_skill_names_another_role_ends_the_attempt_before_any_process_exists()
    {
        using var directory = new TempDataDir();
        var workDir = directory.Paths.AttemptWorkDirectory(WorkItemId, AttemptId);
        await SkillAsync(directory.Paths, "---\nname: someone-else\ndescription: not this role\n---\n");

        var outcome = await RunAsync(directory.Paths, workDir, FakeAgentHost.EntryCommand("silent"));

        var failed = Assert.IsType<CommandOutcome.LaunchFailed>(outcome);
        Assert.Equal(AttemptErrors.RoleSkillInvalid, failed.Code);
        Assert.Null(failed.Launch.Pid);
        Assert.Contains("someone-else", failed.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(workDir, "stderr.log")), "a process was started for an attempt that was refused");
    }

    [Fact]
    public async Task A_role_is_taught_its_job_where_its_host_will_look_for_it()
    {
        using var directory = new TempDataDir();
        var workDir = directory.Paths.AttemptWorkDirectory(WorkItemId, AttemptId);
        await SkillAsync(directory.Paths, "---\nname: researcher\ndescription: finds the decision maker\n---\n\nthe job\n");

        var outcome = await RunAsync(directory.Paths, workDir, FakeAgentHost.EntryCommand("silent"));

        Assert.IsType<CommandOutcome.Exited>(outcome);
        Assert.True(File.Exists(Path.Combine(workDir, ".claude", "skills", "researcher", "SKILL.md")));
    }

    [Fact]
    public async Task A_host_that_says_more_than_the_maximum_has_its_transcript_cut_and_the_attempt_says_so()
    {
        using var directory = new TempDataDir();
        var workDir = directory.Paths.AttemptWorkDirectory(WorkItemId, AttemptId);

        // The envelope is far longer than this, and echo-envelope writes the whole of it to standard output.
        var outcome = await RunAsync(
            directory.Paths,
            workDir,
            FakeAgentHost.EntryCommand("echo-envelope"),
            roles: new RolesOptions { MaxStdoutBytes = 64 });

        var exited = Assert.IsType<CommandOutcome.Exited>(outcome);
        Assert.True(exited.Launch.StdoutTruncated);
        var transcript = await File.ReadAllTextAsync(Path.Combine(workDir, "stdout.log"), Ct);
        Assert.Contains("Roles:MaxStdoutBytes", transcript, StringComparison.Ordinal);

        // The exit code is the child's own and owes nothing to what was kept of its output.
        Assert.Equal(0, exited.ExitCode);
    }

    [Fact]
    public async Task A_host_inside_the_maximum_leaves_a_whole_transcript_and_the_attempt_says_nothing()
    {
        using var directory = new TempDataDir();
        var workDir = directory.Paths.AttemptWorkDirectory(WorkItemId, AttemptId);

        var outcome = await RunAsync(directory.Paths, workDir, FakeAgentHost.EntryCommand("echo-envelope"));

        var exited = Assert.IsType<CommandOutcome.Exited>(outcome);
        Assert.False(exited.Launch.StdoutTruncated);
        Assert.DoesNotContain(
            "Roles:MaxStdoutBytes",
            await File.ReadAllTextAsync(Path.Combine(workDir, "stdout.log"), Ct),
            StringComparison.Ordinal);
    }

    /// <summary>The kill signal is handed over as its source, not as a token: it is the attempt's, not the test's.</summary>
    private static Task<CommandOutcome> RunAsync(
        JasonPaths paths,
        string workDir,
        IReadOnlyList<string> entryCommand,
        CancellationTokenSource? kill = null,
        IReadOnlyList<string>? deny = null,
        RolesOptions? roles = null)
    {
        var command = new AiRoleCommand(paths, new TokenRedactor(new RuntimeSecrets(Token)), TestOptions.RoleSettings(roles), NullLogger<AiRoleCommand>.Instance);
        var context = new CommandContext(
            WorkItemId,
            AttemptId,
            2,
            "cmp_one",
            "cnt_one",
            WorkItemKind.AiRole,
            "researcher",
            null,
            new JsonObject { ["goal"] = "find the decision maker" },
            null,
            new EffectiveLimits(1800, 120, 3),
            new DateTimeOffset(2026, 9, 14, 10, 0, 0, TimeSpan.Zero),
            entryCommand,
            workDir,
            kill?.Token ?? CancellationToken.None,
            Deny: deny);

        return command.RunAsync(context, Ct);
    }

    /// <summary>The role's own skill, where the runtime keeps them.</summary>
    private static Task SkillAsync(JasonPaths paths, string text) =>
        File.WriteAllTextAsync(
            Path.Combine(Directory.CreateDirectory(Path.Combine(paths.RoleSkillsDirectory, "researcher")).FullName, "SKILL.md"),
            text,
            Ct);

    /// <summary>The <c>cwd=</c> value of the host's diagnostic line, which runs up to the next field.</summary>
    private static string CurrentDirectoryReportedIn(string stderr)
    {
        const string field = "cwd=";
        const string next = " token-in-env=";
        var start = stderr.IndexOf(field, StringComparison.Ordinal);
        Assert.True(start >= 0, "The host's diagnostic line does not report its working directory.");
        start += field.Length;
        var end = stderr.IndexOf(next, start, StringComparison.Ordinal);
        return (end < 0 ? stderr[start..] : stderr[start..end]).Trim();
    }

    private static void WriteDescriptor(JasonPaths paths)
    {
        Directory.CreateDirectory(paths.RunDirectory);
        // Port 9 is the discard port and nothing listens on it here: the host can read the token but never call.
        var descriptor = new RuntimeDescriptor(ApiVersion.Current, "0.1.0-dev", "ins_one", 1, "http://127.0.0.1:9", Token, DateTimeOffset.UtcNow);
        File.WriteAllText(paths.DescriptorFile, JsonSerializer.Serialize(descriptor, JasonJson.Options));
    }

    private static async Task AssertGoneAsync(int pid)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var process = Process.GetProcessById(pid);
                if (process.HasExited)
                {
                    return;
                }
            }
            catch (ArgumentException)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50), Ct);
        }

        Assert.Fail(string.Create(CultureInfo.InvariantCulture, $"process {pid} was still running five seconds after the kill"));
    }
}
