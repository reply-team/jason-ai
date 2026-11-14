using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Plugins;
using Jason.Runtime.Contacts;
using Jason.Runtime.Domain;
using Jason.Runtime.Persistence;
using Jason.Runtime.Tests.Contacts;
using Jason.Runtime.Tests.Plugins;
using Jason.Runtime.Tests.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests.Senders;

/// <summary>
/// The provider's accounts a send can go from, read for a campaign when a person or an agent asks and answered in the
/// request: routed by <c>sender.list</c>'s own route for the campaign, by the channel asked for, held to the route's half
/// of the claim, and recorded nowhere — no work item, no attempt, no journal line and no pin, and no capability minted for
/// the plugin.
/// </summary>
public sealed class SenderReadTests
{
    private const string Mailer = "stand-in-mailer";

    private const string Social = "stand-in-social";

    private const string Deaf = "stand-in-deaf";

    private const string Main = """
        export function invoke(operation, input) {
          throw new Error("a test answers every read: " + operation);
        }
        """;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// A draft campaign with no routes of its own is read now, by the global route, and the plugin's page comes back as it
    /// gave it, under the plugin that answered. The input is composed as a work item's would be: the arguments with their
    /// defaults, and the campaign with the routed plugin's own pins only. Nothing is kept of the read, and the plugin is
    /// invoked with no attempt, no work item and no door, so no capability is minted for it and the run token reaches it
    /// nowhere.
    /// </summary>
    [Fact]
    public async Task A_draft_campaign_s_accounts_are_read_by_the_global_route_and_nothing_is_recorded()
    {
        var reads = new FakeProviderReads();
        await using var api = await StartAsync(reads);
        var campaign = await CampaignAsync(api);
        await PinAsync(api, campaign, Mailer, "c_mailer");
        await PinAsync(api, campaign, Social, "c_social");
        reads.Answer(FakeProviderReads.Result(Page()));
        var before = await CountsAsync(api);

        var answer = await api.PostOkAsync<SenderListResponse>(
            Operations.SenderList, new { campaign_id = campaign, owners = "own", sender_channel = "email" }, Ct);

        Assert.Equal(Mailer, answer.PluginId);
        Assert.Equal(Page()["items"]!.ToJsonString(), answer.Items.ToJsonString());
        Assert.True(answer.HasMore);
        Assert.Equal("""{"email":"listed","linkedin":"not_asked"}""", answer.Channels.ToJsonString());
        Assert.Equal("""{"ordering":"email_then_linkedin"}""", answer.Vendor.ToJsonString());

        var request = Assert.Single(reads.Requests);
        Assert.Equal((Mailer, "sender.list"), (request.PluginId, request.Operation));
        Assert.Equal("""{"owners":"own","sender_channel":"email","limit":20,"offset":0}""", request.Input["args"]!.ToJsonString());
        Assert.Equal((campaign, "draft"), ((string?)request.Input["campaign"]!["id"], (string?)request.Input["campaign"]!["status"]));
        Assert.Equal("""{"campaign":"c_mailer"}""", request.Input["campaign"]!["external_ids"]!.ToJsonString());
        Assert.Equal(
            (null, null, null, null),
            (request.AttemptId, request.WorkItemId, request.CampaignId, request.Door));
        Assert.DoesNotContain(api.Runtime.Token, JsonSerializer.Serialize(request.Input) + request.Binding?.ToJsonString(), StringComparison.Ordinal);
        Assert.Equal(before, await CountsAsync(api));
    }

