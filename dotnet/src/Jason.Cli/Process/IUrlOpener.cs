using System.ComponentModel;
using Jason.Contracts.Discovery;

namespace Jason.Cli.Process;

/// <summary>How a hand-on through the platform's opener ended.</summary>
public enum UrlHandOff
{
    /// <summary>The opener started and finished in time with success: the system has the address.</summary>
    Handed,

    /// <summary>The opener could not be started.</summary>
    NotStarted,

    /// <summary>The opener finished with a failure.</summary>
    Failed,

    /// <summary>The opener did not finish within the wait.</summary>
    TimedOut,
}

/// <summary>
/// How a link is handed to the person's default browser — and the fifth seam whose default is the refusal. A test
/// that let the real one run would open a browser on whoever runs the suite, and on three CI runners.
/// </summary>
public interface IUrlOpener
{
    /// <summary>True when the platform's opener was started; false when it could not be.</summary>
    bool Open(string url);

    /// <summary>
    /// Hands <paramref name="url"/> to the platform's opener and waits up to <paramref name="wait"/> for it to finish,
    /// for a caller that has somewhere else to go when the hand-on fails: a click on a toast that opens the desktop,
    /// and the browser UI when the desktop cannot be reached. Without a wait of its own, an opener answers whether it
    /// started.
    /// </summary>
    UrlHandOff HandOn(string url, TimeSpan wait) => Open(url) ? UrlHandOff.Handed : UrlHandOff.NotStarted;
}

/// <summary>xdg-open, open, or Windows's own URL handler, each a child with no window and no shell.</summary>
public sealed class PlatformUrlOpener : IUrlOpener
{
    public bool Open(string url)
    {
        using var process = Start(url);
        return process is not null;
    }

    public UrlHandOff HandOn(string url, TimeSpan wait)
    {
        using var process = Start(url);
        if (process is null)
        {
            return UrlHandOff.NotStarted;
        }

        try
        {
            if (!process.WaitForExit(wait))
            {
                return UrlHandOff.TimedOut;
            }

            return process.ExitCode == 0 ? UrlHandOff.Handed : UrlHandOff.Failed;
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            return UrlHandOff.Failed;
        }
    }

    private static System.Diagnostics.Process? Start(string url)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        var (program, arguments) = OperatingSystem.IsWindows()
            ? ("rundll32", new[] { "url.dll,FileProtocolHandler", url })
            : OperatingSystem.IsMacOS()
                ? ("open", new[] { url })
                : ("xdg-open", new[] { url });

        var start = ChildProcess.StartInfo(program);
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        try
        {
            return ChildProcess.Start(start);
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or PlatformNotSupportedException)
        {
            return null;
        }
    }
}
