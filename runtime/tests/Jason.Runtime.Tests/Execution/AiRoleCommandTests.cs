using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Contracts.Execution;
using Jason.Contracts.Json;
using Jason.Runtime.Execution;
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
        Assert.Contains("\"envelope_version\":1", echoed, StringComparison.Ordinal);
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
    }

    /// <summary>The kill signal is handed over as its source, not as a token: it is the attempt's, not the test's.</summary>
    private static Task<CommandOutcome> RunAsync(JasonPaths paths, string workDir, IReadOnlyList<string> entryCommand, CancellationTokenSource? kill = null)
    {
        var command = new AiRoleCommand(paths, new TokenRedactor(new RuntimeSecrets(Token)), NullLogger<AiRoleCommand>.Instance);
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
            kill?.Token ?? CancellationToken.None);

        return command.RunAsync(context, Ct);
    }

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
