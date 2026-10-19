using System.Net;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Ids;
using Jason.Contracts.Plugins;
using Jason.Runtime.Domain;
using Jason.Runtime.Events;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Plugins.Registry;
using Jason.Runtime.Routing;
using Jason.Runtime.Tests.Plugins;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests.Events;

/// <summary>
/// The record of the accounts whose events are read, and a person's request to read a span of one again, through the
/// Runtime API on a held clock: what a source says of itself and never its cursor, whether it is healthy by the inbound
/// read's own question, and a span asked for within its bounds, each refused by its own name.
/// </summary>
public class EventsServiceTests
{
    private const string Reader = "reader";

    private static readonly DateTimeOffset Noon = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ---------------------------------------------------------------------------------------------------------------
    // events.backfill
    // ---------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_backfill_waits_and_is_said_once_in_the_chronicle()
    {
        await using var api = await StartAsync();
        var source = await SourceAsync(api, "a");

        var asked = await api.PostOkAsync<EventBackfillDto>(
            Operations.EventsBackfill,
            new { source = source.PublicId, from = Noon.AddDays(-60), to = Noon.AddDays(-30), actor = new { type = "human", id = "marta" }, reason = "the weeks nobody read" },
            Ct);

        Assert.StartsWith("evb_", asked.Id, StringComparison.Ordinal);
        Assert.Equal(
            (source.PublicId, Noon.AddDays(-60), Noon.AddDays(-30), EventBackfillStatus.Pending, Noon, new ActorRef(ActorType.Human, "marta"), "the weeks nobody read", 0, 0, 0),
            (asked.Source, asked.From, asked.To, asked.Status, asked.RequestedAt, asked.Actor, asked.Reason, asked.Stored, asked.Duplicates, asked.Failures));
        Assert.Null(asked.EndedAt);

        await using var db = Open(api);
        var line = await db.Journal.AsNoTracking().SingleAsync(entry => entry.Kind == JournalKinds.EventsBackfillRequested, Ct);
        Assert.Equal((null, asked.Id, ActorType.Human, "marta", "the weeks nobody read"), (line.CampaignId, line.Key, line.ActorType, line.ActorId, line.Reason));
        Assert.Equal(
            new JsonObject
            {
                ["backfill"] = asked.Id,
                ["source"] = source.PublicId,
                ["plugin"] = Reader,
                ["binding_identity"] = "sha256:a",
                ["from"] = "2026-08-19T12:00:00.000Z",
                ["to"] = "2026-09-18T12:00:00.000Z",
            }.ToJsonString(),
            line.New!.ToJsonString());
    }

    public static TheoryData<string, string, string> Bounds() => new()
    {
        { "no source", "source", "required" },
        { "no from", "from", "required" },
        { "no to", "to", "required" },
        { "from at to", "to", "not_after_from" },
        { "from after to", "to", "not_after_from" },
        { "to after now", "to", "in_the_future" },
        { "from over a year back", "from", "too_old" },
        { "a reason too long", "reason", "too_long" },
    };

    /// <summary>Each bound of a span is refused by its own name, alone: a span inside them all is the only one asked for.</summary>
    [Theory]
    [MemberData(nameof(Bounds))]
    public async Task Each_bound_is_refused_by_its_own_name(string @case, string field, string code)
    {
        await using var api = await StartAsync();
        var source = await SourceAsync(api, "a");
        var from = Noon.AddDays(-60);
        var to = Noon.AddDays(-30);
        var body = new JsonObject { ["source"] = source.PublicId, ["from"] = Stamp(from), ["to"] = Stamp(to) };
        switch (@case)
        {
            case "no source": body.Remove("source"); break;
            case "no from": body.Remove("from"); break;
            case "no to": body.Remove("to"); break;
            case "from at to": body["from"] = Stamp(to); break;
            case "from after to": body["from"] = Stamp(to.AddMilliseconds(1)); break;
            case "to after now": body["to"] = Stamp(Noon.AddMilliseconds(1)); break;
            case "from over a year back": body["from"] = Stamp(Noon.AddDays(-EventsService.BackfillDays).AddMilliseconds(-1)); break;
            case "a reason too long": body["reason"] = new string('x', EventsService.MaxReasonLength + 1); break;
        }

        var error = await api.PostErrorAsync(Operations.EventsBackfill, body, HttpStatusCode.BadRequest, Ct);

        var detail = Assert.Single(error.Details!);
        Assert.True((detail.Field, detail.Code) == (field, code), $"{@case}: refused as {detail.Field}/{detail.Code}, not {field}/{code}.");
        await using var db = Open(api);
        Assert.Equal(0, await db.EventBackfills.CountAsync(Ct));
    }

