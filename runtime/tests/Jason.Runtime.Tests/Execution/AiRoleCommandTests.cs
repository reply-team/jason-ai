using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Contracts.Execution;
using Jason.Contracts.Harness;
using Jason.Contracts.Json;
using Jason.Runtime.Configuration;
using Jason.Runtime.Execution;
using Jason.Runtime.Execution.Hosts;
using Jason.Runtime.Harness;
using Jason.Runtime.Hosting;
using Jason.Runtime.Tests.Harness;
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
    [Critical(Family.S3, "the token never reaches a child's environment")]
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
    [Critical(Family.S3, "a leaked token is redacted everywhere")]
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
    [Critical(Family.W7, "the launch envelope on stdin")]
    public async Task The_envelope_the_child_reads_is_the_one_the_contract_describes()
    {
        using var directory = new TempDataDir();
        var workDir = directory.Paths.AttemptWorkDirectory(WorkItemId, AttemptId);

        await RunAsync(directory.Paths, workDir, FakeAgentHost.EntryCommand("echo-envelope"), projectId: "prj_one");

        var echoed = await File.ReadAllTextAsync(Path.Combine(workDir, "stdout.log"), Ct);
        Assert.Contains("\"envelope_version\":4", echoed, StringComparison.Ordinal);
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

        // Where the role's notes are at its three levels: the campaign's, its project's, and the client's, which names neither.
        Assert.Equal(new RoleMemoryLocation("cmp_one", "researcher", "prj_one"), envelope.RoleMemory);
    }

    /// <summary>
    /// The client's memory file is named where Jason knows the root is its own, and that is known by the root's marker
    /// alone, which no record holds: a folder there with no marker, one marked as something else — a project, or a root
    /// that names an id, which no root marker this runtime writes does — or none at all names nothing. A marker the disk
    /// will not give up, held open by another program or denied to this account, names nothing either, and the launch
    /// goes ahead without it. The marker is all that is read — the root marked here has no <c>AGENTS.md</c>, and its path
    /// is given all the same — and nothing is made, taken or written at the root by a launch.
    /// </summary>
    [Theory]
    [InlineData("marked", true)]
    [InlineData("unmarked", false)]
    [InlineData("marked as a project", false)]
    [InlineData("marked as a root that names an id", false)]
    [InlineData("marked, and the marker cannot be read", false)]
    [InlineData("absent", false)]
    [Critical(Family.W7, "the client's memory file is named only where the root carries its marker")]
    public async Task The_client_s_memory_file_is_named_only_where_the_root_carries_its_marker(string root, bool named)
    {
        using var directory = new TempDataDir();
        var workDir = directory.Paths.AttemptWorkDirectory(WorkItemId, AttemptId);
        var folders = TestHarness.Folders(directory.Home);
        var at = folders.Root;
        switch (root)
        {
            case "marked":
            case "marked, and the marker cannot be read":
                Mark(at, new HarnessMarker(HarnessMarker.Root, null));
                break;
            case "unmarked":
                Directory.CreateDirectory(at);
                break;
            case "marked as a project":
                Mark(at, new HarnessMarker(HarnessMarker.Project, "prj_one"));
                break;
            case "marked as a root that names an id":
                Mark(at, new HarnessMarker(HarnessMarker.Root, "prj_one"));
                break;
        }

        var before = Entries(at);

        CommandOutcome outcome;
        using (root == "marked, and the marker cannot be read" ? Unreadable(HarnessMarker.PathIn(at)) : null)
        {
            outcome = await RunAsync(directory.Paths, workDir, FakeAgentHost.EntryCommand("echo-envelope"), folders: folders);
        }

        Assert.True(outcome is CommandOutcome.Exited { ExitCode: 0 }, $"With the root {root}, the launch ended as {outcome}.");
        var memory = Envelope(workDir).MemoryFiles;
        Assert.NotNull(memory);
        Assert.Equal(named ? Path.Combine(at, "AGENTS.md") : null, memory.Client);
        Assert.Equal(before, Entries(at));
    }

    /// <summary>
    /// A project's and a campaign's memory files are where their folders were recorded, and a level with no folder
    /// recorded is null beside the others, so the object is there whatever is known. A recorded folder is named without
    /// a look: the folders recorded here were never made, and a stale one is the host's to find.
    /// </summary>
    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [Critical(Family.W7, "the memory files are the recorded folders, never looked at, and the object is always there")]
    public async Task A_project_s_and_a_campaign_s_memory_files_are_their_recorded_folders_named_without_a_look(bool project, bool campaign)
    {
        using var directory = new TempDataDir();
        var workDir = directory.Paths.AttemptWorkDirectory(WorkItemId, AttemptId);
        var projectFolder = Path.Combine(directory.Home, "Jason", "projects", "acme-corp");
        var campaignFolder = Path.Combine(projectFolder, "campaigns", "q3-marketing-leads");

        await RunAsync(
            directory.Paths,
            workDir,
            FakeAgentHost.EntryCommand("echo-envelope"),
            projectFolder: project ? projectFolder : null,
            campaignFolder: campaign ? campaignFolder : null);

        var echoed = await File.ReadAllTextAsync(Path.Combine(workDir, "stdout.log"), Ct);
        var memory = Envelope(workDir).MemoryFiles;
        Assert.NotNull(memory);
        Assert.Null(memory.Client);
        Assert.Equal(project ? Path.Combine(projectFolder, "AGENTS.md") : null, memory.Project);
        Assert.Equal(campaign ? Path.Combine(campaignFolder, "AGENTS.md") : null, memory.Campaign);
        Assert.False(Directory.Exists(projectFolder), $"a launch made the folder recorded at '{projectFolder}'");
        if (!project)
        {
            Assert.Contains("\"memory_files\":{\"client\":null,\"project\":null,\"campaign\":null}", echoed, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The envelope is ASCII on the wire whatever its context holds: every character outside ASCII crosses as an escape.
    /// That is a second defence beside the encoding the door chooses, the one an agent that read its input in another
    /// page would still survive. The child reads the bytes itself and answers with their code points.
    /// </summary>
    [Fact]
    public async Task The_envelope_is_ascii_on_the_wire_whatever_its_context_holds()
    {
        using var directory = new TempDataDir();
        var workDir = directory.Paths.AttemptWorkDirectory(WorkItemId, AttemptId);

        await RunAsync(directory.Paths, workDir, [Plugins.FakeProviderCli.ExecutablePath, "stdin-codepoints"], brief: new JsonObject { ["note"] = "José 😀" });

        var wire = Plugins.FakeProviderCli.FromCodepoints(await File.ReadAllTextAsync(Path.Combine(workDir, "stdout.log"), Ct));
        Assert.True(wire.Length > 0 && wire.All(char.IsAscii), $"The envelope crossed as '{wire}'.");
        Assert.Equal("José 😀", (string?)JsonNode.Parse(wire)!["context"]!["note"]);
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
        RolesOptions? roles = null,
        JsonObject? brief = null,
        HarnessFolders? folders = null,
        string? projectId = null,
        string? projectFolder = null,
        string? campaignFolder = null)
    {
        var command = new AiRoleCommand(
            paths,
            new TokenRedactor(new RuntimeSecrets(Token)),
            TestOptions.RoleSettings(roles),
            folders ?? TestHarness.Folders(TempDataDir.HomeOf(paths.Root)),
            NullLogger<AiRoleCommand>.Instance);
        var context = new CommandContext(
            WorkItemId,
            AttemptId,
            2,
            "cmp_one",
            "cnt_one",
            WorkItemKind.AiRole,
            "researcher",
            null,
            brief ?? new JsonObject { ["goal"] = "find the decision maker" },
            null,
            new EffectiveLimits(1800, 120, 3),
            new DateTimeOffset(2026, 9, 14, 10, 0, 0, TimeSpan.Zero),
            entryCommand,
            workDir,
            kill?.Token ?? CancellationToken.None,
            Deny: deny,
            ProjectId: projectId,
            ProjectFolderPath: projectFolder,
            CampaignFolderPath: campaignFolder);

        return command.RunAsync(context, Ct);
    }

    /// <summary>The envelope the host echoed to its standard output.</summary>
    private static LaunchEnvelope Envelope(string workDir) =>
        JsonSerializer.Deserialize<LaunchEnvelope>(File.ReadAllText(Path.Combine(workDir, "stdout.log")), JasonJson.Options)!;

    /// <summary>A folder at <paramref name="folder"/> with <paramref name="marker"/> as its marker, and nothing else in it.</summary>
    private static void Mark(string folder, HarnessMarker marker)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(HarnessMarker.PathIn(folder))!);
        File.WriteAllBytes(HarnessMarker.PathIn(folder), marker.Bytes());
    }

    /// <summary>
    /// <paramref name="file"/> kept from this account until disposed: on Windows held open for this test alone, so a read
    /// meets a sharing violation, as it does while another program holds the file; elsewhere stripped of every permission,
    /// so a read meets a denial. An account that reads a file whatever its mode says, as root does, has no denial to meet.
    /// </summary>
    private static IDisposable Unreadable(string file)
    {
        if (OperatingSystem.IsWindows())
        {
            return new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }

        var denied = DenyByMode(file);
        if (Readable(file))
        {
            denied.Dispose();
            Assert.Skip("This account reads a file whatever its mode says, as root does, so there is no denial to meet.");
        }

        return denied;
    }

    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    private static Lifted DenyByMode(string file)
    {
        var mode = File.GetUnixFileMode(file);
        File.SetUnixFileMode(file, UnixFileMode.None);
        return new Lifted(() => LiftByMode(file, mode));
    }

    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    private static void LiftByMode(string file, UnixFileMode mode) => File.SetUnixFileMode(file, mode);

    private static bool Readable(string file)
    {
        try
        {
            using var read = File.OpenRead(file);
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private sealed class Lifted(Action lift) : IDisposable
    {
        public void Dispose() => lift();
    }

    /// <summary>Everything below <paramref name="folder"/>, by its path there, with each file's size; empty where there is no folder.</summary>
    private static List<string> Entries(string folder) =>
        Directory.Exists(folder)
            ? [.. new DirectoryInfo(folder).EnumerateFileSystemInfos("*", SearchOption.AllDirectories)
                .Select(entry => $"{Path.GetRelativePath(folder, entry.FullName)} {(entry as FileInfo)?.Length}")
                .Order(StringComparer.Ordinal)]
            : [];

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
