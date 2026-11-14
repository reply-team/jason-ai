using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Ids;
using Jason.Runtime.Approvals;
using Jason.Runtime.Configuration;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Replies;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Dispatch;

/// <summary>
/// The daily ceiling of automatic sends. Where the automatic mode would release a send, the claim first counts the sends
/// it released from the campaign since 00:00Z; at <c>Sending:AutomaticMaxPerDay</c> the send parks for the person as
/// <c>daily_ceiling</c>, even where a standing approval would have covered it, the preview carrying the limit, the count
/// and the moment the count began. Nothing changes where no mode is automatic.
/// </summary>
public partial class ClaimerTests
{
    /// <summary>The 00:00Z the claim's day begins at; a property, since a field of another part of the class may not be set yet.</summary>
    private static DateTime Midnight => Noon.Date;

    /// <summary>
    /// Under the ceiling the send is released as before; at it, the send parks for the person, pending, with the reason
    /// <c>daily_ceiling</c> on the row and on the line that asks, and the three numbers on the preview, and nothing is
    /// released. The ceiling is read from the settings at the scan, the default a hundred.
    /// </summary>
    [Theory]
    [InlineData(3, 2, false)]
    // Critical: the automatic mode: at the daily ceiling a send waits for the person.
    [InlineData(3, 3, true, Traits = ["Category", "Critical", "CriticalFamily", nameof(Family.S2)])]
    [InlineData(3, 4, true)]
    [InlineData(null, 99, false)]
    [InlineData(null, 100, true)]
    public async Task Ceiling_UnderItASendIsReleased_AtItTheSendParks(int? limit, int releasedToday, bool parks)
    {
        using var harness = ModeHarness();
        if (limit is { } set)
        {
            harness.Sending.CurrentValue = new SendingOptions { AutomaticMaxPerDay = set };
        }

        using var database = new TestDatabase();
        await using var db = database.Open();
        var reading = await ReadingAsync(db);
        await ModeAsync(db, ColdMode, "automatic");
        await ReleasesAsync(db, reading.Campaign, releasedToday, Noon.AddHours(-1));
        var send = await ComposeAsync(db, reading, waitOn: null);
        var pass = await CriticPassAsync(db, reading, send.Id, send.ReviewHash);

        var claimed = await ClaimSettledAsync(harness, db);

        await using var fresh = database.Open();
        var approval = await fresh.Approvals.AsNoTracking().SingleAsync(a => a.WorkItem!.PublicId == send.Id, Ct);
        var applied = await fresh.Journal.CountAsync(e => e.Kind == "approval_automatic_applied", Ct);
        if (!parks)
        {
            Assert.Equal(send.Id, Assert.Single(claimed).WorkItemPublicId);
            Assert.Equal((ApprovalStatus.Approved, "automatic_mode", 1), (approval.Status, approval.Reason, applied));
            Assert.False(approval.Preview.ContainsKey("daily_ceiling"));
            return;
        }

        Assert.Empty(claimed);
        Assert.Equal((ApprovalStatus.Pending, "daily_ceiling", 0), (approval.Status, approval.Reason, applied));
        Assert.Equal(
            new JsonObject { ["limit"] = limit ?? 100, ["released"] = releasedToday, ["since"] = "2026-09-14T00:00:00Z" }.ToJsonString(),
            approval.Preview["daily_ceiling"]!.ToJsonString());
        Assert.Equal(
            new JsonObject { ["mode"] = "automatic", ["source"] = "preference", ["temperature"] = "cold", ["review"] = pass }.ToJsonString(),
            approval.Preview["send_mode"]!.ToJsonString());
        var asked = await fresh.Journal.AsNoTracking().SingleAsync(e => e.Kind == JournalKinds.ApprovalRequested, Ct);
        Assert.Equal("daily_ceiling", asked.Reason);
        Assert.Equal(WorkItemStatus.AwaitingApproval, (await fresh.WorkItems.AsNoTracking().SingleAsync(w => w.PublicId == send.Id, Ct)).Status);
    }

    /// <summary>
    /// The day is the UTC day: a release decided the last moment before 00:00Z is yesterday's and counts for nothing today,
    /// and one decided at 00:00Z exactly is today's.
    /// </summary>
    [Theory]
    [InlineData("yesterday's last moment", false)]
    [InlineData("today's first moment", true)]
    public async Task Ceiling_CountsTheUtcDay(string when, bool parks)
    {
        using var harness = ModeHarness();
        harness.Sending.CurrentValue = new SendingOptions { AutomaticMaxPerDay = 1 };
        using var database = new TestDatabase();
        await using var db = database.Open();
        var reading = await ReadingAsync(db);
        await ModeAsync(db, ColdMode, "automatic");
        await ReleasesAsync(db, reading.Campaign, 1, when == "today's first moment" ? Midnight : Midnight.AddTicks(-1));
        var send = await ComposeAsync(db, reading, waitOn: null);
        await CriticPassAsync(db, reading, send.Id, send.ReviewHash);

        var claimed = await ClaimSettledAsync(harness, db);

        await using var fresh = database.Open();
        var approval = await fresh.Approvals.AsNoTracking().SingleAsync(a => a.WorkItem!.PublicId == send.Id, Ct);
        Assert.Equal(parks ? "daily_ceiling" : "automatic_mode", approval.Reason);
        Assert.Equal(parks ? 0 : 1, claimed.Count);
    }