    /// <summary>The edges are inside: a span may end now, and begin exactly a year and a day back.</summary>
    [Fact]
    public async Task A_span_may_end_now_and_begin_366_days_back()
    {
        await using var api = await StartAsync();
        var source = await SourceAsync(api, "a");

        var asked = await api.PostOkAsync<EventBackfillDto>(
            Operations.EventsBackfill,
            new { source = source.PublicId, from = Noon.AddDays(-EventsService.BackfillDays), to = Noon },
            Ct);

        Assert.Equal((Noon.AddDays(-366), Noon), (asked.From, asked.To));
    }

    /// <summary>The same span asked for again while it waits is the one already waiting, said once; once it has ended it may be asked for anew.</summary>
    [Fact]
    public async Task The_same_waiting_span_answers_the_one_already_asked_for()
    {
        await using var api = await StartAsync();
        var source = await SourceAsync(api, "a");
        var body = new { source = source.PublicId, from = Noon.AddDays(-60), to = Noon.AddDays(-30) };

        var first = await api.PostOkAsync<EventBackfillDto>(Operations.EventsBackfill, body, Ct);
        var again = await api.PostOkAsync<EventBackfillDto>(Operations.EventsBackfill, body, Ct);
        await using (var db = Open(api))
        {
            await db.EventBackfills.ExecuteUpdateAsync(set => set.SetProperty(row => row.Status, EventBackfillStatus.Done), Ct);
        }

        var anew = await api.PostOkAsync<EventBackfillDto>(Operations.EventsBackfill, body, Ct);

        Assert.Equal(first.Id, again.Id);
        Assert.NotEqual(first.Id, anew.Id);
        await using var read = Open(api);
        Assert.Equal(2, await read.Journal.CountAsync(entry => entry.Kind == JournalKinds.EventsBackfillRequested, Ct));
    }

    /// <summary>A span is the same span only by both its ends: one that begins where a waiting one does and ends elsewhere is another.</summary>
    [Fact]
    public async Task A_span_that_begins_where_a_waiting_one_does_and_ends_elsewhere_is_another()
    {
        await using var api = await StartAsync();
        var source = await SourceAsync(api, "a");

        var first = await api.PostOkAsync<EventBackfillDto>(Operations.EventsBackfill, new { source = source.PublicId, from = Noon.AddDays(-60), to = Noon.AddDays(-30) }, Ct);
        var other = await api.PostOkAsync<EventBackfillDto>(Operations.EventsBackfill, new { source = source.PublicId, from = Noon.AddDays(-60), to = Noon.AddDays(-29) }, Ct);

        Assert.NotEqual(first.Id, other.Id);
        Assert.Equal(Noon.AddDays(-29), other.To);
    }

    /// <summary>A span asked for again while eight wait is the one already waiting, answered before the limit is asked: asking again costs no place.</summary>
    [Fact]
    public async Task A_waiting_span_asked_for_again_among_eight_is_answered_not_refused()
    {
        await using var api = await StartAsync();
        var source = await SourceAsync(api, "a");
        var spans = new List<EventBackfillDto>();
        for (var span = 0; span < EventsService.MaxPending; span++)
        {
            spans.Add(await api.PostOkAsync<EventBackfillDto>(Operations.EventsBackfill, Span(source, span), Ct));
        }

        var again = await api.PostOkAsync<EventBackfillDto>(Operations.EventsBackfill, Span(source, 3), Ct);

        Assert.Equal(spans[3].Id, again.Id);
    }

