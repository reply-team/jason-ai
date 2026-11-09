using System.Diagnostics;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Ids;
using Jason.Contracts.Json;
using Jason.Runtime.Persistence;
using Jason.Runtime.Ui;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests.Journal;

/// <summary>
/// <c>GET /v1/journal.stream</c>, opened the way a program and a page open it: every line written after it opens arrives,
/// oldest first, one text frame each in the shape <c>journal.list</c> answers a line; a line's id, as <c>after</c>, starts
/// it after that line, in the order the lines were committed; the bearer comes in the header, or as a subprotocol on this route alone,
/// and a token that is revoked or unknown is refused at the upgrade; and the stream ends when the UI token it was opened
/// with is revoked and when the runtime stops.
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
    public async Task The_stream_starts_after_the_line_after_names()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var campaign = await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { Name = "Resumed" }, Ct);
        await AppendAsync(api, campaign, "older");
        var named = await AppendAsync(api, campaign, "named");
        var next = await AppendAsync(api, campaign, "next");

        using var socket = await OpenAsync(api, header: api.Runtime.Token, after: named.Id);
        Assert.Equal(next.Id, (string?)(await ReadAsync(socket))["id"]);

        // Neither the line after names nor one before it comes after it: the next frame is the next line written.
        var later = await AppendAsync(api, campaign, "later");
        Assert.Equal(later.Id, (string?)(await ReadAsync(socket))["id"]);
    }

    /// <summary>
    /// A line's id is made before its write waits for the database, so a line can be committed after one whose id is
    /// newer. A program that resumes from the newest id <c>journal.list</c> showed it is sent the line committed after
    /// that one again, which it already has and drops by its id, and is sent everything written after: nothing is lost.
    /// </summary>
    [Fact]
    public async Task A_resume_from_a_line_committed_before_one_with_an_older_id_sends_that_one_again_and_loses_nothing()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var campaign = await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { Name = "Raced" }, Ct);
        var olderLooking = PublicId.New("jrn");
        var newerLooking = PublicId.New("jrn");
        await CommitAsync(api, newerLooking, "committed_first");
        await CommitAsync(api, olderLooking, "committed_second");

        var listed = await api.PostOkAsync<Page<JournalEntryDto>>(Operations.JournalList, new { Limit = 2 }, Ct);
        Assert.Equal([newerLooking, olderLooking], listed.Items.Select(line => line.Id));

        using var socket = await OpenAsync(api, header: api.Runtime.Token, after: listed.Items[0].Id);
        Assert.Equal(olderLooking, (string?)(await ReadAsync(socket))["id"]);
        var later = await AppendAsync(api, campaign, "later");
        Assert.Equal(later.Id, (string?)(await ReadAsync(socket))["id"]);
    }

    [Theory]
    [InlineData("not an id!", "invalid")]
    [InlineData("jrn_01K7QN00000000000000000000", "unknown")]
    [InlineData("anJuXzAxSzdRTjAwMDAwMDAwMDAwMDAwMDAwMDAw", "invalid")]
    public async Task An_after_that_names_no_line_is_refused_before_the_socket_opens_in_the_api_s_error_shape(string after, string code)
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        var (status, body) = await UpgradeAsync(api, after);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        var error = JsonSerializer.Deserialize<ErrorResponse>(body, JasonJson.Options)!.Error;
        Assert.Equal("validation_failed", error.Code);
        var detail = Assert.Single(error.Details!);
        Assert.Equal(Streams.AfterParameter, detail.Field);
        Assert.Equal(code, detail.Code);

        // The answer says what the value must be and never repeats it.
        Assert.DoesNotContain(after, body, StringComparison.Ordinal);
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
    [Critical(Family.S5, "a revoked or unknown token is refused at the upgrade")]
    public async Task A_revoked_or_unknown_token_is_refused_at_the_upgrade_and_never_opened()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var (revoked, issued) = await api.Resolve<UiTokenStore>().IssueAsync("a page", Ct);
        await api.PostOkAsync<UiRevokeResponse>(Operations.UiRevoke, new { TokenId = issued.Id }, Ct);

        foreach (var token in new[] { revoked, "not-a-token-this-runtime-made" })
        {
            foreach (var socket in new[] { Client(header: token), Client(subprotocolBearer: token) })
            {
                using (socket)
                {
                    socket.Options.CollectHttpResponseDetails = true;
                    await Assert.ThrowsAsync<WebSocketException>(() => socket.ConnectAsync(Address(api), Ct));
                    Assert.Equal(HttpStatusCode.Unauthorized, socket.HttpStatusCode);
                }
            }
        }
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

        var (status, description) = await ClosedAsync(socket);
        Assert.Equal(WebSocketCloseStatus.PolicyViolation, status);
        Assert.Equal("revoked", description);
    }

    [Fact]
    [Critical(Family.S5, "the runtime's stop closes an open stream")]
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

    /// <summary>
    /// A reader that has stopped taking frames, a <c>jason journal watch | less</c> left paused on a busy journal: the
    /// lines fill the connection's buffers and the runtime's send waits on them. The stop cuts that send rather than
    /// wait out the host's shutdown timeout, fifteen seconds here, and lets the connection go without a close, which
    /// nobody would read.
    /// </summary>
    [Fact]
    [Critical(Family.S5, "a reader that stops taking frames never holds the stop")]
    public async Task A_reader_that_stops_taking_frames_does_not_hold_the_runtime_s_stop()
    {
        var api = await RuntimeApiFixture.StartAsync(Ct);
        await using (api)
        {
            using var socket = await OpenAsync(api, header: api.Runtime.Token);

            // Far more than the loopback buffers and the server's own hold, which the stream fills and then waits on.
            var large = new string('x', 512 * 1024);
            for (var i = 0; i < 128; i++)
            {
                await CommitAsync(api, PublicId.New("jrn"), $"large_{i}", large);
            }

            await Task.Delay(TimeSpan.FromSeconds(3), Ct);

            var stopping = Stopwatch.StartNew();
            await api.Runtime.StopAsync();
            stopping.Stop();

            Assert.True(stopping.Elapsed < TimeSpan.FromSeconds(10), $"The stop took {stopping.Elapsed.TotalSeconds:0.0} s with a reader that takes nothing.");
        }
    }

    internal static Uri Address(RuntimeApiFixture api, string? after = null) =>
        new($"ws://127.0.0.1:{api.Runtime.BaseUrl.Port}{Streams.JournalRoute}{(after is null ? string.Empty : $"?{Streams.AfterParameter}={Uri.EscapeDataString(after)}")}");

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

    internal static async Task<ClientWebSocket> OpenAsync(RuntimeApiFixture api, string? header = null, string? subprotocolBearer = null, string? after = null)
    {
        var socket = Client(header, subprotocolBearer);
        await socket.ConnectAsync(Address(api, after), Ct);
        return socket;
    }

    private static Task<JournalEntryDto> AppendAsync(RuntimeApiFixture api, CampaignDto campaign, string key) =>
        api.PostOkAsync<JournalEntryDto>(
            Operations.JournalAppend,
            new { CampaignId = campaign.Id, Kind = "plan_revision", Key = key, New = "x", Actor = new { Type = "role", Id = "planner" } },
            Ct);

    /// <summary>A journal line committed as it stands, under an id the test chose, the way the journal's writer stores one.</summary>
    private static async Task CommitAsync(RuntimeApiFixture api, string id, string key, string? reason = null)
    {
        using var scope = api.Resolve<IServiceScopeFactory>().CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JasonDbContext>();
        db.Journal.Add(new JournalEntry { PublicId = id, Ts = DateTime.UtcNow, ActorType = ActorType.Role, ActorId = "planner", Kind = "plan_revision", Key = key, Reason = reason });
        await db.SaveChangesAsync(Ct);
    }

    /// <summary>An upgrade made by hand, so a refusal's status and body are both read.</summary>
    private static async Task<(HttpStatusCode Status, string Body)> UpgradeAsync(RuntimeApiFixture api, string after)
    {
        using var http = new HttpClient { BaseAddress = api.Runtime.BaseUrl };
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{Streams.JournalRoute}?{Streams.AfterParameter}={Uri.EscapeDataString(after)}")
        {
            Version = HttpVersion.Version11,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact,
        };
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {api.Runtime.Token}");
        request.Headers.TryAddWithoutValidation("Connection", "Upgrade");
        request.Headers.TryAddWithoutValidation("Upgrade", "websocket");
        request.Headers.TryAddWithoutValidation("Sec-WebSocket-Version", "13");
        request.Headers.TryAddWithoutValidation("Sec-WebSocket-Key", Convert.ToBase64String(new byte[16]));
        request.Headers.TryAddWithoutValidation("Sec-WebSocket-Protocol", Streams.JournalSubProtocol);
        using var within = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        within.CancelAfter(Arrival);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, within.Token);

        // An upgrade the runtime accepted has no body to read to its end: its status is the whole answer.
        return response.StatusCode == HttpStatusCode.SwitchingProtocols
            ? (response.StatusCode, string.Empty)
            : (response.StatusCode, await response.Content.ReadAsStringAsync(within.Token));
    }

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
