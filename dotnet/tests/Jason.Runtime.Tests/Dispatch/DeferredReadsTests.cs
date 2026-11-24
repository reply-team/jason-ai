using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Ids;
using Jason.Contracts.Plugins;
using Jason.Runtime.Configuration;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Domain;
using Jason.Runtime.Execution;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Plugins.Registry;
using Jason.Runtime.Routing;
using Jason.Runtime.Tests.Plugins;
using Jason.Runtime.WorkItems;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Dispatch;

/// <summary>
/// The deferred read step: a send the provider took for a moment that has passed by the grace, and no more than thirty days
/// ago, is read where it ran, as the dispatcher, until something says what became of it: once the grace has passed, an
/// hour after the first read ended, then a day after each, until a read ends a week after the moment, nine reads at most.
/// At most five reads the runtime made are open in a campaign, never two of one send; a read no route takes is not made,
/// and said once. It writes no line itself.
/// </summary>
public class DeferredReadsTests
{
    private static readonly DateTime Noon = new(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);

    private const string Mailer = "deferred-mailer";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// A first letter and a follow-up, each taken for a moment that passed by the grace, are each read: one
    /// <c>message.status</c> naming the send and nothing else, made by the dispatcher as root work about no person, at
    /// priority 0, with the step's reason; the claim takes it where the send ran. No line is written by the step.
    /// </summary>
    [Fact]
    public async Task A_deferred_send_whose_moment_passed_by_the_grace_is_read_where_it_ran_a_follow_up_as_a_first_letter()
    {
        using var harness = Routed();
        var (campaign, ines) = await SceneAsync(harness);
        var first = await SendAsync(harness, campaign, ines, Noon.AddMinutes(-20));
        var followUp = await SendAsync(harness, campaign, ines, Noon.AddMinutes(-15), follows: first, named: true);

        Assert.Equal(2, await harness.DeferredReadAsync(Ct));

        await using (var db = harness.Open())
        {
            var reads = await db.WorkItems.AsNoTracking().Where(item => item.Operation == SendReads.Operation).OrderBy(item => item.Id).ToListAsync(Ct);
            Assert.Equal([first, followUp], reads.Select(read => (string?)read.Context["input"]!["send"]));
            Assert.All(reads, read =>
            {
                Assert.Equal((WorkItemKind.ProviderOp, ActorType.System, "dispatcher", 0, (int?)null, WorkItemStatus.Created), (read.Kind, read.CreatedByType, read.CreatedById, read.Priority, read.ContactId, read.Status));
                Assert.Equal(["input"], read.Context.Select(pair => pair.Key));
            });
            var created = await db.Journal.AsNoTracking()
                .Where(entry => entry.Kind == JournalKinds.WorkItemCreated && reads.Select(read => read.PublicId).Contains(entry.WorkItemId!))
                .ToListAsync(Ct);
            Assert.Equal([DeferredReads.Reason, DeferredReads.Reason], created.Select(entry => entry.Reason));
            Assert.False(await db.Journal.AnyAsync(entry => entry.Kind == JournalKinds.MessageUnsentObserved, Ct));
        }

        var claimed = Assert.Single(await harness.ClaimAsync(Ct));
        Assert.Equal((SendReads.Operation, Mailer, InboundSyncHarvestTests.RoutedIdentity), (claimed.Plan!.Contract.Id, claimed.Plan.Plugin.Manifest.Id, claimed.Plan.BindingIdentity));
    }

    /// <summary>
    /// A send whose moment is inside the grace waits for it; one whose moment passed more than thirty days ago, one whose
    /// moment is still to come, one with no moment known and one the provider cannot cancel are not read at all. Once the
    /// grace has passed, the first is read.
    /// </summary>
    [Fact]
    public async Task A_send_inside_its_grace_waits_and_one_beyond_thirty_days_or_not_held_is_never_read()
    {
        using var harness = Routed();
        var (campaign, ines) = await SceneAsync(harness);
        var inside = await SendAsync(harness, campaign, ines, Noon.AddMinutes(-10));
        await SendAsync(harness, campaign, ines, Noon.AddDays(-30).AddMinutes(-1));
        await SendAsync(harness, campaign, ines, Noon.AddHours(1));
        await SendAsync(harness, campaign, ines, moment: null);
        await SendAsync(harness, campaign, ines, Noon.AddHours(-2), cancelable: false);

        Assert.Equal(0, await harness.DeferredReadAsync(Ct));

        harness.Clock.Advance(TimeSpan.FromMinutes(4));
        Assert.Equal(0, await harness.DeferredReadAsync(Ct));

        harness.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(1, await harness.DeferredReadAsync(Ct));
        Assert.Equal([inside], await ReadSendsAsync(harness));
    }