    /// <summary>
    /// The edges hold on a clock between two milliseconds too: a span may begin exactly 366 days back from now as the
    /// runtime keeps moments, to the millisecond, and end exactly then.
    /// </summary>
    [Fact]
    public async Task The_edges_hold_on_a_clock_between_two_milliseconds()
    {
        await using var api = await StartAsync(clock: new FixedClock(Noon.AddTicks(5_000)));
        var source = await SourceAsync(api, "a");

        var asked = await api.PostOkAsync<EventBackfillDto>(
            Operations.EventsBackfill,
            new { source = source.PublicId, from = Noon.AddDays(-EventsService.BackfillDays), to = Noon },
            Ct);

        Assert.Equal((Noon.AddDays(-366), Noon), (asked.From, asked.To));
    }

    /// <summary>Eight spans may wait for one account; a ninth is refused until one ends, done or failed — which is what frees its place.</summary>
    [Fact]
    public async Task A_ninth_waiting_span_is_refused_until_one_ends()
    {
        await using var api = await StartAsync();
        var source = await SourceAsync(api, "a");
        for (var span = 0; span < EventsService.MaxPending; span++)
        {
            await api.PostOkAsync<EventBackfillDto>(Operations.EventsBackfill, Span(source, span), Ct);
        }

        var refused = await api.PostErrorAsync(Operations.EventsBackfill, Span(source, 8), HttpStatusCode.Conflict, Ct);
        await using (var db = Open(api))
        {
            await db.EventBackfills.Where(row => row.Id == 1).ExecuteUpdateAsync(set => set.SetProperty(row => row.Status, EventBackfillStatus.Failed), Ct);
        }

        var admitted = await api.PostOkAsync<EventBackfillDto>(Operations.EventsBackfill, Span(source, 8), Ct);

        Assert.Equal("event_backfill_limit", refused.Code);
        Assert.Equal(EventBackfillStatus.Pending, admitted.Status);
    }

    [Fact]
    public async Task An_unknown_account_is_not_found_and_one_no_route_binds_is_refused()
    {
        await using var api = await StartAsync();
        var unbound = await SourceAsync(api, "gone");

        var missing = await api.PostErrorAsync(Operations.EventsBackfill, Span("evs_01K7QN0000000000000000000Z", 0), HttpStatusCode.NotFound, Ct);
        var refused = await api.PostErrorAsync(Operations.EventsBackfill, Span(unbound, 0), HttpStatusCode.Conflict, Ct);

        Assert.Equal(("event_source_not_found", "event_source_unbound"), (missing.Code, refused.Code));
        await using var db = Open(api);
        Assert.Equal(0, await db.EventBackfills.CountAsync(Ct));
    }

    // ---------------------------------------------------------------------------------------------------------------
    // events.sources
    // ---------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_sources_are_listed_a_page_at_a_time_in_the_order_each_was_first_bound()
    {
        await using var api = await StartAsync();
        await SourceAsync(api, "c", id: "evs_01K7QN0000000000000000000C");
        await SourceAsync(api, "a", id: "evs_01K7QN0000000000000000000A");
        await SourceAsync(api, "b", id: "evs_01K7QN0000000000000000000B");

        var first = await api.PostOkAsync<Page<EventSourceDto>>(Operations.EventsSources, new { limit = 2 }, Ct);
        var second = await api.PostOkAsync<Page<EventSourceDto>>(Operations.EventsSources, new { limit = 2, cursor = first.NextCursor }, Ct);

        Assert.Equal(["evs_01K7QN0000000000000000000A", "evs_01K7QN0000000000000000000B"], first.Items.Select(source => source.Id));
        Assert.Equal(["evs_01K7QN0000000000000000000C"], second.Items.Select(source => source.Id));
        Assert.Null(second.NextCursor);
    }

