using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Ids;
using Jason.Contracts.Operations;
using Jason.Runtime.Campaigns;
using Jason.Runtime.Contacts;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.WorkItems;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Reports;

/// <summary>
/// Admission: an effect performed outside Jason, written down as the assertion it is. Nothing here approves,
/// routes, retries, verifies or performs anything — the only rows it writes are the report and one line of the
/// chronicle naming it, and nothing inside the runtime calls these verbs.
/// </summary>
public sealed class ReportService(JasonDbContext db, JournalWriter journal, TimeProvider clock)
{
    public const string IdPrefix = "rpt";

    /// <summary>Caps on what one submission may carry. Constants rather than settings: nothing here is an
    /// installation's choice, and a limit an operator can raise is a limit somebody will raise.</summary>
    public const int MaxTextLength = 200;

    public const int MaxProseLength = 2000;

    public const int MaxEvidenceBytes = 64 * 1024;

    public const int MaxListEntries = 20;

    /// <summary>How many reports a work item's own view carries before it points at the listing instead.</summary>
    public const int MaxReportsOnWorkItem = 20;

    /// <summary>
    /// Admits one report, or answers with the one it repeats. A repeat is a success: a caller retrying after a
    /// dropped connection must not have to tell an error from an echo.
    /// </summary>
    public async Task<ReportDto> SubmitAsync(JsonObject request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var submission = ReportSubmission.Parse(request);
        await ActorVerification.VerifyAsync(db, submission.Reporter, cancellationToken).ConfigureAwait(false);

        var campaign = submission.CampaignId is null
            ? null
            : await CampaignService.LoadAsync(db, submission.CampaignId, cancellationToken).ConfigureAwait(false);
        var contact = submission.ContactId is null
            ? null
            : await ContactService.LoadAsync(db, submission.ContactId, cancellationToken).ConfigureAwait(false);
        var item = submission.WorkItemId is null
            ? null
            : await WorkItemService.LoadAsync(db, submission.WorkItemId, cancellationToken).ConfigureAwait(false);

        Consistent(campaign, contact, item);

        if (await Repeat(submission, cancellationToken).ConfigureAwait(false) is { } held)
        {
            return ReportMapper.ToDto(held, new ReportDedupDto(ReportDedupOutcome.Duplicate, Matched(submission)));
        }

        var report = new Report
        {
            PublicId = PublicId.New(IdPrefix),
            ReporterType = submission.Reporter.Type,
            ReporterId = submission.Reporter.Id!,
            Effect = submission.Effect,
            Tool = submission.Tool,
            Provider = submission.Provider,
            Account = submission.Account,
            OccurredAt = submission.OccurredAt,
            ObservedAt = submission.ObservedAt,
            Campaign = campaign,
            Contact = contact,
            WorkItem = item,
            Operation = submission.Operation,
            OperationKnown = submission.Operation is not null && OperationCatalog.Knows(submission.Operation),
            ContactInCampaign = await MemberAsync(campaign, contact, cancellationToken).ConfigureAwait(false),
            Summary = submission.Summary,
            Assertion = submission.Assertion,
            AssertionHash = submission.AssertionHash,
            IdempotencyKey = submission.IdempotencyKey,
            Reason = submission.Reason,
            ReceivedAt = clock.GetUtcNow().UtcDateTime,
        };

        db.Reports.Add(report);
        journal.Append(
            db,
            submission.Reporter,
            JournalKinds.ExternalEffectReported,
            campaign,
            key: "report",
            updated: Names(report, campaign, contact, item),
            reason: submission.Reason,
            workItem: item);

        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            // Somebody submitted the same thing first. The index is what decides a repeat, not a read before the
            // write, so the answer is theirs and nothing of ours was written. Anything else is not ours to
            // swallow.
            var won = await Repeat(submission, cancellationToken).ConfigureAwait(false);
            if (won is null)
            {
                throw;
            }

            db.ChangeTracker.Clear();
            return ReportMapper.ToDto(won, new ReportDedupDto(ReportDedupOutcome.Duplicate, Matched(submission)));
        }

