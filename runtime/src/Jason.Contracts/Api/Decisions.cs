namespace Jason.Contracts.Api;

/// <summary>One of the answers the asker named, and why somebody might pick it.</summary>
public sealed record DecisionOption(string Label, string? Detail);

/// <summary>
/// Something to read before deciding, named by its identifier. Never a copy: a person opening a question an
/// hour later reads the row as it stands, not as it stood when a role described it.
/// </summary>
/// <remarks>
/// The kind is nullable so that leaving it out is a refusal rather than a guess. A non-nullable enum would
/// have taken an absent field as the first value in the vocabulary, and a caller who forgot to say what they
/// were pointing at would get a lookup against the wrong table instead of an answer about what they left out.
/// </remarks>
public sealed record DecisionReference(DecisionReferenceKind? Kind, string Id);

/// <summary>
/// A question a running role could not answer for itself, as a caller reads it. The attempt that asked is
/// named because it is also the chain the review released by the answer belongs to — the question outlives
/// the attempt, which is the whole reason this is a row rather than a parked operation.
/// </summary>
public sealed record DecisionDto(
    string Id,
    string CampaignId,
    string WorkItemId,
    string AttemptId,
    DecisionStatus Status,
    string Question,
    IReadOnlyList<DecisionOption>? Options,
    IReadOnlyList<DecisionReference>? References,
    DateTimeOffset RaisedAt,
    string? Answer,
    string? ChosenOption,
    DateTimeOffset? AnsweredAt,
    ActorRef? AnsweredBy);

/// <summary>
/// A listing carries the question whole. "What am I being asked?" is the only reason to open one of these,
/// and a question is bounded at a length that keeps a page of them readable.
/// </summary>
public sealed record DecisionSummaryDto(
    string Id,
    string CampaignId,
    string WorkItemId,
    DecisionStatus Status,
    string Question,
    DateTimeOffset RaisedAt,
    DateTimeOffset? AnsweredAt,
    ActorRef? AnsweredBy);

/// <summary>
/// Raising is fenced by the attempt, exactly as recording a result is: the asker must be the run that owns
/// the work item, or a role whose lease was lost could put questions in somebody's queue.
/// </summary>
public sealed record DecisionRaiseRequest(
    string? WorkItemId,
    string? AttemptId,
    string? Question,
    IReadOnlyList<DecisionOption>? Options,
    IReadOnlyList<DecisionReference>? References,
    string? Reason);

/// <summary>
/// Answering is a person's, and the actor is not optional: an absent one is an anonymous human, which is
/// right for creating work and wrong for deciding it.
/// </summary>
public sealed record DecisionAnswerRequest(string? DecisionId, string? Answer, string? Option, ActorRef? Actor, string? Reason);

public sealed record DecisionGetRequest(string? DecisionId);

/// <summary>Pending by default: the question somebody opens this with is "what is waiting on me?".</summary>
public sealed record DecisionListRequest(
    DecisionStatus? Status,
    string? CampaignId,
    string? WorkItemId,
    int? Limit,
    string? Cursor);
