using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Contracts.Json;
using Jason.Runtime.Configuration;
using Jason.Runtime.Plugins.Invocation;
using Jason.Runtime.Plugins.Subscriptions;
using Jason.Runtime.Tests.Integration;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests.Plugins.Subscriptions;

/// <summary>
/// The three subscription operations over the API, against a runtime that loaded a real subscriber and ran its chain
/// once: where the kind stands, the record of the chain, a person's rewind and the refusals a rewind can meet. Each
/// exchange is recorded under <c>docs/api/fixtures/</c>.
/// </summary>
[Collection(ProcessEnvironmentCollection.Name)]
public class SubscriptionEndpointTests
{
    private const string Created = "campaign_created";

    private const string Forwarder = "acme-forwarder";

    private static readonly DateTimeOffset Nine = new(2026, 10, 16, 9, 0, 0, TimeSpan.Zero);

    private static readonly object Marta = new { type = "human", id = "Marta" };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>The runner is composed with no dispatch loop as well, so the list reads it there, and nothing rests.</summary>
    [Fact]
    public async Task With_no_dispatch_loop_a_kind_is_listed_and_does_not_rest()
    {
        var clock = new FixedClock(Nine);
        await using var api = await StartAsync(clock);
        await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { name = "Spring outreach" }, Ct);
        await api.Resolve<SubscriptionRunner>().RunNextAsync(Created, Ct);

        var listed = await api.PostOkAsync<Page<SubscriptionKindDto>>(Operations.SubscriptionList, new { }, Ct);

        Assert.False(api.Resolve<LiveSettings<DispatcherOptions>>().Current.Enabled);
        Assert.Null(Assert.Single(listed.Items).RestUntil);
    }

    [Fact]
    public async Task A_kind_is_listed_its_chains_are_read_and_a_person_rewinds_it_over_the_api()
    {
        var clock = new FixedClock(Nine);
        await using var api = await StartAsync(clock);

        await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { name = "Spring outreach" }, Ct);
        var turn = await api.Resolve<SubscriptionRunner>().RunNextAsync(Created, Ct);
        Assert.Equal(new SubscriptionTurn(Created, 1, 0, SubscriptionTurn.Head), turn);
        clock.Advance(TimeSpan.FromMinutes(5));
        await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { name = "Autumn outreach" }, Ct);

        var listed = await api.RecordAsync<Page<SubscriptionKindDto>>(Operations.SubscriptionList, "one-kind", new { }, Ct);
        var kind = Assert.Single(listed.Items);
        Assert.Equal((Created, 1), (kind.Kind, kind.Lag));
        Assert.Equal([Forwarder], kind.Chain);
        Assert.Empty(kind.WaitingOn);
        Assert.Null(kind.InFlight);

        var runs = await api.RecordAsync<Page<SubscriptionRunDto>>(Operations.SubscriptionRuns, "succeeded", new { kind = Created }, Ct);
        var run = Assert.Single(runs.Items);
        Assert.Equal(SubscriptionRunStatus.Succeeded, run.Status);
        Assert.Equal("{\"forwarded\":true}", run.Envelope!.ToJsonString());
        Assert.Null(run.Failure);

        var line = run.Line;
        var rewound = await api.RecordAsync<SubscriptionKindDto>(
            Operations.SubscriptionRewind,
            "to-a-line",
            new { kind = Created, line, actor = Marta, reason = "the queue lost a day" },
            Ct);
        Assert.Equal(2, rewound.Lag);

        var role = await api.RecordErrorAsync(
            Operations.SubscriptionRewind,
            "not-human",
            new { kind = Created, line, actor = new { type = "role", id = "manager" } },
            HttpStatusCode.BadRequest,
            Ct);
        Assert.Equal("subscription_rewind_not_human", role.Code);

        var moment = await api.RecordErrorAsync(
            Operations.SubscriptionRewind,
            "since-without-offset",
            new { kind = Created, since = "2026-10-16T09:00:00", actor = Marta },
            HttpStatusCode.BadRequest,
            Ct);
        Assert.Equal("validation_failed", moment.Code);
        Assert.Equal(("since", "invalid"), (Assert.Single(moment.Details!).Field, moment.Details![0].Code));

        var both = await api.PostErrorAsync(Operations.SubscriptionRewind, new { kind = Created, line, since = "2026-10-16T09:00:00Z", actor = Marta }, HttpStatusCode.BadRequest, Ct);
        Assert.Equal(("validation_failed", "since", "exactly_one"), (both.Code, Assert.Single(both.Details!).Field, both.Details![0].Code));

        var neither = await api.PostErrorAsync(Operations.SubscriptionRewind, new { kind = Created, actor = Marta }, HttpStatusCode.BadRequest, Ct);
        Assert.Equal(("validation_failed", "line", "exactly_one"), (neither.Code, Assert.Single(neither.Details!).Field, neither.Details![0].Code));

        clock.Advance(TimeSpan.FromMinutes(5));
        var forward = await api.PostErrorAsync(Operations.SubscriptionRewind, new { kind = Created, since = clock.GetUtcNow().ToString("O"), actor = Marta }, HttpStatusCode.Conflict, Ct);
        Assert.Equal("subscription_rewind_forward", forward.Code);

        var unsubscribed = await api.PostErrorAsync(Operations.SubscriptionRewind, new { kind = "campaign_archived", line, actor = Marta }, HttpStatusCode.NotFound, Ct);
        Assert.Equal("subscription_kind_unsubscribed", unsubscribed.Code);
    }

    private static Task<RuntimeApiFixture> StartAsync(FixedClock clock) =>
        RuntimeApiFixture.StartAsync(
            Ct,
            prepare: Install,
            clock: new SteadyClock(clock),
            configureServices: services =>
            {
                services.AddSingleton<IPluginHostLocator>(new JasonDllLocator());
                services.AddSingleton(TestPlugins.SearchPath);
            });

    private static void Install(JasonPaths paths)
    {
        File.WriteAllText(paths.UserSettingsFile, RuntimeApiFixture.DispatcherOff);
        TestPlugins.Write(
            paths,
            Forwarder,
            $"manifest_version: 1\nid: {Forwarder}\nversion: 1.0.0\nkind: subscriber\ncontracts:\n  protocol: [1]\nsubscriptions: [{Created}]\n",
            "export function invoke(operation, input) { return { result: { envelope: { forwarded: true } } }; }");
        TestPlugins.Grant(paths, Forwarder);

        var settings = JsonNode.Parse(File.ReadAllText(paths.UserSettingsFile))!.AsObject();
        settings["Plugins"]!["Grants"]![Forwarder]!["Subscriptions"] = new JsonArray(Created);
        File.WriteAllText(paths.UserSettingsFile, settings.ToJsonString(JasonJson.Options), new UTF8Encoding(false));
    }

    /// <summary>
    /// The fixed clock, with its timestamps read off the same moment, so a handler's measured duration is the time the
    /// clock moved while it ran — none — and a recorded exchange is the same on every machine.
    /// </summary>
    private sealed class SteadyClock(FixedClock inner) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => inner.GetUtcNow();

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => inner.GetUtcNow().UtcTicks;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
            inner.CreateTimer(callback, state, dueTime, period);
    }
}