    /// <summary>
    /// A source says what its row and the active snapshot say: the binding as the route holds it, whether it holds a
    /// cursor and when that was stored — never the cursor — its last read and how it ended, and when it is read next. An
    /// account whose route names no binding is bound with none.
    /// </summary>
    [Fact]
    public async Task A_source_says_what_its_row_and_the_snapshot_say_and_never_its_cursor()
    {
        await using var api = await StartAsync(routes: [Bound("a"), new Route(Reader, null, null)]);
        await SourceAsync(api, "a", id: "evs_01K7QN0000000000000000000A", cursor: "c-SECRETMARK", cursorAt: Noon.AddHours(-2), started: Noon.AddMinutes(-3), failures: 2);
        await SourceAsync(api, EventSource.NoBinding, id: "evs_01K7QN0000000000000000000B");

        var (status, body) = await api.PostAsync(Operations.EventsSources, null, Ct);
        var page = System.Text.Json.JsonSerializer.Deserialize<Page<EventSourceDto>>(body, Jason.Contracts.Json.JasonJson.Options)!;

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.DoesNotContain("SECRETMARK", body, StringComparison.Ordinal);
        var a = page.Items[0];
        Assert.Equal(
            (Reader, "sha256:a", """{"workspace":"a"}""", true, true, Noon.AddHours(-2), Noon.AddMinutes(-3), 2, "events_unreachable", "transient", false),
            (a.Plugin, a.BindingIdentity, a.Binding?.ToJsonString(), a.Bound, a.HasCursor, a.CursorAt, a.ReadStartedAt, a.Failures, a.LastCode, a.LastClass, a.Healthy));
        Assert.Equal(Noon, a.NextReadAt);
        var none = page.Items[1];
        Assert.Equal((EventSource.NoBinding, (string?)null, true, false), (none.BindingIdentity, none.Binding?.ToJsonString(), none.Bound, none.HasCursor));
    }

    /// <summary>
    /// A cursor the plugin forgot is cleared and when it was stored is kept: the record says the account holds no cursor,
    /// and still says when the last one was stored.
    /// </summary>
    [Fact]
    public async Task A_source_whose_cursor_was_forgotten_holds_none()
    {
        await using var api = await StartAsync();
        await SourceAsync(api, "a", cursorAt: Noon.AddHours(-2));

        var source = Assert.Single((await api.PostOkAsync<Page<EventSourceDto>>(Operations.EventsSources, null, Ct)).Items);

        Assert.Equal((false, (DateTimeOffset?)Noon.AddHours(-2)), (source.HasCursor, source.CursorAt));
    }

    [Fact]
    public async Task A_source_no_route_binds_has_no_binding_and_no_next_read()
    {
        await using var api = await StartAsync();
        await SourceAsync(api, "gone", cursor: "c-1", cursorAt: Noon.AddHours(-1));

        var source = Assert.Single((await api.PostOkAsync<Page<EventSourceDto>>(Operations.EventsSources, null, Ct)).Items);

        Assert.Equal((false, (JsonObject?)null, (DateTimeOffset?)null, true), (source.Bound, source.Binding, source.NextReadAt, source.HasCursor));
    }

    /// <summary>
    /// Whether a source is healthy is the inbound read's own question, asked with the settings in force: its last read that
    /// succeeded started within the shorter of two cadences and the look-back, and none has failed since.
    /// </summary>
    [Theory]
    [InlineData(600, 14_400, 1_200, true)]
    [InlineData(600, 14_400, 1_201, false)]
    [InlineData(86_400, 14_400, 14_400, true)]
    [InlineData(86_400, 14_400, 14_401, false)]
    [InlineData(600, 0, 0, false)]
    public async Task Healthy_is_the_inbound_read_s_own_question(int cadence, int lookback, int secondsSinceSuccess, bool healthy)
    {
        var settings = $$$"""{"Dispatcher":{"Enabled":false,"EventsCadenceSeconds":{{{cadence}}}},"Inbound":{"LookbackSeconds":{{{lookback}}}}}""";
        await using var api = await StartAsync(settings: settings);
        var row = await SourceAsync(api, "a", started: Noon.AddSeconds(-secondsSinceSuccess), succeeded: Noon.AddSeconds(-secondsSinceSuccess));

        var source = Assert.Single((await api.PostOkAsync<Page<EventSourceDto>>(Operations.EventsSources, null, Ct)).Items);

        Assert.True(source.Healthy == healthy, $"At cadence {cadence} and look-back {lookback}, {secondsSinceSuccess} s after a success: healthy says {source.Healthy}.");
        Assert.Equal(EventSchedule.IsHealthy(row, cadence, lookback, Noon.UtcDateTime), source.Healthy);
    }

