using System.Runtime.Versioning;
using Jason.Cli.Installation;
using Jason.Cli.Link;
using Jason.Cli.Tests.Process;
using Jason.Cli.Uninstall;
using Jason.Contracts.Programs;

namespace Jason.Cli.Tests.Installation;

/// <summary>
/// An AppImage's menu entry in its one form, which the move writes and <c>jason install</c> brings an earlier one of
/// Jason's to: hidden once the file is gone, declaring the desktop's link scheme, and starting the file bare with
/// <c>-- %U</c> wherever its path needs no quoting, so that xdg-open in every mode starts it. Each test is given its own
/// folders and a search path; none reads this machine's.
/// </summary>
public sealed class AppImageMenuEntryTests : IDisposable
{
    private readonly TempPaths _dir = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string Root => _dir.Paths.Root;

    public void Dispose() => _dir.Dispose();

    /// <summary>
    /// The entry is the carried one with <c>TryExec</c> and <c>MimeType</c> first in its group and <c>Exec</c> starting the
    /// file with <c>--</c> before the address; the path bare where nothing in it needs quoting, else quoted as the
    /// specification reads it. Breaks if a path xdg-open's generic mode can start is quoted, since it then starts nothing and
    /// hands the address to a browser, or if a path that needs quoting is left bare.
    /// </summary>
    [Theory]
    [InlineData("/home/someone/Applications/jason-desktop-linux-x64.AppImage", "/home/someone/Applications/jason-desktop-linux-x64.AppImage")]
    [InlineData("/home/Jo Smith/Applications/jason-desktop-linux-x64.AppImage", "\"/home/Jo Smith/Applications/jason-desktop-linux-x64.AppImage\"")]
    [InlineData("/home/jo$x/Applications/jason-desktop-linux-x64.AppImage", "\"/home/jo\\\\$x/Applications/jason-desktop-linux-x64.AppImage\"")]
    [InlineData("/home/100%/Applications/jason-desktop-linux-x64.AppImage", "\"/home/100%%/Applications/jason-desktop-linux-x64.AppImage\"")]
    [InlineData("/home/Юля/Applications/jason-desktop-linux-x64.AppImage", "/home/Юля/Applications/jason-desktop-linux-x64.AppImage")]
    [InlineData("/home/Jo\u2003Doe/Applications/jason-desktop-linux-x64.AppImage", "/home/Jo\u2003Doe/Applications/jason-desktop-linux-x64.AppImage")]
    public void The_entry_takes_links_and_starts_the_file_bare_wherever_its_path_allows(string file, string exec)
    {
        var carried = "[Desktop Entry]\nName=Jason AI Harness\nExec=AppRun %U\nTerminal=false\nType=Application\nIcon=jason-desktop-app\nStartupWMClass=Jason AI Harness\nCategories=Office;\n";

        var entry = AppImageMenuEntry.Form(carried, file).Split('\n');

        Assert.Equal(["[Desktop Entry]", $"TryExec={DesktopRemoval.DesktopEntryString(file)}", "MimeType=x-scheme-handler/jason-desktop;"], entry[..3]);
        Assert.Equal($"Exec={exec} -- %U", Assert.Single(entry, line => line.StartsWith("Exec=", StringComparison.Ordinal)));
        Assert.Equal(file, DesktopRemoval.DesktopEntryProgram(string.Join('\n', entry)));
        Assert.Contains("Icon=jason-desktop-app", entry);
    }