    /// <summary>
    /// The count is of the campaign's own releases, whatever became of each, and of nothing else: another campaign's releases,
    /// and this campaign's rows under any other reason — a person's approval of an earlier ceiling's park among them — leave
    /// the send released; this campaign's releases superseded at a retry or cancelled with their work still count.
    /// </summary>
    [Theory]
    [InlineData("another campaign's releases", false)]
    [InlineData("this campaign's rows of other reasons", false)]
    [InlineData("this campaign's superseded and cancelled releases", true)]
    public async Task Ceiling_CountsThisCampaignsReleasesAndNothingElse(string rows, bool parks)
    {
        using var harness = ModeHarness();
        harness.Sending.CurrentValue = new SendingOptions { AutomaticMaxPerDay = 2 };
        using var database = new TestDatabase();
        await using var db = database.Open();
        var reading = await ReadingAsync(db);
        await ModeAsync(db, ColdMode, "automatic");
        switch (rows)
        {
            case "another campaign's releases":
                var other = WorkItemFactory.NewCampaign(name: "Other", now: Noon);
                db.Campaigns.Add(other);
                await db.SaveChangesAsync(Ct);
                await ReleasesAsync(db, other, 5, Noon.AddHours(-1));
                break;
            case "this campaign's rows of other reasons":
                foreach (var reason in new[] { "standing_approval", "approval_required", "input_changed", "daily_ceiling", "doubted", "not_reviewed" })
                {
                    await ReleasesAsync(db, reading.Campaign, 1, Noon.AddHours(-1), reason);
                }

                break;
            default:
                await ReleasesAsync(db, reading.Campaign, 1, Noon.AddHours(-1), status: ApprovalStatus.Superseded);
                await ReleasesAsync(db, reading.Campaign, 1, Noon.AddHours(-1), status: ApprovalStatus.Cancelled);
                break;
        }

        var send = await ComposeAsync(db, reading, waitOn: null);
        await CriticPassAsync(db, reading, send.Id, send.ReviewHash);

        var claimed = await ClaimSettledAsync(harness, db);

        await using var fresh = database.Open();
        var approval = await fresh.Approvals.AsNoTracking().SingleAsync(a => a.WorkItem!.PublicId == send.Id, Ct);
        Assert.Equal(parks ? "daily_ceiling" : "automatic_mode", approval.Reason);
        Assert.Equal(parks ? 0 : 1, claimed.Count);
    }

    /// <summary>
    /// While a mode is automatic the ceiling does not slip: at it, a draft a standing approval covers parks
    /// <c>daily_ceiling</c> for the person, the standing approval's answer saying so, and nothing is let go under it. Under
    /// the ceiling the mode releases the same draft itself.
    /// </summary>
    [Theory]
    [InlineData(0, "automatic_mode")]
    // Critical: the automatic mode: a standing approval never lets a send past the daily ceiling.
    [InlineData(1, "daily_ceiling", Traits = ["Category", "Critical", "CriticalFamily", nameof(Family.S2)])]
    public async Task Ceiling_AStandingApprovalDoesNotOverrideIt_WhileAModeIsAutomatic(int releasedToday, string outcome)
    {
        using var harness = ModeHarness();
        harness.Sending.CurrentValue = new SendingOptions { AutomaticMaxPerDay = 1 };
        using var database = new TestDatabase();
        await using var db = database.Open();
        var reading = await ReadingAsync(db);
        await ModeAsync(db, ColdMode, "automatic");
        await ModeAsync(db, WarmMode, "automatic");
        var verdict = await NatureVerdictAsync(db, reading, ReplyNature.Person);
        await StandAsync(db, reading.Campaign);
        await ReleasesAsync(db, reading.Campaign, releasedToday, Noon.AddHours(-1));
        var item = Drafted(db, reading, verdict);
        await db.SaveChangesAsync(Ct);
        await CriticPassAsync(db, reading, item.PublicId, ReviewSubject.Hash(item));

        var claimed = await ClaimSettledAsync(harness, db);

        await using var fresh = database.Open();
        var approval = await fresh.Approvals.AsNoTracking().SingleAsync(a => a.WorkItemId == item.Id, Ct);
        Assert.Equal(outcome, approval.Reason);
        Assert.Equal(0, await fresh.Journal.CountAsync(e => e.Kind == JournalKinds.ApprovalStandingApplied, Ct));
        if (outcome == "daily_ceiling")
        {
            Assert.Empty(claimed);
            Assert.Equal(ApprovalStatus.Pending, approval.Status);
            Assert.Equal(StandingApprovals.Why.DailyCeiling, (string?)approval.Preview["standing_approval"]!["why"]);
            Assert.Equal(1, (int?)approval.Preview["daily_ceiling"]!["released"]);
        }
        else
        {
            Assert.Single(claimed);
        }
    }

