using System.Text.Json.Nodes;

namespace Jason.Contracts.Api;

/// <summary>
/// Whether this submission was admitted or matched one already held, and by which rule. A repeat is an answer,
/// not a failure: a caller retrying after a dropped connection must not have to tell an error from a repeat.
/// </summary>
public sealed record ReportDedupDto(ReportDedupOutcome Outcome, ReportDedupMatch? Matched);

/// <summary>
/// What the reporter says this effect was about. Every part of it is their claim: the runtime checked that the
/// ids it was given exist and nothing else, which is what <c>verified</c> says out loud.
/// </summary>
/// <param name="OperationKnown">Whether <paramref name="Operation"/> names an operation this installation publishes.</param>
/// <param name="ContactInCampaign">
/// Whether the named contact was a member of the named campaign when the report landed; null when the report
/// names neither or only one. False is not an error — an effect that reached somebody who is not in the local
/// campaign yet is exactly the kind of fact a report exists to carry.
/// </param>
/// <param name="Verified">
/// Always false in this version, and said rather than left out: admission establishes that a report is
/// well-formed and attributable, never that what it says is true.
/// </param>
public sealed record ReportCorrelationDto(
    string? CampaignId,
    string? ContactId,
    string? WorkItemId,
    string? Operation,
    bool OperationKnown,
    bool? ContactInCampaign,
    bool Verified);

/// <summary>
/// One admitted report: the reporter's assertion exactly as it was submitted, and the receipt the runtime wrote
/// beside it.
/// </summary>
public sealed record ReportDto(
    string Id,
    DateTimeOffset ReceivedAt,
    ActorRef Reporter,
    string? Reason,
    ReportDedupDto Dedup,
    JsonObject Assertion,
    string AssertionHash,
    ReportCorrelationDto Correlation);

/// <summary>A listing stays small: no assertion, no evidence, no hash.</summary>
public sealed record ReportSummaryDto(
    string Id,
    DateTimeOffset ReceivedAt,
    ActorRef Reporter,
    string Effect,
    string Tool,
    string? Provider,
    string Summary,
    ReportCorrelationDto Correlation);

public sealed record ReportGetRequest(string? ReportId);

public sealed record ReportListRequest(
    string? CampaignId,
    string? ContactId,
    string? WorkItemId,
    string? Operation,
    DateTimeOffset? Since,
    int? Limit,
    string? Cursor);
