using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Cli;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Contracts.Json;
using Jason.Contracts.Plugins;

namespace Jason.Cli.Tests.Commands;

/// <summary>
/// The verbs that name a channel: <c>route resolve|set|unset</c> and <c>operation describe</c> take <c>--channel</c> and send
/// it; a write that names one is held back from a runtime that does not know channels, which would ignore the member and
/// change the operation's own route; and every human view names a channel route as the operation on its channel.
/// </summary>
public class ChannelRouteCommandsTests
{
    private static readonly RuntimeDescriptor Live = new("v1", "0.1.0-dev", "rt_LIVE", 77, "http://127.0.0.1:5000", "the-token", DateTimeOffset.UnixEpoch);

    [Fact]
    public async Task Resolve_sends_the_channel_and_leaves_it_out_when_none_is_named()
    {
        using var run = new Run();

        var with = await run.RunAsync("route", "resolve", "--campaign", "cmp_A", "--operation", "message.send", "--channel", "linkedin");
        var without = await run.RunAsync("route", "resolve", "--campaign", "cmp_A", "--operation", "message.send");

        Assert.Equal((ExitCodes.Success, ExitCodes.Success), (with, without));
        Assert.Equal([Operations.SystemInfo, Operations.RouteResolve, Operations.RouteResolve], run.Asked);
        run.AssertPosted(1, Operations.RouteResolve, """{"campaign_id":"cmp_A","operation":"message.send","channel":"linkedin"}""");
        run.AssertPosted(2, Operations.RouteResolve, """{"campaign_id":"cmp_A","operation":"message.send"}""");
    }

    [Fact]
    public async Task Describe_sends_the_channel()
    {
        using var run = new Run();

        var exit = await run.RunAsync("operation", "describe", "message.send", "--campaign", "cmp_A", "--channel", "email");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal([Operations.SystemInfo, Operations.OperationDescribe], run.Asked);
        run.AssertPosted(1, Operations.OperationDescribe, """{"campaign_id":"cmp_A","operation":"message.send","channel":"email"}""");
    }

    /// <summary>
    /// The runtime reports that it counts channel routes, so it reads the member: the write is sent as it was typed, after
    /// the one read that asked.
    /// </summary>
    [Fact]
    public async Task Set_with_a_channel_is_sent_to_a_runtime_that_knows_channels()
    {
        using var run = new Run(info: Info(channelRoutes: 0));

        var exit = await run.RunAsync("--actor", "human:ada", "route", "set", "--campaign", "cmp_A", "--operation", "message.send", "--channel", "linkedin", "--plugin", "social");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal([Operations.SystemInfo, Operations.RouteSet], run.Asked);
        run.AssertPosted(
            1,
            Operations.RouteSet,
            """{"campaign_id":"cmp_A","operation":"message.send","channel":"linkedin","plugin":"social","actor":{"type":"human","id":"ada"}}""");
    }

    [Fact]
    public async Task Unset_with_a_channel_is_sent_to_a_runtime_that_knows_channels()
    {
        using var run = new Run(info: Info(channelRoutes: 2));

        var exit = await run.RunAsync("--actor", "human:ada", "route", "unset", "--campaign", "cmp_A", "--operation", "message.send", "--channel", "linkedin");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal([Operations.SystemInfo, Operations.RouteUnset], run.Asked);
        run.AssertPosted(1, Operations.RouteUnset, """{"campaign_id":"cmp_A","operation":"message.send","channel":"linkedin","actor":{"type":"human","id":"ada"}}""");
    }

