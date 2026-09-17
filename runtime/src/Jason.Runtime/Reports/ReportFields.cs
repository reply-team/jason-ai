namespace Jason.Runtime.Reports;

/// <summary>
/// The vocabulary of a submission, written once. Three readers share it — the key check, the parser, and the
/// rule that <c>unknown_fields</c> may only name a field that exists — so there is no second list to drift out
/// of step with the first.
/// </summary>
public static class ReportFields
{
    public const string IdempotencyKey = "idempotency_key";
    public const string Effect = "effect";
    public const string Tool = "tool";
    public const string Provider = "provider";
    public const string Account = "account";
    public const string OccurredAt = "occurred_at";
    public const string ObservedAt = "observed_at";
    public const string CampaignId = "campaign_id";
    public const string ContactId = "contact_id";
    public const string WorkItemId = "work_item_id";
    public const string Operation = "operation";
    public const string ExternalIds = "external_ids";
    public const string Summary = "summary";
    public const string Evidence = "evidence";
    public const string UnknownFields = "unknown_fields";
    public const string Uncertainty = "uncertainty";

    public const string Actor = "actor";
    public const string Reason = "reason";

    /// <summary>What the reporter asserts about the world. This, and only this, is stored and hashed.</summary>
    public static readonly IReadOnlySet<string> Assertion = new HashSet<string>(StringComparer.Ordinal)
    {
        IdempotencyKey, Effect, Tool, Provider, Account, OccurredAt, ObservedAt,
        CampaignId, ContactId, WorkItemId, Operation, ExternalIds, Summary, Evidence,
        UnknownFields, Uncertainty,
    };

    /// <summary>The request envelope every mutating verb carries. Neither is an assertion about the world:
    /// <c>actor</c> is who is speaking and <c>reason</c> is why they called.</summary>
    public static readonly IReadOnlySet<string> Envelope = new HashSet<string>(StringComparer.Ordinal) { Actor, Reason };

    /// <summary>
    /// What only the runtime writes. A submission carrying one of these names is refused rather than quietly
    /// stripped: a document that comes back different from the one that was sent is not the assertion anybody
    /// made, and a reporter who thinks they set the time of receipt should be told they did not.
    /// </summary>
    public static readonly IReadOnlySet<string> Reserved = new HashSet<string>(StringComparer.Ordinal)
    {
        "id", "received_at", "reporter", "dedup", "assertion", "assertion_hash", "operation_known", "correlation",
    };
}
