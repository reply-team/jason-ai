using Jason.Runtime.Approvals;
using Jason.Runtime.Persistence;

namespace Jason.Runtime.Tests.Approvals;

/// <summary>
/// A claim that answers, for every send, that it would decide anew and read the reviews: for a test about the review verb's
/// own rules, where no claim is wired and the claim's question is not what is being tested. The claim's real answer is
/// tested in <c>ClaimerTests</c>, through the harness's own claimer.
/// </summary>
public sealed class TheClaimReadsEveryReview : IClaimReading
{
    public static TheClaimReadsEveryReview Instance { get; } = new();

    public Task<ClaimReading> RunsUnderAsync(JasonDbContext db, WorkItem item, CancellationToken ct) => Task.FromResult(ClaimReading.Reads);
}
