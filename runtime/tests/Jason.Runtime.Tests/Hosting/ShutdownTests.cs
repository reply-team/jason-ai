using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Jason.Contracts.Api;
using Jason.Contracts.Json;
using Jason.Runtime.Hosting;

namespace Jason.Runtime.Tests.Hosting;

public class ShutdownTests
{
    private static readonly RuntimeHostOptions Quiet = new(ShippedSettingsDirectory: null, ConsoleLogging: false);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Shutdown_names_the_instance_it_is_stopping()
    {
        using var dir = new TempDataDir();
        await using var runtime = await RuntimeHost.StartAsync(dir.Paths, Quiet, Ct);
        using var http = Client(runtime);

        using var response = await http.PostAsync(Operations.Route(Operations.SystemShutdown), Json("{}"), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(Ct);
        var acknowledgement = JsonSerializer.Deserialize<ShutdownResponse>(body, JasonJson.Options)!;
        Assert.Equal(runtime.Descriptor.InstanceId, acknowledgement.InstanceId);
        Assert.Equal(runtime.Descriptor.Pid, acknowledgement.Pid);
        Assert.True(acknowledgement.Stopping);
        Assert.Contains("\"instance_id\":", body, StringComparison.Ordinal);
        Assert.Contains("\"stopping\":true", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Shutdown_accepts_an_empty_body()
    {
        using var dir = new TempDataDir();
        await using var runtime = await RuntimeHost.StartAsync(dir.Paths, Quiet, Ct);
        using var http = Client(runtime);

        using var response = await http.PostAsync(Operations.Route(Operations.SystemShutdown), content: null, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Shutdown_takes_the_same_path_as_a_console_interrupt()
    {
        using var dir = new TempDataDir();
        var runtime = await RuntimeHost.StartAsync(dir.Paths, Quiet, Ct);
        var baseUrl = runtime.BaseUrl;
        using (var http = Client(runtime))
        {
            using var response = await http.PostAsync(Operations.Route(Operations.SystemShutdown), Json("{}"), Ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        // The acknowledgement is written before the host stops, so waiting is what proves the stop happened.
        await runtime.WaitForShutdownAsync(Ct).WaitAsync(TimeSpan.FromSeconds(10), Ct);
        await runtime.StopAsync();

        Assert.False(File.Exists(dir.Paths.DescriptorFile));
        using var probe = new HttpClient { BaseAddress = baseUrl, Timeout = TimeSpan.FromSeconds(5) };
        await Assert.ThrowsAsync<HttpRequestException>(() => probe.PostAsync(Operations.Route(Operations.SystemInfo), Json("{}"), Ct));

        var logs = string.Concat(Directory.GetFiles(dir.Paths.LogsDirectory).Select(File.ReadAllText));
        Assert.Contains("Shutdown requested through the API", logs, StringComparison.Ordinal);

        // The lock went with the host: a fresh runtime takes the same data directory without a fight.
        await using var again = await RuntimeHost.StartAsync(dir.Paths, Quiet, Ct);
        Assert.NotEqual(runtime.Descriptor.InstanceId, again.Descriptor.InstanceId);
    }

    private static HttpClient Client(RunningRuntime runtime)
    {
        var http = new HttpClient { BaseAddress = runtime.BaseUrl };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", runtime.Token);
        return http;
    }

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");
}