        return ReportMapper.ToDto(report, new ReportDedupDto(ReportDedupOutcome.Admitted, null));
    }

    /// <summary>
    /// One admitted report, as it was submitted.
    /// </summary>
    /// <remarks>
    /// The dedup of a read always says <c>admitted</c>. It describes what this call did with what it was given,
    /// and a read was given nothing: answering <c>duplicate</c> here would be reporting the history of somebody
    /// else's submission back to a caller who never made one.
    /// </remarks>
    public async Task<ReportDto> GetAsync(ReportGetRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.ReportId))
        {
            throw DomainErrors.Required("report_id");
        }

        var id = request.ReportId.Trim();
        var report = await Reports().FirstOrDefaultAsync(r => r.PublicId == id, cancellationToken).ConfigureAwait(false)
            ?? throw DomainErrors.ReportNotFound(id);

        return ReportMapper.ToDto(report, new ReportDedupDto(ReportDedupOutcome.Admitted, null));
    }

    /// <summary>
    /// What has been reported, oldest first. The filters narrow by what a report was correlated to and by when
    /// it landed; an id that names nothing is the loader's own 404, so a caller who mistyped a campaign is told
    /// rather than shown an empty page they would read as a fact about the world.
    /// </summary>
    public async Task<Page<ReportSummaryDto>> ListAsync(ReportListRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var limit = Paging.ResolveLimit(request.Limit);
        var after = Paging.DecodeCursor(request.Cursor);

        var query = Reports();

        if (request.CampaignId is not null)
        {
            var campaign = await CampaignService.LoadAsync(db, request.CampaignId, cancellationToken).ConfigureAwait(false);
            query = query.Where(r => r.CampaignId == campaign.Id);
        }

        if (request.ContactId is not null)
        {
            var contact = await ContactService.LoadAsync(db, request.ContactId, cancellationToken).ConfigureAwait(false);
            query = query.Where(r => r.ContactId == contact.Id);
        }

        if (request.WorkItemId is not null)
        {
            var item = await WorkItemService.LoadAsync(db, request.WorkItemId, cancellationToken).ConfigureAwait(false);
            query = query.Where(r => r.WorkItemId == item.Id);
        }

        if (!string.IsNullOrWhiteSpace(request.Operation))
        {
            // The reporter's word, matched as they wrote it: an operation this installation has never published
            // is still what somebody says they did, and narrowing to it is a fair question.
            var operation = request.Operation.Trim();
            query = query.Where(r => r.Operation == operation);
        }

        if (request.Since is { } since)
        {
            // Inclusive, so a walk of successive windows never loses what landed exactly on a boundary.
            var from = since.UtcDateTime;
            query = query.Where(r => r.ReceivedAt >= from);
        }

        if (after is not null)
        {
            query = query.Where(r => string.Compare(r.PublicId, after) > 0);
        }

        var fetched = await query.OrderBy(r => r.PublicId).Take(limit + 1).ToListAsync(cancellationToken).ConfigureAwait(false);
        return Paging.ToPage(fetched, limit, r => r.PublicId, ReportMapper.ToSummary);
    }

    /// <summary>
    /// The published order: the reporter's own key decides when they gave one, and what they said decides when
    /// they did not. Both are scoped to the reporter — two people describing one effect are two assertions.
    /// </summary>
    private Task<Report?> Repeat(ReportSubmission submission, CancellationToken cancellationToken)
    {
        var query = Reports().Where(r => r.ReporterType == submission.Reporter.Type && r.ReporterId == submission.Reporter.Id);
        query = submission.IdempotencyKey is { } key
            ? query.Where(r => r.IdempotencyKey == key)
            : query.Where(r => r.IdempotencyKey == null && r.AssertionHash == submission.AssertionHash);

        return query.FirstOrDefaultAsync(cancellationToken)!;
    }

    private static ReportDedupMatch Matched(ReportSubmission submission) =>
        submission.IdempotencyKey is null ? ReportDedupMatch.Content : ReportDedupMatch.Key;

    /// <summary>
    /// The ids have to agree with each other. A work item from another campaign, or about another person, is
    /// not a fact about the world worth keeping — it is a mistake the caller can see once it is named.
    /// </summary>
    private static void Consistent(Campaign? campaign, Contact? contact, WorkItem? item)
    {
        if (item is null)
        {
            return;
        }

        if (campaign is not null && item.CampaignId != campaign.Id)
        {
            throw DomainErrors.CorrelationInconsistent(
                $"Work item '{item.PublicId}' does not belong to campaign '{campaign.PublicId}'.");
        }

        if (contact is not null && item.ContactId is { } itemContact && itemContact != contact.Id)
        {
            throw DomainErrors.CorrelationInconsistent(
                $"Work item '{item.PublicId}' is not about contact '{contact.PublicId}'.");
        }
    }

    /// <summary>
    /// Whether the reported person was in the reported campaign when the report landed. False is not a refusal:
    /// an effect that reached somebody the campaign has never heard of is the kind of fact a report carries.
    /// </summary>
    private async Task<bool?> MemberAsync(Campaign? campaign, Contact? contact, CancellationToken cancellationToken)
    {
        if (campaign is null || contact is null)
        {
            return null;
        }

        // A membership is never deleted: removing somebody sets the row to excluded, so the question is about the
        // state and not about the row. Asking whether a row exists would answer "in the campaign" for a person
        // who was taken out of it — which is precisely the person an out-of-band effect is worth reporting about.
        return await db.CampaignContacts.AsNoTracking()
            .AnyAsync(
                m => m.CampaignId == campaign.Id && m.ContactId == contact.Id && m.State != MembershipState.Excluded,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Identifiers only. A journal line is a note about what happened, and the report is where what
    /// was said is kept.</summary>
    private static JsonObject Names(Report report, Campaign? campaign, Contact? contact, WorkItem? item) => new()
    {
        ["id"] = report.PublicId,
        ["effect"] = report.Effect,
        ["tool"] = report.Tool,
        ["operation"] = report.Operation,
        ["campaign_id"] = campaign?.PublicId,
        ["contact_id"] = contact?.PublicId,
        ["work_item_id"] = item?.PublicId,
    };

    private IQueryable<Report> Reports() =>
        db.Reports.AsNoTracking()
            .Include(r => r.Campaign)
            .Include(r => r.Contact)
            .Include(r => r.WorkItem);
}
