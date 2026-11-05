namespace Jason.Runtime.Configuration;

/// <summary>
/// The respond step: whether a reply the runtime says wakes the campaign's responder, where the responder's work sits in
/// the queue, what one run may cost, and how old a reply may be and still wake one. Read through its seam every tick, so
/// an edit applies at the next scan without a restart.
/// </summary>
/// <remarks>
/// Independent of <c>Manager:Triggers</c>: neither reads the other. A reply wakes the responder at once and the manager
/// at its check-in; turning one off leaves the other as it was.
/// </remarks>
public sealed class ResponderOptions
{
    public const string Section = "Responder";

    /// <summary>The narrowest and widest age a reply may have and still wake a responder: an hour and thirty days.</summary>
    public const int MinimumMaxAgeSeconds = 3_600;

    public const int MaximumMaxAgeSeconds = 2_592_000;

    /// <summary>
    /// Off means the step creates nothing; it still reads the replies said and moves past them, so turning it on later
    /// wakes nothing for a reply said while it was off. The manager's trigger is unchanged either way.
    /// </summary>
    public bool OnReply { get; set; } = true;

    /// <summary>
    /// Where the read of the thread and the responder sit in the queue: above ordinary work and a check-in, so a reply is
    /// answered ahead of the campaign's queued sends, and below the urgent work the skills raise. The inbound read of
    /// conversations sits ten above it, so detecting a reply never waits behind answering one.
    /// </summary>
    public int Priority { get; set; } = 50;

    /// <summary>How long one responder run may take: the figure a check-in has, for a bounded semantic run.</summary>
    public int TimeoutSeconds { get; set; } = 900;

    /// <summary>
    /// How many attempts a responder is worth. One: a second run would draft a second answer, and a failure wakes the
    /// manager instead.
    /// </summary>
    public int MaxAttempts { get; set; } = 1;

    /// <summary>
    /// How old a reply may be, by the thread's last activity when the step reads it, and still wake a responder: three
    /// days. An older one — a span read again, a campaign paused for long — is the manager's alone.
    /// </summary>
    public int MaxAgeSeconds { get; set; } = 259_200;

    /// <summary>The fewest and most responder launches a campaign may be allowed in one UTC day.</summary>
    public const int MinimumMaxLaunchesPerDay = 1;

    public const int MaximumMaxLaunchesPerDay = 1_000;

    /// <summary>
    /// How many responder runs one campaign may start in a UTC day, counted from 00:00Z by the attempts that started. A
    /// responder the claim finds at the limit is not failed — a failure would wake the manager — but held: it waits,
    /// with no attempt, until the next 00:00Z, or until a person resumes it. Read once a scan, at the claim's start, so a
    /// raised limit applies from the next scan to work not yet held, and an item already held keeps its moment.
    /// </summary>
    public int MaxLaunchesPerDay { get; set; } = 25;
}
