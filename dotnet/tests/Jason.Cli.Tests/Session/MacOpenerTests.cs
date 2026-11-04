using Jason.Cli.Session;
using Jason.Contracts.Discovery;

namespace Jason.Cli.Tests.Session;

/// <summary>
/// macOS's opener once it has started, with the platform's own shell standing in for <c>/usr/bin/open</c>: it is on every
/// machine, and it opens nothing.
/// </summary>
public sealed class MacOpenerTests
{
    /// <summary>
    /// An opener that refuses says why on its standard error and exits with a code other than zero; the problem carries
    /// the code and the first line it wrote, so the person reads the opener's own reason.
    /// </summary>
    [Fact]
    public void An_opener_that_exits_other_than_zero_gives_its_first_error_line_with_the_code()
    {
        string[] fake = OperatingSystem.IsWindows()
            ? [Path.Combine(Environment.SystemDirectory, "cmd.exe"), "/d", "/c", "echo The application cannot be opened. 1>&2&echo a second line 1>&2&exit /b 1"]
            : ["/bin/sh", "-c", "echo 'The application cannot be opened.' >&2; echo 'a second line' >&2; exit 1"];
        var start = TerminalStartInfo.For(TerminalPlatform.MacOs, fake, Path.GetTempPath());

        using var process = ChildProcess.Start(start);
        Assert.NotNull(process);

        var problem = MacOpener.Problem(process, "open", TimeSpan.FromSeconds(30));

        Assert.Equal("open exited with code 1: The application cannot be opened.", problem);
    }
}