    /// <summary>
    /// An older runtime reads no channel: asked to set one, it would replace the operation's own route, and asked to unset
    /// one, take it away. So the write is not sent; the refusal is the CLI's, exit 1 with an envelope on stdout, and it
    /// says to restart or update the runtime.
    /// </summary>
    [Theory]
    [InlineData("set")]
    [InlineData("unset")]
    public async Task A_write_that_names_a_channel_is_not_sent_to_a_runtime_older_than_channel_routes(string verb)
    {
        using var run = new Run(info: Info(channelRoutes: null));
        string[] plugin = verb == "set" ? ["--plugin", "social"] : [];

        var exit = await run.RunAsync(["--actor", "human:ada", "route", verb, "--campaign", "cmp_A", "--operation", "message.send", "--channel", "linkedin", .. plugin]);

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Equal([Operations.SystemInfo], run.Asked);
        var error = JsonSerializer.Deserialize<ErrorResponse>(run.Text, JasonJson.Options)!.Error;
        Assert.Equal("runtime_too_old", error.Code);
        Assert.False(error.Retryable);
        Assert.Contains("restart", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("update Jason", error.Message, StringComparison.Ordinal);
        Assert.Contains("0.1.0-dev", error.Message, StringComparison.Ordinal);
    }

    /// <summary>A runtime older than the whole routes section has no member either, and is held back the same way.</summary>
    [Fact]
    public async Task A_runtime_that_reports_no_routes_at_all_is_older_than_channel_routes_too()
    {
        using var run = new Run(info: """{"runtime_version":"0.1.0-dev","api_version":"v1","instance_id":"rt_LIVE","pid":77}""");

        var exit = await run.RunAsync("--actor", "human:ada", "route", "unset", "--campaign", "cmp_A", "--operation", "message.send", "--channel", "linkedin");

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Equal([Operations.SystemInfo], run.Asked);
    }

    /// <summary>A write that names no channel is the write it always was: one request, no question first.</summary>
    [Theory]
    [InlineData("set")]
    [InlineData("unset")]
    public async Task A_write_that_names_no_channel_asks_the_runtime_nothing_first(string verb)
    {
        using var run = new Run(info: Info(channelRoutes: null));
        string[] plugin = verb == "set" ? ["--plugin", "social"] : [];

        var exit = await run.RunAsync(["--actor", "human:ada", "route", verb, "--campaign", "cmp_A", "--operation", "message.send", .. plugin]);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal([verb == "set" ? Operations.RouteSet : Operations.RouteUnset], run.Asked);
    }

    /// <summary>
    /// A runtime that cannot say what it knows is not sent a request on a guess: the answer that is not a success is
    /// refused like an old runtime's, whatever verb names the channel.
    /// </summary>
    [Theory]
    [InlineData("set")]
    [InlineData("unset")]
    [InlineData("resolve")]
    [InlineData("describe")]
    public async Task A_runtime_that_cannot_say_what_it_knows_is_not_sent_a_channel_on_a_guess(string verb)
    {
        using var run = new Run(infoStatus: HttpStatusCode.InternalServerError, info: "{}");

        var exit = await run.RunAsync(ChannelVerb(verb));

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Equal([Operations.SystemInfo], run.Asked);
        Assert.Equal("runtime_too_old", JsonSerializer.Deserialize<ErrorResponse>(run.Text, JasonJson.Options)!.Error.Code);
    }

    /// <summary>
    /// A read that names a channel is held to the same rule as a write: an older runtime ignores the member and would
    /// answer for the operation's own route, which the person would take for the channel's.
    /// </summary>
    [Theory]
    [InlineData("resolve", true)]
    [InlineData("resolve", false)]
    [InlineData("describe", true)]
    [InlineData("describe", false)]
    public async Task A_read_that_names_a_channel_is_not_sent_to_a_runtime_older_than_channel_routes(string verb, bool noRoutesSection)
    {
        var info = noRoutesSection ? """{"runtime_version":"0.1.0-dev","api_version":"v1","instance_id":"rt_LIVE","pid":77}""" : Info(channelRoutes: null);
        using var run = new Run(info: info);

        var exit = await run.RunAsync(ChannelVerb(verb));

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Equal([Operations.SystemInfo], run.Asked);
        var error = JsonSerializer.Deserialize<ErrorResponse>(run.Text, JasonJson.Options)!.Error;
        Assert.Equal("runtime_too_old", error.Code);
        Assert.Contains("restart", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("update Jason", error.Message, StringComparison.Ordinal);
    }

    /// <summary>A read that names no channel asks the runtime nothing first.</summary>
    [Theory]
    [InlineData("resolve")]
    [InlineData("describe")]
    public async Task A_read_that_names_no_channel_asks_nothing_first(string verb)
    {
        using var run = new Run(info: Info(channelRoutes: null));
        string[] args = verb == "resolve"
            ? ["route", "resolve", "--campaign", "cmp_A", "--operation", "message.send"]
            : ["operation", "describe", "message.send", "--campaign", "cmp_A"];

        var exit = await run.RunAsync(args);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Single(run.Asked);
        Assert.NotEqual(Operations.SystemInfo, run.Asked[0]);
    }

    /// <summary>A runtime that cannot be reached is exit 3 with a channel named, as it is without one.</summary>
    [Theory]
    [InlineData("set")]
    [InlineData("unset")]
    [InlineData("resolve")]
    [InlineData("describe")]
    public async Task A_runtime_that_cannot_be_reached_is_exit_3_with_a_channel_named_as_without(string verb)
    {
        using var withChannel = new Run(unreachable: true);
        using var without = new Run(unreachable: true);
        var args = ChannelVerb(verb);

        var exit = await withChannel.RunAsync(args);
        var exitWithout = await without.RunAsync(args.Where((_, i) => args[i] != "--channel" && (i == 0 || args[i - 1] != "--channel")).ToArray());

        Assert.Equal(ExitCodes.RuntimeUnavailable, exit);
        Assert.Equal(exitWithout, exit);
        Assert.DoesNotContain("runtime_too_old", withChannel.Text, StringComparison.Ordinal);
    }

    private static string[] ChannelVerb(string verb) => verb switch
    {
        "set" => ["--actor", "human:ada", "route", "set", "--campaign", "cmp_A", "--operation", "message.send", "--channel", "linkedin", "--plugin", "social"],
        "unset" => ["--actor", "human:ada", "route", "unset", "--campaign", "cmp_A", "--operation", "message.send", "--channel", "linkedin"],
        "resolve" => ["route", "resolve", "--campaign", "cmp_A", "--operation", "message.send", "--channel", "linkedin"],
        _ => ["operation", "describe", "message.send", "--campaign", "cmp_A", "--channel", "linkedin"],
    };

    [Fact]
    public async Task Human_route_list_names_a_channel_route_as_its_operation_on_its_channel()
    {
        using var run = new Run(answer: JsonSerializer.Serialize(Routes(), JasonJson.Options));

        var exit = await run.RunAsync("route", "list", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(
            """
            SCOPE     CAMPAIGN  OPERATION                 PLUGIN  BINDING
            --------  --------  ------------------------  ------  --------------
            global    -         message.send              mailer  -
            global    -         message.send on email     mailer  -
            campaign  cmp_A     (default)                 mailer  -
            campaign  cmp_A     message.send              mailer  -
            campaign  cmp_A     message.send on linkedin  social  {"seat":"one"}

            snapshot rts_01J4 over plugins snp_01J4
            """.ReplaceLineEndings() + Environment.NewLine,
            run.Text);
    }

    [Fact]
    public async Task Human_resolve_names_the_operation_on_the_channel_and_the_scope_that_won()
    {
        var resolved = new RouteResolutionDto(
            "message.send",
            "cmp_A",
            "social",
            "1.0.0",
            "sha256:3f2a9c1b4d5e6f708192a3b4c5d6e7f8091a2b3c4d5e6f708192a3b4c5d6e7f8",
            PluginStatus.Valid,
            RouteScope.CampaignChannel,
            new JsonObject { ["seat"] = "one" },
            "sha256:1111",
            "rts_01J4",
            "snp_01J4",
            1,
            true,
            [],
            "linkedin");
        using var run = new Run(answer: JsonSerializer.Serialize(resolved, JasonJson.Options));

        var exit = await run.RunAsync("route", "resolve", "--campaign", "cmp_A", "--operation", "message.send", "--channel", "linkedin", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(
            """
            Operation:  message.send on linkedin v1
            Campaign:   cmp_A
            Plugin:     social 1.0.0 | valid
            Scope:      campaign_channel
            Binding:    {"seat":"one"}
            Snapshots:  routes rts_01J4 | plugins snp_01J4
            Usable:     yes
            """.ReplaceLineEndings() + Environment.NewLine,
            run.Text);
    }

    [Fact]
    public async Task Human_describe_names_the_operation_on_the_channel_it_was_asked_about()
    {
        var described = new OperationDescriptionDto("message.send", 1, "social", "1.0.0", new JsonObject { ["type"] = "object" }, null, "linkedin");
        using var run = new Run(answer: JsonSerializer.Serialize(described, JasonJson.Options));

        var exit = await run.RunAsync("operation", "describe", "message.send", "--campaign", "cmp_A", "--channel", "linkedin", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.StartsWith("Operation:  message.send on linkedin v1" + Environment.NewLine, run.Text, StringComparison.Ordinal);
    }

    /// <summary>A write answers with the same table, so the channel route it wrote reads as one.</summary>
    [Fact]
    public async Task Human_set_shows_the_channel_route_it_wrote_beside_the_operations_own()
    {
        using var run = new Run(info: Info(channelRoutes: 1), answer: JsonSerializer.Serialize(Routes(), JasonJson.Options));

        var exit = await run.RunAsync("--actor", "human:ada", "route", "set", "--campaign", "cmp_A", "--operation", "message.send", "--channel", "linkedin", "--plugin", "social", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains("message.send on linkedin", run.Text, StringComparison.Ordinal);
    }

    private static string Info(int? channelRoutes) => JsonSerializer.Serialize(
        new SystemInfoResponse(
            "0.1.0-dev",
            "v1",
            "rt_LIVE",
            77,
            DateTimeOffset.UnixEpoch,
            "/data",
            new DatabaseInfo(["20260913225419_InitialCreate"], [], null),
            new DispatcherInfo(DispatcherState.Running, 10, 4, 0, null, 0, 0),
            new PluginsInfo(0, "snp_EMPTY", DateTimeOffset.UnixEpoch, true),
            new RoutesInfo("rts_EMPTY", DateTimeOffset.UnixEpoch, null, 0, 0, channelRoutes)),
        JasonJson.Options);

    private static RoutesDto Routes() => new(
        "rts_01J4",
        "snp_01J4",
        new RouteSetDto(
            null,
            new Dictionary<string, RouteDto>(StringComparer.Ordinal) { ["message.send"] = new("mailer", null, null) },
            new Dictionary<string, IReadOnlyDictionary<string, RouteDto>>(StringComparer.Ordinal)
            {
                ["message.send"] = new Dictionary<string, RouteDto>(StringComparer.Ordinal) { ["email"] = new("mailer", null, null) },
            }),
        [
            new CampaignRoutesDto(
                "cmp_A",
                new RouteDto("mailer", null, null),
                new Dictionary<string, RouteDto>(StringComparer.Ordinal) { ["message.send"] = new("mailer", null, null) },
                new Dictionary<string, IReadOnlyDictionary<string, RouteDto>>(StringComparer.Ordinal)
                {
                    ["message.send"] = new Dictionary<string, RouteDto>(StringComparer.Ordinal)
                    {
                        ["linkedin"] = new("social", new JsonObject { ["seat"] = "one" }, "sha256:2222"),
                    },
                }),
        ]);

    /// <summary>
    /// A runtime that answers <c>system.info</c> with one body and every other operation with another, and remembers what it
    /// was asked and in which order.
    /// </summary>
    private sealed class Run : IDisposable
    {
        private readonly TempPaths _paths = new();
        private readonly CliEnvironment _environment;
        private readonly List<(string Operation, string Body)> _calls = [];

        public Run(string? info = null, string answer = "{}", HttpStatusCode infoStatus = HttpStatusCode.OK, bool unreachable = false)
        {
            _paths.WriteDescriptor(Live);
            info ??= Info(channelRoutes: 0);
            _environment = new CliEnvironment(
                Output,
                Error,
                _paths.Paths,
                new FakeHandler(request =>
                {
                    var operation = request.RequestUri!.AbsolutePath["/v1/".Length..];
                    _calls.Add((operation, request.Content!.ReadAsStringAsync(CancellationToken.None).GetAwaiter().GetResult()));
                    if (unreachable)
                    {
                        throw new HttpRequestException("connection refused");
                    }

                    var isInfo = operation == Operations.SystemInfo;
                    return new HttpResponseMessage(isInfo ? infoStatus : HttpStatusCode.OK)
                    {
                        Content = new StringContent(isInfo ? info : answer, Encoding.UTF8, "application/json"),
                    };
                }));
        }

        public StringWriter Output { get; } = new();

        public StringWriter Error { get; } = new();

        public string Text => Output.ToString();

        public IReadOnlyList<string> Asked => _calls.Select(call => call.Operation).ToList();

        public Task<int> RunAsync(params string[] args) => CliApp.RunAsync(args, _environment, TestContext.Current.CancellationToken);

        public void AssertPosted(int index, string operation, string expectedBody)
        {
            Assert.Equal(operation, _calls[index].Operation);
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse(expectedBody), JsonNode.Parse(_calls[index].Body)), $"The body was {_calls[index].Body}");
        }

        public void Dispose() => _paths.Dispose();
    }
}
