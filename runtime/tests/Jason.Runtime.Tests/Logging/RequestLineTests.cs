using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Runtime.Hosting;

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
