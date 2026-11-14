using Jason.Runtime.Persistence;

namespace Jason.Runtime.Approvals;

/// <summary>What the claim would do with a send's reviews if it claimed the send now.</summary>
public enum ReviewHearing
{
    /// <summary>
    /// It would decide about the send anew, and read its reviews as it does: no approved row is live, or the live one no
    /// longer stands for the send as it would now go.
    /// </summary>
    Read,

    /// <summary>It would run the send under a live approval that stands for it as it reads, and read nothing else.</summary>
    NotRead,

    /// <summary>
    /// It could not say right now: the pre-flight refuses the send, so no subject can be composed to hold a live approved
    /// row to, and once the send can be claimed again that row may still stand for it.
    /// </summary>
    Unjudged,
}

/// <summary>The claim's answer about one send, and the approval it names where it names one.</summary>
/// <param name="Hearing">Whether the claim would read the send's reviews.</param>
/// <param name="Approval">
/// The live approved row: for <see cref="ReviewHearing.NotRead"/> and <see cref="ReviewHearing.Unjudged"/>, and for
/// <see cref="ReviewHearing.Read"/> where one is live and no longer stands for the send; null for a read with none live.
/// </param>
public sealed record ClaimReading(ReviewHearing Hearing, Approval? Approval)
{
    /// <summary>The claim would decide anew and read the reviews, with no approved row live.</summary>
    public static ClaimReading Reads { get; } = new(ReviewHearing.Read, null);
}

/// <summary>
/// The claim's question as a review asks it: whether the claim would read a send's reviews if it claimed the send now. The
/// claim answers it with its own facts, pre-flight and test (<see cref="ApprovalGate.Authorizes"/>), so a review never
/// carries a second description of the claim's condition.
/// </summary>
public interface IClaimReading
{
    Task<ClaimReading> RunsUnderAsync(JasonDbContext db, WorkItem item, CancellationToken ct);
}
