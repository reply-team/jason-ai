using System.Globalization;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Ids;
using Jason.Contracts.Plugins;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Plugins.Registry;
using Jason.Runtime.Plugins.Subscriptions;
using Jason.Runtime.Tests.Dispatch;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using static Jason.Runtime.Tests.Plugins.Subscriptions.FakeSubscriptionInvoker;
using static Jason.Runtime.Tests.Plugins.Subscriptions.SubscriptionsBed;

namespace Jason.Runtime.Tests.Plugins.Subscriptions;

/// <summary>
/// Where each subscribed kind stands, the records of its chains, and a person's rewind of a kind's cursor: back to just
/// before a line of the kind or to a moment, never forward, by a person only, said in a journal line of its own, and seen by
/// a chain in flight as a position that moved under it.
/// </summary>
public class SubscriptionServiceTests
{
    private const string Person = "marta";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_kind_is_listed_with_its_chain_its_cursor_its_head_its_lag_and_the_members_it_waits_on()
    {
        using var harness = new DispatchHarness(Noon);
        Load(harness, Subscriber("beta", Failed), Subscriber("alpha", PluginStatus.Unavailable, Failed), Subscriber("gamma", Created));
        var lines = Lines(harness, Failed, Created, Failed, Failed);
        await CursorAsync(harness, Failed, after: lines[0]);
        await CursorAsync(harness, Created, after: lines[3]);
        Lines(harness, Requested);

        await using var db = harness.Open();
        var page = await Service(harness, db).ListAsync(new SubscriptionListRequest(null, null), Ct);

        Assert.Null(page.NextCursor);
        Assert.Equal([Created, Failed], page.Items.Select(kind => kind.Kind));
        var created = page.Items[0];
        Assert.Equal(["gamma"], created.Chain);
        Assert.Equal((await PublicIdOfAsync(harness, lines[3]), await PublicIdOfAsync(harness, lines[1]), 0), (created.Cursor, created.Head, created.Lag));
        Assert.Empty(created.WaitingOn);
        var failed = page.Items[1];
        Assert.Equal(["alpha", "beta"], failed.Chain);
        Assert.Equal((await PublicIdOfAsync(harness, lines[0]), await PublicIdOfAsync(harness, lines[3]), 2), (failed.Cursor, failed.Head, failed.Lag));
        Assert.Equal(["alpha"], failed.WaitingOn);
        Assert.Null(failed.InFlight);
    }

    [Fact]
    public async Task A_kind_at_the_journal_s_start_with_no_line_of_its_own_has_neither_a_cursor_nor_a_head()
    {
        using var harness = new DispatchHarness(Noon);
        Load(harness, Subscriber("beta", Failed));
        await CursorAsync(harness, Failed);

        await using var db = harness.Open();
        var kind = Assert.Single((await Service(harness, db).ListAsync(new SubscriptionListRequest(null, null), Ct)).Items);

        Assert.Equal((null, null, 0), (kind.Cursor, kind.Head, kind.Lag));
    }

    [Fact]
    public async Task A_chain_in_flight_is_listed_with_its_run_its_line_and_when_it_is_tried_again()
    {
        using var harness = new DispatchHarness(Noon);
        Load(harness, Subscriber("beta", Failed));
        var line = Line(harness, Failed);
        await CursorAsync(harness, Failed);
        var run = await RunAsync(harness, Failed, line, SubscriptionRunStatus.Running, retryAt: Noon.AddMinutes(2));

        await using var db = harness.Open();
        var kind = Assert.Single((await Service(harness, db).ListAsync(new SubscriptionListRequest(null, null), Ct)).Items);

        Assert.Equal(
            new SubscriptionInFlightDto(run.PublicId, run.LinePublicId, new DateTimeOffset(Noon), new DateTimeOffset(Noon.AddMinutes(2))),
            kind.InFlight);
    }

    [Fact]
    public async Task Kinds_are_listed_a_page_at_a_time_in_kind_order()
    {
        using var harness = new DispatchHarness(Noon);
        Load(harness, Subscriber("beta", Failed, Created, Requested));
        foreach (var kind in new[] { Failed, Created, Requested })
        {
            await CursorAsync(harness, kind);
        }

        await using var db = harness.Open();
        var service = Service(harness, db);
        var first = await service.ListAsync(new SubscriptionListRequest(2, null), Ct);
        var second = await service.ListAsync(new SubscriptionListRequest(2, first.NextCursor), Ct);

        Assert.Equal([Requested, Created], first.Items.Select(kind => kind.Kind));
        Assert.Equal([Failed], second.Items.Select(kind => kind.Kind));
        Assert.Null(second.NextCursor);
    }

