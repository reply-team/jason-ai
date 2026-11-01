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
        var port = Configurable(RandomStart);
        using var dir = new TempDataDir();
        await using var runtime = await RuntimeHost.StartAsync(dir.Paths, TestRuntimeOptions.Quiet with { Port = port }, Ct);

        Assert.Equal(port, runtime.BaseUrl.Port);
        var info = await SystemInfoAsync(runtime);
        Assert.Equal(new ListeningInfo(port, port), info.Listening);
    }

    [Fact]
    public async Task A_configured_port_that_is_taken_moves_to_the_next_free_one_and_says_which()
    {
        var wanted = Configurable(RandomStart);
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

    /// <summary>
    /// A start whose whole span cannot be bound is drawn again rather than configured. Windows reserves ranges of a hundred
    /// ports and more for Hyper-V and WSL, which refuse a bind, and a start inside one finds nothing: the probe answers 0,
    /// and 0 configures a port the OS chooses, which is a row of its own and not the two above. Here the span is held by
    /// listeners of the row's own, which the probe tells apart from a reserved range no more than a bind does.
    /// </summary>
    [Fact]
    public void A_start_whose_whole_span_cannot_be_bound_is_drawn_again()
    {
        using var held = new HeldSpan();
        var found = PortProbe.FirstFree(held.Start);
        Assert.True(found == 0, $"the span from {held.Start} is held, and the probe found {found} free in it");
        var draws = new Queue<int>([held.Start]);

        var port = Configurable(() => draws.TryDequeue(out var start) ? start : RandomStart());

        Assert.True(
            port != 0 && (port < held.Start || port >= held.Start + PortProbe.Span),
            $"configured {port}, from a first start of {held.Start} whose whole span is held");
    }

    /// <summary>A start in the upper ports, as the two rows that configure one draw it.</summary>
    private static int RandomStart() => 49152 + Random.Shared.Next(10000);

    /// <summary>
    /// A port for a row to configure: one the probe found free from a drawn start, with another free one above it within
    /// the span, so a row that takes it can also move up from it. A start whose span has no such pair is drawn again, a
    /// bounded number of times, and the starts that found none are named if every draw does.
    /// </summary>
    private static int Configurable(Func<int> draw)
    {
        var refused = new List<int>();
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var start = draw();
            var port = PortProbe.FirstFree(start);
            if (port != 0 && PortProbe.FirstFree(port + 1) != 0)
            {
                return port;
            }

            refused.Add(start);
        }

        Assert.Fail($"No start found a free port with another free one above it; the starts were {string.Join(", ", refused)}.");
        return 0;
    }

    /// <summary>
    /// A whole span of loopback ports held by listeners of a test's own. It is drawn below 32768, where no operating system
    /// hands out a port of its own choosing, so the span is not one other programs are busy in.
    /// </summary>
    private sealed class HeldSpan : IDisposable
    {
        private readonly List<TcpListener> _listeners = [];

        public HeldSpan()
        {
            var tried = new List<int>();
            for (var attempt = 0; attempt < 20; attempt++)
            {
                Start = 20000 + Random.Shared.Next(12000);
                if (HoldAll())
                {
                    return;
                }

                Release();
                tried.Add(Start);
            }

            Assert.Fail($"No span could be held whole; the starts were {string.Join(", ", tried)}.");
        }

        public int Start { get; private set; }

        public void Dispose() => Release();

        private bool HoldAll()
        {
            for (var port = Start; port < Start + PortProbe.Span; port++)
            {
                var listener = new TcpListener(IPAddress.Loopback, port);
                try
                {
                    listener.Start();
                }
                catch (SocketException)
                {
                    listener.Dispose();
                    return false;
                }

                _listeners.Add(listener);
            }

            return true;
        }

        private void Release()
        {
            foreach (var listener in _listeners)
            {
                listener.Dispose();
            }

            _listeners.Clear();
        }
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