    /// <summary>
    /// The read goes by <c>sender.list</c>'s own route, channel by channel, as its work item would at the claim: a campaign's
    /// channel route for it carries that channel, the other channel falls to the global route, and a read that names no
    /// channel is routed by the operation alone. A channel route of <c>message.send</c> carries only the sends: where it is
    /// the only route of its channel, the accounts of that channel are read from the plugin the read's own route names.
    /// </summary>
    [Fact]
    public async Task Each_channel_is_read_by_the_read_s_own_route_and_never_by_the_sends()
    {
        var reads = new FakeProviderReads();
        await using var api = await StartAsync(reads);
        var campaign = await CampaignAsync(api);
        await RouteAsync(api, campaign, "sender.list", "linkedin", Social);
        var split = await CampaignAsync(api);
        await RouteAsync(api, split, "message.send", "linkedin", Social);

        var asked = new List<string>();
        foreach (var (id, channel) in new (string, string?)[] { (campaign, "email"), (campaign, "linkedin"), (campaign, null), (split, "linkedin") })
        {
            reads.Answer(FakeProviderReads.Result(Page()));
            var answer = await api.PostOkAsync<SenderListResponse>(
                Operations.SenderList, new { campaign_id = id, owners = "team", sender_channel = channel, limit = 100, offset = 0 }, Ct);
            asked.Add(answer.PluginId);
        }

        Assert.Equal([Mailer, Social, Mailer, Mailer], asked);
        Assert.Equal([Mailer, Social, Mailer, Mailer], reads.Requests.Select(request => request.PluginId));
        Assert.Null(reads.Requests[2].Input["args"]!["sender_channel"]);
        var sends = await api.PostOkAsync<RouteResolutionDto>(
            Operations.RouteResolve, new { campaign_id = split, operation = "message.send", channel = "linkedin" }, Ct);
        Assert.Equal(Social, sends.PluginId);
    }

    /// <summary>
    /// An archived campaign is not refused: it is read by the routes in force, as its other reads on request are. Its own
    /// routes stay in force until the next activation, which leaves them out, and from then on its accounts are read by
    /// the global route.
    /// </summary>
    [Fact]
    public async Task An_archived_campaign_is_read_by_the_routes_in_force_and_then_by_the_global_route()
    {
        var reads = new FakeProviderReads();
        await using var api = await StartAsync(reads);
        var campaign = await CampaignAsync(api);
        await RouteAsync(api, campaign, operation: null, channel: null, Social);
        await api.PostOkAsync<CampaignDto>(Operations.CampaignArchive, new { campaign_id = campaign }, Ct);
        reads.Answer(FakeProviderReads.Result(Page()), FakeProviderReads.Result(Page()));

        var archived = await api.PostOkAsync<SenderListResponse>(Operations.SenderList, new { campaign_id = campaign, owners = "own" }, Ct);
        await api.PostOkAsync<PluginRegistryDto>(Operations.PluginReload, new { }, Ct);
        var reloaded = await api.PostOkAsync<SenderListResponse>(Operations.SenderList, new { campaign_id = campaign, owners = "own" }, Ct);

        Assert.Equal((Social, Mailer), (archived.PluginId, reloaded.PluginId));
        Assert.All(reads.Requests, request => Assert.Equal("archived", (string?)request.Input["campaign"]!["status"]));
    }

    /// <summary>A read asked the wrong way, as written, and the faults the runtime names for it.</summary>
    public static TheoryData<string, string[]> RefusedReads => new()
    {
        { "{}", ["owners:required"] },
        { """{"owners":"everyone"}""", ["owners:invalid"] },
        { """{"owners":"own","sender_channel":"sms"}""", ["sender_channel:invalid"] },
        { """{"owners":"own","limit":0}""", ["limit:invalid"] },
        { """{"owners":"own","limit":101}""", ["limit:invalid"] },
        { """{"owners":"own","offset":-1}""", ["offset:invalid"] },
        { """{"owners":"own","offset":1000001}""", ["offset:invalid"] },
    };

    /// <summary>
    /// A read asked the wrong way is refused by the field at fault before any route is resolved or any plugin is asked:
    /// whose accounts is required and is <c>own</c> or <c>team</c>, the channel is <c>email</c> or <c>linkedin</c>, and the
    /// page is from 1 to 100 accounts from an offset the contract bounds. Nothing is kept of the refusal.
    /// </summary>
    [Theory]
    [MemberData(nameof(RefusedReads))]
    public async Task A_read_asked_the_wrong_way_is_refused_by_the_field_at_fault_and_leaves_nothing(string asked, string[] faults)
    {
        var reads = new FakeProviderReads();
        await using var api = await StartAsync(reads);
        var body = JsonNode.Parse(asked)!.AsObject();
        body["campaign_id"] = await CampaignAsync(api);
        var before = await CountsAsync(api);

        var error = await api.PostErrorAsync(Operations.SenderList, body, HttpStatusCode.BadRequest, Ct);

        Assert.Equal("validation_failed", error.Code);
        Assert.Equal(faults, error.Details!.Select(detail => $"{detail.Field}:{detail.Code}"));
        Assert.Empty(reads.Requests);
        Assert.Equal(before, await CountsAsync(api));
    }