    [Fact]
    public async Task A_kind_s_runs_come_newest_first_a_page_at_a_time_with_the_final_envelope_and_the_failure()
    {
        using var harness = new DispatchHarness(Noon);
        var lines = Lines(harness, Failed, Failed, Failed, Created);
        var succeeded = await RunAsync(harness, Failed, lines[0], SubscriptionRunStatus.Succeeded, envelope: new JsonObject { ["beta"] = "kept" });
        var failed = await RunAsync(harness, Failed, lines[1], SubscriptionRunStatus.Failed, failure: ("beta", "permanent", "beta_refused"));
        var running = await RunAsync(harness, Failed, lines[2], SubscriptionRunStatus.Running);
        await RunAsync(harness, Created, lines[3], SubscriptionRunStatus.Succeeded);

        await using var db = harness.Open();
        var service = Service(harness, db);
        var first = await service.RunsAsync(new SubscriptionRunsRequest(Failed, 2, null), Ct);
        var second = await service.RunsAsync(new SubscriptionRunsRequest(Failed, 2, first.NextCursor), Ct);

        Assert.Equal([running.PublicId, failed.PublicId], first.Items.Select(run => run.Id));
        Assert.Equal([succeeded.PublicId], second.Items.Select(run => run.Id));
        Assert.Null(second.NextCursor);
        var done = second.Items[0];
        Assert.Equal((Failed, succeeded.LinePublicId, SubscriptionRunStatus.Succeeded), (done.Kind, done.Line, done.Status));
        Assert.Equal("""{"beta":"kept"}""", done.Envelope!.ToJsonString());
        Assert.Equal("""[{"plugin_id":"beta","started_at":"2026-11-04T12:00:00.000Z"}]""", done.Handlers.ToJsonString());
        Assert.Null(done.Failure);
        Assert.Equal(new SubscriptionFailureDto("beta", "permanent", "beta_refused"), first.Items[1].Failure);
        Assert.Null(first.Items[0].EndedAt);
    }

    [Fact]
    public async Task Runs_name_their_kind()
    {
        using var harness = new DispatchHarness(Noon);
        await using var db = harness.Open();

        var refused = await Assert.ThrowsAsync<ValidationException>(() => Service(harness, db).RunsAsync(new SubscriptionRunsRequest(null, null, null), Ct));

        Assert.Equal(("kind", "required"), Detail(refused));
    }

    /// <summary>A cursor another page handed out decodes, but names no run of this record, so it is refused rather than read as the start.</summary>
    [Fact]
    public async Task A_runs_cursor_that_is_no_page_of_runs_is_refused()
    {
        using var harness = new DispatchHarness(Noon);
        var lines = Lines(harness, Failed);
        await RunAsync(harness, Failed, lines[0], SubscriptionRunStatus.Succeeded);
        await using var db = harness.Open();

        var refused = await Assert.ThrowsAsync<ValidationException>(
            () => Service(harness, db).RunsAsync(new SubscriptionRunsRequest(Failed, null, Paging.EncodeCursor("jrn_01M6N43SV49XWWJMXJBX95DG7P")), Ct));

        Assert.Equal(("cursor", "invalid"), Detail(refused));
    }

    [Fact]
    public async Task A_rewind_to_a_line_moves_the_cursor_to_just_before_it_bumps_the_revision_and_says_so_in_a_line_of_its_own()
    {
        using var harness = new DispatchHarness(Noon);
        Load(harness, Subscriber("beta", Failed));
        var lines = Lines(harness, Failed, Created, Failed, Failed);
        await CursorAsync(harness, Failed, after: lines[3], revision: 4);
        var target = await PublicIdOfAsync(harness, lines[2]);

        await using var db = harness.Open();
        var kind = await Service(harness, db).RewindAsync(Rewind(line: target, reason: "the forwarder missed these"), Ct);

        Assert.Equal((await PublicIdOfAsync(harness, lines[1]), 2), (kind.Cursor, kind.Lag));
        Assert.Equal([$"{Failed}|{lines[1]}|5"], await PositionsAsync(harness));
        var said = Assert.Single(await JournalAsync(harness, JournalKinds.SubscriptionCursorRewound));
        Assert.Equal((ActorType.Human, Person, Failed, "the forwarder missed these"), (said.ActorType, said.ActorId, said.Key, said.Reason));
        Assert.Equal($$"""{"line":"{{await PublicIdOfAsync(harness, lines[3])}}"}""", said.Old!.ToJsonString());
        Assert.Equal($$"""{"line":"{{await PublicIdOfAsync(harness, lines[1])}}"}""", said.New!.ToJsonString());
        Assert.Null(said.CampaignId);
    }

