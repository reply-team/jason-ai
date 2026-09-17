using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Ids;
using Jason.Runtime.Approvals;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Reports;

/// <summary>
/// Admission is inert. A report says what somebody did elsewhere; it does not advance an item, spend an attempt,
/// answer a decision or teach the runtime a provider's identifier. The claim is made at the level where it can
/// actually be settled — the rows — by reading every managed table before and after and comparing the two.
/// </summary>
public class ReportNonInterferenceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Admitting_a_report_changes_no_managed_record()
    {
        using var database = new TestDatabase();
        await using var seed = database.Open();
        var (campaign, contact, item) = await ReportedWorld.WriteAsync(seed, Ct);
        await ManagedWorkAsync(seed, campaign, contact, item, Ct);

        await using var db = database.Open();
        var before = await DatabaseCensus.TakeAsync(db, Ct);
        var reportsBefore = await db.Reports.CountAsync(Ct);
        var journalBefore = await db.Journal.CountAsync(Ct);

        var submitted = ReportedWorld.Submission();
        submitted["campaign_id"] = campaign.PublicId;
        submitted["contact_id"] = contact.PublicId;
        submitted["work_item_id"] = item.PublicId;
        submitted["operation"] = "campaign.enroll";

        var admitted = await ReportedWorld.Service(db).SubmitAsync(submitted, Ct);
        Assert.Equal(ReportDedupOutcome.Admitted, admitted.Dedup.Outcome);

        var after = await DatabaseCensus.TakeAsync(db, Ct);

        // Named first, so a failure says which table moved instead of printing the whole database twice.
        var changed = before.Keys.Where(table => !string.Equals(before[table], after[table], StringComparison.Ordinal)).ToList();
        Assert.True(
            changed.Count == 0,
            $"Admitting a report changed managed tables: {string.Join(", ", changed)}.");
        Assert.Equal(before, after);

        // The two rows admission is for: the assertion, and one line of the chronicle saying it was made.
        Assert.Equal(reportsBefore + 1, await db.Reports.CountAsync(Ct));
        var journal = await db.Journal.AsNoTracking().OrderBy(entry => entry.Id).ToListAsync(Ct);
        Assert.Equal(journalBefore + 1, journal.Count);
        Assert.Equal(JournalKinds.ExternalEffectReported, journal[^1].Kind);
        Assert.Equal(admitted.Id, (string?)journal[^1].New!["id"]);
    }

    /// <summary>
    /// A pin is something this runtime established for itself, through an attempt it ran and can name. An
    /// identifier a reporter mentions is somebody else's word about a provider nobody here talked to, and
    /// promoting it would put a claim the runtime cannot stand behind where its own findings live.
    /// </summary>
    [Fact]
    public async Task A_reported_provider_identifier_never_becomes_a_pin()
    {
        using var database = new TestDatabase();
        await using var seed = database.Open();
        var (campaign, contact, item) = await ReportedWorld.WriteAsync(seed, Ct);
        await ManagedWorkAsync(seed, campaign, contact, item, Ct);

        await using var db = database.Open();
        var before = await DatabaseCensus.TakeAsync(db, Ct);

        var submitted = ReportedWorld.Submission();
        submitted["campaign_id"] = campaign.PublicId;
        submitted["contact_id"] = contact.PublicId;
        submitted["work_item_id"] = item.PublicId;
        submitted["external_ids"] = new JsonArray(new JsonObject { ["kind"] = "message", ["value"] = "m-1" });

        var admitted = await ReportedWorld.Service(db).SubmitAsync(submitted, Ct);

        var after = await DatabaseCensus.TakeAsync(db, Ct);
        Assert.Equal(before["external_ids"], after["external_ids"]);
        Assert.Equal(before, after);

        // The pin the seeded attempt established is still the only one, and still says what it said.
        var pin = Assert.Single(await db.ExternalIds.AsNoTracking().ToListAsync(Ct));
        Assert.Equal("c-1", pin.Value);
        Assert.DoesNotContain(await db.ExternalIds.AsNoTracking().ToListAsync(Ct), p => p.Value == "m-1");

        // Where the reported identifier does live: inside the assertion, as part of what was said.
        var stored = await db.Reports.AsNoTracking().SingleAsync(Ct);
        Assert.Equal(admitted.Id, stored.PublicId);
        Assert.Equal("m-1", (string?)stored.Assertion["external_ids"]![0]!["value"]);
    }

    /// <summary>
    /// Everything the runtime itself writes about one piece of work: the attempt that ran, the decision the
    /// re-claim is waiting for, and the identifier that attempt learned. A census over an empty database would
    /// prove nothing.
    /// </summary>
    private static async Task ManagedWorkAsync(
        JasonDbContext db,
        Campaign campaign,
        Contact contact,
        WorkItem item,
        CancellationToken ct)
    {
        var attempt = WorkItemFactory.NewAttempt(item, 1, AttemptStatus.Failed, ReportedWorld.Noon);
        attempt.FinishedAt = ReportedWorld.Noon;
        attempt.Error = new AttemptErrorDto("provider_unavailable", "the provider did not answer", true);
        item.Status = WorkItemStatus.AwaitingApproval;
        item.AttemptCount = 1;
        item.LastError = attempt.Error;

        db.Approvals.Add(new Approval
        {
            PublicId = PublicId.New(ApprovalGate.IdPrefix),
            WorkItem = item,
            CampaignId = campaign.Id,
            Operation = "campaign.enroll",
            OperationVersion = 1,
            Subject = new JsonObject { ["operation"] = "campaign.enroll", ["work_item"] = item.PublicId },
            SubjectHash = "sha256:subject",
            Preview = new JsonObject { ["intent"] = "Put one person into a campaign at the provider." },
            PluginId = "stand-in-provider",
            BindingIdentity = "sha256:account",
            RouteScope = RouteScope.GlobalDefault,
            PluginSnapshotId = "snp_one",
            RoutingSnapshotId = "rts_one",
            Reason = ApprovalGate.ApprovalRequired,
            RequestedAt = ReportedWorld.Noon,
        });

        await db.SaveChangesAsync(ct);

        db.ExternalIds.Add(new ExternalId
        {
            ContactId = contact.Id,
            PluginId = "stand-in-provider",
            Kind = "contact",
            Value = "c-1",
            RecordedAt = ReportedWorld.Noon,
            RecordedByAttemptId = attempt.PublicId,
        });

        await db.SaveChangesAsync(ct);
    }
}
