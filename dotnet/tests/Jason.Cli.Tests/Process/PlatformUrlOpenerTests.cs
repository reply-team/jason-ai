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
}