    /// <summary>
    /// An entry 0.49.1's install line or move wrote — quoted <c>Exec</c> with no <c>--</c>, no <c>MimeType</c> — is brought
    /// to this form by <c>jason install</c>, the folder's cache written again, and the Desktop then takes its links, as the
    /// presence check behind <c>jason ui open</c> and a click reads them. Breaks if an install updated from 0.49.1 keeps an
    /// entry that takes no link. Where the cache tool is not there, or ran and failed, the words say which.
    /// </summary>
    [Theory]
    [InlineData("written", "")]
    [InlineData("failed", "; update-desktop-database failed to write the folder's cache, so GNOME may not find it until something does")]
    [InlineData("missing", "; update-desktop-database is not here to write the folder's cache, so GNOME may not find it until something does")]
    public async Task An_entry_in_0_49_1_s_form_is_brought_up_to_date_and_the_desktop_then_takes_its_links(string cache, string said)
    {
        var file = Path.Combine(Root, "home", "Applications", "jason-desktop-linux-x64.AppImage");
        Executable(file);
        var applications = Directory.CreateDirectory(Path.Combine(Root, "home", ".local", "share", "applications")).FullName;
        var menu = Path.Combine(applications, AppImageMenuEntry.Name);
        File.WriteAllText(menu, $"[Desktop Entry]\nTryExec={file}\nName=Jason AI Harness\nExec=\"{file}\" %U\nTerminal=false\nType=Application\nIcon=jason-desktop-app\n");
        var reader = Reader();
        Assert.Equal(CliErrors.DesktopNotInstalled, DesktopLinks.Absent(reader)?.Code);
        var (env, runner, bin) = Environment(exit: cache == "failed" ? 1 : 0);
        if (cache == "missing")
        {
            File.Delete(Path.Combine(bin, AppImageMenuEntry.CacheTool));
        }

        var brought = await AppImageMenuEntry.BringUpToDateAsync(env, applications, file, Ct);

        Assert.Equal((true, $"Brought Jason Desktop's menu entry {menu} up to date, so the system hands it its links{said}."), brought);
        Assert.Contains($"Exec={file} -- %U", File.ReadAllLines(menu));
        Assert.Equal(cache == "missing" ? 0 : 1, runner.Requested.Count);
        if (cache != "missing")
        {
            Assert.Equal((Path.Combine(bin, AppImageMenuEntry.CacheTool), applications), (runner.Requested[0].Program, Assert.Single(runner.Requested[0].Arguments)));
        }

        Assert.Null(DesktopLinks.Absent(reader));
        Assert.Equal(file, reader.Program());
        Assert.Equal([menu], Directory.EnumerateFiles(applications).Where(name => !name.EndsWith("mimeinfo.cache", StringComparison.Ordinal)));
    }

    /// <summary>
    /// Only Jason's own entry for this file is touched, and only where it differs: one that starts another program, or is
    /// already in this form, is left as it is and runs nothing. Breaks if <c>jason install</c> rewrites a person's own entry
    /// under that name, or runs the cache tool at every start.
    /// </summary>
    [Theory]
    [InlineData("another-program")]
    [InlineData("current")]
    [InlineData("none")]
    public async Task Anything_but_jason_s_own_entry_in_an_earlier_form_is_left_alone(string state)
    {
        var file = Program(Path.Combine(Root, "home", "Applications", "jason-desktop-linux-x64.AppImage"));
        var applications = Directory.CreateDirectory(Path.Combine(Root, "home", ".local", "share", "applications")).FullName;
        var menu = Path.Combine(applications, AppImageMenuEntry.Name);
        var text = state switch
        {
            "another-program" => "[Desktop Entry]\nName=Mine\nExec=/opt/mine/run %U\n",
            "current" => AppImageMenuEntry.Form("[Desktop Entry]\nName=Jason AI Harness\nExec=AppRun %U\n", file),
            _ => null,
        };
        if (text is not null)
        {
            File.WriteAllText(menu, text);
        }

        var (env, runner, _) = Environment();

        Assert.Null(await AppImageMenuEntry.BringUpToDateAsync(env, applications, file, Ct));
        Assert.Equal(text, File.Exists(menu) ? File.ReadAllText(menu) : null);
        Assert.Empty(runner.Requested);
    }

    private LinuxDesktopRegistration Reader() =>
        new(new XdgPlaces(Path.Combine(Root, "home", ".config"), [], Path.Combine(Root, "home", ".local", "share"), [Path.Combine(Root, "usr", "share")], [], string.Empty), new ProgramSearchPath(Path.Combine(Root, "bin"), null));

    /// <summary>An environment whose search path has the cache tool, run by a runner that only records it.</summary>
    private (CliEnvironment Env, FakeProgramRunner Runner, string Bin) Environment(int exit = 0)
    {
        var bin = Directory.CreateDirectory(Path.Combine(Root, "bin")).FullName;
        Executable(Path.Combine(bin, AppImageMenuEntry.CacheTool));
        var runner = new FakeProgramRunner(_ => new Jason.Cli.Process.ProgramResult(exit, string.Empty, exit == 0 ? string.Empty : "cache: refused", false));
        return (new CliEnvironment(new StringWriter(), new StringWriter(), _dir.Paths, Programs: runner, SearchPath: bin), runner, bin);
    }

    private static string Program(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "not a program");
        return path;
    }

    private static void Executable(string path)
    {
        Program(path);
        if (!OperatingSystem.IsWindows())
        {
            SetExecutable(path);
        }
    }

    [UnsupportedOSPlatform("windows")]
    private static void SetExecutable(string path) => File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
}
