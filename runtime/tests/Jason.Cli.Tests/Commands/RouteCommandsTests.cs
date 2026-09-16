using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Cli;
using Jason.Contracts.Api;
using Jason.Contracts.Json;
using Jason.Contracts.Plugins;

namespace Jason.Cli.Tests.Commands;

public class RouteCommandsTests
{
    [Fact]
    public async Task Resolve_sends_the_campaign_and_the_operation()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("route", "resolve", "--campaign", "cmp_A", "--operation", "campaign.get");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.RouteResolve, "{\"campaign_id\":\"cmp_A\",\"operation\":\"campaign.get\"}");
    }

    [Fact]
    public async Task List_without_a_campaign_sends_an_empty_body()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("route", "list");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.RouteList, "{}");
    }

    [Fact]
    public async Task List_passes_the_campaign()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("route", "list", "--campaign", "cmp_A");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.RouteList, "{\"campaign_id\":\"cmp_A\"}");
    }

    [Fact]
    public async Task Set_sends_the_route_the_binding_the_reason_and_the_actor()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync(
            "route",
            "set",
            "--campaign",
            "cmp_A",
            "--operation",
            "campaign.get",
            "--plugin",
            "fake-provider",
            "--binding",
            "{\"workspace\":\"west\"}",
            "--reason",
            "the west account owns this list",
            "--actor",
            "human:ada");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(
            Operations.RouteSet,
            "{\"campaign_id\":\"cmp_A\",\"operation\":\"campaign.get\",\"plugin\":\"fake-provider\",\"binding\":{\"workspace\":\"west\"},"
                + "\"reason\":\"the west account owns this list\",\"actor\":{\"type\":\"human\",\"id\":\"ada\"}}");
    }

    /// <summary>No operation is the campaign's default route, and the body says so by leaving the field out.</summary>
    [Fact]
    public async Task Set_without_an_operation_writes_the_campaigns_default_route()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("route", "set", "--campaign", "cmp_A", "--plugin", "other-provider");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.RouteSet, "{\"campaign_id\":\"cmp_A\",\"plugin\":\"other-provider\"}");
    }

    [Fact]
    public async Task Unset_sends_the_campaign_the_operation_and_the_reason()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("route", "unset", "--campaign", "cmp_A", "--operation", "campaign.get", "--reason", "back to the house account");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(
            Operations.RouteUnset,
            "{\"campaign_id\":\"cmp_A\",\"operation\":\"campaign.get\",\"reason\":\"back to the house account\"}");
    }

    /// <summary>
    /// The one place an operator will reach for the wrong thing, so the error is the documentation: there is no
    /// such thing as a global <c>route set</c>, and the message says where a global route is written instead of
    /// merely naming a missing option.
    /// </summary>
    [Fact]
    public async Task Setting_a_route_without_a_campaign_says_where_global_routes_live()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("route", "set", "--plugin", "fake-provider", "--operation", "campaign.get");

        AssertSaidWhereGlobalRoutesLive(cli, exit);
    }

    [Fact]
    public async Task Unsetting_a_route_without_a_campaign_says_where_global_routes_live()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("route", "unset", "--operation", "campaign.get");

        AssertSaidWhereGlobalRoutesLive(cli, exit);
    }

    private static void AssertSaidWhereGlobalRoutesLive(CliRun cli, int exit)
    {
        Assert.Equal(ExitCodes.Usage, exit);
        var message = cli.Error.ToString();
        Assert.Contains("--campaign", message, StringComparison.Ordinal);
        Assert.Contains("settings.json", message, StringComparison.Ordinal);
        Assert.Contains("jason plugin reload", message, StringComparison.Ordinal);
        Assert.Null(cli.Url);
    }

    [Fact]
    public async Task Set_without_a_plugin_is_a_usage_error()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("route", "set", "--campaign", "cmp_A");

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Contains("--plugin", cli.Error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_binding_that_is_not_an_object_is_a_usage_error()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("route", "set", "--campaign", "cmp_A", "--plugin", "fake-provider", "--binding", "[1]");

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Contains("--binding", cli.Error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_reserved_actor_is_refused_on_the_read_only_verb()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("route", "list", "--actor", "system");

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Contains("--actor", cli.Error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Human_mode_renders_a_resolution()
    {
        using var cli = new CliRun(JsonSerializer.Serialize(Resolved(), JasonJson.Options));

        var exit = await cli.RunAsync("route", "resolve", "--campaign", "cmp_A", "--operation", "campaign.get", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(
            """
            Operation:  campaign.get v1
            Campaign:   cmp_A
            Plugin:     fake-provider 1.0.0 · valid
            Scope:      global_default
            Binding:    {"workspace":"west"}
            Snapshots:  routes rts_01J4 · plugins snp_01J4
            Usable:     yes
            """.ReplaceLineEndings() + Environment.NewLine,
            cli.Text);
    }

    /// <summary>
    /// A route that resolves and cannot run is the answer worth rendering well: the verdict alone would send an
    /// operator looking, and the problem says where to look.
    /// </summary>
    [Fact]
    public async Task Human_mode_names_why_a_route_cannot_run()
    {
        var unusable = Resolved() with
        {
            PluginStatus = PluginStatus.Unavailable,
            Usable = false,
            Problems = [new ErrorDetail("Routes:Default", "plugin_unavailable", "Plugin 'fake-provider' is installed but unavailable on this machine.")],
        };
        using var cli = new CliRun(JsonSerializer.Serialize(unusable, JasonJson.Options));

        var exit = await cli.RunAsync("route", "resolve", "--campaign", "cmp_A", "--operation", "campaign.get", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains("Usable:     no", cli.Text, StringComparison.Ordinal);
        Assert.Contains(
            "Routes:Default: plugin_unavailable — Plugin 'fake-provider' is installed but unavailable on this machine.",
            cli.Text,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Human_mode_renders_every_route_as_one_table()
    {
        using var cli = new CliRun(JsonSerializer.Serialize(Routes(), JasonJson.Options));

        var exit = await cli.RunAsync("route", "list", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(
            """
            SCOPE     CAMPAIGN  OPERATION     PLUGIN          BINDING
            --------  --------  ------------  --------------  --------------------
            global    -         (default)     fake-provider   {"workspace":"west"}
            global    -         campaign.get  other-provider  -
            campaign  cmp_A     (default)     other-provider  -
            campaign  cmp_A     campaign.get  fake-provider   {"workspace":"east"}

            snapshot rts_01J4 over plugins snp_01J4
            """.ReplaceLineEndings() + Environment.NewLine,
            cli.Text);
    }

    [Fact]
    public async Task Human_mode_says_when_nothing_is_routed_anywhere()
    {
        var empty = new RoutesDto("rts_01J4", "snp_01J4", new RouteSetDto(null, new Dictionary<string, RouteDto>(StringComparer.Ordinal)), []);
        using var cli = new CliRun(JsonSerializer.Serialize(empty, JasonJson.Options));

        var exit = await cli.RunAsync("route", "list", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(
            """
            no routes: nothing is sent to a plugin yet
            snapshot rts_01J4 over plugins snp_01J4
            """.ReplaceLineEndings() + Environment.NewLine,
            cli.Text);
    }

    [Fact]
    public async Task Human_mode_falls_back_to_the_raw_body_when_the_shape_is_unknown()
    {
        using var cli = new CliRun("{\"unexpected\":true}");

        var exit = await cli.RunAsync("route", "list", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains("{\"unexpected\":true}", cli.Text, StringComparison.Ordinal);
    }

    private static RouteResolutionDto Resolved() => new(
        "campaign.get",
        "cmp_A",
        "fake-provider",
        "1.0.0",
        "sha256:3f2a9c1b4d5e6f708192a3b4c5d6e7f8091a2b3c4d5e6f708192a3b4c5d6e7f8",
        PluginStatus.Valid,
        RouteScope.GlobalDefault,
        new JsonObject { ["workspace"] = "west" },
        "sha256:1111",
        "rts_01J4",
        "snp_01J4",
        1,
        true,
        []);

    private static RoutesDto Routes() => new(
        "rts_01J4",
        "snp_01J4",
        new RouteSetDto(
            new RouteDto("fake-provider", new JsonObject { ["workspace"] = "west" }, "sha256:1111"),
            new Dictionary<string, RouteDto>(StringComparer.Ordinal) { ["campaign.get"] = new("other-provider", null, null) }),
        [
            new CampaignRoutesDto(
                "cmp_A",
                new RouteDto("other-provider", null, null),
                new Dictionary<string, RouteDto>(StringComparer.Ordinal)
                {
                    ["campaign.get"] = new("fake-provider", new JsonObject { ["workspace"] = "east" }, "sha256:2222"),
                }),
        ]);
}
