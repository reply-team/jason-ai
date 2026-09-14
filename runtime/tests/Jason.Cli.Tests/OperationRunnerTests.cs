using System.Net;
using System.Text.Json.Nodes;
using Jason.Cli;
using Jason.Contracts.Discovery;

namespace Jason.Cli.Tests;

public class OperationRunnerTests
{
    private static readonly RuntimeDescriptor Descriptor = new("v1", "0.1.0-dev", "rt_LIVE", 77, "http://127.0.0.1:5000", "the-token", DateTimeOffset.UnixEpoch);

    [Fact]
    public async Task No_descriptor_exits_3_with_no_descriptor_error()
    {
        using var dir = new TempPaths();
        var (env, output, _) = Environment(dir, new FakeHandler(_ => throw new InvalidOperationException("must not be called")));

        var exit = await OperationRunner.RunAsync(env, "campaign.list", RequestBody.Empty(), new RunOptions(Human: false), TestContext.Current.CancellationToken);

        Assert.Equal(ExitCodes.RuntimeUnavailable, exit);
        Assert.Contains("\"code\":\"no_descriptor\"", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_refused_connection_exits_3_with_runtime_unreachable()
    {
        using var dir = new TempPaths();
        dir.WriteDescriptor(Descriptor);
        var (env, output, _) = Environment(dir, new FakeHandler(_ => throw new HttpRequestException("connection refused")));

        var exit = await OperationRunner.RunAsync(env, "campaign.list", RequestBody.Empty(), new RunOptions(Human: false), TestContext.Current.CancellationToken);

        Assert.Equal(ExitCodes.RuntimeUnavailable, exit);
        Assert.Contains("\"code\":\"runtime_unreachable\"", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_rejected_token_exits_3_with_unauthorized()
    {
        using var dir = new TempPaths();
        dir.WriteDescriptor(Descriptor);
        var (env, output, _) = Environment(dir, new FakeHandler(_ => Response(HttpStatusCode.Unauthorized, "{\"error\":{\"code\":\"unauthorized\",\"message\":\"x\",\"retryable\":false}}")));

        var exit = await OperationRunner.RunAsync(env, "campaign.list", RequestBody.Empty(), new RunOptions(Human: false), TestContext.Current.CancellationToken);

        Assert.Equal(ExitCodes.RuntimeUnavailable, exit);
        Assert.Contains("\"code\":\"unauthorized\"", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_business_error_is_printed_verbatim_and_exits_1()
    {
        using var dir = new TempPaths();
        dir.WriteDescriptor(Descriptor);
        const string envelope = "{\"error\":{\"code\":\"invalid_transition\",\"message\":\"nope\",\"retryable\":false}}";
        var (env, output, error) = Environment(dir, new FakeHandler(_ => Response(HttpStatusCode.Conflict, envelope)));

        var exit = await OperationRunner.RunAsync(env, "campaign.start", RequestBody.Empty(), new RunOptions(Human: false), TestContext.Current.CancellationToken);

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Equal(envelope + System.Environment.NewLine, output.ToString());
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public async Task A_successful_response_is_printed_verbatim_and_the_request_carries_the_body_and_the_token()
    {
        using var dir = new TempPaths();
        dir.WriteDescriptor(Descriptor);
        const string responseBody = "{\"id\":\"cmp_A\",\"name\":\"LatAm\"}";
        HttpRequestMessage? seen = null;
        string? sent = null;
        var (env, output, error) = Environment(dir, new FakeHandler(request =>
        {
            seen = request;
            sent = request.Content!.ReadAsStringAsync(CancellationToken.None).GetAwaiter().GetResult();
            return Response(HttpStatusCode.OK, responseBody);
        }));

        var body = RequestBody.Empty().Set("name", "LatAm").SetActor(ActorOption.Parse("role:planner"));
        var exit = await OperationRunner.RunAsync(env, "campaign.create", body, new RunOptions(Human: false), TestContext.Current.CancellationToken);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(responseBody + System.Environment.NewLine, output.ToString());
        Assert.Equal(string.Empty, error.ToString());
        Assert.NotNull(seen);
        Assert.Equal(HttpMethod.Post, seen!.Method);
        Assert.Equal("http://127.0.0.1:5000/v1/campaign.create", seen.RequestUri!.ToString());
        Assert.Equal("Bearer", seen.Headers.Authorization!.Scheme);
        Assert.Equal("the-token", seen.Headers.Authorization.Parameter);
        Assert.Equal("{\"name\":\"LatAm\",\"actor\":{\"type\":\"role\",\"id\":\"planner\"}}", sent);
    }

    [Fact]
    public async Task Human_mode_prints_what_the_renderer_returns()
    {
        using var dir = new TempPaths();
        dir.WriteDescriptor(Descriptor);
        var (env, output, _) = Environment(dir, new FakeHandler(_ => Response(HttpStatusCode.OK, "{\"name\":\"LatAm\"}")));

        var exit = await OperationRunner.RunAsync(
            env,
            "campaign.get",
            RequestBody.Empty(),
            new RunOptions(Human: true, RenderHuman: json => "Name: " + JsonNode.Parse(json)!["name"]!.GetValue<string>()),
            TestContext.Current.CancellationToken);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal("Name: LatAm" + System.Environment.NewLine, output.ToString());
    }

    [Fact]
    public async Task Human_mode_falls_back_to_the_raw_body_when_the_renderer_declines()
    {
        using var dir = new TempPaths();
        dir.WriteDescriptor(Descriptor);
        const string responseBody = "{\"name\":\"LatAm\"}";
        var (env, output, _) = Environment(dir, new FakeHandler(_ => Response(HttpStatusCode.OK, responseBody)));

        var exit = await OperationRunner.RunAsync(env, "campaign.get", RequestBody.Empty(), new RunOptions(Human: true, RenderHuman: _ => null), TestContext.Current.CancellationToken);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(responseBody + System.Environment.NewLine, output.ToString());
    }

    [Fact]
    public async Task Human_mode_without_a_renderer_prints_the_raw_body()
    {
        using var dir = new TempPaths();
        dir.WriteDescriptor(Descriptor);
        const string responseBody = "{\"name\":\"LatAm\"}";
        var (env, output, _) = Environment(dir, new FakeHandler(_ => Response(HttpStatusCode.OK, responseBody)));

        var exit = await OperationRunner.RunAsync(env, "campaign.get", RequestBody.Empty(), new RunOptions(Human: true), TestContext.Current.CancellationToken);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(responseBody + System.Environment.NewLine, output.ToString());
    }

    [Fact]
    public async Task Send_hands_the_response_back_without_printing_it()
    {
        using var dir = new TempPaths();
        dir.WriteDescriptor(Descriptor);
        const string responseBody = "{\"instance_id\":\"rt_LIVE\",\"pid\":77,\"stopping\":true}";
        var (env, output, _) = Environment(dir, new FakeHandler(_ => Response(HttpStatusCode.OK, responseBody)));

        var (exit, response) = await OperationRunner.SendAsync(env, "system.shutdown", RequestBody.Empty(), TestContext.Current.CancellationToken);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.NotNull(response);
        Assert.True(response!.IsSuccess);
        Assert.Equal(responseBody, response.Body);
        Assert.Equal(string.Empty, output.ToString());
    }

    [Fact]
    public async Task Send_reports_an_unreachable_runtime_itself()
    {
        using var dir = new TempPaths();
        var (env, output, _) = Environment(dir, new FakeHandler(_ => throw new InvalidOperationException("must not be called")));

        var (exit, response) = await OperationRunner.SendAsync(env, "system.shutdown", RequestBody.Empty(), TestContext.Current.CancellationToken);

        Assert.Equal(ExitCodes.RuntimeUnavailable, exit);
        Assert.Null(response);
        Assert.Contains("\"code\":\"no_descriptor\"", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Send_hands_back_a_business_error_for_the_caller_to_interpret()
    {
        using var dir = new TempPaths();
        dir.WriteDescriptor(Descriptor);
        const string envelope = "{\"error\":{\"code\":\"not_found\",\"message\":\"nope\",\"retryable\":false}}";
        var (env, output, _) = Environment(dir, new FakeHandler(_ => Response(HttpStatusCode.NotFound, envelope)));

        var (exit, response) = await OperationRunner.SendAsync(env, "campaign.get", RequestBody.Empty(), TestContext.Current.CancellationToken);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.NotNull(response);
        Assert.False(response!.IsSuccess);
        Assert.Equal(envelope, response.Body);
        Assert.Equal(string.Empty, output.ToString());
    }

    private static (CliEnvironment Env, StringWriter Out, StringWriter Error) Environment(TempPaths dir, HttpMessageHandler handler)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        return (new CliEnvironment(output, error, dir.Paths, handler), output, error);
    }

    private static HttpResponseMessage Response(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };
}
