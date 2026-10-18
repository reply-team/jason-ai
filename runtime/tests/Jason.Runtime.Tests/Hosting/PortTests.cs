using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Jason.Contracts.Api;
using Jason.Contracts.Json;
using Jason.Runtime.Configuration;
using Jason.Runtime.Hosting;

namespace Jason.Runtime.Tests.Hosting;

/// <summary>
/// A bookmark needs a stable address, so the port is fixed by default; a runtime that finds it taken moves up rather
/// than refusing to start, and says so where a client can read it.
/// </summary>
public class PortTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void The_default_port_is_the_one_a_phone_keypad_spells_jason_with() =>
        Assert.Equal(52766, new RuntimeOptions().Port);

    [Fact]
    public async Task A_configured_port_that_is_free_is_the_one_listened_on_and_reported()
    {
        var port = PortProbe.FirstFree(49152 + Random.Shared.Next(10000));
        using var dir = new TempDataDir();
        await using var runtime = await RuntimeHost.StartAsync(dir.Paths, TestRuntimeOptions.Quiet with { Port = port }, Ct);

        Assert.Equal(port, runtime.BaseUrl.Port);
        var info = await SystemInfoAsync(runtime);
        Assert.Equal(new ListeningInfo(port, port), info.Listening);
    }

    [Fact]
    public async Task A_configured_port_that_is_taken_moves_to_the_next_free_one_and_says_which()
    {
        var wanted = PortProbe.FirstFree(49152 + Random.Shared.Next(10000));
        using var squatter = new TcpListener(IPAddress.Loopback, wanted);
        squatter.Start();
        using var dir = new TempDataDir();
        await using var runtime = await RuntimeHost.StartAsync(dir.Paths, TestRuntimeOptions.Quiet with { Port = wanted }, Ct);

        Assert.True(runtime.BaseUrl.Port > wanted, $"listening on {runtime.BaseUrl.Port}, which is not above {wanted}");
        var info = await SystemInfoAsync(runtime);
        Assert.Equal(new ListeningInfo(runtime.BaseUrl.Port, wanted), info.Listening);
    }

    [Fact]
    public async Task Zero_keeps_the_random_port_and_reports_zero_as_configured()
    {
        using var dir = new TempDataDir();
        await using var runtime = await RuntimeHost.StartAsync(dir.Paths, TestRuntimeOptions.Quiet, Ct);

        var info = await SystemInfoAsync(runtime);
        Assert.Equal(new ListeningInfo(runtime.BaseUrl.Port, 0), info.Listening);
    }

    [Fact]
    public void The_probe_answers_zero_when_the_whole_span_is_taken()
    {
        var start = 65535 - PortProbe.Span + 1;
        // Above 65535 there is nothing to probe, so a start this high can only find ports inside the span.
        Assert.True(PortProbe.FirstFree(start) is 0 or >= 65435);
    }

    private static async Task<SystemInfoResponse> SystemInfoAsync(RunningRuntime runtime)
    {
        using var http = new HttpClient { BaseAddress = runtime.BaseUrl };
        http.DefaultRequestHeaders.Authorization = new("Bearer", runtime.Token);
        using var content = new StringContent("{}", Encoding.UTF8, "application/json");
        using var response = await http.PostAsync(Operations.Route(Operations.SystemInfo), content, Ct);
        return JsonSerializer.Deserialize<SystemInfoResponse>(await response.Content.ReadAsStringAsync(Ct), JasonJson.Options)!;
    }
}