    /// <summary>
    /// The look-back is on the moment: a send handed over 35 days ago for a moment 5 days ago is read, and so is one handed
    /// over a minute inside the horizon before the look-back's start; one handed over a minute before that is left out by
    /// its own column, whatever its moment.
    /// </summary>
    [Fact]
    public async Task A_send_handed_over_long_before_a_moment_inside_the_look_back_is_read_within_the_horizon()
    {
        using var harness = Routed();
        var (campaign, ines) = await SceneAsync(harness);
        var edge = Noon - DeferredReads.LookBack - DeferredReads.Horizon;
        var month = await SendAsync(harness, campaign, ines, Noon.AddDays(-5), taken: Noon.AddDays(-35));
        var year = await SendAsync(harness, campaign, ines, Noon.AddDays(-5), taken: edge.AddMinutes(1));
        await SendAsync(harness, campaign, ines, Noon.AddDays(-5), taken: edge.AddMinutes(-1));

        Assert.Equal(2, await harness.DeferredReadAsync(Ct));
        Assert.Equal([month, year], await ReadSendsAsync(harness));
    }

    /// <summary>
    /// A paused campaign's reads would wait, so none is made there; a send of a campaign that resumes is read at the next
    /// scan after it.
    /// </summary>
    [Fact]
    public async Task A_paused_campaign_s_deferred_sends_are_read_once_it_resumes()
    {
        using var harness = Routed();
        var (campaign, ines) = await SceneAsync(harness, CampaignStatus.Paused);
        var send = await SendAsync(harness, campaign, ines, Noon.AddHours(-1));

        Assert.Equal(0, await harness.DeferredReadAsync(Ct));

        await using (var db = harness.Open())
        {
            await db.Campaigns.Where(c => c.Id == campaign.Id).ExecuteUpdateAsync(set => set.SetProperty(c => c.Status, CampaignStatus.Active), Ct);
        }

        harness.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(1, await harness.DeferredReadAsync(Ct));
        Assert.Equal([send], await ReadSendsAsync(harness));
    }

    /// <summary>
    /// A read no route takes where the send ran is not made, since the claim would refuse it; the step says so once per
    /// campaign and asks again at its next run, and once a route names the account the read is made.
    /// </summary>
    [Fact]
    public async Task A_send_no_route_takes_is_not_read_said_once_and_read_once_routed()
    {
        using var harness = new DispatchHarness(Noon);
        InboundSyncHarvestTests.Load(harness, Plugin());
        RouteElsewhere(harness);
        var (campaign, ines) = await SceneAsync(harness);
        var send = await SendAsync(harness, campaign, ines, Noon.AddHours(-1));

        Assert.Equal(0, await harness.DeferredReadAsync(Ct));
        harness.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(0, await harness.DeferredReadAsync(Ct));
        Assert.Single(harness.Logs.All, line => line.Contains(DeferredReadSkipCodes.NotRouted, StringComparison.Ordinal));

        InboundSyncHarvestTests.RouteAll(harness, Mailer);
        harness.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(1, await harness.DeferredReadAsync(Ct));
        Assert.Equal([send], await ReadSendsAsync(harness));
    }

    /// <summary>
    /// At most five reads the runtime made are open in a campaign, whichever step made them, the oldest sends first; the
    /// rest wait until reads end. Another campaign has room of its own.
    /// </summary>
    [Fact]
    public async Task At_most_five_reads_the_runtime_made_are_open_in_a_campaign()
    {
        using var harness = Routed();
        var (campaign, ines) = await SceneAsync(harness);
        var (other, omar) = await SceneAsync(harness);
        var bounced = await SendAsync(harness, campaign, ines, Noon.AddDays(-2));
        await ReadAsync(harness, campaign, bounced, ended: null);
        var sends = new List<string>();
        for (var hour = 9; hour >= 3; hour--)
        {
            sends.Add(await SendAsync(harness, campaign, ines, Noon.AddHours(-hour)));
        }

        var elsewhere = await SendAsync(harness, other, omar, Noon.AddHours(-1));

        Assert.Equal(5, await harness.DeferredReadAsync(Ct));
        Assert.Equal([bounced, .. sends.Take(4), elsewhere], await ReadSendsAsync(harness));

        harness.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(0, await harness.DeferredReadAsync(Ct));

        await EndOpenReadsAsync(harness, campaign, "queued", sends[0]);
        harness.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(1, await harness.DeferredReadAsync(Ct));
        Assert.Equal(sends[4], (await ReadSendsAsync(harness))[^1]);
    }

