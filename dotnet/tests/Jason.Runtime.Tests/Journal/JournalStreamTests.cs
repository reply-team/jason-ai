using System.Diagnostics;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Json;
using Jason.Runtime.Ui;

namespace Jason.Runtime.Tests.Journal;

/// <summary>
/// <c>GET /v1/journal.stream</c>, opened the way a program and a page open it: every line written after it opens arrives,
/// oldest first, one text frame each in the shape <c>journal.list</c> answers a line; a cursor of <c>journal.list</c>'s
/// starts it after the line the cursor names; the bearer comes in the header, or as a subprotocol on this route alone;
/// and the stream ends when the UI token it was opened with is revoked and when the runtime stops.
/// </summary>
public class JournalStreamTests
{
    /// <summary>How long a frame or a close may take to arrive: several reads of the stream, on a busy machine.</summary>
    private static readonly TimeSpan Arrival = TimeSpan.FromSeconds(15);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_line_written_after_the_connection_opens_arrives_as_journal_list_answers_it_and_one_written_before_does_not()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var campaign = await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { Name = "Before" }, Ct);
        using var socket = await OpenAsync(api, header: api.Runtime.Token);
        Assert.Equal(Streams.JournalSubProtocol, socket.SubProtocol);

        var first = await AppendAsync(api, campaign, "step_1");
        var second = await AppendAsync(api, campaign, "step_2");

        var arrived = await ReadAsync(socket);
        Assert.Equal(first.Id, (string?)arrived["id"]);
        Assert.Equal(second.Id, (string?)(await ReadAsync(socket))["id"]);

        var listed = JsonNode.Parse((await api.PostAsync(Operations.JournalList, new { CampaignId = campaign.Id, Limit = 2 }, Ct)).Body)!["items"]!.AsArray();
        var asListed = listed.Single(line => (string?)line!["id"] == first.Id)!;
        Assert.True(JsonNode.DeepEquals(asListed, arrived), $"journal.list answers {asListed.ToJsonString()}; the stream sent {arrived.ToJsonString()}.");
    }

    [Fact]
    public async Task The_stream_starts_after_the_line_a_journal_list_cursor_names()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var campaign = await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { Name = "Resumed" }, Ct);
        var older = await AppendAsync(api, campaign, "older");
        var named = await AppendAsync(api, campaign, "named");
        var after = await AppendAsync(api, campaign, "after");

        // Newest first, two to a page: the page is the two newest lines, and its cursor names the older of them.
        var page = await api.PostOkAsync<Page<JournalEntryDto>>(Operations.JournalList, new { Limit = 2 }, Ct);
        Assert.Equal([after.Id, named.Id], page.Items.Select(line => line.Id));
        Assert.NotNull(page.NextCursor);

        using var socket = await OpenAsync(api, header: api.Runtime.Token, since: page.NextCursor);
        Assert.Equal(after.Id, (string?)(await ReadAsync(socket))["id"]);

        // Neither the line the cursor names nor one before it comes after it: the next frame is the next line written.
        var later = await AppendAsync(api, campaign, "later");
        Assert.Equal(later.Id, (string?)(await ReadAsync(socket))["id"]);
        Assert.NotEqual(older.Id, later.Id);
    }

    [Fact]
    public async Task A_since_that_is_no_cursor_is_refused_before_the_socket_opens()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        using var socket = Client(header: api.Runtime.Token);
        socket.Options.CollectHttpResponseDetails = true;

        await Assert.ThrowsAsync<WebSocketException>(() => socket.ConnectAsync(Address(api, since: "not a cursor!"), Ct));
        Assert.Equal(HttpStatusCode.BadRequest, socket.HttpStatusCode);
    }

    [Fact]
    [Critical(Family.S5, "the stream needs a bearer")]
    public async Task A_connection_with_no_bearer_is_refused_before_the_socket_opens()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        using var socket = Client();
        socket.Options.CollectHttpResponseDetails = true;

        await Assert.ThrowsAsync<WebSocketException>(() => socket.ConnectAsync(Address(api), Ct));
        Assert.Equal(HttpStatusCode.Unauthorized, socket.HttpStatusCode);
    }

    [Fact]
    public async Task A_page_may_offer_its_bearer_as_a_subprotocol_and_is_answered_with_the_stream_s_alone()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var campaign = await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { Name = "Paged" }, Ct);
        using var socket = Client(subprotocolBearer: api.Runtime.Token);
        socket.Options.CollectHttpResponseDetails = true;

        await socket.ConnectAsync(Address(api), Ct);

        Assert.Equal(WebSocketState.Open, socket.State);
        Assert.Equal(Streams.JournalSubProtocol, socket.SubProtocol);
        Assert.Equal(Streams.JournalSubProtocol, Assert.Single(socket.HttpResponseHeaders!["Sec-WebSocket-Protocol"]));
        var appended = await AppendAsync(api, campaign, "step_1");
        Assert.Equal(appended.Id, (string?)(await ReadAsync(socket))["id"]);
    }

    [Fact]
    [Critical(Family.S5, "a bearer as a subprotocol opens the stream alone")]
    public async Task A_bearer_offered_as_a_subprotocol_opens_only_the_stream()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        // An upgrade elsewhere, offering the bearer the same way.
        using var socket = Client(subprotocolBearer: api.Runtime.Token);
        socket.Options.CollectHttpResponseDetails = true;
        var elsewhere = new Uri($"ws://127.0.0.1:{api.Runtime.BaseUrl.Port}{Operations.Route(Operations.SystemInfo)}");
        await Assert.ThrowsAsync<WebSocketException>(() => socket.ConnectAsync(elsewhere, Ct));
        Assert.Equal(HttpStatusCode.Unauthorized, socket.HttpStatusCode);

        // And an operation, with the header a page's upgrade would carry.
        using var http = new HttpClient { BaseAddress = api.Runtime.BaseUrl };
        using var request = new HttpRequestMessage(HttpMethod.Post, Operations.Route(Operations.SystemInfo)) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
        request.Headers.TryAddWithoutValidation("Sec-WebSocket-Protocol", $"{Streams.JournalSubProtocol}, {Streams.BearerSubProtocolPrefix}{api.Runtime.Token}");
        using var response = await http.SendAsync(request, Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_plain_get_is_refused_in_the_api_s_error_shape()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        using var http = new HttpClient { BaseAddress = api.Runtime.BaseUrl };
        using var request = new HttpRequestMessage(HttpMethod.Get, Streams.JournalRoute);
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {api.Runtime.Token}");

        using var response = await http.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = JsonSerializer.Deserialize<ErrorResponse>(await response.Content.ReadAsStringAsync(Ct), JasonJson.Options)!.Error;
        Assert.Equal("websocket_required", error.Code);
        Assert.False(error.Retryable);
    }

    [Fact]
    public async Task An_upgrade_that_does_not_offer_the_stream_s_subprotocol_is_refused()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        using var socket = new ClientWebSocket();
        socket.Options.SetRequestHeader("Authorization", $"Bearer {api.Runtime.Token}");
        socket.Options.CollectHttpResponseDetails = true;

        await Assert.ThrowsAsync<WebSocketException>(() => socket.ConnectAsync(Address(api), Ct));
        Assert.Equal(HttpStatusCode.BadRequest, socket.HttpStatusCode);
    }

    [Fact]
    [Critical(Family.S5, "a revoked UI token's stream closes")]
    public async Task A_stream_opened_with_a_ui_token_closes_as_policy_once_the_token_is_revoked()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var (token, issued) = await api.Resolve<UiTokenStore>().IssueAsync("a page", Ct);
        var campaign = await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { Name = "Paired" }, Ct);
        using var socket = Client(subprotocolBearer: token);
        await socket.ConnectAsync(Address(api), Ct);
        var appended = await AppendAsync(api, campaign, "step_1");
        Assert.Equal(appended.Id, (string?)(await ReadAsync(socket))["id"]);

        await api.PostOkAsync<UiRevokeResponse>(Operations.UiRevoke, new { TokenId = issued.Id }, Ct);

        var (status, _) = await ClosedAsync(socket);
        Assert.Equal(WebSocketCloseStatus.PolicyViolation, status);
    }

    [Fact]
    public async Task The_runtime_s_stop_closes_an_open_stream_as_going_away_and_is_not_held_by_it()
    {
        var api = await RuntimeApiFixture.StartAsync(Ct);
        await using (api)
        {
            using var socket = await OpenAsync(api, header: api.Runtime.Token);
            var closed = ClosedAsync(socket);

            var stopping = Stopwatch.StartNew();
            await api.Runtime.StopAsync();
            stopping.Stop();

            var (status, _) = await closed;
            Assert.Equal(WebSocketCloseStatus.EndpointUnavailable, status);

            // The host would wait out its shutdown timeout, fifteen seconds here, for a request that never ended.
            Assert.True(stopping.Elapsed < TimeSpan.FromSeconds(10), $"The stop took {stopping.Elapsed.TotalSeconds:0.0} s with a stream open.");
        }
    }

    internal static Uri Address(RuntimeApiFixture api, string? since = null) =>
        new($"ws://127.0.0.1:{api.Runtime.BaseUrl.Port}{Streams.JournalRoute}{(since is null ? string.Empty : $"?{Streams.SinceParameter}={Uri.EscapeDataString(since)}")}");

    internal static ClientWebSocket Client(string? header = null, string? subprotocolBearer = null)
    {
        var socket = new ClientWebSocket();
        socket.Options.AddSubProtocol(Streams.JournalSubProtocol);
        if (header is not null)
        {
            socket.Options.SetRequestHeader("Authorization", $"Bearer {header}");
        }

        if (subprotocolBearer is not null)
        {
            socket.Options.AddSubProtocol(Streams.BearerSubProtocolPrefix + subprotocolBearer);
        }

        return socket;
    }

    internal static async Task<ClientWebSocket> OpenAsync(RuntimeApiFixture api, string? header = null, string? subprotocolBearer = null, string? since = null)
    {
        var socket = Client(header, subprotocolBearer);
        await socket.ConnectAsync(Address(api, since), Ct);
        return socket;
    }

    private static Task<JournalEntryDto> AppendAsync(RuntimeApiFixture api, CampaignDto campaign, string key) =>
        api.PostOkAsync<JournalEntryDto>(
            Operations.JournalAppend,
            new { CampaignId = campaign.Id, Kind = "plan_revision", Key = key, New = "x", Actor = new { Type = "role", Id = "planner" } },
            Ct);

    /// <summary>The next frame, whole, as JSON; a close or a wait that runs out fails the test saying which.</summary>
    internal static async Task<JsonObject> ReadAsync(WebSocket socket)
    {
        using var within = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        within.CancelAfter(Arrival);
        using var frame = new MemoryStream();
        var buffer = new byte[4096];
        WebSocketReceiveResult received;
        do
        {
            received = await socket.ReceiveAsync(buffer, within.Token);
            Assert.True(received.MessageType == WebSocketMessageType.Text, $"The stream sent a {received.MessageType} frame ({received.CloseStatus}) where a line was expected.");
            frame.Write(buffer, 0, received.Count);
        }
        while (!received.EndOfMessage);

        return JsonNode.Parse(frame.ToArray())!.AsObject();
    }

    /// <summary>Reads until the runtime closes the stream, and answers how; a line that arrives first is passed over.</summary>
    internal static async Task<(WebSocketCloseStatus? Status, string? Description)> ClosedAsync(WebSocket socket)
    {
        using var within = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        within.CancelAfter(Arrival);
        var buffer = new byte[4096];
        while (true)
        {
            var received = await socket.ReceiveAsync(buffer, within.Token);
            if (received.MessageType == WebSocketMessageType.Close)
            {
                return (received.CloseStatus, received.CloseStatusDescription);
            }
        }
    }
}
