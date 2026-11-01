using Jason.Cli.Status;
using Jason.Cli.Tests.Commands;
using Jason.Contracts.Api;

namespace Jason.Cli.Tests.Status;

/// <summary>
/// jason status and jason runtime status say the harness root the runtime uses and what stands there, as the runtime
/// reports it: the CLI never works the root out for itself. The root is made at the first project or campaign, so the
/// check never decides readiness; it fails where the runtime can tell that first one would be refused, and no more.
/// </summary>
public class HarnessRootCheckTests
{
    private const string Root = "/home/marta/Jason";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<HarnessRootState?, CheckState, string> Cases() => new()
    {
        { HarnessRootState.Absent, CheckState.Ok, $"The harness root is {Root}; it is made at the first project or campaign." },
        { HarnessRootState.Marked, CheckState.Ok, $"The harness root is {Root}, with its marker." },
        { HarnessRootState.Unmarked, CheckState.Ok, $"The harness root is {Root}, a folder with no marker yet" },
        { HarnessRootState.Taken, CheckState.Failed, $"The harness root {Root} is marked as something else, so the first project or campaign is refused until that is repaired: set Harness:Root to another folder, or move that folder away." },
        { HarnessRootState.NotAFolder, CheckState.Failed, $"A file stands at the harness root {Root}, so the first project or campaign is refused until that is repaired: set Harness:Root to a folder, or move the file away." },
        { HarnessRootState.Unreadable, CheckState.Failed, $"The harness root {Root} cannot be listed or read by this account, so the first project or campaign is refused until that is repaired: let this account read and write that folder, or set Harness:Root to one it can." },
        { null, CheckState.Unknown, "does not say which harness root it uses" },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task The_check_says_the_root_the_runtime_reports_and_never_decides_readiness(HarnessRootState? state, CheckState expected, string fact)
    {
        using var dir = new TempPaths();
        StatusCommandTests.Running(dir, harness: state is { } standing ? new HarnessInfo(Root, standing) : null);
        var output = new StringWriter();

        await CliApp.RunAsync(["status"], StatusCommandTests.Machine(dir, output), Ct);

        var check = Assert.Single(StatusCommandTests.Read(output).Checks, c => c.Name == "harness_root");
        Assert.Equal(expected, check.State);
        Assert.False(check.Required);
        Assert.Contains(fact, check.Fact, StringComparison.Ordinal);
        Assert.Null(check.Fix);
    }

    [Fact]
    public async Task A_runtime_that_does_not_answer_leaves_the_root_unknown()
    {
        using var dir = new TempPaths();
        var output = new StringWriter();

        await CliApp.RunAsync(["status"], StatusCommandTests.Machine(dir, output), Ct);

        var check = Assert.Single(StatusCommandTests.Read(output).Checks, c => c.Name == "harness_root");
        Assert.Equal(CheckState.Unknown, check.State);
    }

    [Theory]
    [InlineData("""{"root":"/home/marta/Jason","root_state":"marked"}""", "Harness:    /home/marta/Jason, with its marker")]
    [InlineData("""{"root":"/home/marta/Jason","root_state":"absent"}""", "Harness:    /home/marta/Jason, made at the first project or campaign")]
    [InlineData("""{"root":"/home/marta/Jason","root_state":"taken"}""", "Harness:    /home/marta/Jason, marked as something else: the first project or campaign is refused until Harness:Root names another folder or that one is moved away")]
    [InlineData("""{"root":"/home/marta/Jason","root_state":"not_a_folder"}""", "Harness:    /home/marta/Jason, a file: the first project or campaign is refused until Harness:Root names a folder or the file is moved away")]
    [InlineData("""{"root":"/home/marta/Jason","root_state":"unreadable"}""", "Harness:    /home/marta/Jason, which this account cannot list or read: the first project or campaign is refused until this account may read and write it or Harness:Root names a folder it can")]
    [InlineData("""{"root":"/home/marta/Jason","root_state":"unmarked"}""", "Harness:    /home/marta/Jason, a folder with no marker yet, taken as the root at the first project or campaign")]
    [InlineData("null", "Harness:    unknown: the runtime is older than this CLI")]
    public async Task Human_runtime_status_says_the_harness_root(string harness, string line)
    {
        var info = $$"""
            {"runtime_version":"0.15.0-dev","api_version":"v1","instance_id":"rt_LIVE","pid":77,"started_at":"2026-10-16T07:02:00.000Z","data_dir":"/home/marta/.jason",
             "database":{"applied_migrations":["a"],"newly_applied":[],"backup_file":null},
             "dispatcher":{"state":"running","tick_seconds":10,"max_parallel":8,"running_attempts":0,"last_scan_at":null,"scans":1,"summons":0},
             "plugins":{"active_count":0,"snapshot_id":"snp_1","loaded_at":"2026-10-16T07:02:00.000Z","last_reload_activated":null},
             "routes":{"snapshot_id":"rts_1","activated_at":"2026-10-16T07:02:00.000Z","global_default_plugin":null,"global_override_count":0,"campaign_route_count":0},
             "harness":{{harness}}}
            """;
        using var cli = new CliRun(info);

        await cli.RunAsync("runtime", "status", "--human");

        Assert.Contains(line, cli.Text, StringComparison.Ordinal);
    }
}
