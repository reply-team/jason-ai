using System.Globalization;
using System.Text.Json.Nodes;

namespace Jason.Cli.Logs;

/// <summary>
/// The six levels a line of the runtime's log can carry, lowest first, so that "at least" is a comparison.
/// </summary>
public enum LogLevelName
{
    Verbose,
    Debug,
    Information,
    Warning,
    Error,
    Fatal,
}

/// <summary>Reading a level from text: from a person's <c>--level</c> and from a line's <c>@l</c>.</summary>
public static class LogLevels
{
    /// <summary>
    /// Parses one of the six names, in any case. Nothing else is a level: not a number, which an enum would take for
    /// a name, not a list of names, and not a near miss such as <c>warn</c> that a person would believe had been
    /// understood.
    /// </summary>
    public static bool TryParse(string? text, out LogLevelName level)
    {
        foreach (var candidate in Enum.GetValues<LogLevelName>())
        {
            if (string.Equals(text, candidate.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                level = candidate;
                return true;
            }
        }

        level = default;
        return false;
    }

    /// <summary>
    /// The level of one event. The runtime leaves <c>@l</c> out at Information, so an event that names none is at
    /// Information; and so is one whose <c>@l</c> is not a level this runtime writes, which is not worth hiding from a
    /// person asking for everything and is not an error either.
    /// </summary>
    public static LogLevelName Of(JsonObject evt)
    {
        ArgumentNullException.ThrowIfNull(evt);
        return StringProperty(evt, "@l") is { } text && TryParse(text, out var level) ? level : LogLevelName.Information;
    }

    internal static string? StringProperty(JsonObject evt, string name) =>
        evt[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
}

/// <summary>
/// What a person asked the log to be narrowed to. Every part that is set must hold; a part that is null asks nothing.
/// </summary>
/// <param name="AtLeast">Events at this level or above.</param>
/// <param name="Since">Events stamped at this instant or later. An event that carries no readable time is never shown to be since one.</param>
/// <param name="CampaignId">Events whose <c>CampaignId</c> property is exactly this text.</param>
/// <param name="WorkItemId">Events whose <c>WorkItemId</c> property is exactly this text.</param>
public sealed record LogFilter(LogLevelName? AtLeast = null, DateTimeOffset? Since = null, string? CampaignId = null, string? WorkItemId = null)
{
    /// <summary>Whether one event passes every part of the filter.</summary>
    public bool Matches(JsonObject evt)
    {
        ArgumentNullException.ThrowIfNull(evt);

        if (AtLeast is { } atLeast && LogLevels.Of(evt) < atLeast)
        {
            return false;
        }

        if (Since is { } since && !(Time(evt) is { } time && time >= since))
        {
            return false;
        }

        return (CampaignId is null || LogLevels.StringProperty(evt, "CampaignId") == CampaignId)
            && (WorkItemId is null || LogLevels.StringProperty(evt, "WorkItemId") == WorkItemId);
    }

    /// <summary>
    /// The instant an event carries in <c>@t</c>, or null when it has none that can be read. The runtime writes UTC;
    /// a stamp with no offset is read as UTC rather than as the reader's own time, which is not where it was written.
    /// Shared with whatever shows the time, so that what is filtered on and what is shown are one reading.
    /// </summary>
    internal static DateTimeOffset? Time(JsonObject evt) =>
        LogLevels.StringProperty(evt, "@t") is { } text
        && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var time)
            ? time
            : null;
}