    /// <summary>
    /// The latest span of the account nobody will read, by its own line and not another account's; and its ten newest spans
    /// asked for again, newest first, a failed one with its code and class among them.
    /// </summary>
    [Fact]
    public async Task A_source_shows_its_latest_gap_and_its_ten_newest_backfills()
    {
        await using var api = await StartAsync(routes: [Bound("a"), Bound("b")]);
        var a = await SourceAsync(api, "a", id: "evs_01K7QN0000000000000000000A");
        var b = await SourceAsync(api, "b", id: "evs_01K7QN0000000000000000000B");
        await GapAsync(api, a, Noon.AddDays(-90), Noon.AddDays(-30), said: Noon.AddDays(-10));
        await GapAsync(api, a, Noon.AddDays(-50), Noon.AddDays(-30), said: Noon.AddDays(-2));
        await GapAsync(api, b, Noon.AddDays(-40), Noon.AddDays(-30), said: Noon.AddDays(-1));
        await using (var db = Open(api))
        {
            for (var span = 0; span < 12; span++)
            {
                db.EventBackfills.Add(new EventBackfill
                {
                    PublicId = $"evb_01K7QN00000000000000000{span:D3}",
                    SourceId = a.Id,
                    From = Noon.UtcDateTime.AddDays(-100 + span),
                    To = Noon.UtcDateTime.AddDays(-99 + span),
                    Status = span == 9 ? EventBackfillStatus.Failed : span < 9 ? EventBackfillStatus.Done : EventBackfillStatus.Pending,
                    LastCode = span == 9 ? "events_unauthorized" : null,
                    LastClass = span == 9 ? "permanent" : null,
                    RequestedAt = Noon.UtcDateTime.AddHours(-12 + span),
                    ActorType = ActorType.Human,
                });
            }

            await db.SaveChangesAsync(Ct);
        }

        var page = await api.PostOkAsync<Page<EventSourceDto>>(Operations.EventsSources, null, Ct);

        var record = page.Items[0];
        Assert.Equal(new EventGapDto(Noon.AddDays(-50), Noon.AddDays(-30), Noon.AddDays(-2)), record.LatestGap);
        Assert.Equal(
            Enumerable.Range(2, 10).Reverse().Select(span => $"evb_01K7QN00000000000000000{span:D3}"),
            record.Backfills.Select(backfill => backfill.Id));
        var failed = record.Backfills.Single(backfill => backfill.Status == EventBackfillStatus.Failed);
        Assert.Equal(("events_unauthorized", "permanent"), (failed.Code, failed.Class));
        Assert.Empty(page.Items[1].Backfills);
        Assert.Equal(Noon.AddDays(-40), page.Items[1].LatestGap!.From);
    }

    // ---------------------------------------------------------------------------------------------------------------
    // events.signal
    // ---------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// A signal is kept on the account and answered with its receipt: when it came, and when the next read is due — at the
    /// next scan, or a minute after the last read started where that is later. The record shows it too.
    /// </summary>
    [Theory]
    [InlineData(-300, 0)]
    [InlineData(-20, 40)]
    public async Task A_signal_brings_the_next_read_to_the_next_scan_never_within_a_minute_of_the_last(int lastStartSeconds, int dueInSeconds)
    {
        await using var api = await StartAsync();
        var source = await SourceAsync(api, "a", cursor: "c-1", cursorAt: Noon.AddHours(-1), started: Noon.AddSeconds(lastStartSeconds));

        // A source this process has not read yet is due at once anyway; one it has read waits its cadence or the signal.
        await ReadByThisProcessAsync(api, source);
        var receipt = await api.PostOkAsync<EventsSignalResponse>(Operations.EventsSignal, new { source = source.PublicId }, Ct);

        Assert.Equal((Noon, Noon.AddSeconds(dueInSeconds)), (receipt.SignaledAt, receipt.NextReadAt));
        var record = Assert.Single((await api.PostOkAsync<Page<EventSourceDto>>(Operations.EventsSources, null, Ct)).Items);
        Assert.Equal(((DateTimeOffset?)Noon, (DateTimeOffset?)Noon.AddSeconds(dueInSeconds)), (record.SignaledAt, record.NextReadAt));
    }

