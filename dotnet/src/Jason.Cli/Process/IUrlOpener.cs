using System.ComponentModel;
using System.Diagnostics;
using Jason.Contracts.Discovery;
using Jason.Contracts.Programs;

namespace Jason.Cli.Process;

/// <summary>How a hand-on through the platform's opener ended.</summary>
public enum UrlHandOff
{
    /// <summary>
    /// The opener started and finished in time with success: the system has the address. On Windows the opener is
    /// <c>rundll32</c>, which answers once the shell has the address and never carries the handler's own result.
    /// </summary>
    Handed,

    /// <summary>The opener could not be started.</summary>
    NotStarted,

    /// <summary>The opener itself finished with a failure; a handler that does not start is not seen here.</summary>
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
    /// Hands <paramref name="url"/> to the platform's opener and waits up to <paramref name="wait"/> for it to finish, for
    /// a caller that says how the hand-on ended: Jason Desktop's address, which opens nothing else when it fails. Without a
    /// wait of its own, an opener answers whether it started.
    /// </summary>
    UrlHandOff HandOn(string url, TimeSpan wait) => Open(url) ? UrlHandOff.Handed : UrlHandOff.NotStarted;
}

/// <summary>
/// xdg-open, open, or Windows's own URL handler, each a child with no window and no shell. On Linux nothing is handed to
/// xdg-open outside a graphical session (<see cref="InGraphicalSession"/>).
/// </summary>
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

    /// <summary>
    /// The opener, by its full path: Windows' own handler in the system folder, macOS's opener where macOS keeps it, and
    /// on Linux xdg-open as the search path's full entries give it, or null where they give none, which is a link not
    /// handed on, as with no xdg-open at all.
    /// </summary>
    internal static string? Opener(string? searchPath, string? pathExtensions) =>
        OperatingSystem.IsWindows() ? Path.Combine(Environment.SystemDirectory, "rundll32.exe")
        : OperatingSystem.IsMacOS() ? "/usr/bin/open"
        : ProgramLookup.FullPathOf("xdg-open", new ProgramSearchPath(searchPath, pathExtensions));

    /// <summary>
    /// Whether a Linux session can show a window: <c>DISPLAY</c> or <c>WAYLAND_DISPLAY</c> is set. Without either, xdg-open
    /// never asks which program takes an address and goes straight to a list of text browsers.
    /// </summary>
    internal static bool InGraphicalSession(string? display, string? waylandDisplay) =>
        !string.IsNullOrEmpty(display) || !string.IsNullOrEmpty(waylandDisplay);

    private static System.Diagnostics.Process? Start(string url)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        if (OperatingSystem.IsLinux() && !InGraphicalSession(Environment.GetEnvironmentVariable("DISPLAY"), Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")))
        {
            return null;
        }

        if (Opener(Environment.GetEnvironmentVariable("PATH"), Environment.GetEnvironmentVariable("PATHEXT")) is not { } opener)
        {
            return null;
        }

        var start = StartOf(opener, url);
        try
        {
            StandardStreams.KeepOutOfChildren();
            if (ChildProcess.Start(start) is not { } process)
            {
                return null;
            }

            process.StandardInput.Close();
            process.OutputDataReceived += Discard;
            process.ErrorDataReceived += Discard;
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            return process;
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or PlatformNotSupportedException)
        {
            return null;
        }
    }

    /// <summary>
    /// The opener's start, with three streams of the CLI's own: what the opener starts inherits them, and outside GNOME and
    /// KDE xdg-open runs the program it starts to its end, so the caller's own streams would stay open in Jason Desktop's
    /// window, and whoever reads the caller's output to its end would wait for that window to close.
    /// </summary>
    internal static ProcessStartInfo StartOf(string opener, string url)
    {
        var start = ChildProcess.StartInfo(opener, ChildKind.SystemToolWithInput);
        if (OperatingSystem.IsWindows())
        {
            start.ArgumentList.Add("url.dll,FileProtocolHandler");
        }

        start.ArgumentList.Add(url);
        return start;
    }

    private static void Discard(object sender, DataReceivedEventArgs e)
    {
    }
}
