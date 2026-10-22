using System.Net;
using System.Text;
using System.Text.Json;
using Jason.Contracts.Api;
using Jason.Contracts.Json;
using Jason.Runtime.Ui;

namespace Jason.Runtime.Tests.Ui;

/// <summary>
/// Pair once: a link worth one pairing within a minute, a door that trades it for a token, and the token thereafter.
/// The door is the only POST with no bearer, and it listens only to the runtime's own origin.
/// </summary>
public class UiPairingTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly DateTimeOffset Nine = new(2026, 10, 16, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Open_without_a_module_says_what_is_missing()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var error = await api.PostErrorAsync(Operations.UiOpen, null, HttpStatusCode.NotFound, Ct);
        Assert.Equal("ui_not_installed", error.Code);
        Assert.Contains("Run 'jason ui install' to place this release's build", error.Message, StringComparison.Ordinal);
        Assert.Contains(api.Paths.UiCustomDirectory, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Open_mints_a_one_minute_link_into_attention_or_into_one_item()
    {
        var clock = new FixedClock(Nine);
        await using var api = await StartWithBuildAsync(clock);

        var opened = await api.PostOkAsync<UiOpenResponse>(Operations.UiOpen, null, Ct);
        Assert.Equal(Nine + UiTickets.Lifetime, opened.ExpiresAt);
        Assert.Matches($"^http://127\\.0\\.0\\.1:{api.Runtime.BaseUrl.Port}/ui/#/attention\\?t=[A-Za-z0-9_-]{{43}}$", opened.Url);

        var approval = await api.PostOkAsync<UiOpenResponse>(Operations.UiOpen, new { approval_id = "apr_1" }, Ct);
        Assert.Contains("/ui/#/attention/approvals/apr_1?t=", approval.Url, StringComparison.Ordinal);
        var question = await api.PostOkAsync<UiOpenResponse>(Operations.UiOpen, new { decision_id = "dec_1" }, Ct);
        Assert.Contains("/ui/#/attention/questions/dec_1?t=", question.Url, StringComparison.Ordinal);
    }

    [Fact]
    [Critical(Family.S5, "a ticket pairs once")]
    public async Task The_door_trades_a_live_ticket_for_a_token_once()
    {
        var clock = new FixedClock(Nine);
        await using var api = await StartWithBuildAsync(clock);
        var ticket = await TicketAsync(api);

        var (status, body, headers) = await DoorAsync(api, ticket, Own(api));
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("no-store", headers["Cache-Control"]);
        Assert.Equal("nosniff", headers["X-Content-Type-Options"]);
        Assert.Equal(UiServingTests.Csp, headers["Content-Security-Policy"]);
        var session = JsonSerializer.Deserialize<UiSessionResponse>(body, JasonJson.Options)!;
        Assert.Equal(43, session.Token.Length);
        Assert.StartsWith("uit_", session.TokenId, StringComparison.Ordinal);
        Assert.Equal(Environment.UserName, session.ActorId);
        Assert.Equal(ApiVersion.Current, session.ApiVersion);
        Assert.Equal(api.Runtime.Descriptor.InstanceId, session.InstanceId);

        var (asBearer, _) = await UiTokensTests.PostAsBearerAsync(api, session.Token, Operations.SystemInfo, Own(api));
        Assert.Equal(HttpStatusCode.OK, asBearer);
        var listed = Assert.Single((await api.PostOkAsync<UiTokensResponse>(Operations.UiTokens, null, Ct)).Items);
        Assert.Equal(session.TokenId, listed.Id);
        Assert.Equal(Environment.UserName, listed.IssuedTo);

        var (again, againBody, _) = await DoorAsync(api, ticket, Own(api));
        Assert.Equal(HttpStatusCode.Unauthorized, again);
        Assert.Contains("\"ticket_invalid\"", againBody, StringComparison.Ordinal);
        Assert.Single((await api.PostOkAsync<UiTokensResponse>(Operations.UiTokens, null, Ct)).Items);
    }

    [Fact]
    public async Task An_expired_or_unknown_ticket_is_refused_and_nothing_is_minted()
    {
        var clock = new FixedClock(Nine);
        await using var api = await StartWithBuildAsync(clock);
        var ticket = await TicketAsync(api);
        clock.Advance(UiTickets.Lifetime + TimeSpan.FromSeconds(1));

        var (expired, _, _) = await DoorAsync(api, ticket, Own(api));
        Assert.Equal(HttpStatusCode.Unauthorized, expired);
        var (unknown, _, _) = await DoorAsync(api, "not-a-ticket", Own(api));
        Assert.Equal(HttpStatusCode.Unauthorized, unknown);
        var (empty, emptyBody, _) = await DoorAsync(api, null, Own(api));
        Assert.Equal(HttpStatusCode.Unauthorized, empty);
        Assert.Contains("\"ticket_invalid\"", emptyBody, StringComparison.Ordinal);
        Assert.Empty((await api.PostOkAsync<UiTokensResponse>(Operations.UiTokens, null, Ct)).Items);
    }

    [Fact]
    [Critical(Family.S5, "pairing origin")]
    public async Task The_door_listens_only_to_the_runtimes_own_origin()
    {
        await using var api = await StartWithBuildAsync(new FixedClock(Nine));

        var (noOrigin, noOriginBody, _) = await DoorAsync(api, await TicketAsync(api), origin: null);
        Assert.Equal(HttpStatusCode.Forbidden, noOrigin);
        Assert.Contains("\"origin_refused\"", noOriginBody, StringComparison.Ordinal);

        var (foreign, _, _) = await DoorAsync(api, await TicketAsync(api), origin: "http://evil.example");
        Assert.Equal(HttpStatusCode.Forbidden, foreign);

        var (localhost, _, _) = await DoorAsync(api, await TicketAsync(api), origin: $"http://localhost:{api.Runtime.BaseUrl.Port}");
        Assert.Equal(HttpStatusCode.OK, localhost);
    }

    [Theory]
    [InlineData("not json", "application/json")]
    [InlineData("ticket=abc", "text/plain")]
    public async Task A_malformed_body_at_the_door_is_a_400_not_a_pairing(string body, string contentType)
    {
        await using var api = await StartWithBuildAsync(new FixedClock(Nine));
        using var http = new HttpClient { BaseAddress = api.Runtime.BaseUrl };
        http.DefaultRequestHeaders.Add("Origin", Own(api));
        using var content = new StringContent(body, Encoding.UTF8, contentType);
        using var response = await http.PostAsync(UiPaths.Door, content, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("\"invalid_request\"", await response.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal(UiServingTests.Csp, response.Headers.GetValues("Content-Security-Policy").Single());
    }

    internal static async Task<RuntimeApiFixture> StartWithBuildAsync(FixedClock clock) =>
        await RuntimeApiFixture.StartAsync(Ct, clock: clock, prepare: paths =>
        {
            File.WriteAllText(paths.UserSettingsFile, RuntimeApiFixture.DispatcherOff);
            UiBuild.Write(paths.UiDefaultDirectory);
        });

    internal static string Own(RuntimeApiFixture api) => $"http://127.0.0.1:{api.Runtime.BaseUrl.Port}";

    internal static async Task<string> TicketAsync(RuntimeApiFixture api)
    {
        var opened = await api.PostOkAsync<UiOpenResponse>(Operations.UiOpen, null, Ct);
        return opened.Url[(opened.Url.IndexOf("?t=", StringComparison.Ordinal) + 3)..];
    }

    internal static async Task<(HttpStatusCode Status, string Body, Dictionary<string, string> Headers)> DoorAsync(RuntimeApiFixture api, string? ticket, string? origin)
    {
        using var http = new HttpClient { BaseAddress = api.Runtime.BaseUrl };
        if (origin is not null)
        {
            http.DefaultRequestHeaders.Add("Origin", origin);
        }

        var body = ticket is null ? "{}" : JsonSerializer.Serialize(new { ticket });
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = await http.PostAsync(UiPaths.Door, content, Ct);
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, values) in response.Headers.Concat(response.Content.Headers))
        {
            headers[name] = string.Join(", ", values);
        }

        return (response.StatusCode, await response.Content.ReadAsStringAsync(Ct), headers);
    }
}
