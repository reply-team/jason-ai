using Jason.Cli.Status;
using Jason.Cli.Tests.Commands;
using Jason.Contracts.Api;

namespace Jason.Cli.Tests.Status;

/// <summary>
/// jason status says which UI is served, or that none is; the UI is optional, so the check never fails readiness. Where
/// the release's own build is missing, or is another release's, it names the command that places this one's — but only
/// while the runtime is this CLI's build: between a package manager's upgrade and the restart, the restart is the fix,
/// and the runtime_version check names it.
/// </summary>
public class UiModuleCheckTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly UiModuleInfo Default = new("default", "/home/ada/.jason/ui/default", "0.10.0", "v1", true, "sha256:abc", null);

    private static UiModuleInfo Served(string source, string version, string api = "v1") =>
        new(source, $"/home/ada/.jason/ui/{source}", version, api, api == "v1", "sha256:abc", null);

    public static TheoryData<UiModuleInfo?, string, string, CheckState, string, string?> Cases() => new()
    {
        { null, StatusCommandTests.Version, StatusCommandTests.Version, CheckState.Absent, "No UI is installed.", "jason ui install" },
        { Default, StatusCommandTests.Version, StatusCommandTests.Version, CheckState.Ok, "default 0.10.0", null },
        { new UiModuleInfo("custom", "/home/ada/.jason/ui/custom", "mine", "v0", false, "sha256:abc", null), StatusCommandTests.Version, StatusCommandTests.Version, CheckState.Failed, "built for API v0", null },
        { Served("default", "0.13.0", "v0"), "0.13.0", "0.13.0", CheckState.Failed, "built for API v0", "jason ui install" },
        { new UiModuleInfo("default", "/home/ada/.jason/ui/default", null, null, false, null, "'/home/ada/.jason/ui/custom' holds an index.html but no manifest.json, so it is not served."), StatusCommandTests.Version, StatusCommandTests.Version, CheckState.Failed, "no manifest.json", null },
        { Served("default", "0.12.0"), "0.13.0", "0.13.0", CheckState.Failed, "Serving default 0.12.0 from /home/ada/.jason/ui/default; this runtime is 0.13.0.", "jason ui install" },
        { Served("default", "0.13.0"), "0.13.0+1a2b3c4", "0.13.0+1a2b3c4", CheckState.Ok, "default 0.13.0", null },
        { Served("custom", "0.12.0"), "0.13.0", "0.13.0", CheckState.Ok, "custom 0.12.0", null },
        { Served("default", "0.12.0"), "0.13.0-dev", "0.13.0-dev", CheckState.Ok, "default 0.12.0", null },
        { Served("default", "0.12.0"), "0.13.0", "0.14.0", CheckState.Ok, "default 0.12.0", null },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task The_check_says_which_ui_is_served_and_never_decides_readiness(UiModuleInfo? ui, string runtime, string build, CheckState state, string fact, string? fix)
    {
        using var dir = new TempPaths();
        StatusCommandTests.Running(dir, version: runtime, ui: ui);
        var output = new StringWriter();
        await CliApp.RunAsync(["status"], StatusCommandTests.Machine(dir, output, build: build), Ct);

        var check = Assert.Single(StatusCommandTests.Read(output).Checks, c => c.Name == "ui_module");
        Assert.Equal(state, check.State);
        Assert.False(check.Required);
        Assert.Contains(fact, check.Fact, StringComparison.Ordinal);
        Assert.Equal(fix, check.Fix);
    }

    [Fact]
    public async Task Human_runtime_status_says_the_port_and_the_ui()
    {
        const string Info = """
            {"runtime_version":"0.10.0-dev","api_version":"v1","instance_id":"rt_LIVE","pid":77,"started_at":"2026-10-16T07:02:00.000Z","data_dir":"/home/ada/.jason",
             "database":{"applied_migrations":["a"],"newly_applied":[],"backup_file":null},
             "dispatcher":{"state":"running","tick_seconds":10,"max_parallel":8,"running_attempts":0,"last_scan_at":null,"scans":1,"summons":0},
             "plugins":{"active_count":0,"snapshot_id":"snp_1","loaded_at":"2026-10-16T07:02:00.000Z","last_reload_activated":null},
             "routes":{"snapshot_id":"rts_1","activated_at":"2026-10-16T07:02:00.000Z","global_default_plugin":null,"global_override_count":0,"campaign_route_count":0},
             "listening":{"port":52767,"configured_port":52766},
             "ui":{"source":"default","directory":"/home/ada/.jason/ui/default","version":"0.10.0","api_version":"v1","api_version_matches":true,"digest":"sha256:abc","problem":null}}
            """;
        using var cli = new CliRun(Info);
        await cli.RunAsync("runtime", "status", "--human");
        Assert.Contains("Port:       52767 (52766 was taken)", cli.Text, StringComparison.Ordinal);
        Assert.Contains("UI:         default 0.10.0 from /home/ada/.jason/ui/default, API v1 (this runtime's), sha256:abc", cli.Text, StringComparison.Ordinal);
    }
}
