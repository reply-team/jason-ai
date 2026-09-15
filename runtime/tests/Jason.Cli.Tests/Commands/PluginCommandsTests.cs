using System.Net;
using System.Text.Json;
using Jason.Cli;
using Jason.Contracts.Api;
using Jason.Contracts.Json;
using Jason.Contracts.Plugins;

namespace Jason.Cli.Tests.Commands;

public class PluginCommandsTests
{
    private static readonly DateTimeOffset Moment = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task List_sends_an_empty_body()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("plugin", "list");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.PluginList, "{}");
    }

    [Fact]
    public async Task Reload_sends_the_reason_and_the_actor()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("plugin", "reload", "--reason", "installed the fake provider", "--actor", "human:ada");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.PluginReload, "{\"reason\":\"installed the fake provider\",\"actor\":{\"type\":\"human\",\"id\":\"ada\"}}");
    }

    [Fact]
    public async Task Reload_without_options_sends_an_empty_body()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("plugin", "reload");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.PluginReload, "{}");
    }

    [Fact]
    public async Task A_reserved_actor_is_refused_on_the_read_only_verb()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("plugin", "list", "--actor", "system");

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Contains("--actor", cli.Error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Human_mode_renders_the_registry_as_a_table()
    {
        using var cli = new CliRun(JsonSerializer.Serialize(TwoPlugins(), JasonJson.Options));

        var exit = await cli.RunAsync("plugin", "list", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(
            """
            ID             VERSION  KIND          STATUS       OPERATIONS            GRANTS                         DIGEST
            -------------  -------  ------------  -----------  --------------------  -----------------------------  ------------
            fake-provider  1.0.0    provider      valid        echo.run, exec.run …  exec 1/1 · http 0/2 · env 1/2  3f2a9c1b4d5e
            notifier       0.2.0    notification  unavailable  -                     exec 1/1 · http - · env -      9a8b7c6d5e4f
            """.ReplaceLineEndings() + Environment.NewLine,
            cli.Text);
    }

    [Fact]
    public async Task Human_mode_names_the_problems_of_a_rejected_reload()
    {
        using var cli = new CliRun(JsonSerializer.Serialize(KeptAfterARejectedReload(), JasonJson.Options));

        var exit = await cli.RunAsync("plugin", "list", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(
            """
            ID             VERSION  KIND      STATUS  OPERATIONS  GRANTS  DIGEST
            -------------  -------  --------  ------  ----------  ------  ------------
            fake-provider  1.0.0    provider  valid   echo.run    -       3f2a9c1b4d5e

            last reload rejected at 2026-09-14 12:00:00 UTC:
            broken/plugin.yaml#3:1: yaml_invalid — mapping values are not allowed here
            broken/plugin.yaml#id: id_mismatch — the manifest says 'other', the directory says 'broken'
            """.ReplaceLineEndings() + Environment.NewLine,
            cli.Text);
    }

    [Fact]
    public async Task Human_mode_falls_back_to_the_raw_body_when_the_shape_is_unknown()
    {
        using var cli = new CliRun("{\"unexpected\":true}");

        var exit = await cli.RunAsync("plugin", "list", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains("{\"unexpected\":true}", cli.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Human_mode_reports_what_the_reload_activated()
    {
        using var cli = new CliRun(JsonSerializer.Serialize(TwoPlugins(), JasonJson.Options));

        var exit = await cli.RunAsync("plugin", "reload", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(
            """
            activated snapshot snp_01J4 with 2 plugins
            notifier unavailable: executable_missing
            """.ReplaceLineEndings() + Environment.NewLine,
            cli.Text);
    }

    [Fact]
    public async Task Human_mode_counts_one_plugin_as_one()
    {
        using var cli = new CliRun(JsonSerializer.Serialize(KeptAfterARejectedReload() with { LastReload = null, Activated = true }, JasonJson.Options));

        var exit = await cli.RunAsync("plugin", "reload", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal("activated snapshot snp_01J4 with 1 plugin" + Environment.NewLine, cli.Text);
    }

    [Fact]
    public async Task Human_mode_falls_back_to_the_raw_body_when_the_reload_was_not_activated()
    {
        using var cli = new CliRun(JsonSerializer.Serialize(KeptAfterARejectedReload(), JasonJson.Options));

        var exit = await cli.RunAsync("plugin", "reload", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains("\"activated\":false", cli.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_refused_reload_prints_the_error_body_and_exits_1()
    {
        const string envelope =
            "{\"error\":{\"code\":\"plugin_reload_rejected\",\"message\":\"2 problems in 1 package.\",\"retryable\":false,"
            + "\"details\":[{\"field\":\"broken/plugin.yaml#3:1\",\"code\":\"yaml_invalid\",\"message\":\"mapping values are not allowed here\"}]}}";
        using var cli = new CliRun(envelope, status: HttpStatusCode.Conflict);

        var exit = await cli.RunAsync("plugin", "reload", "--human");

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Equal(envelope + Environment.NewLine, cli.Text);
    }

    /// <summary>A registry the way a machine with one working plugin and one it cannot run answers.</summary>
    private static PluginRegistryDto TwoPlugins() => new(
        new SnapshotDto("snp_01J4", Moment, SnapshotSource.Reload, 2),
        [
            new PluginDto(
                "fake-provider",
                "1.0.0",
                PluginKind.Provider,
                "Fake provider",
                "A stand-in for a vendor CLI.",
                null,
                "/home/u/.jason/plugins/fake-provider",
                "sha256:3f2a9c1b4d5e6f708192a3b4c5d6e7f8091a2b3c4d5e6f708192a3b4c5d6e7f8",
                new PluginContractsDto([1], [1]),
                ["echo.run", "exec.run", "http.get"],
                new PluginEntry("main.js", "invoke"),
                new PluginCapabilitiesDto(
                    new ExecCapabilityDto([new ExecutableDto("dotnet", "/usr/bin/dotnet", "10.0.100", null)], ["dotnet"]),
                    new ListCapabilityDto(["localhost:5555", "api.example.com"], []),
                    new ListCapabilityDto(["FAKE_TOKEN", "OTHER_TOKEN"], ["FAKE_TOKEN"])),
                new PluginLimitsDto(60000, 64),
                null,
                PluginStatus.Valid,
                []),
            new PluginDto(
                "notifier",
                "0.2.0",
                PluginKind.Notification,
                "Notifier",
                null,
                null,
                "/home/u/.jason/plugins/notifier",
                "sha256:9a8b7c6d5e4f3a2b1c0d9e8f7a6b5c4d3e2f1a0b9c8d7e6f5a4b3c2d1e0f9a8b",
                new PluginContractsDto([1], []),
                [],
                new PluginEntry("main.js", "invoke"),
                new PluginCapabilitiesDto(new ExecCapabilityDto([new ExecutableDto("notify", null, null, "0.4.0")], ["notify"]), null, null),
                new PluginLimitsDto(60000, 64),
                null,
                PluginStatus.Unavailable,
                [new PluginProblemDto("executable_missing", "capabilities.exec.executables[0]", "No 'notify' was found.")]),
        ],
        new ReloadReportDto(Moment, SnapshotSource.Reload, true, []),
        Activated: true);

    /// <summary>The snapshot that survived a refused reload, with the diagnostics that explain the refusal.</summary>
    private static PluginRegistryDto KeptAfterARejectedReload() => new(
        new SnapshotDto("snp_01J4", Moment, SnapshotSource.Startup, 1),
        [
            new PluginDto(
                "fake-provider",
                "1.0.0",
                PluginKind.Provider,
                "Fake provider",
                null,
                null,
                "/home/u/.jason/plugins/fake-provider",
                "sha256:3f2a9c1b4d5e6f708192a3b4c5d6e7f8091a2b3c4d5e6f708192a3b4c5d6e7f8",
                new PluginContractsDto([1], [1]),
                ["echo.run"],
                new PluginEntry("main.js", "invoke"),
                new PluginCapabilitiesDto(null, null, null),
                new PluginLimitsDto(60000, 64),
                null,
                PluginStatus.Valid,
                []),
        ],
        new ReloadReportDto(
            Moment,
            SnapshotSource.Reload,
            Activated: false,
            [
                new CandidateDto("fake-provider", "fake-provider", CandidateStatus.Valid, []),
                new CandidateDto(
                    "broken",
                    null,
                    CandidateStatus.Invalid,
                    [
                        // A syntax error names its position in the file; a field problem names the field. The
                        // renderer spells both as one location without doubling the file name.
                        new PluginProblemDto("yaml_invalid", "plugin.yaml#3:1", "mapping values are not allowed here"),
                        new PluginProblemDto("id_mismatch", "id", "the manifest says 'other', the directory says 'broken'"),
                    ]),
            ]),
        Activated: false);
}