    [Fact]
    public async Task Every_rewind_is_a_line_of_its_own_the_cursor_where_it_stands_included()
    {
        using var harness = new DispatchHarness(Noon);
        Load(harness, Subscriber("beta", Failed));
        var lines = Lines(harness, Failed, Failed);
        await CursorAsync(harness, Failed, after: lines[1]);
        var target = await PublicIdOfAsync(harness, lines[1]);

        await using var db = harness.Open();
        var service = Service(harness, db);
        await service.RewindAsync(Rewind(line: target), Ct);
        await service.RewindAsync(Rewind(line: target), Ct);

        Assert.Equal(2, (await JournalAsync(harness, JournalKinds.SubscriptionCursorRewound)).Count);
        Assert.Equal([$"{Failed}|{lines[0]}|2"], await PositionsAsync(harness));
    }

    [Fact]
    public async Task A_rewind_to_a_moment_hands_again_every_line_of_the_kind_written_from_it_on()
    {
        using var harness = new DispatchHarness(Noon);
        Load(harness, Subscriber("beta", Failed));
        var early = LineAt(harness, Failed, Noon.AddHours(-3));
        var before = LineAt(harness, Created, Noon.AddHours(-2));
        var from = LineAt(harness, Failed, Noon.AddHours(-1));
        var last = LineAt(harness, Failed, Noon);
        await CursorAsync(harness, Failed, after: last);

        await using var db = harness.Open();
        var kind = await Service(harness, db).RewindAsync(Rewind(since: "2026-11-04T12:00:00+01:00"), Ct);

        Assert.Equal((await PublicIdOfAsync(harness, before), 2), (kind.Cursor, kind.Lag));
        Assert.Equal([$"{Failed}|{before}|1"], await PositionsAsync(harness));
        Assert.True(early < before && before < from);
    }

    /// <summary>
    /// A line's moment is stamped when it is appended and its id when it is saved, so a writer that stamped earlier and
    /// saved later holds a higher id and an earlier moment. The cursor goes to just before the first line at or after the
    /// moment, never to the greatest line before it, so the line at the moment is handed again and so is the late one.
    /// </summary>
    [Fact]
    public async Task A_line_stamped_before_the_moment_and_saved_after_a_line_at_it_never_carries_the_cursor_past_that_line()
    {
        using var harness = new DispatchHarness(Noon);
        Load(harness, Subscriber("beta", Failed));
        var early = LineAt(harness, Failed, Noon.AddHours(-3));
        var atMoment = LineAt(harness, Failed, Noon.AddHours(-1));
        var late = LineAt(harness, Failed, Noon.AddHours(-2));
        var last = LineAt(harness, Failed, Noon);
        await CursorAsync(harness, Failed, after: last);

        await using var db = harness.Open();
        var kind = await Service(harness, db).RewindAsync(Rewind(since: "2026-11-04T11:00:00Z"), Ct);

        Assert.True(early < atMoment && atMoment < late && late < last);
        Assert.Equal((await PublicIdOfAsync(harness, early), 3), (kind.Cursor, kind.Lag));
        Assert.Equal([$"{Failed}|{early}|1"], await PositionsAsync(harness));
    }

    [Fact]
    public async Task A_moment_before_the_journal_s_first_line_puts_the_cursor_at_the_start()
    {
        using var harness = new DispatchHarness(Noon);
        Load(harness, Subscriber("beta", Failed));
        var lines = Lines(harness, Failed, Failed);
        await CursorAsync(harness, Failed, after: lines[1]);

        await using var db = harness.Open();
        var kind = await Service(harness, db).RewindAsync(Rewind(since: "2026-01-01T00:00:00Z"), Ct);

        Assert.Equal((null, 2), (kind.Cursor, kind.Lag));
        Assert.Equal([$"{Failed}|0|1"], await PositionsAsync(harness));
        Assert.Equal("""{"line":null}""", Assert.Single(await JournalAsync(harness, JournalKinds.SubscriptionCursorRewound)).New!.ToJsonString());
    }