    /// <summary>
    /// The read is held to the route's half of the claim: no campaign named, a campaign nobody holds, nothing routing the
    /// read, and a route to a plugin that does not perform it are each refused before a plugin is asked, under the claim's
    /// own codes, and none of them leaves a trace.
    /// </summary>
    [Theory]
    [InlineData("no campaign", HttpStatusCode.BadRequest, "validation_failed")]
    [InlineData("unknown campaign", HttpStatusCode.NotFound, "campaign_not_found")]
    [InlineData("no route", HttpStatusCode.NotFound, "no_route")]
    [InlineData("a plugin that does not perform it", HttpStatusCode.Conflict, "plugin_operation_unsupported")]
    public async Task A_read_no_route_can_answer_is_refused_and_leaves_nothing(string what, HttpStatusCode status, string code)
    {
        var reads = new FakeProviderReads();
        await using var api = await StartAsync(reads, routed: what != "no route");
        var campaign = await CampaignAsync(api);
        if (what == "a plugin that does not perform it")
        {
            await RouteAsync(api, campaign, operation: null, channel: null, Deaf);
        }

        var before = await CountsAsync(api);
        object body = what switch
        {
            "no campaign" => new { owners = "own" },
            "unknown campaign" => new { campaign_id = "cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD", owners = "own" },
            _ => new { campaign_id = campaign, owners = "own", sender_channel = "linkedin" },
        };

        var error = await api.PostErrorAsync(Operations.SenderList, body, status, Ct);

        Assert.Equal(code, error.Code);
        Assert.Empty(reads.Requests);
        Assert.Equal(before, await CountsAsync(api));
    }

    /// <summary>
    /// A provider that could not answer is the request's answer, with what it said: a code the operation declares under its
    /// own class, retryable only when transient, and an undeclared code or an answer outside the result schema as
    /// <c>result_invalid</c>. Nothing is retried, and nothing is kept.
    /// </summary>
    [Theory]
    [InlineData("rate limited", "rate_limited", true)]
    [InlineData("channel not in the plan", "feature_unavailable", false)]
    [InlineData("a code the contract does not declare", "result_invalid", false)]
    [InlineData("an answer outside the schema", "result_invalid", false)]
    public async Task A_failed_read_answers_with_what_the_plugin_said_and_leaves_nothing(string what, string code, bool retryable)
    {
        var reads = new FakeProviderReads();
        await using var api = await StartAsync(reads);
        var campaign = await CampaignAsync(api);
        reads.Answer(what switch
        {
            "rate limited" => FakeProviderReads.Failure("rate_limited", FailureClass.Transient),
            "channel not in the plan" => FakeProviderReads.Failure("feature_unavailable", FailureClass.Permanent),
            "a code the contract does not declare" => FakeProviderReads.Failure("made_up", FailureClass.Permanent),
            _ => FakeProviderReads.Result(new JsonObject { ["items"] = new JsonArray() }),
        });
        var before = await CountsAsync(api);

        var error = await api.PostErrorAsync(
            Operations.SenderList, new { campaign_id = campaign, owners = "own", sender_channel = "linkedin" }, HttpStatusCode.BadGateway, Ct);

        Assert.Equal((code, retryable), (error.Code, error.Retryable));
        Assert.Single(reads.Requests);
        Assert.Equal(before, await CountsAsync(api));
    }