    /// <summary>
    /// A send something already says what became of is not read: a cancel that succeeded, an answer that the provider holds
    /// no such send, by a cancel or by a read, or a final read, whoever made it. A cancel answered that nothing was pending,
    /// and a read that says the send can no longer be cancelled, say only that it can no longer be taken back, so it is
    /// still read.
    /// </summary>
    [Fact]
    public async Task A_send_something_says_what_became_of_is_not_read_and_one_no_longer_cancellable_is()
    {
        using var harness = Routed();
        var (campaign, ines) = await SceneAsync(harness);
        var cancelled = await SendAsync(harness, campaign, ines, Noon.AddHours(-6));
        await ReadAsync(harness, campaign, cancelled, Noon.AddHours(-7), operation: HeldSends.Cancel, state: null, by: ActorType.Human);
        var cancelGone = await SendAsync(harness, campaign, ines, Noon.AddHours(-6));
        await ReadAsync(harness, campaign, cancelGone, Noon.AddHours(-7), operation: HeldSends.Cancel, code: "send_not_found", by: ActorType.Human);
        var readGone = await SendAsync(harness, campaign, ines, Noon.AddHours(-6));
        await ReadAsync(harness, campaign, readGone, Noon.AddHours(-1), code: "send_not_found", by: ActorType.Role);
        var went = await SendAsync(harness, campaign, ines, Noon.AddHours(-6));
        await ReadAsync(harness, campaign, went, Noon.AddHours(-1), state: "sent", by: ActorType.Role);
        var failed = await SendAsync(harness, campaign, ines, Noon.AddHours(-6));
        await ReadAsync(harness, campaign, failed, Noon.AddHours(-1), state: "failed", by: ActorType.Human);
        var notPending = await SendAsync(harness, campaign, ines, Noon.AddHours(-6));
        await ReadAsync(harness, campaign, notPending, Noon.AddHours(-7), operation: HeldSends.Cancel, code: "send_not_pending", by: ActorType.Human);
        var sending = await SendAsync(harness, campaign, ines, Noon.AddHours(-6));
        await ReadAsync(harness, campaign, sending, Noon.AddHours(-7), state: "queued", cancelable: false, by: ActorType.Human);

        Assert.Equal(2, await harness.DeferredReadAsync(Ct));
        Assert.Equal([notPending, sending], await ReadSendsAsync(harness));
    }

    /// <summary>
    /// An open read the runtime made of a send — the bounce step's, say — holds a second: never two at once. A read a
    /// person or a role made is theirs, and the runtime makes its own beside it.
    /// </summary>
    [Fact]
    public async Task An_open_read_the_runtime_made_holds_a_second_and_a_person_s_does_not()
    {
        using var harness = Routed();
        var (campaign, ines) = await SceneAsync(harness);
        var held = await SendAsync(harness, campaign, ines, Noon.AddHours(-2));
        await ReadAsync(harness, campaign, held, ended: null);
        var persons = await SendAsync(harness, campaign, ines, Noon.AddHours(-1));
        await ReadAsync(harness, campaign, persons, ended: null, by: ActorType.Human);

        Assert.Equal(1, await harness.DeferredReadAsync(Ct));

        await using var db = harness.Open();
        var system = await db.WorkItems.AsNoTracking()
            .Where(item => item.Operation == SendReads.Operation && item.CreatedByType == ActorType.System)
            .Select(item => item.Context)
            .ToListAsync(Ct);
        Assert.Equal(1, system.Count(context => (string?)context["input"]!["send"] == held));
        Assert.Equal(1, system.Count(context => (string?)context["input"]!["send"] == persons));
    }

    /// <summary>
    /// The reads follow the waits: the first once the grace has passed, the second an hour after the first ended, then a
    /// day after each, never a minute before; the read that ends a week after the moment is the last, and with the shipped
    /// settings that is the ninth.
    /// </summary>
    [Fact]
    public async Task Reads_come_after_the_grace_then_an_hour_then_a_day_until_one_ends_a_week_after_the_moment()
    {
        using var harness = Routed();
        var (campaign, ines) = await SceneAsync(harness);
        var moment = Noon.AddMinutes(-15);
        var send = await SendAsync(harness, campaign, ines, moment);

        var made = await DriveAsync(harness, campaign, send);

        Assert.Equal(DeferredReads.MostReads, made.Count);
        Assert.Equal(moment.AddMinutes(15), made[0]);
        Assert.Equal(made[0].AddMinutes(1).AddHours(1), made[1]);
        for (var read = 2; read < made.Count; read++)
        {
            Assert.Equal(made[read - 1].AddMinutes(1).AddDays(1), made[read]);
        }

        Assert.True(made[^1].AddMinutes(1) >= moment.AddDays(7));
        Assert.True(made[^2].AddMinutes(1) < moment.AddDays(7));
    }