    [Fact]
    public async Task A_target_after_the_cursor_is_refused_since_a_rewind_never_skips()
    {
        using var harness = new DispatchHarness(Noon);
        Load(harness, Subscriber("beta", Failed));
        var lines = Lines(harness, Failed, Failed, Failed);
        await CursorAsync(harness, Failed, after: lines[0]);

        await using var db = harness.Open();
        var service = Service(harness, db);
        var third = await PublicIdOfAsync(harness, lines[2]);
        var byLine = await Assert.ThrowsAsync<ConflictException>(() => service.RewindAsync(Rewind(line: third), Ct));
        var byMoment = await Assert.ThrowsAsync<ConflictException>(() => service.RewindAsync(Rewind(since: "2026-11-04T12:00:01Z"), Ct));

        Assert.Equal(["subscription_rewind_forward", "subscription_rewind_forward"], new[] { byLine.Code, byMoment.Code });
        await NothingMovedAsync(harness, lines[0]);
    }

    [Theory]
    [InlineData(ActorType.Role, "researcher")]
    [InlineData(ActorType.Attempt, "att_01JC0000000000000000000001")]
    [InlineData(ActorType.Plugin, "acme-forwarder")]
    public async Task Only_a_person_rewinds(ActorType type, string id)
    {
        using var harness = new DispatchHarness(Noon);
        Load(harness, Subscriber("beta", Failed));
        var lines = Lines(harness, Failed);
        await CursorAsync(harness, Failed, after: lines[0]);

        var line = await PublicIdOfAsync(harness, lines[0]);
        await using var db = harness.Open();
        using var door = type == ActorType.Plugin ? ActorScope.ForPlugin(id) : null;
        var refused = await Assert.ThrowsAsync<InvalidRequestException>(
            () => Service(harness, db).RewindAsync(Rewind(line: line) with { Actor = new ActorRef(type, id) }, Ct));

        Assert.Equal("subscription_rewind_not_human", refused.Code);
        await NothingMovedAsync(harness, lines[0]);
    }

    [Fact]
    public async Task A_rewind_names_the_person_whose_word_it_is()
    {
        using var harness = new DispatchHarness(Noon);
        Load(harness, Subscriber("beta", Failed));
        var lines = Lines(harness, Failed);
        await CursorAsync(harness, Failed, after: lines[0]);

        await using var db = harness.Open();
        var service = Service(harness, db);
        var line = await PublicIdOfAsync(harness, lines[0]);
        var absent = await Assert.ThrowsAsync<InvalidRequestException>(() => service.RewindAsync(Rewind(line: line) with { Actor = null }, Ct));
        var nameless = await Assert.ThrowsAsync<InvalidRequestException>(() => service.RewindAsync(Rewind(line: line) with { Actor = new ActorRef(ActorType.Human) }, Ct));

        Assert.Equal(["actor_required", "actor_required"], new[] { absent.Code, nameless.Code });
        await NothingMovedAsync(harness, lines[0]);
    }

    [Theory]
    [InlineData(null, null, "line", "exactly_one")]
    [InlineData("jrn_01JC0000000000000000000001", "2026-11-04T11:00:00Z", "since", "exactly_one")]
    [InlineData(null, "2026-11-04 11:00", "since", "invalid")]
    [InlineData(null, "2026-11-04T11:00:00", "since", "invalid")]
    [InlineData(null, "yesterday", "since", "invalid")]
    [InlineData(null, "2026-11-04T11:00:00Z\n", "since", "invalid")]
    public async Task A_rewind_names_one_target_and_a_moment_with_its_offset(string? line, string? since, string field, string code)
    {
        using var harness = new DispatchHarness(Noon);
        Load(harness, Subscriber("beta", Failed));
        await CursorAsync(harness, Failed);

        await using var db = harness.Open();
        var refused = await Assert.ThrowsAsync<ValidationException>(() => Service(harness, db).RewindAsync(Rewind(line: line, since: since), Ct));

        Assert.Equal("validation_failed", refused.Code);
        Assert.Equal((field, code), Detail(refused));
    }

    [Fact]
    public async Task A_kind_nobody_subscribes_to_and_a_line_of_another_kind_are_refused()
    {
        using var harness = new DispatchHarness(Noon);
        Load(harness, Subscriber("beta", Failed));
        var lines = Lines(harness, Failed, Created);
        await CursorAsync(harness, Failed, after: lines[1]);

        await using var db = harness.Open();
        var service = Service(harness, db);
        var created = await PublicIdOfAsync(harness, lines[1]);
        var unsubscribed = await Assert.ThrowsAsync<NotFoundException>(() => service.RewindAsync(Rewind(line: created) with { Kind = Created }, Ct));
        var other = await Assert.ThrowsAsync<NotFoundException>(() => service.RewindAsync(Rewind(line: created), Ct));
        var unknown = await Assert.ThrowsAsync<NotFoundException>(() => service.RewindAsync(Rewind(line: "jrn_01JC0000000000000000000099"), Ct));

        Assert.Equal(
            ["subscription_kind_unsubscribed", "subscription_line_not_found", "subscription_line_not_found"],
            new[] { unsubscribed.Code, other.Code, unknown.Code });
        await NothingMovedAsync(harness, lines[1]);
    }

