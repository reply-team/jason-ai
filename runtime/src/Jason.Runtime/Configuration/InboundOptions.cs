namespace Jason.Runtime.Configuration;

/// <summary>
/// The inbound read: whether the scan reads each linked active campaign's conversations at all, how often, and how
/// far behind the last complete read the next one starts. Read through its seam every tick, so an edit applies at
/// the next scan without a restart.
/// </summary>
public sealed class InboundOptions
{
    public const string Section = "Inbound";

    /// <summary>
    /// The narrowest and widest cadence. A minute is the shortest wait between two reads of one campaign that is
    /// still a read rather than a poll; a day is the longest a reply may go unseen and still be called a sync.
    /// Named here rather than in the validator because the page prints them.
    /// </summary>
    public const int MinimumSyncSeconds = 60;

    public const int MaximumSyncSeconds = 86_400;

    /// <summary>The widest look-back: a day, the same as the widest cadence.</summary>
    public const int MaximumLookbackSeconds = 86_400;

    /// <summary>
    /// Off means the scan creates no read and says nothing it has not already said. A campaign it skipped while off
    /// catches up when it is on again: the next read starts from where the last complete one left it.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>How long after one read of a campaign's conversations the next is due: fifteen minutes by default.</summary>
    public int SyncSeconds { get; set; } = 900;

    /// <summary>
    /// How far behind the runtime's clock at the last complete read the next read starts. Four hours by default: it
    /// absorbs the difference between this machine's clock and the provider's, and the time a provider takes to bring
    /// a reply into its inbox after the moment it stamps on the thread, which has been seen at over an hour and a half.
    /// A reply the provider takes in later than this after its stamp is never said. Zero starts each read where the
    /// last one did, and a reply listed late is then never said.
    /// </summary>
    public int LookbackSeconds { get; set; } = 14_400;
}
