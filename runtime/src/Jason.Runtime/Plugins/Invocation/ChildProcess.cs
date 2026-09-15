using System.Diagnostics;

namespace Jason.Runtime.Plugins.Invocation;

/// <summary>
/// Waiting for a child the runtime has already ended. A kill is a request rather than a guarantee: a platform
/// that cannot end a whole tree, or a process the operating system will not let go of, leaves something running
/// that an unbounded wait would sit behind for as long as it lived. Every wait that follows a kill is given the
/// same grace as the kill itself and answers what it found, so no path out of an invocation lacks a deadline.
/// </summary>
public static class ChildProcess
{
    /// <summary>
    /// Waits for <paramref name="process"/> to end, for at most <paramref name="graceMs"/>, and answers whether
    /// it did. A child still running when the grace is spent is the caller's to describe, not to keep waiting on.
    /// </summary>
    public static async Task<bool> EndedWithinAsync(Process process, int graceMs)
    {
        ArgumentNullException.ThrowIfNull(process);

        // Asked before the grace is started, so that a grace of nothing still answers what is already true.
        if (process.HasExited)
        {
            return true;
        }

        using var grace = new CancellationTokenSource(graceMs);
        try
        {
            await process.WaitForExitAsync(grace.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
