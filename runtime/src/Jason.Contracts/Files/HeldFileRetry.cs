namespace Jason.Contracts.Files;

/// <summary>
/// One call on a file, tried again while another program holds the file open, and given up at the end of a rule's wait.
/// </summary>
/// <remarks>
/// <para>
/// Only Windows refuses for that reason. There, an open file stops a delete, a rename over it and a replace of it for as
/// long as the program holding it keeps it open: a reader, an indexer, a scanner. Elsewhere an unlink or a rename never
/// waits on an open file, so nothing counts as held and the call is tried once.
/// </para>
/// <para>
/// A hold is a sharing violation or a lock violation, read from the exception's HResult, and an access-denied only
/// where the rule says so, in either form .NET gives it: its own exception, or an IOException carrying
/// ERROR_ACCESS_DENIED, which is how Windows refuses to rename a directory with a file in it held open. Any other
/// failure is thrown at once, because waiting does not change a permission, a missing directory or a full disk.
/// </para>
/// <para>
/// The uninstall's remover and the runtime's endpoint descriptor both go through it, so the two agree on what a held file
/// is.
/// </para>
/// </remarks>
public static class HeldFileRetry
{
    /// <summary>
    /// Runs the call, and runs it again after each pause while the file is held, until it goes through or the wait is over.
    /// </summary>
    /// <param name="call">The one act on the file: a delete, a move, a directory's delete.</param>
    /// <param name="rule">How long to keep trying, and how often.</param>
    /// <param name="clock">What the wait is measured by.</param>
    /// <param name="pause">How a pause is taken: <see cref="Sleep"/> outside a test.</param>
    /// <param name="cancellationToken">Handed to each pause, which throws when it is cancelled.</param>
    /// <returns>Null when the call went through, or the last refusal when the file was still held at the end of the wait.</returns>
    public static Exception? Run(
        Action call,
        HeldFileRule rule,
        TimeProvider clock,
        Action<TimeSpan, CancellationToken> pause,
        CancellationToken cancellationToken = default) =>
        Run(call, rule, clock, pause, OperatingSystem.IsWindows(), cancellationToken);

    /// <summary>The same, with the platform's answer given, so a test can ask what either platform would do.</summary>
    internal static Exception? Run(
        Action call,
        HeldFileRule rule,
        TimeProvider clock,
        Action<TimeSpan, CancellationToken> pause,
        bool windows,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(call);
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(pause);

        long? started = null;
        while (true)
        {
            try
            {
                call();
                return null;
            }
            catch (Exception refusal) when (windows && IsHold(refusal, rule))
            {
                started ??= clock.GetTimestamp();
                var left = rule.Wait - clock.GetElapsedTime(started.Value);
                if (left <= TimeSpan.Zero)
                {
                    return refusal;
                }

                pause(left < rule.Interval ? left : rule.Interval, cancellationToken);
            }
        }
    }

    /// <summary>ERROR_SHARING_VIOLATION, as .NET carries it in an exception's HResult.</summary>
    private const int SharingViolation = unchecked((int)0x80070020);

    /// <summary>ERROR_LOCK_VIOLATION, as .NET carries it in an exception's HResult.</summary>
    private const int LockViolation = unchecked((int)0x80070021);

    /// <summary>ERROR_ACCESS_DENIED, as .NET carries it in an exception's HResult.</summary>
    private const int AccessDenied = unchecked((int)0x80070005);

    private static bool IsHold(Exception refusal, HeldFileRule rule) =>
        refusal is IOException { HResult: SharingViolation or LockViolation }
        || (rule.AccessDeniedCounts && refusal is UnauthorizedAccessException or IOException { HResult: AccessDenied });

    /// <summary>
    /// A pause outside a test: the delay in real time, ended early by a cancellation, which it then throws.
    /// </summary>
    public static void Sleep(TimeSpan delay, CancellationToken cancellationToken)
    {
        if (cancellationToken.WaitHandle.WaitOne(delay))
        {
            cancellationToken.ThrowIfCancellationRequested();
        }
    }
}
