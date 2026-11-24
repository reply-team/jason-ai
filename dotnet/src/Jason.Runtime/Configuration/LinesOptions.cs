namespace Jason.Runtime.Configuration;

/// <summary>
/// How far the line with one person in one campaign may go. Read through its seam once a scan, at the claim's start, so an
/// edit applies from the next scan. No verb writes it; only the settings file does.
/// </summary>
public sealed class LinesOptions
{
    public const string Section = "Lines";

    /// <summary>
    /// The fewest and most touches a person may have in one campaign. There is no zero and no unlimited: a campaign that
    /// should touch nobody touches nobody by its plan, and a line never runs on without an end.
    /// </summary>
    public const int MinimumTouches = 1;

    public const int MaximumTouches = 20;

    /// <summary>
    /// How many touches — the operations whose document says <c>touch: true</c> — a person may have in one campaign, counted
    /// by the work that succeeded or any attempt of which ended classed ambiguous, since that may have reached them. At the ceiling the claim refuses the
    /// next touch with <c>touch_ceiling_reached</c>, and the manager ends the line.
    /// </summary>
    public int MaxTouches { get; set; } = 5;
}
