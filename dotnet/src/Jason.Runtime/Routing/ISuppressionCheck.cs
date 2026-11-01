using Jason.Runtime.Persistence;

namespace Jason.Runtime.Routing;

/// <summary>
/// The one question the claim asks the do-not-contact register: is this address out of reach? A seam rather than
/// a direct call, so that the pre-flight itself stays a pure function over facts somebody else read — and so a
/// test can state the answer instead of arranging a row to produce it.
/// </summary>
/// <remarks>
/// The context is passed in rather than injected: the claim's own connection is inside the claim transaction,
/// and a second one would be asking a different database about the same moment.
/// </remarks>
public interface ISuppressionCheck
{
    /// <summary>Both parts are already normalized — channel values are stored canonical, and so are suppressions.</summary>
    Task<bool> IsSuppressedAsync(JasonDbContext db, string channel, string value, CancellationToken cancellationToken);
}