    /// <summary>
    /// An installation whose modes are not automatic loses nothing to the ceiling: however many automatic releases its
    /// campaign has today, a send nobody covers parks as it always did and one a standing approval covers goes under it.
    /// </summary>
    [Theory]
    [InlineData(false, "approval_required")]
    [InlineData(true, "standing_approval")]
    public async Task Ceiling_AnInstallationWhoseModesAreNotAutomatic_IsUnaffected(bool standing, string outcome)
    {
        using var harness = ModeHarness();
        harness.Sending.CurrentValue = new SendingOptions { AutomaticMaxPerDay = 1 };
        using var database = new TestDatabase();
        await using var db = database.Open();
        var reading = await ReadingAsync(db);
        var verdict = await NatureVerdictAsync(db, reading, ReplyNature.Person);
        if (standing)
        {
            await StandAsync(db, reading.Campaign);
        }

        await ReleasesAsync(db, reading.Campaign, 5, Noon.AddHours(-1));
        var item = Drafted(db, reading, verdict);
        await db.SaveChangesAsync(Ct);
        await CriticPassAsync(db, reading, item.PublicId, ReviewSubject.Hash(item));

        var claimed = await ClaimSettledAsync(harness, db);

        await using var fresh = database.Open();
        var approval = await fresh.Approvals.AsNoTracking().SingleAsync(a => a.WorkItemId == item.Id, Ct);
        Assert.Equal(outcome, approval.Reason);
        Assert.Equal(standing ? 1 : 0, claimed.Count);
        Assert.False(approval.Preview.ContainsKey("daily_ceiling"));
    }

    /// <summary>
    /// The ceiling is asked only where the mode would release: a send the mode parks for another reason keeps that reason,
    /// at the ceiling or past it, so the ceiling never hides a doubt or a missing review.
    /// </summary>
    [Theory]
    [InlineData("doubted")]
    [InlineData("not_reviewed")]
    public async Task Ceiling_IsAskedOnlyWhereTheModeWouldRelease(string parks)
    {
        using var harness = ModeHarness();
        harness.Sending.CurrentValue = new SendingOptions { AutomaticMaxPerDay = 1 };
        using var database = new TestDatabase();
        await using var db = database.Open();
        var reading = await ReadingAsync(db);
        await ModeAsync(db, ColdMode, "automatic");
        await ReleasesAsync(db, reading.Campaign, 3, Noon.AddHours(-1));
        var send = await ComposeAsync(db, reading, waitOn: null, context: parks == "doubted" ? Asking("sensitive_topic") : null);
        if (parks == "doubted")
        {
            await CriticPassAsync(db, reading, send.Id, send.ReviewHash);
        }

        Assert.Empty(await ClaimSettledAsync(harness, db));

        await using var fresh = database.Open();
        var approval = await fresh.Approvals.AsNoTracking().SingleAsync(a => a.WorkItem!.PublicId == send.Id, Ct);
        Assert.Equal(parks, approval.Reason);
        Assert.False(approval.Preview.ContainsKey("daily_ceiling"));
    }

    /// <summary>The card's sentence for a send parked at the ceiling, as a person reads it.</summary>
    [Fact]
    public void Ceiling_TheParksSentence_IsTheOneAPersonReads() =>
        Assert.Equal(
            "This campaign has reached its limit of automatic sends for today, so this one waits for you to approve it.",
            SendModeReasons.Sentence("daily_ceiling"));

    /// <summary>
    /// <paramref name="count"/> rows of <paramref name="campaign"/> decided at <paramref name="at"/>, released by the automatic
    /// mode unless another reason is named, on a finished send of the campaign's that the claim never reads again.
    /// </summary>
    private static async Task ReleasesAsync(
        JasonDbContext db,
        Campaign campaign,
        int count,
        DateTime at,
        string reason = ApprovalGate.AutomaticMode,
        ApprovalStatus status = ApprovalStatus.Approved)
    {
        if (count == 0)
        {
            return;
        }

        var sent = WorkItemFactory.NewProviderOp(campaign, "message.send", Noon);
        sent.Status = WorkItemStatus.Succeeded;
        sent.FinishedAt = Noon;
        db.WorkItems.Add(sent);
        await db.SaveChangesAsync(Ct);
        for (var n = 0; n < count; n++)
        {
            db.Approvals.Add(new Approval
            {
                PublicId = PublicId.New(ApprovalGate.IdPrefix),
                WorkItemId = sent.Id,
                CampaignId = campaign.Id,
                Operation = "message.send",
                OperationVersion = 1,
                Subject = [],
                SubjectHash = "sha256:" + new string('d', 64),
                Preview = [],
                PluginId = Provider,
                RouteScope = RouteScope.GlobalDefault,
                PluginSnapshotId = "pls_01K52JR0000000000000000001",
                RoutingSnapshotId = "rts_01K52JR0000000000000000001",
                Status = status,
                Reason = reason,
                RequestedAt = at,
                DecidedAt = at,
                DecidedByType = ActorType.Human,
                DecidedById = "Marta",
            });
        }

        await db.SaveChangesAsync(Ct);
    }
}
