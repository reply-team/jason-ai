using System.Net;
using Jason.Contracts.Api;
using Jason.Runtime.Api;
using Jason.Runtime.Tests.Ui;

namespace Jason.Runtime.Tests.Api;

/// <summary>
/// A request that says where it came from must have come from here. CLI and agent calls say nothing and are
/// untouched; a browser page from anywhere but the runtime's own origin is refused before the bearer is looked at.
/// </summary>
public class OriginRuleTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("http://127.0.0.1:{port}", true)]
    [InlineData("http://localhost:{port}", true)]
    [InlineData("http://LOCALHOST:{port}", true)]
    [InlineData("http://127.0.0.1:1", false)]
    [InlineData("https://127.0.0.1:{port}", false)]
    [InlineData("http://evil.example", false)]
    [InlineData("http://127.0.0.1:{port}/path", false)]
    [InlineData("null", false)]
    [InlineData("", false)]
    public void Only_the_runtimes_own_origin_is_its_own(string origin, bool own) =>
        Assert.Equal(own, OriginRule.IsOwn(origin.Replace("{port}", "52766", StringComparison.Ordinal), 52766));

    [Fact]
    [Critical(Family.S5, "a browser on another origin cannot drive the API")]
    public async Task An_operation_with_a_foreign_origin_is_refused_before_authentication_and_without_one_is_untouched()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        var (none, _) = await UiTokensTests.PostAsBearerAsync(api, api.Runtime.Token, Operations.SystemInfo);
        Assert.Equal(HttpStatusCode.OK, none);

        var (own, _) = await UiTokensTests.PostAsBearerAsync(api, api.Runtime.Token, Operations.SystemInfo, $"http://127.0.0.1:{api.Runtime.BaseUrl.Port}");
        Assert.Equal(HttpStatusCode.OK, own);

        var (foreign, body) = await UiTokensTests.PostAsBearerAsync(api, api.Runtime.Token, Operations.SystemInfo, "http://evil.example");
        Assert.Equal(HttpStatusCode.Forbidden, foreign);
        Assert.Contains("\"origin_refused\"", body, StringComparison.Ordinal);

        var (foreignNoBearer, noBearerBody) = await UiTokensTests.PostAsBearerAsync(api, "wrong", Operations.SystemInfo, "http://evil.example");
        Assert.Equal(HttpStatusCode.Forbidden, foreignNoBearer);
        Assert.DoesNotContain("\"unauthorized\"", noBearerBody, StringComparison.Ordinal);
    }
}
