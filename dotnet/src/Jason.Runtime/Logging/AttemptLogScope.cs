using Microsoft.Extensions.Logging;

namespace Jason.Runtime.Logging;

/// <summary>
/// The one log scope an attempt is written under. Every line logged while it is open carries the campaign, the
/// work item and the attempt as properties, so a reader filtering the log for one campaign finds its attempts —
/// including the lines whose own message names none of them, such as a plugin invocation's.
/// </summary>
/// <remarks>
/// The keys are the property names the log already uses for these ids, and the values are the public ids a person
/// can type into a command. A line whose message template names one of them keeps its own value: the same id.
/// The claim opens one around each line it writes and closes it before the pool is handed the work, so the scan's
/// later lines carry no attempt. A task carries the scopes open when it started, but by the time the pool runs the
/// handler the claim's has closed, so the handler opens another that it holds to the end.
/// </remarks>
public static class AttemptLogScope
{
    private const string CampaignKey = "CampaignId";
    private const string WorkItemKey = "WorkItemId";
    private const string AttemptKey = "AttemptId";

    /// <summary>
    /// Opens the scope. Disposing it closes it. Null when the logger has nothing to scope — which is how a
    /// logger that ignores scopes says so — and a <c>using</c> over a null is the same as a <c>using</c> over
    /// nothing.
    /// </summary>
    public static IDisposable? Begin(ILogger logger, string campaignId, string workItemId, string attemptId)
    {
        ArgumentNullException.ThrowIfNull(logger);
        return logger.BeginScope(new KeyValuePair<string, object>[]
        {
            new(CampaignKey, campaignId),
            new(WorkItemKey, workItemId),
            new(AttemptKey, attemptId),
        });
    }
}