    /// <summary>
    /// The reads stop at the read that ends the overdue span after the moment, whatever is left of the nine, since it says
    /// the send is overdue: with three days, the fifth read is the last, and none is made after it however long the clock
    /// runs.
    /// </summary>
    [Fact]
    public async Task The_reads_stop_at_the_read_that_ends_the_overdue_span_after_the_moment()
    {
        using var harness = Routed(options => options.DeferredReadOverdueSeconds = 3 * 86_400);
        var (campaign, ines) = await SceneAsync(harness);
        var moment = Noon.AddMinutes(-15);
        var send = await SendAsync(harness, campaign, ines, moment);

        var made = await DriveAsync(harness, campaign, send);

        Assert.Equal(5, made.Count);
        Assert.True(made[^1].AddMinutes(1) >= moment.AddDays(3));
        Assert.Equal(SentFacts.Overdue, (string?)(await UnsentAsync(harness, send))?["state"]);
    }

    /// <summary>
    /// A span lowered while a send's reads are under way is met by its next read: the reads made under a week all ended
    /// before the line could be said, and said nothing then; the next, a day after the last, ends past the new two days
    /// and says the send is overdue, and it is the last.
    /// </summary>
    [Fact]
    public async Task A_span_lowered_while_a_send_is_read_is_met_by_its_next_read_which_says_it_is_overdue()
    {
        using var harness = Routed();
        var (campaign, ines) = await SceneAsync(harness);
        var moment = Noon.AddMinutes(-15);
        var send = await SendAsync(harness, campaign, ines, moment);
        var silent = true;

        var made = await DriveAsync(harness, campaign, send, ended: count =>
        {
            if (count == 5)
            {
                silent = UnsentAsync(harness, send).GetAwaiter().GetResult() is null;
                harness.Options.CurrentValue.DeferredReadOverdueSeconds = 2 * 86_400;
            }
        });

        Assert.True(silent);
        Assert.Equal(6, made.Count);
        Assert.True(made[4].AddMinutes(1) >= moment.AddDays(2));
        Assert.Equal(SentFacts.Overdue, (string?)(await UnsentAsync(harness, send))?["state"]);
    }

    /// <summary>
    /// A read that failed spaces the next as any read does, a day after it ended, and counts toward a nine of its own:
    /// after eight that failed, the last of them past the overdue span, the read that follows says the send is overdue;
    /// a send whose nine reads failed is read no more, and nothing is said of it.
    /// </summary>
    [Fact]
    public async Task Reads_that_failed_count_to_a_nine_of_their_own_and_a_last_one_that_failed_is_followed_by_another()
    {
        using var harness = Routed();
        var (campaign, ines) = await SceneAsync(harness);
        var moment = Noon.AddDays(-10);
        var send = await SendAsync(harness, campaign, ines, moment);
        var spent = await SendAsync(harness, campaign, ines, moment);
        for (var read = 0; read < DeferredReads.MostReads; read++)
        {
            var ended = Noon.AddDays(read - DeferredReads.MostReads).AddMinutes(1);
            await ReadAsync(harness, campaign, spent, ended, code: "provider_unavailable");
            if (read > 0)
            {
                await ReadAsync(harness, campaign, send, ended, code: "provider_unavailable");
            }
        }

        Assert.Equal(0, await harness.DeferredReadAsync(Ct));
        harness.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(1, await harness.DeferredReadAsync(Ct));
        Assert.Null(await UnsentAsync(harness, send));

        harness.Clock.Advance(TimeSpan.FromMinutes(1));
        await EndOpenReadsAsync(harness, campaign, "queued", send);
        Assert.Equal(SentFacts.Overdue, (string?)(await UnsentAsync(harness, send))?["state"]);
        harness.Clock.Advance(TimeSpan.FromDays(2));
        Assert.Equal(0, await harness.DeferredReadAsync(Ct));
        Assert.Null(await UnsentAsync(harness, spent));
        var named = await ReadSendsAsync(harness);
        Assert.Equal((DeferredReads.MostReads, DeferredReads.MostReads), (named.Count(one => one == send), named.Count(one => one == spent)));
    }