    /// <summary>
    /// A rewind while a handler runs bumps the revision the chain started from, so the chain's end matches nothing: its
    /// record ends superseded, no failure is said, and the next scan offers the kind from where the rewind put it.
    /// </summary>
    [Fact]
    public async Task A_chain_in_flight_during_a_rewind_ends_superseded()
    {
        using var harness = new DispatchHarness(Noon);
        Load(harness, Subscriber("watcher", Failed));
        var lines = Lines(harness, Failed, Failed);
        await CursorAsync(harness, Failed, after: lines[0]);
        var handled = await PublicIdOfAsync(harness, lines[1]);
        harness.Handlers.Answer("watcher", (_, _) =>
        {
            using var db = harness.Open();
            Service(harness, db).RewindAsync(Rewind(line: handled), Ct).GetAwaiter().GetResult();
            return Envelope(new JsonObject { ["watcher"] = "seen" });
        });

        Assert.Equal(new SubscriptionTurn(Failed, 1, 0, SubscriptionTurn.Superseded), await harness.SubscriptionRunner.RunNextAsync(Failed, Ct));

        var run = Assert.Single(await RunsAsync(harness));
        Assert.Equal(SubscriptionRunStatus.Superseded, run.Status);
        Assert.Empty(await JournalAsync(harness, JournalKinds.SubscriptionFailed));
        Assert.Equal([$"{Failed}|{lines[0]}|1"], await PositionsAsync(harness));
    }

    private static SubscriptionService Service(DispatchHarness harness, JasonDbContext db) =>
        new(db, new JournalWriter(harness.Clock), harness.Resolve<PluginRegistry>(), harness.Clock);

    private static SubscriptionRewindRequest Rewind(string? line = null, string? since = null, string? reason = null) =>
        new(Failed, line, since, new ActorRef(ActorType.Human, Person), reason);

    private static (string Field, string Code) Detail(ValidationException refused)
    {
        var detail = Assert.Single(refused.Details!);
        return (detail.Field, detail.Code);
    }

    private static async Task NothingMovedAsync(DispatchHarness harness, int after)
    {
        Assert.Equal(
            [string.Create(CultureInfo.InvariantCulture, $"{Failed}|{after}|0")],
            await PositionsAsync(harness));
        Assert.Empty(await JournalAsync(harness, JournalKinds.SubscriptionCursorRewound));
    }

    private static async Task<List<SubscriptionRun>> RunsAsync(DispatchHarness harness)
    {
        await using var db = harness.Open();
        return await db.SubscriptionRuns.AsNoTracking().OrderBy(run => run.Id).ToListAsync(Ct);
    }

    /// <summary>A run's record as the runner would have left it: one handler started at noon, and how the chain ended.</summary>
    private static async Task<SubscriptionRun> RunAsync(
        DispatchHarness harness,
        string kind,
        int line,
        SubscriptionRunStatus status,
        JsonObject? envelope = null,
        (string Plugin, string Class, string Code)? failure = null,
        DateTime? retryAt = null)
    {
        await using var db = harness.Open();
        var run = new SubscriptionRun
        {
            PublicId = PublicId.New("sbr"),
            Kind = kind,
            LineId = line,
            LinePublicId = await PublicIdOfAsync(harness, line),
            Status = status,
            Handlers = [new JsonObject { ["plugin_id"] = "beta", ["started_at"] = "2026-11-04T12:00:00.000Z" }],
            Envelope = envelope,
            FailedPluginId = failure?.Plugin,
            FailureClass = failure?.Class,
            FailureCode = failure?.Code,
            StartedAt = Noon,
            EndedAt = status == SubscriptionRunStatus.Running ? null : Noon,
            RetryAt = retryAt,
        };
        db.SubscriptionRuns.Add(run);
        await db.SaveChangesAsync(Ct);
        return run;
    }

    /// <summary>A line of the kind written by the runtime at the moment given: its id.</summary>
    private static int LineAt(DispatchHarness harness, string kind, DateTime at)
    {
        using var connection = new SqliteConnection($"Data Source={harness.DatabaseFile}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO journal (public_id, ts, actor_type, actor_id, kind) VALUES ($id, $ts, 'system', 'runtime', $kind); SELECT last_insert_rowid();";
        command.Parameters.AddWithValue("$id", PublicId.New("jrn"));
        command.Parameters.AddWithValue("$ts", at.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$kind", kind);
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }
}
