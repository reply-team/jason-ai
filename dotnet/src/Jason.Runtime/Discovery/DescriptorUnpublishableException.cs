using Jason.Contracts.Files;

namespace Jason.Runtime.Discovery;

/// <summary>
/// The endpoint descriptor could not be put in place, so this runtime has not started: no client could find it.
/// </summary>
/// <remarks>
/// <para>
/// Its own type so that a start refuses with a sentence rather than ending unhandled. The descriptor is published last,
/// after the API is listening, and a failure there escaped the start: the process died, and <c>jason runtime start</c>
/// could say only that it had exited before publishing an endpoint descriptor.
/// </para>
/// <para>
/// The sentence names the path and says what to do. It never carries the operating system's message, as no refusal at a
/// start does. <see cref="Why"/> is the same reason as one closed phrase, which the runtime's log carries.
/// </para>
/// </remarks>
public sealed class DescriptorUnpublishableException : Exception
{
    private DescriptorUnpublishableException(string message, string why, Exception? cause)
        : base(message, cause)
    {
        Why = why;
    }

    /// <summary>Why it could not be published, as one of three closed phrases, for the log.</summary>
    public string Why { get; }

    /// <summary>Replacing it was refused for the whole of the wait: a program held it, or the account may not replace it.</summary>
    public static DescriptorUnpublishableException Held(string path, HeldFileRule rule, Exception? cause) =>
        new(
            $"The runtime could not publish its endpoint descriptor at '{path}': replacing it was refused for {rule.WaitInWords}. "
            + "Another program may be holding the file open, or this account may not be allowed to replace it; free it or "
            + "fix its permissions, and start the runtime again.",
            $"replacing it was refused for {rule.WaitInWords}",
            cause);

    /// <summary>Replacing it was refused at once, by a failure waiting does not change.</summary>
    public static DescriptorUnpublishableException Refused(string path, string runDirectory, Exception? cause) =>
        new(
            $"The runtime could not publish its endpoint descriptor at '{path}': replacing it was refused. Check that the "
            + $"account the runtime runs as can write to '{runDirectory}', and that its volume has room; then start the "
            + "runtime again.",
            "replacing it was refused",
            cause);

    /// <summary>The descriptor there is marked read-only, which Windows will not replace.</summary>
    public static DescriptorUnpublishableException ReadOnly(string path, Exception? cause) =>
        new(
            $"The runtime could not publish its endpoint descriptor at '{path}': the file is read-only. Clear its read-only "
            + "attribute and start the runtime again.",
            "the file is read-only",
            cause);
}
