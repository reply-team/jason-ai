using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Jason.Contracts.Api;
using Jason.Contracts.Json;
using Jason.Runtime.Plugins.Door;
using Jason.Runtime.Ui;

namespace Jason.Runtime.Tests.Plugins.Door;

/// <summary>
/// The API's half of a handler's door: a capability opens the operations it was minted for and nothing else, a request
/// through it speaks as that plugin about that journal line and as nobody else, and a dead capability is no bearer.
/// </summary>
public class DoorApiTests
{
    private const string Plugin = "acme-forwarder";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    [Critical(Family.R2, "the API refuses a handler outside its capability")]
    public async Task A_capability_opens_the_operations_it_was_minted_for_and_no_other()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var ticket = Mint(api, Operations.CampaignList, Operations.JournalAppend);

        var (granted, _) = await PostThroughDoorAsync(api, ticket, Operations.CampaignList, DoorBody("jrn_A"));
        Assert.Equal(HttpStatusCode.OK, granted);

        foreach (var operation in new[] { Operations.ContactList, Operations.SystemInfo, Operations.PluginReload, Operations.ApprovalApprove })
        {
            var (status, body) = await PostThroughDoorAsync(api, ticket, operation, DoorBody("jrn_A"));
            Assert.Equal(HttpStatusCode.Forbidden, status);
            Assert.Contains("\"operation_not_granted\"", body, StringComparison.Ordinal);
        }

        foreach (var path in new[] { "/v1/campaign.list/extra", "/v1/CAMPAIGN.LIST", "/v0/campaign.list", "/" })
        {
            var (status, body) = await PostPathAsync(api, ticket.Capability, path, DoorBody("jrn_A"));
            Assert.Equal(HttpStatusCode.Forbidden, status);
            Assert.Contains("\"operation_not_granted\"", body, StringComparison.Ordinal);
        }
    }

    [Fact]
    [Critical(Family.R2, "a handler's capability dies with its invocation")]
    public async Task A_revoked_or_expired_capability_is_no_bearer()
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 1, 5, 9, 0, 0, TimeSpan.Zero));
        await using var api = await RuntimeApiFixture.StartAsync(Ct, clock: clock);
        var capabilities = api.Resolve<HandlerCapabilities>();

        var revoked = capabilities.Mint(Plugin, Set(Operations.CampaignList), "jrn_A", clock.Now.AddMinutes(5));
        Assert.Equal(HttpStatusCode.OK, (await PostThroughDoorAsync(api, revoked, Operations.CampaignList, DoorBody("jrn_A"))).Status);
        capabilities.Revoke(revoked);
        var (afterRevoke, revokeBody) = await PostThroughDoorAsync(api, revoked, Operations.CampaignList, DoorBody("jrn_A"));
        Assert.Equal(HttpStatusCode.Unauthorized, afterRevoke);
        Assert.Contains("\"unauthorized\"", revokeBody, StringComparison.Ordinal);

        var expiring = capabilities.Mint(Plugin, Set(Operations.CampaignList), "jrn_A", clock.Now.AddMinutes(5));
        clock.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(HttpStatusCode.Unauthorized, (await PostThroughDoorAsync(api, expiring, Operations.CampaignList, DoorBody("jrn_A"))).Status);
    }

    [Fact]
    [Critical(Family.R2, "a handler speaks only as its own plugin about its own line")]
    public async Task Through_a_door_the_actor_is_the_plugin_and_the_reason_is_the_line()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var ticket = Mint(api, Operations.CampaignList);

        string[] wrong =
        [
            "{}",
            """{"reason":"journal jrn_A"}""",
            """{"actor":{"type":"plugin","id":"acme-forwarder"}}""",
            """{"actor":{"type":"plugin","id":"other-plugin"},"reason":"journal jrn_A"}""",
            """{"actor":{"type":"human","id":"ada"},"reason":"journal jrn_A"}""",
            """{"actor":{"type":"plugin","id":"acme-forwarder"},"reason":"journal jrn_B"}""",
            """{"actor":{"type":"plugin","id":"acme-forwarder","extra":1},"reason":"journal jrn_A"}""",
            """{"actor":"plugin","reason":"journal jrn_A"}""",
            "[]",
        ];

        foreach (var body in wrong)
        {
            var (status, text) = await PostThroughDoorAsync(api, ticket, Operations.CampaignList, body);
            Assert.True(status == HttpStatusCode.BadRequest, body + " -> " + text);
            Assert.Contains("\"validation_failed\"", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task A_write_through_a_door_is_recorded_as_the_plugins_own()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var campaign = await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { Name = "LatAm" }, Ct);
        var ticket = Mint(api, Operations.JournalAppend);

        var body = JsonSerializer.Serialize(
            new
            {
                CampaignId = campaign.Id,
                Kind = "forwarded",
                Key = "note",
                New = "seen",
                Actor = new { Type = "plugin", Id = Plugin },
                Reason = "journal jrn_A",
            },
            JasonJson.Options);
        var (status, text) = await PostThroughDoorAsync(api, ticket, Operations.JournalAppend, body);

        Assert.True(status == HttpStatusCode.OK, text);
        var entry = JsonSerializer.Deserialize<JournalEntryDto>(text, JasonJson.Options)!;
        Assert.Equal(new ActorRef(ActorType.Plugin, Plugin), entry.Actor);
        Assert.Equal("journal jrn_A", entry.Reason);
    }

    [Fact]
    [Critical(Family.R2, "a plugin actor is claimed only through its own door")]
    public async Task The_run_token_and_a_ui_token_cannot_claim_a_plugin()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var campaign = await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { Name = "LatAm" }, Ct);
        var (uiToken, _) = await api.Resolve<UiTokenStore>().IssueAsync("ada", Ct);
        var body = JsonSerializer.Serialize(
            new
            {
                CampaignId = campaign.Id,
                Kind = "forwarded",
                Key = "note",
                New = "seen",
                Actor = new { Type = "plugin", Id = Plugin },
                Reason = "journal jrn_A",
            },
            JasonJson.Options);

        foreach (var bearer in new[] { api.Runtime.Token, uiToken })
        {
            var (status, text) = await PostPathAsync(api, bearer, Operations.Route(Operations.JournalAppend), body);
            Assert.Equal(HttpStatusCode.BadRequest, status);
            Assert.Contains("\"not_allowed\"", text, StringComparison.Ordinal);
        }
    }

    private static DoorTicket Mint(RuntimeApiFixture api, params string[] operations) =>
        api.Resolve<HandlerCapabilities>().Mint(Plugin, Set(operations), "jrn_A", DateTimeOffset.UtcNow.AddHours(1));

    private static HashSet<string> Set(params string[] operations) => new(operations, StringComparer.Ordinal);

    private static string DoorBody(string lineId) =>
        $$"""{"actor":{"type":"plugin","id":"{{Plugin}}"},"reason":"journal {{lineId}}"}""";

    private static Task<(HttpStatusCode Status, string Body)> PostThroughDoorAsync(RuntimeApiFixture api, DoorTicket ticket, string operation, string body) =>
        PostPathAsync(api, ticket.Capability, Operations.Route(operation), body);

    private static async Task<(HttpStatusCode Status, string Body)> PostPathAsync(RuntimeApiFixture api, string bearer, string path, string body = "{}")
    {
        using var http = new HttpClient { BaseAddress = api.Runtime.BaseUrl };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = await http.PostAsync(path, content, Ct);
        return (response.StatusCode, await response.Content.ReadAsStringAsync(Ct));
    }
}