    /// <summary>
    /// At the one-minute minimum of the grace and of every wait, reads that fail and reads that answer by turns stop inside
    /// eighteen: the read that makes either count nine is the last, so nine of one kind and eight of the other, and none
    /// comes after it however long the clock runs. Its ninth that failed says nothing, so nothing is said of the send.
    /// </summary>
    [Fact]
    public async Task Eighteen_reads_at_the_most_at_the_one_minute_minimum_of_every_wait()
    {
        using var harness = Routed(options =>
        {
            options.DeferredReadGraceSeconds = DispatcherOptions.MinimumDeferredReadSeconds;
            options.DeferredReadFirstWaitSeconds = DispatcherOptions.MinimumDeferredReadSeconds;
            options.DeferredReadWaitSeconds = DispatcherOptions.MinimumDeferredReadSeconds;
        });
        var (campaign, ines) = await SceneAsync(harness);
        var send = await SendAsync(harness, campaign, ines, Noon.AddMinutes(-1));

        var made = 0;
        for (var minute = 0; minute < 3 * DeferredReads.MostReads; minute++)
        {
            if (await harness.DeferredReadAsync(Ct) == 1)
            {
                made++;
                if (made % 2 == 1)
                {
                    await FailOpenReadsAsync(harness, campaign, send);
                }
                else
                {
                    await EndOpenReadsAsync(harness, campaign, "queued", send);
                }
            }

            harness.Clock.Advance(TimeSpan.FromMinutes(1));
        }

        harness.Clock.Advance(TimeSpan.FromDays(29));
        made += await harness.DeferredReadAsync(Ct);

        Assert.Equal(2 * DeferredReads.MostReads - 1, made);
        Assert.InRange(made, 1, DeferredReads.MostInAll);
        Assert.Equal(made, (await ReadSendsAsync(harness)).Count);
        Assert.Null(await UnsentAsync(harness, send));
    }
    /// <summary>
    /// A value the candidates' read cannot parse, in any send, stored the way an edit made with the column's own check
    /// turned off would store it, skips the step's run, said by a template that carries none of the stored text, and never
    /// reaches the scan; once the row reads again, the run a minute later reads the send due.
    /// </summary>
    [Fact]
    public async Task A_row_the_candidates_read_cannot_parse_skips_the_run_and_never_the_scan()
    {
        using var harness = Routed();
        var (campaign, ines) = await SceneAsync(harness);
        var due = await SendAsync(harness, campaign, ines, Noon.AddMinutes(-20));
        var broken = await SendAsync(harness, campaign, ines, Noon.AddMinutes(-30), named: true);
        const string Stored = "{\"input\":{\"send_after\":\"a stored text of hers";
        await using (var db = harness.Open())
        {
            await db.Database.OpenConnectionAsync(Ct);
            await db.Database.ExecuteSqlRawAsync("PRAGMA ignore_check_constraints = ON;", Ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE work_items SET context_json = {Stored} WHERE public_id = {broken}", Ct);
            await db.Database.ExecuteSqlRawAsync("PRAGMA ignore_check_constraints = OFF;", Ct);
        }

        Assert.Equal(0, await harness.DeferredReadAsync(Ct));
        Assert.Empty(await ReadSendsAsync(harness));
        Assert.Contains("The scan's deferred step skipped its run, 1 in a row: it could not read the deferred sends, and reads them again at its next run; the rest of the scan continues without it", harness.Logs.Warnings);
        Assert.False(harness.Logs.Mentions("stored text"));

        await using (var db = harness.Open())
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE work_items SET context_json = {"{}"} WHERE public_id = {broken}", Ct);
        }

        Assert.Equal(0, await harness.DeferredReadAsync(Ct));
        harness.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(1, await harness.DeferredReadAsync(Ct));
        Assert.Equal([due], await ReadSendsAsync(harness));
    }

