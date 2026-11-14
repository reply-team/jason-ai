namespace Jason.Runtime.Configuration;

/// <summary>
/// How sends leave without a person's look. Read through its seam once a scan, at the claim's start, so an edit applies
/// from the next scan.
/// </summary>
public sealed class SendingOptions
{
    public const string Section = "Sending";

    /// <summary>
    /// The fewest and most sends the automatic mode may release from one campaign in one UTC day. There is no zero and no
    /// unlimited: a person who wants no automatic sends sets the mode to every message, and the ceiling never slips.
    /// </summary>
    public const int MinimumPerDay = 1;

    public const int MaximumPerDay = 1_000;

    /// <summary>
    /// How many sends the automatic mode may release from one campaign in a UTC day, counted from 00:00Z by the releases
    /// written, whatever became of each. At the ceiling, the next send the mode would release waits for the person instead,
    /// even where a standing approval would have covered it; a person's own approval is no release and counts for nothing.
    /// </summary>
    public int AutomaticMaxPerDay { get; set; } = 100;
}