    /// <summary>
    /// A signal carries the account's id and nothing else: a webhook's body posted with it lands nowhere — not on the row,
    /// not in the chronicle — and a signal writes no line of its own.
    /// </summary>
    [Fact]
    public async Task A_webhook_s_body_posted_with_a_signal_lands_nowhere()
    {
        await using var api = await StartAsync();
        var source = await SourceAsync(api, "a");
        int before;
        await using (var count = Open(api))
        {
            before = await count.Journal.CountAsync(Ct);
        }

        var (status, _) = await api.PostRawAsync(
            Operations.EventsSignal,
            $$"""{"source":"{{source.PublicId}}","event":"reply","subject":"SECRETMARK","actor":{"type":"human","id":"marta"},"reason":"SECRETMARK"}""",
            Ct);

        Assert.Equal(HttpStatusCode.OK, status);
        await using var db = Open(api);
        Assert.Equal(before, await db.Journal.CountAsync(Ct));
        var said = await db.Database.SqlQueryRaw<string>("SELECT COALESCE(new_json, '') || COALESCE(reason, '') AS Value FROM journal").ToListAsync(Ct);
        Assert.DoesNotContain(said, line => line.Contains("SECRETMARK", StringComparison.Ordinal));
        var rows = await db.Database.SqlQueryRaw<string>("SELECT COALESCE(cursor, '') || public_id AS Value FROM event_sources").ToListAsync(Ct);
        Assert.DoesNotContain(rows, row => row.Contains("SECRETMARK", StringComparison.Ordinal));
        Assert.Equal(Noon.UtcDateTime, (await db.EventSources.AsNoTracking().SingleAsync(Ct)).SignalAt);
    }

    [Fact]
    public async Task A_signal_for_an_unknown_account_is_not_found_and_for_one_no_route_binds_is_refused()
    {
        await using var api = await StartAsync();
        var unbound = await SourceAsync(api, "gone");

        var missing = await api.PostErrorAsync(Operations.EventsSignal, new { source = "evs_01K7QN0000000000000000000Z" }, HttpStatusCode.NotFound, Ct);
        var refused = await api.PostErrorAsync(Operations.EventsSignal, new { source = unbound.PublicId }, HttpStatusCode.Conflict, Ct);
        var nothing = await api.PostErrorAsync(Operations.EventsSignal, new { }, HttpStatusCode.BadRequest, Ct);

        Assert.Equal(("event_source_not_found", "event_source_unbound", "source"), (missing.Code, refused.Code, Assert.Single(nothing.Details!).Field));
        await using var db = Open(api);
        Assert.Null((await db.EventSources.AsNoTracking().SingleAsync(Ct)).SignalAt);
    }

    /// <summary>
    /// What a runtime that has read the account since it started would say: the reader remembers the read — one page from
    /// a plugin of the test's, with no child — and the row keeps the start the test gave it.
    /// </summary>
    private static async Task ReadByThisProcessAsync(RuntimeApiFixture api, EventSource source)
    {
        var reader = api.Resolve<EventReader>();
        var read = new EventRead(
            source.Id,
            source.PublicId,
            source.PluginId,
            source.BindingIdentity,
            null,
            source.Cursor,
            EventSchedule.Plan(source, Noon.UtcDateTime),
            new Jason.Runtime.Plugins.Invocation.PinnedPlugin(api.Resolve<PluginRegistry>().Snapshot.Find(Reader)!, "snp_1"),
            "rts_1");
        await reader.ReadAsync(read, Ct);
        await using var db = Open(api);
        await db.EventSources.Where(row => row.Id == source.Id).ExecuteUpdateAsync(set => set.SetProperty(row => row.ReadStartedAt, source.ReadStartedAt), Ct);
    }

