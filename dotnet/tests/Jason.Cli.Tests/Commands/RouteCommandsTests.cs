using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Cli;
using Jason.Cli.Commands;
using Jason.Contracts.Api;
using Jason.Contracts.Json;
using Jason.Contracts.Plugins;

namespace Jason.Cli.Tests.Commands;

public class RouteCommandsTests
{
    /// <summary>The help of route unset names its global form, as route set's does.</summary>
    [Fact]
    public void Unset_says_its_global_form_in_its_help()
    {
        using var paths = new TempPaths();
        var unset = CliApp.BuildRootCommand(new CliEnvironment(TextWriter.Null, TextWriter.Null, paths.Paths))
            .Subcommands.Single(command => command.Name == "route")
            .Subcommands.Single(command => command.Name == "unset");

        Assert.StartsWith(
            "Take one campaign route away, and with it whatever it was hiding, or with --global the route every campaign follows that has none of its own. ",
            unset.Description,
            System.StringComparison.Ordinal);
    }

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

        var exit = await cli.RunAsync("--actor", "human:ada", "route", "set", "--campaign", "cmp_A", "--plugin", "other-provider");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.RouteSet, "{\"campaign_id\":\"cmp_A\",\"plugin\":\"other-provider\",\"actor\":{\"type\":\"human\",\"id\":\"ada\"}}");
    }

    [Fact]
    public async Task Unset_sends_the_campaign_the_operation_and_the_reason()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("--actor", "human:ada", "route", "unset", "--campaign", "cmp_A", "--operation", "campaign.get", "--reason", "back to the house account");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(
            Operations.RouteUnset,
            "{\"campaign_id\":\"cmp_A\",\"operation\":\"campaign.get\",\"reason\":\"back to the house account\",\"actor\":{\"type\":\"human\",\"id\":\"ada\"}}");
    }

    /// <summary>
    /// A write names its scope, and a write that names none is refused before anything is sent, with both scopes said:
    /// one that forgot its campaign never re-routes every campaign.
    /// </summary>
    [Theory]
    [InlineData("set", "--plugin", "fake-provider", "--operation", "campaign.get")]
    [InlineData("unset", "--operation", "campaign.get")]
    public async Task A_write_that_names_no_scope_is_refused_with_both_said(params string[] rest)
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync(["--actor", "human:ada", "route", .. rest]);

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Contains(RouteCommands.NoScope, cli.Error.ToString(), StringComparison.Ordinal);
        Assert.Null(cli.Url);
    }

    [Theory]
    [InlineData("set", "--plugin", "fake-provider")]
    [InlineData("unset")]
    public async Task A_write_that_names_both_scopes_is_refused(params string[] rest)
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync(["--actor", "human:ada", "route", rest[0], "--campaign", "cmp_A", "--global", .. rest[1..]]);

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Contains(RouteCommands.BothScopes, cli.Error.ToString(), StringComparison.Ordinal);
        Assert.Null(cli.Url);
    }

    [Fact]
    public async Task Set_with_global_sends_the_scope_and_no_campaign()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync(
            "--actor", "human:ada", "route", "set", "--global", "--operation", "campaign.get", "--plugin", "fake-provider", "--binding", "{\"workspace\":\"west\"}");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(
            Operations.RouteSet,
            "{\"scope\":\"global\",\"operation\":\"campaign.get\",\"plugin\":\"fake-provider\",\"binding\":{\"workspace\":\"west\"},\"actor\":{\"type\":\"human\",\"id\":\"ada\"}}");
    }

    [Fact]
    public async Task Unset_with_global_sends_the_scope_and_no_campaign()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("--actor", "human:ada", "route", "unset", "--global", "--operation", "campaign.get");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.RouteUnset, "{\"scope\":\"global\",\"operation\":\"campaign.get\",\"actor\":{\"type\":\"human\",\"id\":\"ada\"}}");
    }

    /// <summary>A global write names who does it, as a campaign's does: the CLI never puts this account's name on it by itself.</summary>
    [Fact]
    public async Task A_global_write_still_needs_an_actor()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("route", "set", "--global", "--plugin", "fake-provider");

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Null(cli.Url);
    }

    /// <summary>
    /// A runtime older than the scope ignores it and asks for the campaign; its answer and the exit code stay its own, and
    /// one line on stderr says why, so that the campaign is not added as the fix. A campaign's write asked the same says
    /// nothing more.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task An_older_runtime_asking_for_a_campaign_of_a_global_write_is_named_on_stderr(bool global)
    {
        const string refusal = "{\"error\":{\"code\":\"validation_failed\",\"message\":\"The request is not valid.\",\"retryable\":false,\"details\":[{\"field\":\"campaign_id\",\"code\":\"required\",\"message\":\"campaign_id is required.\"}]}}";
        using var cli = new CliRun(refusal, status: System.Net.HttpStatusCode.BadRequest);

        var exit = await cli.RunAsync(["--actor", "human:ada", "route", "set", global ? "--global" : "--campaign", .. global ? Array.Empty<string>() : ["cmp_A"], "--plugin", "fake-provider"]);

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Equal(refusal, cli.Text.Trim());
        if (global)
        {
            Assert.Contains(RouteCommands.OlderThanGlobal, cli.Error.ToString(), StringComparison.Ordinal);
        }
        else
        {
            Assert.DoesNotContain(RouteCommands.OlderThanGlobal, cli.Error.ToString(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Set_without_a_plugin_is_a_usage_error()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("--actor", "human:ada", "route", "set", "--campaign", "cmp_A");

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Contains("--plugin", cli.Error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_binding_that_is_not_an_object_is_a_usage_error()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("--actor", "human:ada", "route", "set", "--campaign", "cmp_A", "--plugin", "fake-provider", "--binding", "[1]");

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
            Plugin:     fake-provider 1.0.0 | valid
            Scope:      global_default
            Binding:    {"workspace":"west"}
            Snapshots:  routes rts_01J4 | plugins snp_01J4
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
