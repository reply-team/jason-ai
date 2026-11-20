using Jason.Cli.Process;

namespace Jason.Cli.Tests.Process;

/// <summary>
/// Which program a link is handed to. Nothing here starts it: a test that let the real opener run would open a browser
/// on whoever runs the suite.
/// </summary>
public class PlatformUrlOpenerTests
{
    /// <summary>
    /// The opener is named by its full path on every system: Windows' own handler in the system folder, macOS's opener
    /// where macOS keeps it, and on Linux xdg-open as the search path's full entries give it, or none. A bare name would be
    /// looked up by the operating system in the working folder too, and on Windows in the folder of the program asking.
    /// </summary>
    [Fact]
    public void The_opener_is_named_by_its_full_path()
    {
        var opener = PlatformUrlOpener.Opener(Environment.GetEnvironmentVariable("PATH"), Environment.GetEnvironmentVariable("PATHEXT"));

        if (OperatingSystem.IsWindows())
        {
            Assert.Equal(Path.Combine(Environment.SystemDirectory, "rundll32.exe"), opener);
        }
        else if (OperatingSystem.IsMacOS())
        {
            Assert.Equal("/usr/bin/open", opener);
        }
        else
        {
            Assert.True(opener is null || Path.IsPathFullyQualified(opener), $"'{opener}' is not a full path.");
        }
    }

    /// <summary>
    /// On Linux xdg-open is taken from an entry of the search path that is a full path, and never from an empty one,
    /// <c>.</c> or a relative folder, which name wherever the process happens to stand; with none of those it is not
    /// found, and the link is not handed on, as with no xdg-open at all.
    /// </summary>
    [Fact]
    public void On_linux_xdg_open_is_found_on_the_search_path_s_full_entries_alone()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "xdg-open is the opener on Linux alone.");

        var folder = Directory.CreateTempSubdirectory("jason-opener-").FullName;
        try
        {
            var program = Path.Combine(folder, "xdg-open");
            File.WriteAllText(program, "#!/bin/sh\nexit 0\n");
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(program, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }

            Assert.Equal(program, PlatformUrlOpener.Opener(string.Join(':', string.Empty, ".", "relative", folder), null));
            Assert.Null(PlatformUrlOpener.Opener(string.Join(':', string.Empty, ".", "relative"), null));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    /// The opener is handed streams of the CLI's own, never its caller's: what it starts inherits them, and outside GNOME
    /// and KDE xdg-open runs Jason Desktop to its end, so a caller that reads <c>jason ui open</c>'s output to its end, an
    /// agent's tool call among them, would wait until the window closed. Seen on Ubuntu with no desktop session. Breaks if
    /// the opener's start inherits its caller's streams again; that the start closes the input and drains the output is
    /// the start's own code, which no test runs without starting a real opener.
    /// </summary>
    [Fact]
    public void The_opener_gets_streams_of_its_own_and_none_of_its_callers()
    {
        var opener = Path.Combine(Path.GetTempPath(), "opener");

        var start = PlatformUrlOpener.StartOf(opener, "jason-desktop://attention");

        Assert.Equal(opener, start.FileName);
        Assert.True(start.RedirectStandardInput);
        Assert.True(start.RedirectStandardOutput);
        Assert.True(start.RedirectStandardError);
        Assert.Equal("jason-desktop://attention", start.ArgumentList[^1]);
    }

    /// <summary>
    /// On Linux nothing is handed to xdg-open outside a graphical session: with neither <c>DISPLAY</c> nor
    /// <c>WAYLAND_DISPLAY</c> it never asks which program takes the address and goes straight to a list of text browsers.
    /// Breaks if the predicate the opener's start asks takes a session with no display for one.
    /// </summary>
    [Theory]
    [InlineData(null, null, false)]
    [InlineData("", "", false)]
    [InlineData(":0", null, true)]
    [InlineData(null, "wayland-0", true)]
    public void On_linux_an_address_is_handed_on_only_in_a_graphical_session(string? display, string? wayland, bool handed) =>
        Assert.Equal(handed, PlatformUrlOpener.InGraphicalSession(display, wayland));
}
