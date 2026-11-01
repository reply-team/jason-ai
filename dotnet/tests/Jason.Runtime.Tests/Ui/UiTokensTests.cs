using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Jason.Contracts.Api;
using Jason.Runtime.Execution;
using Jason.Runtime.Hosting;
using Jason.Runtime.Ui;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests.Ui;

/// <summary>
/// A UI token is a second bearer the runtime accepts: minted once, stored as a hash, good until revoked, and
/// listed so a person can see which browsers hold one.
/// </summary>
public class UiTokensTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_minted_token_is_accepted_as_a_bearer_and_listed_with_who_paired()
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 10, 16, 9, 0, 0, TimeSpan.Zero));
        await using var api = await RuntimeApiFixture.StartAsync(Ct, clock: clock);
        var (token, issued) = await api.Resolve<UiTokenStore>().IssueAsync("ada", Ct);

        Assert.StartsWith("uit_", issued.Id, StringComparison.Ordinal);
        Assert.Equal("ada", issued.IssuedTo);
        Assert.Equal(clock.Now, issued.IssuedAt);
        Assert.Null(issued.LastUsedAt);

        var (status, _) = await PostAsBearerAsync(api, token, Operations.SystemInfo);
        Assert.Equal(HttpStatusCode.OK, status);

        var listed = await api.PostOkAsync<UiTokensResponse>(Operations.UiTokens, null, Ct);
        var one = Assert.Single(listed.Items);
        Assert.Equal(issued.Id, one.Id);
        Assert.Equal(clock.Now, one.LastUsedAt);
    }

    [Fact]
    public async Task Last_use_is_written_coarsely_once_a_minute()
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 10, 16, 9, 0, 0, TimeSpan.Zero));
        await using var api = await RuntimeApiFixture.StartAsync(Ct, clock: clock);
        var (token, _) = await api.Resolve<UiTokenStore>().IssueAsync("ada", Ct);

        await PostAsBearerAsync(api, token, Operations.SystemInfo);
        clock.Advance(TimeSpan.FromSeconds(30));
        await PostAsBearerAsync(api, token, Operations.SystemInfo);
        var afterThirty = Assert.Single((await api.PostOkAsync<UiTokensResponse>(Operations.UiTokens, null, Ct)).Items);
        Assert.Equal(clock.Now - TimeSpan.FromSeconds(30), afterThirty.LastUsedAt);

        clock.Advance(TimeSpan.FromSeconds(31));
        await PostAsBearerAsync(api, token, Operations.SystemInfo);
        var afterMinute = Assert.Single((await api.PostOkAsync<UiTokensResponse>(Operations.UiTokens, null, Ct)).Items);
        Assert.Equal(clock.Now, afterMinute.LastUsedAt);
    }

    [Fact]
    [Critical(Family.S5, "revocation")]
    public async Task A_revoked_token_is_refused_from_the_next_request_on()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var (token, issued) = await api.Resolve<UiTokenStore>().IssueAsync("ada", Ct);

        var revoked = await api.PostOkAsync<UiRevokeResponse>(Operations.UiRevoke, new { token_id = issued.Id }, Ct);
        Assert.Equal(1, revoked.Revoked);

        var (status, body) = await PostAsBearerAsync(api, token, Operations.SystemInfo);
        Assert.Equal(HttpStatusCode.Unauthorized, status);
        Assert.Contains("\"unauthorized\"", body, StringComparison.Ordinal);
        Assert.Empty((await api.PostOkAsync<UiTokensResponse>(Operations.UiTokens, null, Ct)).Items);
    }

    [Fact]
    public async Task Revoking_all_ends_every_token_and_an_unknown_id_is_a_404()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var store = api.Resolve<UiTokenStore>();
        await store.IssueAsync("ada", Ct);
        await store.IssueAsync("ada", Ct);

        var all = await api.PostOkAsync<UiRevokeResponse>(Operations.UiRevoke, new { all = true }, Ct);
        Assert.Equal(2, all.Revoked);

        var error = await api.PostErrorAsync(Operations.UiRevoke, new { token_id = "uit_nobody" }, HttpStatusCode.NotFound, Ct);
        Assert.Equal("ui_token_not_found", error.Code);

        var neither = await api.PostErrorAsync(Operations.UiRevoke, new { }, HttpStatusCode.BadRequest, Ct);
        Assert.Equal("validation_failed", neither.Code);
    }

    [Fact]
    public async Task A_token_outlives_the_runtime_that_minted_it()
    {
        using var dir = new TempDataDir();
        Directory.CreateDirectory(dir.Paths.ConfigDirectory);
        File.WriteAllText(dir.Paths.UserSettingsFile, RuntimeApiFixture.DispatcherOff);
        string token;
        await using (var first = await RuntimeHost.StartAsync(dir.Paths, TestRuntimeOptions.Quiet, Ct))
        {
            (token, _) = await first.Services.GetRequiredService<UiTokenStore>().IssueAsync("ada", Ct);
        }

        await using var second = await RuntimeHost.StartAsync(dir.Paths, TestRuntimeOptions.Quiet, Ct);
        using var http = new HttpClient { BaseAddress = second.BaseUrl };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var content = new StringContent("{}", Encoding.UTF8, "application/json");
        using var response = await http.PostAsync(Operations.Route(Operations.SystemInfo), content, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    [Critical(Family.S3, "UI tokens are redacted")]
    public async Task The_redactor_masks_a_token_minted_in_this_process()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var (token, _) = await api.Resolve<UiTokenStore>().IssueAsync("ada", Ct);

        Assert.Equal($"before {TokenRedactor.Mask} after", api.Resolve<TokenRedactor>().Redact($"before {token} after"));
    }

    internal static async Task<(HttpStatusCode Status, string Body)> PostAsBearerAsync(RuntimeApiFixture api, string bearer, string operation, string? origin = null)
    {
        using var http = new HttpClient { BaseAddress = api.Runtime.BaseUrl };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        if (origin is not null)
        {
            http.DefaultRequestHeaders.Add("Origin", origin);
        }

        using var content = new StringContent("{}", Encoding.UTF8, "application/json");
        using var response = await http.PostAsync(Operations.Route(operation), content, Ct);
        return (response.StatusCode, await response.Content.ReadAsStringAsync(Ct));
    }
}
