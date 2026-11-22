using Jason.Contracts.Api;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Routing;
using Jason.Runtime.Tests.Dispatch;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Routing;

/// <summary>
/// One line never stops a campaign's respond step. A line whose responder cannot be written still holds the person and
/// takes back what the provider holds for them, once; a take-back the database refuses costs neither the hold nor the
/// line, and is logged under an event of its own; and a hold the database refuses is logged once while the watermark still
/// moves past the line.
/// </summary>
public partial class HeldSendsCancelTests
{
    /// <summary>
    /// A thread whose identifier would make the responder's context larger than any context may be: the line's write is
    /// refused and rolled back, and the transaction that passes it holds the person again and takes back their held send,
    /// exactly once, with the watermark past the line.
    /// </summary>
    [Fact]
    public async Task A_line_whose_responder_cannot_be_written_still_holds_the_person_and_takes_back_once()
    {
        using var harness = Routed();
        var (campaign, person) = await SceneAsync(harness);
        var send = await SentAsync(harness, campaign, person, Later);
        var line = await ReplyLineAsync(harness, campaign, person, new string('t', 300_000), Noon.AddMinutes(-5));

        Assert.Equal(0, await harness.RespondAsync(Ct));

        await using var db = harness.Open();
        Assert.Equal(MembershipState.Paused, await StateOfAsync(db, campaign, person));
        var held = Assert.Single(await db.Journal.AsNoTracking().Where(e => e.Kind == JournalKinds.ContactsPaused).ToListAsync(Ct));
        Assert.Equal(line, (string?)held.New!["line"]);
        Assert.Equal(await LineRowAsync(db, line), await db.Campaigns.Where(c => c.Id == campaign).Select(c => c.ResponderWatermark).SingleAsync(Ct));
        var cancel = await db.WorkItems.AsNoTracking().SingleAsync(w => w.Operation == HeldSends.Cancel, Ct);
        Assert.Equal(send, (string?)cancel.Context["input"]!["send"]);
        Assert.False(await db.WorkItems.AnyAsync(w => w.Role == Responder.Role, Ct));
    }

    /// <summary>
    /// A take-back the database refuses — here a trigger that refuses every cancel, standing in for any constraint — is
    /// absorbed: the person is held, the reply still wakes its responder, the watermark moves past the line, and the log
    /// says once, under the take-back's own event, by the campaign and the code, that the letter was not taken back.
    /// </summary>
    [Fact]
    public async Task A_take_back_the_database_refuses_costs_neither_the_hold_nor_the_line()
    {
        using var harness = Routed();
        var (campaign, person) = await SceneAsync(harness);
        await SentAsync(harness, campaign, person, Later);
        var line = await ReplyLineAsync(harness, campaign, person, "th-1", Noon.AddMinutes(-5));
        await RefuseAsync(harness, "CREATE TRIGGER refuse_cancel BEFORE INSERT ON work_items WHEN NEW.operation = 'message.cancel' BEGIN SELECT RAISE(ABORT, 'refused'); END");

        Assert.Equal(1, await harness.RespondAsync(Ct));

        await using (var db = harness.Open())
        {
            Assert.Equal(MembershipState.Paused, await StateOfAsync(db, campaign, person));
            Assert.Equal(1, await db.Journal.CountAsync(e => e.Kind == JournalKinds.ContactsPaused, Ct));
            Assert.Equal(await LineRowAsync(db, line), await db.Campaigns.Where(c => c.Id == campaign).Select(c => c.ResponderWatermark).SingleAsync(Ct));
            Assert.False(await db.WorkItems.AnyAsync(w => w.Operation == HeldSends.Cancel, Ct));
            Assert.True(await db.WorkItems.AnyAsync(w => w.Role == Responder.Role, Ct));
        }

        var said = Assert.Single(harness.Logs.All, text => text.StartsWith("A held send of campaign ", StringComparison.Ordinal));
        Assert.EndsWith("could not be taken back: write_refused", said, StringComparison.Ordinal);
        Assert.DoesNotContain(harness.Logs.All, text => text.StartsWith("The respond step passed over", StringComparison.Ordinal));
    }

    /// <summary>
    /// A hold the database refuses — here a trigger that refuses its line — fails the line's transaction and then the one
    /// that passes it; that one still moves the watermark past the line, so the next scan reads the lines after it, and the
    /// log says once that the line could not be held.
    /// </summary>
    [Fact]
    public async Task A_hold_the_database_refuses_is_said_once_and_the_watermark_still_moves()
    {
        using var harness = Routed();
        var (campaign, person) = await SceneAsync(harness);
        var line = await ReplyLineAsync(harness, campaign, person, "th-1", Noon.AddMinutes(-5));
        await RefuseAsync(harness, $"CREATE TRIGGER refuse_hold BEFORE INSERT ON journal WHEN NEW.kind = '{JournalKinds.ContactsPaused}' BEGIN SELECT RAISE(ABORT, 'refused'); END");

        Assert.Equal(0, await harness.RespondAsync(Ct));

        await using (var db = harness.Open())
        {
            Assert.Equal(MembershipState.Enrolled, await StateOfAsync(db, campaign, person));
            Assert.Equal(await LineRowAsync(db, line), await db.Campaigns.Where(c => c.Id == campaign).Select(c => c.ResponderWatermark).SingleAsync(Ct));
        }

        Assert.Single(harness.Logs.All, text => text.StartsWith("The respond step could not hold the line a reply of campaign ", StringComparison.Ordinal));
        Assert.DoesNotContain(harness.Logs.All, text => text.StartsWith("The respond step for campaign ", StringComparison.Ordinal));
        Assert.Equal(0, await harness.RespondAsync(Ct));
    }

    private static async Task RefuseAsync(DispatchHarness harness, string trigger)
    {
        await using var db = harness.Open();
        await db.Database.ExecuteSqlRawAsync(trigger, Ct);
    }

    private static Task<int> LineRowAsync(JasonDbContext db, string line) =>
        db.Journal.Where(e => e.PublicId == line).Select(e => e.Id).SingleAsync(Ct);
}
