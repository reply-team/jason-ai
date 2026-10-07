using System.Net;

namespace Jason.PluginHost.Tests.Fixtures;

/// <summary>
/// The loopback server picks a free port by binding and releasing one, so two servers in tests running side by side can
/// be handed the same port, and the second cannot listen on it. That cost a macOS run an HTTP test: the server's second
/// try reused a listener the failed start had already closed. A server whose address is taken has to come up at another
/// — on another port, or on the same port by the other name for loopback — and answer.
/// </summary>
public sealed class LoopbackHttpServerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_server_whose_port_another_already_listens_on_comes_up_at_an_address_of_its_own()
    {
        using var first = new LoopbackHttpServer();
        var offered = new Queue<int>([first.Port]);

        using var second = new LoopbackHttpServer(() => offered.Count > 0 ? offered.Dequeue() : LoopbackHttpServer.FreePort());

        Assert.NotEqual(first.BaseUrl, second.BaseUrl);
        using var client = new HttpClient();
        using var answer = await client.GetAsync(new Uri(second.Url("/ok")), Ct);
        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
    }
}