    /// <summary>
    /// Nine reads that succeeded since the moment are the most, whatever the waits, and so are nine that did not: either
    /// nine ends the reads. A send said not to have gone is read no more, and a read that ended past the overdue span stops
    /// nothing by itself; before either, the next is due a wait after the last ended, whatever it answered, the first wait
    /// after the first.
    /// </summary>
    [Fact]
    public void Nine_reads_that_succeeded_or_nine_that_failed_are_the_most_and_a_send_said_not_to_have_gone_is_read_no_more()
    {
        var options = new DispatcherOptions();
        var moment = new DateTimeOffset(Noon);
        var send = new HeldSend("cmp_x", "wi_x", "message.send", moment, Mailer, null);
        var far = moment.AddDays(29);
        var span = moment.AddSeconds(options.DeferredReadOverdueSeconds);
        var nine = DeferredReads.MostReads;

        Assert.False(DeferredReads.Due(send, null, said: false, options, moment.AddSeconds(options.DeferredReadGraceSeconds - 1)));
        Assert.True(DeferredReads.Due(send, null, said: false, options, moment.AddSeconds(options.DeferredReadGraceSeconds)));
        Assert.False(DeferredReads.Due(send, null, said: true, options, far));
        Assert.False(DeferredReads.Due(send, new MadeReads(1, 0, moment.AddHours(1)), said: false, options, moment.AddHours(2).AddSeconds(-1)));
        Assert.True(DeferredReads.Due(send, new MadeReads(1, 0, moment.AddHours(1)), said: false, options, moment.AddHours(2)));
        Assert.False(DeferredReads.Due(send, new MadeReads(0, 1, moment.AddHours(1)), said: false, options, moment.AddHours(2).AddSeconds(-1)));
        Assert.True(DeferredReads.Due(send, new MadeReads(0, 1, moment.AddHours(1)), said: false, options, moment.AddHours(2)));
        Assert.False(DeferredReads.Due(send, new MadeReads(2, 0, moment.AddHours(2)), said: false, options, moment.AddHours(26).AddSeconds(-1)));
        Assert.True(DeferredReads.Due(send, new MadeReads(2, 0, moment.AddHours(2)), said: false, options, moment.AddHours(26)));
        Assert.True(DeferredReads.Due(send, new MadeReads(nine - 1, nine - 1, moment.AddDays(1)), said: false, options, far));
        Assert.False(DeferredReads.Due(send, new MadeReads(nine, 0, moment.AddDays(1)), said: false, options, far));
        Assert.False(DeferredReads.Due(send, new MadeReads(0, nine, moment.AddDays(1)), said: false, options, far));
        Assert.True(DeferredReads.Due(send, new MadeReads(3, 0, span), said: false, options, far));
        Assert.False(DeferredReads.Due(send, new MadeReads(3, 0, span), said: true, options, far));
    }
    /// <summary>
    /// A read that failed never brings the next one sooner than its wait: after a first read that failed, the second is due
    /// the first wait after it ended, and after a later one the daily wait, exactly as after a read that succeeded.
    /// </summary>
    [Fact]
    public void A_read_that_failed_never_brings_the_next_sooner_than_its_wait()
    {
        var options = new DispatcherOptions();
        var moment = new DateTimeOffset(Noon);
        var send = new HeldSend("cmp_x", "wi_x", "message.send", moment, Mailer, null);
        var ended = moment.AddDays(2);
        var pairs = new[] { (Failed: new MadeReads(0, 1, ended), Succeeded: new MadeReads(1, 0, ended)), (Failed: new MadeReads(2, 3, ended), Succeeded: new MadeReads(3, 2, ended)) };
        foreach (var (failed, succeeded) in pairs)
        {
            var wait = failed.Ended == 1 ? options.DeferredReadFirstWaitSeconds : options.DeferredReadWaitSeconds;
            foreach (var at in new[] { ended, ended.AddSeconds(wait - 1), ended.AddSeconds(wait), ended.AddSeconds(wait + 1) })
            {
                Assert.Equal(at >= ended.AddSeconds(wait), DeferredReads.Due(send, failed, said: false, options, at));
                Assert.Equal(DeferredReads.Due(send, succeeded, said: false, options, at), DeferredReads.Due(send, failed, said: false, options, at));
            }
        }
    }

    /// <summary>A whole dispatcher's composition with a plugin that sends, cancels and reads sends, routed by default under one account.</summary>
    private static DispatchHarness Routed(Action<DispatcherOptions>? dispatcher = null)
    {
        var harness = new DispatchHarness(Noon, dispatcher);
        InboundSyncHarvestTests.Load(harness, Plugin());
        InboundSyncHarvestTests.RouteAll(harness, Mailer);
        return harness;
    }

    private static LoadedPlugin Plugin() => TestPlugins.Loaded(Mailer, operations: ["message.send", "message.cancel", SendReads.Operation]);

    /// <summary>Every operation routed to the plugin under another account than the one the sends ran under.</summary>
    private static void RouteElsewhere(DispatchHarness harness)
    {
        var plugins = harness.Resolve<PluginRegistry>().Snapshot;
        harness.Resolve<RouteRegistry>().Replace(new RouteSnapshot(
            PublicId.New(RouteSnapshot.IdPrefix),
            Noon,
            plugins.Id,
            new RouteSet(new Route(Mailer, new JsonObject(), "sha256:another"), RouteSet.Empty.Operations),
            RouteSnapshot.Empty(Noon, plugins.Id).Campaigns));
    }

    private static async Task<(Campaign Campaign, Contact Person)> SceneAsync(DispatchHarness harness, CampaignStatus status = CampaignStatus.Active)
    {
        await using var db = harness.Open();
        var campaign = WorkItemFactory.NewCampaign(status: status, now: Noon.AddDays(-60));
        var person = new Contact { PublicId = PublicId.New("cnt"), FirstName = "Ines", CreatedAt = Noon.AddDays(-60), UpdatedAt = Noon.AddDays(-60) };
        db.Campaigns.Add(campaign);
        db.Contacts.Add(person);
        await db.SaveChangesAsync(Ct);
        return (campaign, person);
    }

