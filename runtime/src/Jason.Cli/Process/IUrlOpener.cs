using System.ComponentModel;
using Jason.Contracts.Discovery;

namespace Jason.Cli.Process;

/// <summary>
/// How a link is handed to the person's default browser — and the fifth seam whose default is the refusal. A test
/// that let the real one run would open a browser on whoever runs the suite, and on three CI runners.
/// </summary>
public interface IUrlOpener
{
    /// <summary>True when the platform's opener was started; false when it could not be.</summary>
    bool Open(string url);
}

/// <summary>xdg-open, open, or Windows's own URL handler, each a child with no window and no shell.</summary>
public sealed class PlatformUrlOpener : IUrlOpener
{
    public bool Open(string url)
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
            using var process = ChildProcess.Start(start);
            return process is not null;
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or PlatformNotSupportedException)
        {
            return false;
        }
    }
}
