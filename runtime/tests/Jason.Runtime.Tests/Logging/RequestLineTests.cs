using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Runtime.Api;
using Jason.Runtime.Hosting;
using Microsoft.AspNetCore.Http;

namespace Jason.Runtime.Tests.Logging;

/// <summary>
/// The line the host writes for every request, read back from the runtime's own log. Its path and its method are
/// whatever a local client sends, and the line is written for every request — before the host it named is checked and
/// before its token is — so neither field may carry them as sent: the path is the route of the operation the request
/// matched, or <c>unmatched</c>, and the method is the one the API uses, or <c>other</c>.
/// </summary>
public class RequestLineTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_request_that_matched_no_operation_is_logged_as_unmatched_and_its_path_reaches_no_line()
    {
        var sentinel = "SENTINEL_PATH_" + Guid.NewGuid().ToString("N");

        var (logs, lines) = await LoggedAsync(async (http, token) =>
        {
            // With the token and without it: the line is written either way, and the refusal comes after it.
            Assert.Equal(HttpStatusCode.NotFound, await SendAsync(http, HttpMethod.Post, "/" + sentinel + "/campaign.list", token));
            Assert.Equal(HttpStatusCode.Unauthorized, await SendAsync(http, HttpMethod.Post, "/v1/" + sentinel, token: null));
        });

        Assert.DoesNotContain(sentinel, logs, StringComparison.Ordinal);
        Assert.Equal("unmatched", (string?)Assert.Single(lines, line => (int?)line["StatusCode"] == 404)["RequestPath"]);
        Assert.Equal("unmatched", (string?)Assert.Single(lines, line => (int?)line["StatusCode"] == 401)["RequestPath"]);
    }

    /// <summary>
    /// Routing compares a path without regard to case, so the path as sent is not the route even when it matched one.
    /// The line names the route as the runtime spells it.
    /// </summary>
    [Fact]
    public async Task A_request_that_matched_an_operation_is_logged_by_the_route_and_not_by_the_path_as_sent()
    {
        var (_, lines) = await LoggedAsync(async (http, token) =>
            Assert.Equal(HttpStatusCode.OK, await SendAsync(http, HttpMethod.Post, "/V1/SYSTEM.INFO", token)));

        var line = Assert.Single(lines, line => (int?)line["StatusCode"] == 200);
        Assert.Equal(Operations.Route(Operations.SystemInfo), (string?)line["RequestPath"]);
        Assert.Equal("POST", (string?)line["RequestMethod"]);
    }

    [Fact]
    public async Task A_method_the_api_does_not_use_is_logged_as_other_and_reaches_no_line_as_sent()
    {
        var sentinel = "SENTINEL_METHOD_" + Guid.NewGuid().ToString("N");

        var (logs, lines) = await LoggedAsync(async (http, token) =>
            await SendAsync(http, new HttpMethod(sentinel), Operations.Route(Operations.SystemInfo), token));

        Assert.DoesNotContain(sentinel, logs, StringComparison.Ordinal);
        Assert.Equal("other", (string?)Assert.Single(lines)["RequestMethod"]);
    }

    /// <summary>
    /// A caller that goes away halfway through its body is answered by nobody, and its line says what happened: 499, the
    /// status of a request its caller closed, and not the 200 that a response never written would otherwise keep. Nothing
    /// of the half of the body that arrived reaches the log.
    /// </summary>
    [Fact]
    public async Task A_request_whose_caller_went_away_mid_body_is_logged_as_closed_and_not_as_answered()
    {
        var route = Operations.Route(Operations.JournalList);
        var (logs, lines) = await LoggedAsync(async (http, token) =>
        {
            var address = http.BaseAddress!;
            using (var client = new TcpClient())
            {
                await client.ConnectAsync(address.Host, address.Port, Ct);
                var head = $"POST {route} HTTP/1.1\r\nHost: {address.Authority}\r\nAuthorization: Bearer {token}\r\nContent-Type: application/json\r\nContent-Length: 500\r\n\r\n{{\"since\":\"SENTINEL_HALF_A_BODY";
                await client.GetStream().WriteAsync(Encoding.ASCII.GetBytes(head), Ct);
                await client.GetStream().FlushAsync(Ct);

                // The runtime is reading the body when the caller resets the connection.
                await Task.Delay(300, Ct);
                client.LingerState = new LingerOption(enable: true, seconds: 0);
            }

            // The line is written as the request ends, which is the runtime's to finish after the reset.
            await Task.Delay(500, Ct);
        });

        var line = Assert.Single(lines, line => (string?)line["RequestPath"] == route);
        Assert.Equal(499, (int?)line["StatusCode"]);
        Assert.DoesNotContain("SENTINEL_HALF_A_BODY", logs, StringComparison.Ordinal);
    }

    /// <summary>
    /// The body read fails before the request is known to be closed: the server throws as the connection's input ends,
    /// and cancels the request's token afterwards, from the thread pool, so on a busy machine the door's catch runs
    /// first. The server learns that a caller is gone in one of two ways, and the read's failure says which. A premature
    /// end — the body stopped short of the length its head announced — is how it reaches the door where the connection's
    /// input simply ends (Windows, Linux). This test is that way; the token is never cancelled.
    /// </summary>
    [Fact]
    public void A_body_that_stopped_short_of_its_announced_length_is_a_caller_gone_before_the_token_says_so()
    {
        var context = new DefaultHttpContext();
        context.Request.ContentLength = 500;

        Assert.False(context.RequestAborted.IsCancellationRequested);
        Assert.True(OperationEndpoints.CallerWentAway(context, EndedEarly()));
    }

    /// <summary>
    /// The other way: a reset, which is how the same abandoned request reaches the door where the connection's input
    /// ends in a socket error (macOS). The caller is gone however its body was framed, so no announced length is needed;
    /// and again the token is never cancelled.
    /// </summary>
    [Fact]
    public void A_connection_the_caller_reset_is_a_caller_gone_before_the_token_says_so()
    {
        var chunked = new DefaultHttpContext();

        Assert.False(chunked.RequestAborted.IsCancellationRequested);
        Assert.True(OperationEndpoints.CallerWentAway(chunked, Reset()));
    }

    /// <summary>
    /// What is not a caller gone: a body the caller finished that does not parse, a body over the server's limit, a
    /// chunked body the server could not read, which has no announced length to have stopped short of, and a failure to
    /// read that is neither of the server's two words for a gone caller. Each is answered, because whoever sent it may
    /// still be there to read the answer.
    /// </summary>
    [Fact]
    public void A_body_that_was_finished_or_refused_for_its_size_or_has_no_announced_length_is_answered()
    {
        var announced = new DefaultHttpContext();
        announced.Request.ContentLength = 500;
        var chunked = new DefaultHttpContext();

        Assert.False(OperationEndpoints.CallerWentAway(announced, new System.Text.Json.JsonException("not JSON")));
        Assert.False(OperationEndpoints.CallerWentAway(
            announced,
            new BadHttpRequestException("Request body too large.", StatusCodes.Status413PayloadTooLarge)));
        Assert.False(OperationEndpoints.CallerWentAway(chunked, EndedEarly()));
        Assert.False(OperationEndpoints.CallerWentAway(announced, new IOException("a read failed")));
    }

    /// <summary>A request whose token was cancelled is a caller gone, whatever the read threw.</summary>
    [Fact]
    public void A_request_whose_token_was_cancelled_is_a_caller_gone()
    {
        using var aborted = new CancellationTokenSource();
        aborted.Cancel();
        var context = new DefaultHttpContext { RequestAborted = aborted.Token };

        Assert.True(OperationEndpoints.CallerWentAway(context, new System.Text.Json.JsonException("not JSON")));
    }

    private static BadHttpRequestException EndedEarly() =>
        new("Unexpected end of request content.", StatusCodes.Status400BadRequest);

    /// <summary>The server's own exception for a reset, over the socket's, as the door meets it on macOS.</summary>
    private static Microsoft.AspNetCore.Connections.ConnectionResetException Reset() =>
        new("Connection reset by peer", new System.Net.Sockets.SocketException((int)System.Net.Sockets.SocketError.ConnectionReset));

    /// <summary>
    /// Starts a runtime, sends what the test sends, stops it — stopping flushes and closes the file sink, so what is
    /// on disk then is everything there is — and hands back the whole log as text and its request lines as events.
    /// </summary>
    private static async Task<(string Logs, List<JsonObject> RequestLines)> LoggedAsync(Func<HttpClient, string, Task> send)
    {
        using var dir = new TempDataDir();
        var runtime = await RuntimeHost.StartAsync(dir.Paths, TestRuntimeOptions.Quiet, Ct);
        try
        {
            using var http = new HttpClient { BaseAddress = runtime.BaseUrl };
            await send(http, runtime.Token);
        }
        finally
        {
            await runtime.StopAsync();
        }

        var logs = string.Concat(Directory.GetFiles(dir.Paths.LogsDirectory, "*", SearchOption.AllDirectories).Select(File.ReadAllText));
        var requestLines = logs
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => JsonNode.Parse(line)!.AsObject())
            .Where(line => line.ContainsKey("RequestPath") && line.ContainsKey("StatusCode"))
            .ToList();
        return (logs, requestLines);
    }

    private static async Task<HttpStatusCode> SendAsync(HttpClient http, HttpMethod method, string path, string? token)
    {
        using var request = new HttpRequestMessage(method, path) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        using var response = await http.SendAsync(request, Ct);
        return response.StatusCode;
    }
}