    /// <summary>
    /// A send of the campaign to the person that the provider took with its reference, three days before its moment, for
    /// <paramref name="moment"/> — named by its result, or by its arguments' <c>send_after</c> where
    /// <paramref name="named"/> — or for none, saying it can cancel it unless <paramref name="cancelable"/> is false; handed
    /// over at <paramref name="taken"/> where given.
    /// </summary>
    internal static async Task<string> SendAsync(
        DispatchHarness harness, Campaign campaign, Contact contact, DateTime? moment, string? follows = null, bool named = false, bool cancelable = true, DateTime? taken = null)
    {
        await using var db = harness.Open();
        db.Attach(campaign);
        db.Attach(contact);
        var handed = taken ?? (moment ?? Noon).AddDays(-3);
        var arguments = new JsonObject { ["channel"] = "email" };
        if (named && moment is { } after)
        {
            arguments["send_after"] = Instant(after);
        }

        if (follows is not null)
        {
            arguments["follows"] = follows;
        }

        var reference = "send-" + PublicId.New("ref")[^8..];
        var send = WorkItemFactory.NewProviderOp(campaign, "message.send", handed);
        send.Contact = contact;
        send.Status = WorkItemStatus.Succeeded;
        send.FinishedAt = handed;
        send.Context = new JsonObject { [WorkItemService.InputKey] = arguments };
        send.Result = new JsonObject
        {
            ["items"] = new JsonArray(new JsonObject
            {
                ["status"] = "accepted",
                ["reference"] = reference,
                ["accepted_for"] = named || moment is null ? null : Instant(moment.Value),
                ["cancelable"] = cancelable,
            }),
        };
        var attempt = WorkItemFactory.NewAttempt(send, 1, AttemptStatus.Succeeded, handed);
        attempt.FinishedAt = handed;
        attempt.Provenance = new AttemptProvenanceDto(
            Mailer, "1.0.0", "sha256:0", PluginProtocol.CurrentVersion, PluginProtocol.OperationContractVersion, "message.send", 1, null, null,
            RouteScope.GlobalDefault, InboundSyncHarvestTests.RoutedIdentity,
            ExternalIdsReturned: new Dictionary<string, string>(StringComparer.Ordinal) { ["send"] = reference });
        db.WorkItems.Add(send);
        db.Attempts.Add(attempt);
        await db.SaveChangesAsync(Ct);
        return send.PublicId;
    }

    /// <summary>
    /// A read of <paramref name="send"/>, or another <paramref name="operation"/> naming it, made by <paramref name="by"/>:
    /// open where <paramref name="ended"/> is null; otherwise ended then, succeeded with <paramref name="state"/>, or failed
    /// with <paramref name="code"/> where one is given.
    /// </summary>
    private static async Task ReadAsync(
        DispatchHarness harness,
        Campaign campaign,
        string send,
        DateTime? ended,
        string? state = "queued",
        string? code = null,
        ActorType by = ActorType.System,
        bool cancelable = true,
        string operation = SendReads.Operation)
    {
        await using var db = harness.Open();
        db.Attach(campaign);
        var item = WorkItemFactory.NewProviderOp(campaign, operation, (ended ?? Noon).AddMinutes(-1));
        item.CreatedByType = by;
        item.CreatedById = by == ActorType.System ? "dispatcher" : by == ActorType.Role ? "manager" : null;
        item.Context = new JsonObject { [WorkItemService.InputKey] = new JsonObject { ["send"] = send } };
        if (ended is { } at)
        {
            item.FinishedAt = at;
            if (code is not null)
            {
                item.Status = WorkItemStatus.Failed;
                item.LastError = new AttemptErrorDto(code, "The provider answered.", Retriable: false);
            }
            else
            {
                item.Status = WorkItemStatus.Succeeded;
                item.Result = operation == SendReads.Operation ? Answer(state!, cancelable) : new JsonObject();
            }
        }

        db.WorkItems.Add(item);
        await db.SaveChangesAsync(Ct);
    }

    /// <summary>
    /// Ends the campaign's open reads the runtime made, of the sends named or of all, now, with <paramref name="state"/>,
    /// the way the recorder ends a read: what the read says of its send is decided in the change set that ends it.
    /// </summary>
    private static async Task EndOpenReadsAsync(DispatchHarness harness, Campaign campaign, string state, params string[] sends)
    {
        await using var db = harness.Open();
        var facts = new SentFacts(new JournalWriter(harness.Clock), TestOptions.Settings(harness.Options));
        var open = await db.WorkItems
            .Where(item => item.CampaignId == campaign.Id && item.Operation == SendReads.Operation && item.CreatedByType == ActorType.System && item.Status == WorkItemStatus.Created)
            .ToListAsync(Ct);
        foreach (var read in open.Where(read => sends.Length == 0 || sends.Contains((string?)read.Context["input"]!["send"])))
        {
            read.Status = WorkItemStatus.Succeeded;
            read.FinishedAt = harness.Clock.GetUtcNow().UtcDateTime;
            read.Result = Answer(state, cancelable: true);
            var named = (string)read.Context["input"]!["send"]!;
            var made = await db.WorkItems.AsNoTracking().SingleAsync(item => item.PublicId == named, Ct);
            var input = new JsonObject { ["send"] = new JsonObject { ["work_item"] = named, ["reference"] = made.Result!["items"]![0]!["reference"]!.DeepClone() } };
            await facts.SayUnsentAsync(db, read, Mailer, input, read.Result, Ct);
        }

        await db.SaveChangesAsync(Ct);
    }

