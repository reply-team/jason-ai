using Jason.Cli.Link;

namespace Jason.Cli.Tests.Link;

/// <summary>
/// Whether Jason Desktop takes its links on this account, read per system from what the desktop's install leaves: on
/// Windows the registry command its start writes, on Linux the menu entry that declares the scheme, on macOS the
/// application in one of the two folders its install uses. Each reader is given its places; none reads this machine's.
/// </summary>
public sealed class DesktopRegistrationTests : IDisposable
{
    private readonly TempPaths _dir = new();

    private string Root => _dir.Paths.Root;

    public void Dispose() => _dir.Dispose();

    /// <summary>
    /// The registry command in the form the desktop's start writes names its program: quoted where the path needs it, bare
    /// where it does not, then <c>--</c> and the quoted address. Any other form names none, and a program named by a
    /// relative path or gone from its place is no desktop. Breaks if a command the desktop does not write is taken.
    /// </summary>
    [Theory]
    [InlineData("quoted", true)]
    [InlineData("bare", true)]
    [InlineData("gone", false)]
    [InlineData("relative", false)]
    [InlineData("empty", false)]
    [InlineData("none", false)]
    [InlineData("other-form", false)]
    [InlineData("unclosed-quote", false)]
    public void On_windows_the_registered_command_names_the_desktop_only_in_its_own_form(string form, bool present)
    {
        var spaced = Program(Path.Combine(Root, "Jason Desktop", "Jason.exe"));
        var plain = Program(Path.Combine(Root, "Jason.exe"));
        var command = form switch
        {
            "quoted" => "\"" + spaced + "\" -- \"%1\"",
            "bare" => plain + " -- \"%1\"",
            "gone" => "\"" + Path.Combine(Root, "removed", "Jason.exe") + "\" -- \"%1\"",
            "relative" => "\"Jason.exe\" -- \"%1\"",
            "empty" => string.Empty,
            "none" => null,
            "other-form" => "\"" + spaced + "\" --inspect \"%1\"",
            _ => "\"" + spaced + " -- \"%1\"",
        };

        Assert.Equal(present, DesktopLinks.Present(new FakeRegistration(DesktopLinks.WindowsProgram(command))));
    }

    /// <summary>
    /// On Linux the desktop takes links where a menu entry declares its scheme and starts a program by a bare full path with
    /// <c>--</c> before the address, the deb's form: the form xdg-open starts in every mode. An entry that declares no scheme
    /// (an AppImage's), or quotes its program, which xdg-open outside GNOME and KDE never starts and then hands the address
    /// to a browser, is no desktop. Breaks if an entry xdg-open would not start is taken, since a browser could then open.
    /// </summary>
    [Theory]
    [InlineData("deb", true)]
    [InlineData("no-scheme", false)]
    [InlineData("quoted", false)]
    [InlineData("other-group", false)]
    [InlineData("gone", false)]
    public void On_linux_a_menu_entry_that_declares_the_scheme_in_the_debs_form_is_the_desktop(string entry, bool present)
    {
        var program = Program(Path.Combine(Root, "opt", "Jason", "jason-desktop-app"));
        var gone = Path.Combine(Root, "opt", "Gone", "jason-desktop-app");
        var text = entry switch
        {
            "deb" => Entry($"Exec={program} -- %U", "MimeType=x-scheme-handler/jason-desktop;"),
            "no-scheme" => Entry($"Exec=\"{program}\" %U"),
            "quoted" => Entry($"Exec=\"{program}\" -- %U", "MimeType=x-scheme-handler/jason-desktop;"),
            "other-group" => Entry($"Exec={program} -- %U") + "\n[Desktop Action New]\nMimeType=x-scheme-handler/jason-desktop;\n",
            _ => Entry($"Exec={gone} -- %U", "MimeType=x-scheme-handler/jason-desktop;"),
        };
        var applications = Directory.CreateDirectory(Path.Combine(Root, "share", "applications")).FullName;
        File.WriteAllText(Path.Combine(applications, "jason-desktop-app.desktop"), text);

        var found = new LinuxDesktopRegistration([Path.Combine(Root, "empty", "applications"), applications]).Program();

        Assert.Equal(present, DesktopLinks.Present(new FakeRegistration(found)));
        Assert.Equal(present ? program : null, present ? found : null);
    }

    /// <summary>
    /// On macOS the bundle declares the scheme, so the desktop takes links where its application is in one of the two
    /// folders its install uses: the account's own Applications, else the shared one.
    /// </summary>
    [Fact]
    public void On_macos_the_application_in_either_folder_is_the_desktop()
    {
        var own = Path.Combine(Root, "home", "Applications");
        var shared = Path.Combine(Root, "Applications");
        var reader = new MacDesktopRegistration([own, shared]);
        Assert.False(DesktopLinks.Present(reader));

        var executable = Program(Path.Combine(shared, "Jason.app", "Contents", "MacOS", "Jason"));
        Assert.Equal(executable, reader.Program());
        Assert.True(DesktopLinks.Present(reader));
    }

    private static string Entry(params string[] lines) =>
        "[Desktop Entry]\nName=Jason\nType=Application\n" + string.Concat(lines.Select(line => line + "\n"));

    private static string Program(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "not a program");
        return path;
    }
}