    /// <summary>The same operation stays work too: a planner may still submit it as a provider operation of a campaign.</summary>
    [Fact]
    public async Task The_read_is_still_accepted_as_work()
    {
        await using var api = await StartAsync(new FakeProviderReads());
        var campaign = await CampaignAsync(api);

        var item = await api.PostOkAsync<WorkItemDto>(
            Operations.WorkItemCreate,
            new
            {
                campaign_id = campaign,
                kind = "provider_op",
                operation = "sender.list",
                context = new { input = new { owners = "own", sender_channel = "email", limit = 50, offset = 0 } },
            },
            Ct);

        Assert.Equal("sender.list", item.Operation);
    }

    private static async Task<RuntimeApiFixture> StartAsync(FakeProviderReads reads, bool routed = true) =>
        await RuntimeApiFixture.StartAsync(
            Ct,
            prepare: paths =>
            {
                File.WriteAllText(paths.UserSettingsFile, RuntimeApiFixture.DispatcherOff);
                TestPlugins.Write(paths, Mailer, Sender(Mailer), Main);
                TestPlugins.Write(paths, Social, Sender(Social), Main);
                TestPlugins.Write(paths, Deaf, TestPlugins.Manifest(Deaf, "[message.send]"), Main);
                if (routed)
                {
                    TestRoutes.WriteGlobal(paths, TestRoutes.GlobalDefault(Mailer));
                }
            },
            configureServices: services => services.AddSingleton<IProviderReadInvoker>(reads));

    /// <summary>A package that lists its accounts, sends and reads its accounts' events: what a channel route may name.</summary>
    private static string Sender(string id) =>
        $"""
        manifest_version: 1
        id: {id}
        version: 1.0.0
        kind: provider
        contracts:
          protocol: [1]
          operations: [1]
          events: [1]
        operations: [sender.list, message.send]

        """;

    private static async Task<string> CampaignAsync(RuntimeApiFixture api) =>
        (await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { name = "Seed-stage owners" }, Ct)).Id;

    private static async Task RouteAsync(RuntimeApiFixture api, string campaign, string? operation, string? channel, string plugin)
    {
        var (status, body) = await api.PostAsync(Operations.RouteSet, new { campaign_id = campaign, operation, channel, plugin }, Ct);
        Assert.True(status == HttpStatusCode.OK, $"route.set answered {(int)status}: {body}");
    }

    /// <summary>A pin of the campaign at <paramref name="plugin"/>, as an attempt there would have left it.</summary>
    private static async Task PinAsync(RuntimeApiFixture api, string campaign, string plugin, string value)
    {
        using var scope = api.Resolve<IServiceScopeFactory>().CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JasonDbContext>();
        var held = await db.Campaigns.SingleAsync(c => c.PublicId == campaign, Ct);
        db.ExternalIds.Add(new ExternalId
        {
            Campaign = held,
            PluginId = plugin,
            Kind = "campaign",
            Value = value,
            RecordedAt = new DateTime(2026, 11, 13, 9, 0, 0, DateTimeKind.Utc),
            RecordedByAttemptId = "att_01JB6K8TQ2W9V4MZ0C3Y7H5NRD",
        });
        await db.SaveChangesAsync(Ct);
    }

    private static async Task<(int Journal, int WorkItems, int Attempts, int Pins, int Approvals)> CountsAsync(RuntimeApiFixture api)
    {
        using var scope = api.Resolve<IServiceScopeFactory>().CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JasonDbContext>();
        return (
            await db.Journal.CountAsync(Ct),
            await db.WorkItems.CountAsync(Ct),
            await db.Attempts.CountAsync(Ct),
            await db.ExternalIds.CountAsync(Ct),
            await db.Approvals.CountAsync(Ct));
    }

    /// <summary>One page of a provider's accounts, the first of more.</summary>
    private static JsonObject Page() => JsonNode.Parse(
        """
        {
          "items": [
            { "external_id": "e_1", "channel": "email", "name": "Sales desk", "address": "sales@vendor.example", "status": "active", "owner": "own", "vendor": { "status": "connected" } },
            { "external_id": "e_2", "channel": "email", "name": null, "address": "old@vendor.example", "status": "disconnected", "owner": "own", "vendor": { "status": null } }
          ],
          "has_more": true,
          "channels": { "email": "listed", "linkedin": "not_asked" },
          "vendor": { "ordering": "email_then_linkedin" }
        }
        """)!.AsObject();
}