    private static async Task<RuntimeApiFixture> StartAsync(string? settings = null, Route[]? routes = null, FixedClock? clock = null)
    {
        var api = await RuntimeApiFixture.StartAsync(
            Ct,
            prepare: settings is null ? null : paths => File.WriteAllText(paths.UserSettingsFile, settings),
            clock: clock ?? new FixedClock(Noon),
            configureServices: services => services.AddScoped<IEventsInvoker, FakeEventsInvoker>());
        var plugins = new PluginSnapshot(
            PublicId.New(PluginProtocol.SnapshotIdPrefix),
            Noon.UtcDateTime,
            SnapshotSource.Startup,
            [TestPlugins.Loaded(Reader, operations: ["campaign.get", "conversation.list"], events: [1])]);
        api.Resolve<PluginRegistry>().Replace(plugins, new ReloadReport(Noon.UtcDateTime, SnapshotSource.Startup, Activated: true, [], []));
        routes ??= [Bound("a")];
        string[] operations = ["campaign.get", "conversation.list"];
        var overrides = routes.Skip(1).Select((route, index) => (route, index)).ToDictionary(entry => operations[entry.index], entry => entry.route, StringComparer.Ordinal);
        api.Resolve<RouteRegistry>().Replace(new RouteSnapshot(
            PublicId.New(RouteSnapshot.IdPrefix),
            Noon.UtcDateTime,
            plugins.Id,
            new RouteSet(routes[0], overrides),
            new Dictionary<string, RouteSet>(StringComparer.Ordinal)));
        return api;
    }

    private static Route Bound(string account) => new(Reader, new JsonObject { ["workspace"] = account }, "sha256:" + account);

    private static JasonDbContext Open(RuntimeApiFixture api) => new(JasonDbContext.CreateOptions(api.Paths.DatabaseFile));

    private static async Task<EventSource> SourceAsync(
        RuntimeApiFixture api,
        string account,
        string? id = null,
        string? cursor = null,
        DateTimeOffset? cursorAt = null,
        DateTimeOffset? started = null,
        DateTimeOffset? succeeded = null,
        int failures = 0)
    {
        await using var db = Open(api);
        var source = new EventSource
        {
            PublicId = id ?? PublicId.New("evs"),
            PluginId = Reader,
            BindingIdentity = account == EventSource.NoBinding ? account : "sha256:" + account,
            Cursor = cursor,
            CursorAt = cursorAt?.UtcDateTime,
            CreatedAt = Noon.UtcDateTime.AddDays(-60),
            ReadStartedAt = started?.UtcDateTime,
            ReadEndedAt = started?.UtcDateTime,
            SucceededAt = succeeded?.UtcDateTime,
            Failures = failures,
            LastCode = failures > 0 ? "events_unreachable" : null,
            LastClass = failures > 0 ? "transient" : null,
        };
        db.EventSources.Add(source);
        await db.SaveChangesAsync(Ct);
        return source;
    }

    private static async Task GapAsync(RuntimeApiFixture api, EventSource source, DateTimeOffset from, DateTimeOffset to, DateTimeOffset said)
    {
        await using var db = Open(api);
        var gap = new EventGap(from.UtcDateTime, to.UtcDateTime);
        new JournalWriter(new FixedClock(said)).Append(db, Actors.Dispatcher, JournalKinds.EventsGapObserved, campaign: null, gap.Key(source.PublicId), updated: new JsonObject
        {
            ["source"] = source.PublicId,
            ["plugin"] = source.PluginId,
            ["from"] = Stamp(from),
            ["to"] = Stamp(to),
        });
        await db.SaveChangesAsync(Ct);
    }

    private static object Span(EventSource source, int index) => Span(source.PublicId, index);

    private static object Span(string source, int index) => new { source, from = Noon.AddDays(-60 - index), to = Noon.AddDays(-30) };

    private static string Stamp(DateTimeOffset moment) => moment.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture);
}
