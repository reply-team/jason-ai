using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Runtime.Configuration;
using Jason.Runtime.Domain;
using Jason.Runtime.Persistence;
using Jason.Runtime.WorkItems;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Dispatch;

/// <summary>
/// The work item the runtime creates to have a campaign reviewed — a <em>check-in</em>, which is the only word
/// for it. It is an <c>ai_role</c> item like any other: claimed by the same scan, launched through the same
/// profile resolution, held to the same lease, ended through the same routine. Nothing about the manager loop
/// is a second kind of work.
/// </summary>
public static class ManagerCheckIn
{
    public const string Role = "manager";

    /// <summary>
    /// What a review's answer has to look like. A check-in that reported prose would be a review nobody could
    /// count, and the three outcomes are the three things a review can honestly have done. <c>escalated</c> is
    /// here before anything can escalate on purpose: the vocabulary is fixed once, so the version that adds
    /// escalation adds no shape.
    /// </summary>
    public static JsonNode ResultFormat { get; } = JsonNode.Parse(
        """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "outcome": { "type": "string", "enum": ["acted", "escalated", "nothing"] },
            "summary": { "type": "string", "maxLength": 500 },
            "created_work_items": { "type": "array", "items": { "type": "string" } },
            "cancelled_work_items": { "type": "array", "items": { "type": "string" } }
          },
          "required": ["outcome", "summary"]
        }
        """)!;

    /// <summary>
    /// What the review's job normally involves, written into the brief as guidance. It grants nothing: the
    /// runtime's answer to a call outside this list is the answer anybody gets, approvals still park what needs
    /// a person, and being the manager confers no authority. A second permission system here would be a second
    /// place for authority to be wrong.
    /// </summary>
    private static readonly string[] AllowedOperations =
    [
        "workitem.create",
        "workitem.update",
        "workitem.cancel",
        "campaign.update_context",
        "journal.append",
        "rolenote.set",
        "approval.list",
        "report.list",
    ];

    /// <summary>
    /// Creates the check-in through the routine every other work item is created by, with two things supplied
    /// rather than derived: the actor, which is the dispatcher, and the lineage, which is the chain of the
    /// thing the review is about.
    /// </summary>
    public static async Task<WorkItem> CreateAsync(
        WorkItemService items,
        JasonDbContext db,
        Campaign campaign,
        ManagerCause? cause,
        ManagerOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(campaign);
        ArgumentNullException.ThrowIfNull(options);

        var lineage = await LineageAsync(db, cause, cancellationToken).ConfigureAwait(false);
        var request = new WorkItemCreateRequest(
            campaign.PublicId,
            WorkItemKind.AiRole,
            Role,
            Operation: null,
            ContactId: null,
            ExecutionProfile: null,
            options.Priority,
            NotBefore: null,
            DueAt: null,
            options.TimeoutSeconds,
            HeartbeatSeconds: null,
            options.MaxAttempts,
            Context(cause),
            ResultFormat.DeepClone(),
            Actor: null,
            Reason: cause is null ? "the review cadence came round" : "the chronicle asked for a review");

        var item = await items.CreateCoreAsync(request, Actors.Dispatcher, lineage, cancellationToken).ConfigureAwait(false);

        // The cadence is measured from the last check-in the runtime created, not from the last one that
        // finished: a review whose host was missing, or whose answer was refused, must not stop the loop. The
        // next one comes round on time either way.
        campaign.ManagerReviewAnchor = item.CreatedAt;
        return item;
    }

    /// <summary>
    /// The brief, which is identifiers and constants and nothing else. The dispatcher knows why it woke a
    /// manager and not one thing about what the manager will find; a sentence here would be the dispatcher
    /// reading meaning, which is the one thing it must never do.
    /// </summary>
    private static JsonObject Context(ManagerCause? cause)
    {
        var context = new JsonObject
        {
            ["review_intent"] = cause is null ? "scheduled" : "triggered",
            ["allowed_operations"] = new JsonArray([.. AllowedOperations.Select(operation => (JsonNode?)JsonValue.Create(operation))]),
        };

        if (cause is { } because)
        {
            context["trigger"] = because.Kind;
            context["cause"] = new JsonObject
            {
                ["journal_entry_id"] = because.JournalEntryId,
                ["work_item_id"] = because.WorkItemId,
                ["attempt_id"] = because.AttemptId,

                // Present when the answer to a question is what woke this review, and absent otherwise — a
                // scan's read is one review, so a decision answered behind a failure is read by the manager
                // rather than named in its brief.
                ["decision_id"] = because.DecisionId,
                ["qualifying_count"] = because.QualifyingCount,
            };
        }

        return context;
    }

    /// <summary>
    /// Whose chain this review belongs to. The dispatcher creates the row, but a review of a failed attempt
    /// belongs to that attempt's chain — so lineage is read from the <em>cause</em> and not from the actor,
    /// which would have made every check-in root work and cut the chain at exactly the step that matters.
    /// </summary>
    /// <remarks>
    /// Reading it through <see cref="Lineage.ForCreationAsync"/> rather than by hand is what keeps a failed
    /// provider operation out of <c>Unresolved</c>: an attempt that pinned no profile hands on its own item's
    /// record, and that function is where that rule lives.
    /// </remarks>
    private static async Task<LineageRecord> LineageAsync(JasonDbContext db, ManagerCause? cause, CancellationToken cancellationToken)
    {
        if (cause is not { } because)
        {
            // Nothing caused a scheduled review but the clock, and the clock is the runtime itself: root work,
            // which resolves its profile from the campaign, the role or the default like any other root work.
            return LineageRecord.Root;
        }

        if (because.AttemptId is { Length: > 0 } attempt)
        {
            return await Lineage
                .ForCreationAsync(db, new ActorRef(ActorType.Attempt, attempt), cancellationToken)
                .ConfigureAwait(false);
        }

        // A line with an item but no attempt — a person rejecting an approval is the one that matters — still
        // names the chain the review is about. The rejected item's own record is that chain.
        if (because.WorkItemId is { Length: > 0 } workItem)
        {
            var caused = await db.WorkItems
                .AsNoTracking()
                .FirstOrDefaultAsync(w => w.PublicId == workItem, cancellationToken)
                .ConfigureAwait(false);

            return caused is null ? LineageRecord.Root : LineageRecord.Of(caused);
        }

        return LineageRecord.Root;
    }
}
