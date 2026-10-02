using Jason.Contracts.Notifications;
using Jason.Contracts.Plugins;

namespace Jason.Runtime.Notifications;

/// <summary>The four things that can happen to a notification on one channel.</summary>
public enum ChannelResult
{
    Delivered,
    Silenced,
    Failed,
    NotAttempted,
}

/// <summary>
/// What one channel did with one notification. A failure carries a code and one of the four failure classes; a channel
/// not tried, or a toast shown into nothing, carries a reason. <see cref="Detail"/> is a diagnostic and never what the
/// notification said; <see cref="Receipt"/> is a plugin's own id for the message it sent.
/// </summary>
public sealed record ChannelOutcome(
    string Channel,
    string Kind,
    ChannelResult Result,
    string? Code = null,
    FailureClass? Class = null,
    string? Reason = null,
    string? Detail = null,
    string? Receipt = null,
    int Attempts = 1)
{
    public static ChannelOutcome Delivered(string channel, string kind, string? receipt = null) =>
        new(channel, kind, ChannelResult.Delivered, Receipt: receipt);

    public static ChannelOutcome Silenced(string channel, string kind, string reason) =>
        new(channel, kind, ChannelResult.Silenced, Reason: reason);

    public static ChannelOutcome Failed(string channel, string kind, string code, FailureClass @class, string? detail = null) =>
        new(channel, kind, ChannelResult.Failed, code, @class, Detail: detail);

    public static ChannelOutcome NotAttempted(string channel, string kind, string reason, string? detail = null) =>
        new(channel, kind, ChannelResult.NotAttempted, Reason: reason, Detail: detail, Attempts: 0);

    /// <summary>The word a line and <c>system.info</c> carry for a result.</summary>
    public static string Word(ChannelResult result) => result switch
    {
        ChannelResult.Delivered => ChannelResults.Delivered,
        ChannelResult.Silenced => ChannelResults.Silenced,
        ChannelResult.Failed => ChannelResults.Failed,
        ChannelResult.NotAttempted => ChannelResults.NotAttempted,
        _ => throw new ArgumentOutOfRangeException(nameof(result), result, "Not a channel result."),
    };

    /// <summary>The result a word names, or null for a word this build does not know.</summary>
    public static ChannelResult? FromWord(string? word) => word switch
    {
        ChannelResults.Delivered => ChannelResult.Delivered,
        ChannelResults.Silenced => ChannelResult.Silenced,
        ChannelResults.Failed => ChannelResult.Failed,
        ChannelResults.NotAttempted => ChannelResult.NotAttempted,
        _ => null,
    };
}
