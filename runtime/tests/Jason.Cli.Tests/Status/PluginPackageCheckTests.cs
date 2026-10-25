using Jason.Cli.Status;
using Jason.Cli.Tests.Commands;
using Jason.Contracts.Api;

namespace Jason.Cli.Tests.Status;

/// <summary>
/// jason status says whether the installed plugin packages are the ones this runtime's release publishes. Optional, so it
/// never decides readiness: an earlier release's package fails it, and the fact carries the repair in words, since no
/// command installs a package; a package with the same version or a later one passes with a line that says it is a later release's or one built or edited here; no package is absent; and
/// a runtime older than the section, or one that did not answer, cannot tell.
/// </summary>
public class PluginPackageCheckTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly PluginPackageInfo Released = new("0.7.0", "sha256:released");

    private static ReleasedPluginInfo Reply(ReleasedPluginState state, PluginPackageInfo? installed) => new("reply", state, Released, installed);

    public static TheoryData<ReleasedPluginInfo, CheckState, string[]> Cases() => new()
    {
        {
            Reply(ReleasedPluginState.Current, Released),
            CheckState.Ok,
            ["reply 0.7.0 is installed, the package runtime 0.7.1 carries."]
        },
        {
            Reply(ReleasedPluginState.Behind, new PluginPackageInfo("0.6.1", "sha256:earlier")),
            CheckState.Failed,
            [
                "reply 0.6.1 is installed; runtime 0.7.1 carries reply 0.7.0, the package its release publishes.",
                "Step 3 of the wiring check prints the two lines that place it: the checksum match, and the unpack into /data/plugins/reply once the package there is moved away.",
                "Then run `jason plugin reload`.",
            ]
        },
        {
            Reply(ReleasedPluginState.Differs, new PluginPackageInfo("0.7.0", "sha256:edited")),
            CheckState.Ok,
            ["reply 0.7.0 is installed and is not the package runtime 0.7.1 carries (reply 0.7.0); it names the same version or a later one, so it is a later release's package or one built or edited on this machine."]
        },
        {
            Reply(ReleasedPluginState.NotInstalled, null),
            CheckState.Absent,
            ["reply is not installed; runtime 0.7.1 carries reply 0.7.0, the package its release publishes, and step 3 of the wiring check prints the lines that install it."]
        },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task The_check_says_how_the_installed_package_stands_and_never_decides_readiness(ReleasedPluginInfo package, CheckState state, string[] fact)
    {
        var report = await ReportAsync(released: [package]);
        var check = Assert.Single(report.Checks, c => c.Name == "plugin_package");

        // Optional whatever it says: a package behind its release fails the check and leaves the installation ready.
        Assert.True(report.Ready);
        Assert.Equal(state, check.State);
        Assert.False(check.Required);
        Assert.All(fact, line => Assert.Contains(line, check.Fact, StringComparison.Ordinal));
        Assert.Null(check.Fix);
    }

    /// <summary>A development build compares against its own tree, and the fact names the build it compared with.</summary>
    [Fact]
    public async Task A_released_package_under_a_development_build_is_behind_it_and_the_fact_names_the_build()
    {
        var check = await CheckAsync(
            released: [new ReleasedPluginInfo("reply", ReleasedPluginState.Behind, new PluginPackageInfo("0.7.1-dev", "sha256:tree"), Released)],
            version: "0.7.1-dev+1a2b3c4");

        Assert.Equal(CheckState.Failed, check.State);
        Assert.Contains("reply 0.7.0 is installed; runtime 0.7.1-dev+1a2b3c4 carries reply 0.7.1-dev", check.Fact, StringComparison.Ordinal);
    }

    [Fact]
    public async Task One_package_behind_fails_the_check_whatever_the_others_say()
    {
        var check = await CheckAsync(released:
        [
            new ReleasedPluginInfo("mailer", ReleasedPluginState.Current, new PluginPackageInfo("1.0.0", "sha256:m"), new PluginPackageInfo("1.0.0", "sha256:m")),
            Reply(ReleasedPluginState.Behind, new PluginPackageInfo("0.6.1", "sha256:earlier")),
        ]);

        Assert.Equal(CheckState.Failed, check.State);
        Assert.Contains("mailer 1.0.0 is installed, the package runtime 0.7.1 carries.", check.Fact, StringComparison.Ordinal);
        Assert.Contains("reply 0.6.1 is installed;", check.Fact, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_runtime_whose_release_carries_no_package_has_none_to_compare()
    {
        var check = await CheckAsync(released: []);

        Assert.Equal(CheckState.Absent, check.State);
        Assert.Equal("Runtime 0.7.1 carries no plugin package.", check.Fact);
        Assert.Null(check.Fix);
    }

    [Fact]
    public async Task A_runtime_older_than_the_section_cannot_tell()
    {
        var check = await CheckAsync(released: null);

        Assert.Equal(CheckState.Unknown, check.State);
        Assert.Equal("The runtime is older than this CLI and does not say which plugin packages its release publishes.", check.Fact);
        Assert.Null(check.Fix);
    }

    [Fact]
    public async Task A_runtime_that_did_not_answer_was_not_asked()
    {
        using var dir = new TempPaths();
        var output = new StringWriter();
        await CliApp.RunAsync(["status"], StatusCommandTests.Machine(dir, output), Ct);

        var check = Assert.Single(StatusCommandTests.Read(output).Checks, c => c.Name == "plugin_package");
        Assert.Equal(CheckState.Unknown, check.State);
        Assert.False(check.Required);
    }

    [Fact]
    public async Task The_check_follows_the_provider_plugin_check()
    {
        using var dir = new TempPaths();
        StatusCommandTests.Running(dir);
        var output = new StringWriter();
        await CliApp.RunAsync(["status"], StatusCommandTests.Machine(dir, output), Ct);

        var names = StatusCommandTests.Read(output).Checks.Select(c => c.Name).ToList();
        Assert.Equal(names.IndexOf("provider_plugin") + 1, names.IndexOf("plugin_package"));
    }

    [Fact]
    public async Task Human_runtime_status_says_how_each_released_package_stands()
    {
        const string Info = """
            {"runtime_version":"0.7.1","api_version":"v1","instance_id":"rt_LIVE","pid":77,"started_at":"2026-10-16T07:02:00.000Z","data_dir":"/home/ada/.jason",
             "database":{"applied_migrations":["a"],"newly_applied":[],"backup_file":null},
             "dispatcher":{"state":"running","tick_seconds":10,"max_parallel":8,"running_attempts":0,"last_scan_at":null,"scans":1,"summons":0},
             "plugins":{"active_count":1,"snapshot_id":"snp_1","loaded_at":"2026-10-16T07:02:00.000Z","last_reload_activated":null},
             "routes":{"snapshot_id":"rts_1","activated_at":"2026-10-16T07:02:00.000Z","global_default_plugin":null,"global_override_count":0,"campaign_route_count":0},
             "released_plugins":[
               {"id":"reply","state":"behind","released":{"version":"0.7.0","digest":"sha256:b"},"installed":{"version":"0.6.1","digest":"sha256:a"}},
               {"id":"mailer","state":"not_installed","released":{"version":"1.0.0","digest":"sha256:c"},"installed":null}]}
            """;
        using var cli = new CliRun(Info);
        await cli.RunAsync("runtime", "status", "--human");
        Assert.Contains("Packages:   reply 0.6.1, behind this release's 0.7.0; mailer not installed, this release's is 1.0.0", cli.Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, "Packages:   unknown")]
    [InlineData("[]", "Packages:   none carried")]
    public async Task Human_runtime_status_says_when_there_is_no_package_to_name(string? section, string line)
    {
        var info = $$"""
            {"runtime_version":"0.7.1","api_version":"v1","instance_id":"rt_LIVE","pid":77,"started_at":"2026-10-16T07:02:00.000Z","data_dir":"/home/ada/.jason",
             "database":{"applied_migrations":["a"],"newly_applied":[],"backup_file":null},
             "dispatcher":{"state":"running","tick_seconds":10,"max_parallel":8,"running_attempts":0,"last_scan_at":null,"scans":1,"summons":0},
             "plugins":{"active_count":1,"snapshot_id":"snp_1","loaded_at":"2026-10-16T07:02:00.000Z","last_reload_activated":null},
             "routes":{"snapshot_id":"rts_1","activated_at":"2026-10-16T07:02:00.000Z","global_default_plugin":null,"global_override_count":0,"campaign_route_count":0},
             "released_plugins":{{section ?? "null"}}}
            """;
        using var cli = new CliRun(info);
        await cli.RunAsync("runtime", "status", "--human");
        Assert.Contains(line, cli.Text, StringComparison.Ordinal);
    }

    private static async Task<StatusCheck> CheckAsync(IReadOnlyList<ReleasedPluginInfo>? released, string version = "0.7.1") =>
        Assert.Single((await ReportAsync(released, version)).Checks, c => c.Name == "plugin_package");

    private static async Task<StatusReport> ReportAsync(IReadOnlyList<ReleasedPluginInfo>? released, string version = "0.7.1")
    {
        using var dir = new TempPaths();
        StatusCommandTests.Running(dir, version: version, releasedPlugins: released);
        var output = new StringWriter();
        await CliApp.RunAsync(["status"], StatusCommandTests.Machine(dir, output, build: version), Ct);

        return StatusCommandTests.Read(output);
    }
}