    /// <summary>Ends the campaign's open reads the runtime made of the sends named, now, failed, as a provider that is down fails them.</summary>
    private static async Task FailOpenReadsAsync(DispatchHarness harness, Campaign campaign, params string[] sends)
    {
        await using var db = harness.Open();
        var open = await db.WorkItems
            .Where(item => item.CampaignId == campaign.Id && item.Operation == SendReads.Operation && item.CreatedByType == ActorType.System && item.Status == WorkItemStatus.Created)
            .ToListAsync(Ct);
        foreach (var read in open.Where(read => sends.Contains((string?)read.Context["input"]!["send"])))
        {
            read.Status = WorkItemStatus.Failed;
            read.FinishedAt = harness.Clock.GetUtcNow().UtcDateTime;
            read.LastError = new AttemptErrorDto("provider_unavailable", "The provider answered.", Retriable: true);
        }

        await db.SaveChangesAsync(Ct);
    }

    /// <summary>The body of the line that says <paramref name="send"/> did not go, or null where none says it.</summary>
    private static async Task<JsonNode?> UnsentAsync(DispatchHarness harness, string send)
    {
        await using var db = harness.Open();
        var lines = await db.Journal.AsNoTracking().Where(entry => entry.Kind == JournalKinds.MessageUnsentObserved && entry.Key == send).ToListAsync(Ct);
        return lines.Count == 0 ? null : Assert.Single(lines).New;
    }

    /// <summary>
    /// Runs the step a minute before each read is due and at the moment it is, ending each read a minute after it was made,
    /// still queued, and telling <paramref name="ended"/> how many have ended, until two days pass with no read: answers
    /// when each read was made.
    /// </summary>
    private static async Task<List<DateTime>> DriveAsync(DispatchHarness harness, Campaign campaign, string send, Action<int>? ended = null)
    {
        var made = new List<DateTime>();
        Assert.Equal(1, await harness.DeferredReadAsync(Ct));
        made.Add(harness.Clock.GetUtcNow().UtcDateTime);
        while (true)
        {
            harness.Clock.Advance(TimeSpan.FromMinutes(1));
            await EndOpenReadsAsync(harness, campaign, "queued", send);
            ended?.Invoke(made.Count);
            var wait = made.Count == 1 ? TimeSpan.FromHours(1) : TimeSpan.FromDays(1);
            harness.Clock.Advance(wait - TimeSpan.FromMinutes(1));
            Assert.Equal(0, await harness.DeferredReadAsync(Ct));
            harness.Clock.Advance(TimeSpan.FromMinutes(1));
            var queued = await harness.DeferredReadAsync(Ct);
            if (queued == 0)
            {
                harness.Clock.Advance(TimeSpan.FromDays(2));
                Assert.Equal(0, await harness.DeferredReadAsync(Ct));
                return made;
            }

            Assert.Equal(1, queued);
            made.Add(harness.Clock.GetUtcNow().UtcDateTime);
        }
    }

    /// <summary>The sends the system's reads name, in the order the reads were made.</summary>
    private static async Task<List<string>> ReadSendsAsync(DispatchHarness harness)
    {
        await using var db = harness.Open();
        return [.. (await db.WorkItems.AsNoTracking()
            .Where(item => item.Operation == SendReads.Operation && item.CreatedByType == ActorType.System)
            .OrderBy(item => item.Id)
            .Select(item => item.Context)
            .ToListAsync(Ct)).Select(context => (string)context["input"]!["send"]!)];
    }

    /// <summary>A read's answer of <paramref name="state"/>, every member the operation publishes present.</summary>
    internal static JsonObject Answer(string state, bool cancelable) => new()
    {
        ["state"] = state,
        ["channel"] = "email",
        ["cancelable"] = cancelable,
        ["accepted_for"] = null,
        ["sent_at"] = null,
        ["message_id"] = null,
        ["rendered_subject"] = null,
        ["rendered_body"] = null,
        ["failure_reason"] = null,
        ["failure_code"] = null,
        ["cancelled_at"] = null,
    };

    private static string Instant(DateTime moment) => InboundSync.Instant(new DateTimeOffset(moment));
}
