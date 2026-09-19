using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Jason.Cli;
using Jason.Contracts.Api;
using Jason.Contracts.Json;
using Jason.Runtime.Discovery;
using Jason.Runtime.Hosting;

namespace Jason.Runtime.Tests.Hosting;

public class RuntimeHostTests
{

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Starting_publishes_a_descriptor_that_matches_the_listening_api()
    {
        using var dir = new TempDataDir();
        await using var runtime = await RuntimeHost.StartAsync(dir.Paths, TestRuntimeOptions.Quiet, Ct);

        Assert.True(File.Exists(dir.Paths.DescriptorFile));
        Assert.StartsWith("http://127.0.0.1:", runtime.Descriptor.BaseUrl, StringComparison.Ordinal);
        Assert.True(FilePermissions.IsRestrictedToCurrentUser(dir.Paths.DescriptorFile));

        using var http = Client(runtime);
        using var response = await http.PostAsync(Operations.Route(Operations.SystemInfo), Json("{}"), Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(Ct);
        var info = JsonSerializer.Deserialize<SystemInfoResponse>(body, JasonJson.Options)!;
        Assert.Equal(runtime.Descriptor.InstanceId, info.InstanceId);
        Assert.Equal("v1", info.ApiVersion);
        Assert.Equal(Environment.ProcessId, info.Pid);
        Assert.Equal(dir.Paths.Root, info.DataDir);
        Assert.Contains(info.Database.AppliedMigrations, m => m.EndsWith("_InitialCreate", StringComparison.Ordinal));
        Assert.Contains("\"instance_id\":", body, StringComparison.Ordinal);
        Assert.Contains("\"applied_migrations\":", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task System_info_accepts_an_empty_body()
    {
        using var dir = new TempDataDir();
        await using var runtime = await RuntimeHost.StartAsync(dir.Paths, TestRuntimeOptions.Quiet, Ct);
        using var http = Client(runtime);

        using var response = await http.PostAsync(Operations.Route(Operations.SystemInfo), content: null, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Missing_or_wrong_token_is_401_with_the_error_envelope()
    {
        using var dir = new TempDataDir();
        await using var runtime = await RuntimeHost.StartAsync(dir.Paths, TestRuntimeOptions.Quiet, Ct);
        using var http = new HttpClient { BaseAddress = runtime.BaseUrl };

        using var missing = await http.PostAsync(Operations.Route(Operations.SystemInfo), Json("{}"), Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);
        var envelope = JsonSerializer.Deserialize<ErrorResponse>(await missing.Content.ReadAsStringAsync(Ct), JasonJson.Options)!;
        Assert.Equal("unauthorized", envelope.Error.Code);
        Assert.False(envelope.Error.Retryable);

        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", runtime.Token + "x");
        using var wrong = await http.PostAsync(Operations.Route(Operations.SystemInfo), Json("{}"), Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
    }

    [Fact]
    public async Task Foreign_host_header_is_rejected_before_authentication()
    {
        using var dir = new TempDataDir();
        await using var runtime = await RuntimeHost.StartAsync(dir.Paths, TestRuntimeOptions.Quiet, Ct);
        using var http = Client(runtime);
        using var request = new HttpRequestMessage(HttpMethod.Post, Operations.Route(Operations.SystemInfo)) { Content = Json("{}") };
        request.Headers.Host = "evil.example";

        using var response = await http.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var envelope = JsonSerializer.Deserialize<ErrorResponse>(await response.Content.ReadAsStringAsync(Ct), JasonJson.Options)!;
        Assert.Equal("invalid_request", envelope.Error.Code);
    }

    [Fact]
    public async Task Unknown_operations_and_wrong_methods_are_404_with_the_error_envelope()
    {
        using var dir = new TempDataDir();
        await using var runtime = await RuntimeHost.StartAsync(dir.Paths, TestRuntimeOptions.Quiet, Ct);
        using var http = Client(runtime);

        using var unknown = await http.PostAsync("/v1/nothing.here", Json("{}"), Ct);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal("not_found", JsonSerializer.Deserialize<ErrorResponse>(await unknown.Content.ReadAsStringAsync(Ct), JasonJson.Options)!.Error.Code);

        using var get = await http.GetAsync(Operations.Route(Operations.SystemInfo), Ct);
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
    }

    [Fact]
    public async Task A_second_runtime_on_the_same_data_directory_refuses_to_start()
    {
        using var dir = new TempDataDir();
        await using var first = await RuntimeHost.StartAsync(dir.Paths, TestRuntimeOptions.Quiet, Ct);

        await Assert.ThrowsAsync<RuntimeAlreadyRunningException>(() => RuntimeHost.StartAsync(dir.Paths, TestRuntimeOptions.Quiet, Ct));

        Assert.True(File.Exists(dir.Paths.DescriptorFile)); // the first runtime's descriptor is untouched
    }

    [Fact]
    public async Task Stopping_removes_the_descriptor_and_closes_the_port()
    {
        using var dir = new TempDataDir();
        var runtime = await RuntimeHost.StartAsync(dir.Paths, TestRuntimeOptions.Quiet, Ct);
        var baseUrl = runtime.BaseUrl;

        await runtime.StopAsync();

        Assert.False(File.Exists(dir.Paths.DescriptorFile));
        using var http = new HttpClient { BaseAddress = baseUrl, Timeout = TimeSpan.FromSeconds(5) };
        await Assert.ThrowsAsync<HttpRequestException>(() => http.PostAsync(Operations.Route(Operations.SystemInfo), Json("{}"), Ct));

        await using var again = await RuntimeHost.StartAsync(dir.Paths, TestRuntimeOptions.Quiet, Ct); // lock released, restart is an ordinary start
        Assert.NotEqual(runtime.Descriptor.InstanceId, again.Descriptor.InstanceId);
    }

    [Fact]
    public async Task The_token_never_reaches_the_logs()
    {
        using var dir = new TempDataDir();
        string token;
        await using (var runtime = await RuntimeHost.StartAsync(dir.Paths, TestRuntimeOptions.Quiet, Ct))
        {
            token = runtime.Token;
            using var http = Client(runtime);
            (await http.PostAsync(Operations.Route(Operations.SystemInfo), Json("{}"), Ct)).Dispose();
            using var bad = new HttpClient { BaseAddress = runtime.BaseUrl };
            bad.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "wrong-" + token);
            (await bad.PostAsync(Operations.Route(Operations.SystemInfo), Json("{}"), Ct)).Dispose();
        }

        var logs = string.Concat(Directory.GetFiles(dir.Paths.LogsDirectory).Select(File.ReadAllText));
        Assert.NotEmpty(logs);
        Assert.DoesNotContain(token, logs, StringComparison.Ordinal);
        Assert.Contains("system.info", logs, StringComparison.Ordinal); // requests are logged by operation
    }

    [Fact]
    public async Task The_real_cli_reads_the_descriptor_and_reports_status()
    {
        using var dir = new TempDataDir();
        await using var runtime = await RuntimeHost.StartAsync(dir.Paths, TestRuntimeOptions.Quiet, Ct);
        var output = new StringWriter();
        var error = new StringWriter();

        var exit = await CliApp.RunAsync(["runtime", "status"], new CliEnvironment(output, error, dir.Paths), Ct);

        Assert.Equal(ExitCodes.Success, exit);
        var info = JsonSerializer.Deserialize<SystemInfoResponse>(output.ToString(), JasonJson.Options)!;
        Assert.Equal(runtime.Descriptor.InstanceId, info.InstanceId);
        Assert.Equal(string.Empty, error.ToString());
    }

    private static HttpClient Client(RunningRuntime runtime)
    {
        var http = new HttpClient { BaseAddress = runtime.BaseUrl };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", runtime.Token);
        return http;
    }

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");
}
