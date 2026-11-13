using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Ids;
using Jason.Contracts.Plugins;
using Jason.Runtime.Contacts;
using Jason.Runtime.Domain;
using Jason.Runtime.Persistence;
using Jason.Runtime.Plugins.Registry;
using Jason.Runtime.Routing;
using Jason.Runtime.Tests.Plugins;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Contacts;

/// <summary>
/// A person's threads and a thread's messages are read from the provider when somebody asks, in the request: in the
/// account the person's provider identifier was learned in, with no work item, no attempt, no journal line and no pin.
/// </summary>
public class ContactThreadServiceTests
{
    private const string Plugin = "fake-provider";

    private const string Pin = "48211";

    private static readonly DateTime Noon = new(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Threads_are_read_in_the_account_the_pin_was_learned_in_and_nothing_is_recorded()
    {
        using var world = await World.CreateAsync();
        world.Reads.Answer(FakeProviderReads.Result(Threads()));
        var before = await world.CountsAsync();

        var answer = await world.Service.ThreadsAsync(new ContactThreadsRequest(world.Ada.PublicId, null, null, null), Ct);

        var request = Assert.Single(world.Reads.Requests);
        Assert.Equal((Plugin, "conversation.list"), (request.PluginId, request.Operation));
        Assert.Equal("""{"workspace":"a"}""", request.Binding!.ToJsonString());
        Assert.Equal("""{"limit":20,"offset":0,"source":"all"}""", request.Input["args"]!.ToJsonString());
        Assert.Equal(Pin, (string?)request.Input["contacts"]![0]!["external_ids"]!["contact"]);
        Assert.Null(request.Input["campaign"]);
        Assert.Equal(Plugin, answer.PluginId);
        Assert.Equal(Threads()["items"]!.ToJsonString(), answer.Items.ToJsonString());
        Assert.False(answer.HasMore);
        Assert.Equal(before, await world.CountsAsync());
    }

    [Theory]
    [InlineData("re-routed")]
    [InlineData("global holds it")]
    [InlineData("paused campaign")]
    [InlineData("no pin")]
    public async Task The_account_is_found_by_the_pin_s_binding(string world_)
    {
        using var world = await World.CreateAsync();
        var person = world.Ada.PublicId;
        switch (world_)
        {
            case "re-routed":
                world.Route(campaign: Binding("b"), global: null);
                break;
            case "global holds it":
                world.Route(campaign: Binding("b"), global: Binding("a"));
                break;
            case "paused campaign":
                world.Autumn.Status = CampaignStatus.Paused;
                await world.Db.SaveChangesAsync(Ct);
                break;
            default:
                person = world.Grace.PublicId;
                break;
        }

        world.Reads.Answer(FakeProviderReads.Result(Threads()));
        var reading = world.Service.ThreadsAsync(new ContactThreadsRequest(person, null, null, null), Ct);

        if (world_ is "re-routed" or "no pin")
        {
            var refused = await Assert.ThrowsAsync<NotFoundException>(() => reading);
            Assert.Equal(world_ == "re-routed" ? "no_route" : "contact_not_linked", refused.Code);
            Assert.Empty(world.Reads.Requests);
        }
        else
        {
            await reading;
            Assert.Equal("""{"workspace":"a"}""", Assert.Single(world.Reads.Requests).Binding!.ToJsonString());
        }
    }

    /// <summary>
    /// Every route is walked, channel routes included: where the campaign reads its threads in another account, a route
    /// that sends on a channel in the account the person's identifier was learned in is the one the read goes by, for a
    /// person's threads and for one thread alike. Where no route names that account, the read is refused as before.
    /// </summary>
    [Theory]
    [InlineData("conversation.list", true)]
    [InlineData("conversation.get", true)]
    [InlineData("conversation.list", false)]
    public async Task A_channel_route_that_names_the_pin_s_account_carries_the_read(string operation, bool namesTheAccount)
    {
        using var world = await World.CreateAsync();
        var bySend = new Route(Plugin, Binding(namesTheAccount ? "a" : "c"), BindingIdentity.Of(Binding(namesTheAccount ? "a" : "c")));
        world.Route(new RouteSet(
            new Route(Plugin, Binding("b"), BindingIdentity.Of(Binding("b"))),
            RouteSet.Empty.Operations,
            new Dictionary<string, IReadOnlyDictionary<string, Route>>(StringComparer.Ordinal)
            {
                ["message.send"] = new Dictionary<string, Route>(StringComparer.Ordinal) { ["linkedin"] = bySend },
            }));
        world.Reads.Answer(FakeProviderReads.Result(operation == "conversation.list" ? Threads() : Thread(owner: Pin)));

        Task reading = operation == "conversation.list"
            ? world.Service.ThreadsAsync(new ContactThreadsRequest(world.Ada.PublicId, null, null, null), Ct)
            : world.Service.ThreadAsync(new ContactThreadRequest(world.Ada.PublicId, null, "t_1", null, null), Ct);

        if (namesTheAccount)
        {
            await reading;
            var request = Assert.Single(world.Reads.Requests);
            Assert.Equal((operation, """{"workspace":"a"}"""), (request.Operation, request.Binding!.ToJsonString()));
        }
        else
        {
            Assert.Equal("no_route", (await Assert.ThrowsAsync<NotFoundException>(() => reading)).Code);
            Assert.Empty(world.Reads.Requests);
        }
    }

    [Fact]
    [Critical(Family.S1, "a thread read is the person's own")]
    public async Task A_thread_of_somebody_else_is_refused()
    {
        using var world = await World.CreateAsync();
        world.Reads.Answer(FakeProviderReads.Result(Thread(owner: "999")), FakeProviderReads.Result(Thread(owner: Pin)));

        var refused = await Assert.ThrowsAsync<NotFoundException>(
            () => world.Service.ThreadAsync(new ContactThreadRequest(world.Ada.PublicId, null, "t_1", null, null), Ct));
        var answer = await world.Service.ThreadAsync(new ContactThreadRequest(world.Ada.PublicId, null, "t_1", null, null), Ct);

        Assert.Equal("thread_not_this_contact", refused.Code);
        var tooLong = await Assert.ThrowsAsync<ValidationException>(
            () => world.Service.ThreadAsync(new ContactThreadRequest(world.Ada.PublicId, null, new string('t', 257), null, null), Ct));
        var problem = Assert.Single(tooLong.Details!);
        Assert.Equal(("thread_id", "too_long"), (problem.Field, problem.Code));
        Assert.Equal(2, world.Reads.Requests.Count);
        var request = world.Reads.Requests[1];
        Assert.Equal("conversation.get", request.Operation);
        Assert.Equal("""{"thread":{"external_id":"t_1"},"limit":20,"offset":0}""", request.Input["args"]!.ToJsonString());
        Assert.Null(request.Input["campaign"]);
        Assert.Null(request.Input["contacts"]);
        Assert.Equal(2, answer.Messages.Count);
        Assert.Equal("<p>Thanks, could we talk on Thursday?</p>", (string?)answer.Messages[1]!["body"]);
    }

    [Theory]
    [InlineData("rate limited", "rate_limited", true)]
    [InlineData("feature unavailable", "feature_unavailable", false)]
    [InlineData("a provider block", "provider_blocked", false)]
    [InlineData("a block outside its contract", "provider_blocked", false)]
    [InlineData("a code the contract does not declare", "result_invalid", false)]
    [InlineData("an answer outside the schema", "result_invalid", false)]
    [InlineData("the protocol", "provider_read_failed", false)]
    public async Task A_failed_read_answers_with_what_the_plugin_said(string what, string code, bool retryable)
    {
        using var world = await World.CreateAsync();
        world.Reads.Answer(what switch
        {
            "rate limited" => FakeProviderReads.Failure("rate_limited", FailureClass.Transient),
            "feature unavailable" => FakeProviderReads.Failure("feature_unavailable", FailureClass.Permanent),
            "a provider block" => FakeProviderReads.Failure(
                "provider_blocked",
                FailureClass.Blocked,
                new JsonObject { ["scope"] = "account", ["reason"] = "credential" }),
            "a block outside its contract" => FakeProviderReads.Failure(
                "provider_blocked",
                FailureClass.Blocked,
                new JsonObject { ["scope"] = "account", ["reason"] = "insufficient_credits" }),
            "a code the contract does not declare" => FakeProviderReads.Failure("made_up", FailureClass.Permanent),
            "an answer outside the schema" => FakeProviderReads.Result(new JsonObject { ["items"] = new JsonArray() }),
            _ => FakeProviderReads.Protocol("plugin_timeout"),
        });

        var failed = await Assert.ThrowsAnyAsync<DomainException>(
            () => world.Service.ThreadsAsync(new ContactThreadsRequest(world.Ada.PublicId, null, null, null), Ct));

        Assert.Equal((502, code, retryable), (failed.StatusCode, failed.Code, failed.Retryable));
    }

    /// <summary>
    /// A read on request is held to its operation's declared codes by the same check a work item's failure is, so the
    /// host's own endings — a plugin that threw, a timeout after a call left the process — stand as the host's codes
    /// rather than reading as a code the operation never declared; under a class the host never gives them they do not.
    /// </summary>
    [Theory]
    [InlineData(HostOutcomeCodes.PluginException, FailureClass.Permanent, HostOutcomeCodes.PluginException)]
    [InlineData(HostOutcomeCodes.PluginTimeout, FailureClass.Ambiguous, HostOutcomeCodes.PluginTimeout)]
    [InlineData(HostOutcomeCodes.PluginException, FailureClass.Transient, "result_invalid")]
    public async Task A_host_ending_on_a_thread_read_stands_as_the_host_s_code(string reported, FailureClass failureClass, string code)
    {
        using var world = await World.CreateAsync();
        world.Reads.Answer(FakeProviderReads.Failure(reported, failureClass));

        var failed = await Assert.ThrowsAnyAsync<DomainException>(
            () => world.Service.ThreadAsync(new ContactThreadRequest(world.Ada.PublicId, null, "t_1", null, null), Ct));

        Assert.Equal("conversation.get", Assert.Single(world.Reads.Requests).Operation);
        Assert.Equal((502, code, false), (failed.StatusCode, failed.Code, failed.Retryable));
    }

    private static JsonObject Binding(string workspace) => new() { ["workspace"] = workspace };

    private static JsonObject Threads() => JsonNode.Parse(
        """
        {
          "items": [
            {
              "external_id": "t_1", "channel": "email", "subject": "Re: Soluna's next quarter",
              "preview": "Thanks, could we talk on Thursday?", "last_activity_at": "2026-10-20T10:41:00.000Z", "unread": true,
              "contact": { "external_id": "48211", "name": "Mateo Ruiz" }, "campaign_external_id": "c_7714",
              "has_meeting_intent": true, "vendor": { "is_last_message_planned": false, "state": "needs_attention", "category": null }
            }
          ],
          "has_more": false,
          "vendor": { "ordering": "last_activity_desc" }
        }
        """)!.AsObject();

    private static JsonObject Thread(string owner) => JsonNode.Parse(
        $$"""
        {
          "thread": {
            "external_id": "t_1", "channel": "email", "subject": "Re: Soluna's next quarter",
            "contact": { "external_id": "{{owner}}", "name": "Mateo Ruiz" }, "campaign_external_id": "c_7714", "unread": true,
            "vendor": { "category": null, "has_ai_draft": false }
          },
          "messages": [
            {
              "external_id": "<m1@mail.example.com>", "direction": "outbound", "sent_at": "2026-10-20T08:15:00.000Z",
              "from": "sdr@vendor.example", "to": ["mateo@soluna.example"], "cc": null, "bcc": null, "subject": "Soluna's next quarter",
              "body": "<p>Hi Mateo, worth 20 minutes next week?</p>", "truncated": false,
              "vendor": { "state": "ok", "status_code": null, "ai_sent_type": null }
            },
            {
              "external_id": "<m2@mail.example.com>", "direction": "inbound", "sent_at": "2026-10-20T10:41:00.000Z",
              "from": "mateo@soluna.example", "to": ["sdr@vendor.example"], "cc": null, "bcc": null, "subject": "Re: Soluna's next quarter",
              "body": "<p>Thanks, could we talk on Thursday?</p>", "truncated": false,
              "vendor": { "state": "needs_attention", "status_code": null, "ai_sent_type": null }
            }
          ],
          "has_more": false,
          "vendor": { "ordering": "oldest_first" }
        }
        """)!.AsObject();

    /// <summary>
    /// Ada, pinned at the provider by an attempt of Autumn's that ran in account "a"; Grace, never pinned. Autumn routes
    /// to the provider in account "a", and nothing else is routed until a case says so.
    /// </summary>
    private sealed class World : IDisposable
    {
        private readonly TestDatabase _database = new();
        private readonly PluginRegistry _plugins;
        private readonly RouteRegistry _routes;

        private World()
        {
            Db = _database.Open();
            var clock = new FixedClock(new DateTimeOffset(Noon));
            _plugins = new PluginRegistry(clock);
            _routes = new RouteRegistry(clock, _plugins);
            var schema = JsonNode.Parse("""{ "type": "object", "properties": { "workspace": { "type": "string" } } }""")!.AsObject();
            _plugins.Replace(
                new PluginSnapshot(PublicId.New(PluginProtocol.SnapshotIdPrefix), Noon, SnapshotSource.Startup, [TestPlugins.Loaded(Plugin, ["conversation.list", "conversation.get"], binding: schema)]),
                new ReloadReport(Noon, SnapshotSource.Startup, Activated: true, [], []));
        }

        public JasonDbContext Db { get; }

        public FakeProviderReads Reads { get; } = new();

        public Campaign Autumn { get; private set; } = null!;

        public Contact Ada { get; private set; } = null!;

        public Contact Grace { get; private set; } = null!;

        public ContactThreadService Service => new(Db, _plugins, _routes, Reads);

        public static async Task<World> CreateAsync()
        {
            var world = new World();
            var db = world.Db;
            world.Autumn = WorkItemFactory.NewCampaign(now: Noon);
            world.Ada = new Contact { PublicId = PublicId.New("cnt"), FirstName = "Ada", CreatedAt = Noon, UpdatedAt = Noon };
            world.Grace = new Contact { PublicId = PublicId.New("cnt"), FirstName = "Grace", CreatedAt = Noon, UpdatedAt = Noon };
            var enrol = WorkItemFactory.NewProviderOp(world.Autumn, "campaign.enroll", Noon);
            enrol.Contact = world.Ada;
            var attempt = WorkItemFactory.NewAttempt(enrol, 1, AttemptStatus.Succeeded, Noon);
            attempt.Provenance = new AttemptProvenanceDto(
                Plugin, "1.0.0", "sha256:0", PluginProtocol.CurrentVersion, PluginProtocol.OperationContractVersion,
                "campaign.enroll", 1, PluginProtocol.SnapshotIdPrefix + "_01K5B7Q2WE5X3M9T0YH4C6RDNC", "rts_01K5B7Q2WE5X3M9T0YH4C6RDND",
                RouteScope.CampaignDefault, BindingIdentity.Of(Binding("a")), CorrelationId: attempt.PublicId);
            db.Campaigns.Add(world.Autumn);
            db.Contacts.AddRange(world.Ada, world.Grace);
            db.WorkItems.Add(enrol);
            db.Attempts.Add(attempt);
            db.ExternalIds.Add(new ExternalId { Contact = world.Ada, PluginId = Plugin, Kind = "contact", Value = Pin, RecordedAt = Noon, RecordedByAttemptId = attempt.PublicId });
            await db.SaveChangesAsync(Ct);
            world.Route(campaign: Binding("a"), global: null);
            return world;
        }

        public void Route(JsonObject? campaign, JsonObject? global)
        {
            var plugins = _plugins.Snapshot;
            Route? To(JsonObject? binding) => binding is null ? null : new Route(Plugin, binding, BindingIdentity.Of(binding));
            _routes.Replace(new RouteSnapshot(
                PublicId.New(RouteSnapshot.IdPrefix),
                new DateTimeOffset(Noon),
                plugins.Id,
                new RouteSet(To(global), RouteSet.Empty.Operations),
                new Dictionary<string, RouteSet>(StringComparer.Ordinal) { [Autumn.PublicId] = new RouteSet(To(campaign), RouteSet.Empty.Operations) }));
        }

        /// <summary>Autumn's routes are <paramref name="campaign"/>, and nothing is routed globally.</summary>
        public void Route(RouteSet campaign) =>
            _routes.Replace(new RouteSnapshot(
                PublicId.New(RouteSnapshot.IdPrefix),
                new DateTimeOffset(Noon),
                _plugins.Snapshot.Id,
                RouteSet.Empty,
                new Dictionary<string, RouteSet>(StringComparer.Ordinal) { [Autumn.PublicId] = campaign }));

        public async Task<(int Journal, int WorkItems, int Attempts, int Pins)> CountsAsync() => (
            await Db.Journal.CountAsync(Ct),
            await Db.WorkItems.CountAsync(Ct),
            await Db.Attempts.CountAsync(Ct),
            await Db.ExternalIds.CountAsync(Ct));

        public void Dispose()
        {
            Db.Dispose();
            _database.Dispose();
        }
    }
}
