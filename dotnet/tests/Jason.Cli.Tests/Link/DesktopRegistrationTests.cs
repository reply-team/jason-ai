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
    /// On Linux the desktop takes links where xdg-open's generic mode would run Jason Desktop for the scheme: the entry
    /// <c>xdg-mime query default x-scheme-handler/jason-desktop</c> names (an account's or a system's <c>mimeapps.list</c>
    /// default whose program is there, else the first <c>defaults.list</c> or <c>mimeinfo.cache</c> line, taken unchecked,
    /// else the entry of highest <c>InitialPreference</c> that declares the type), found as xdg-open finds it, starting its
    /// program, and in the desktop's own form: <c>MimeType</c> declaring the scheme and <c>Exec=&lt;bare full path&gt; -- %U</c>.
    /// Breaks if a scan of entries is trusted where xdg-open would run another program, or nothing and then a browser: an
    /// account default naming another program, a stale cache line, a quoted <c>Exec</c> xdg-open cannot start, a program
    /// with no execute bit, which <c>command -v</c> refuses.
    /// </summary>
    [Theory]
    [InlineData("deb", "deb", null)]
    [InlineData("appimage", "appimage", null)]
    [InlineData("appimage-no-cache", "appimage", null)]
    [InlineData("account-default-jason", "appimage", null)]
    [InlineData("account-default-other", null, CliErrors.DesktopLinksElsewhere)]
    [InlineData("account-default-gone-program", "deb", null)]
    [InlineData("desktop-specific-default", null, CliErrors.DesktopLinksElsewhere)]
    [InlineData("config-dirs-default", null, CliErrors.DesktopLinksElsewhere)]
    [InlineData("data-dirs-default", null, CliErrors.DesktopLinksElsewhere)]
    [InlineData("stale-cache", null, CliErrors.DesktopLinksElsewhere)]
    [InlineData("defaults-list-first", null, CliErrors.DesktopLinksElsewhere)]
    [InlineData("higher-preference", null, CliErrors.DesktopLinksElsewhere)]
    [InlineData("quoted", null, CliErrors.DesktopLinksElsewhere)]
    [InlineData("not-executable", null, CliErrors.DesktopNotInstalled)]
    [InlineData("no-scheme", null, CliErrors.DesktopNotInstalled)]
    [InlineData("gone", null, CliErrors.DesktopNotInstalled)]
    [InlineData("none", null, CliErrors.DesktopNotInstalled)]
    public void On_linux_the_desktop_is_what_xdg_open_would_run_for_the_scheme(string state, string? runs, string? refusal)
    {
        var xdg = new FakeXdg(Root);
        var deb = Executable(Path.Combine(Root, "opt", "Jason", "jason-desktop-app"));
        var appImage = Executable(Path.Combine(Root, "home", "Applications", "jason-desktop-linux-x64.AppImage"));
        Executable(Path.Combine(xdg.Bin, "firefox"));
        const string DebEntry = "jason-desktop-app.desktop";
        const string AppImageEntry = "jason-desktop.desktop";
        var debText = Entry($"Exec={deb} -- %U", "MimeType=x-scheme-handler/jason-desktop;");
        var appImageText = Entry($"TryExec={appImage}", $"Exec={appImage} -- %U", "MimeType=x-scheme-handler/jason-desktop;");
        var otherText = Entry("Exec=firefox %u", "MimeType=text/html;x-scheme-handler/jason-desktop;");
        var currentDesktop = string.Empty;
        switch (state)
        {
            case "deb":
                xdg.System(DebEntry, debText).SystemCache(DebEntry);
                break;
            case "appimage":
                xdg.Own(AppImageEntry, appImageText).OwnCache(AppImageEntry);
                break;
            case "appimage-no-cache":
                xdg.Own(AppImageEntry, appImageText);
                break;
            case "account-default-jason":
                xdg.Own(AppImageEntry, appImageText).Own("other.desktop", otherText).Default(xdg.ConfigHome, AppImageEntry).OwnCache("other.desktop");
                break;
            case "account-default-other":
                xdg.System(DebEntry, debText).SystemCache(DebEntry).Own("other.desktop", otherText).Default(xdg.ConfigHome, "other.desktop");
                break;
            case "account-default-gone-program":
                xdg.System(DebEntry, debText).SystemCache(DebEntry).Own("other.desktop", Entry($"Exec={Path.Combine(Root, "removed", "firefox")} %u")).Default(xdg.ConfigHome, "other.desktop");
                break;
            case "desktop-specific-default":
                currentDesktop = "Hyprland";
                xdg.System(DebEntry, debText).SystemCache(DebEntry).Own("other.desktop", otherText).Default(xdg.ConfigHome, "other.desktop", "hyprland-");
                break;
            case "config-dirs-default":
                xdg.System(DebEntry, debText).SystemCache(DebEntry).Own("other.desktop", otherText).Default(xdg.ConfigDir, "other.desktop");
                break;
            case "data-dirs-default":
                xdg.System(DebEntry, debText).SystemCache(DebEntry).Own("other.desktop", otherText).Default(Path.Combine(xdg.DataDir, "applications"), "other.desktop");
                break;
            case "stale-cache":
                xdg.System(DebEntry, debText).SystemCache(DebEntry).OwnCache("removed.desktop");
                break;
            case "defaults-list-first":
                xdg.Own(AppImageEntry, appImageText).Own("other.desktop", otherText).OwnCache(AppImageEntry).OwnDefaultsList("other.desktop");
                break;
            case "higher-preference":
                xdg.Own(AppImageEntry, appImageText).Own("other.desktop", otherText + "InitialPreference=5\n");
                break;
            case "quoted":
                xdg.Own(AppImageEntry, Entry($"Exec=\"{appImage}\" -- %U", "MimeType=x-scheme-handler/jason-desktop;")).OwnCache(AppImageEntry);
                break;
            case "not-executable":
                xdg.Own(AppImageEntry, appImageText).OwnCache(AppImageEntry);
                NotExecutable(appImage);
                break;
            case "no-scheme":
                xdg.Own(AppImageEntry, Entry($"Exec={appImage} %U"));
                break;
            case "gone":
                xdg.System(DebEntry, Entry($"Exec={Path.Combine(Root, "opt", "Gone", "jason-desktop-app")} -- %U", "MimeType=x-scheme-handler/jason-desktop;")).SystemCache(DebEntry);
                break;
        }

        var reader = xdg.Reader(currentDesktop);
        var expected = runs switch { "deb" => deb, "appimage" => appImage, _ => null };

        Assert.Equal(expected, reader.Program());
        Assert.Equal(refusal, DesktopLinks.Absent(reader)?.Code);
        if (refusal == CliErrors.DesktopLinksElsewhere)
        {
            Assert.Contains("Nothing was opened", DesktopLinks.Absent(reader)!.Message, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Where another program would take the links, the refusal says which entry takes them and how Jason Desktop gets them
    /// back; where a cache line names an entry that is gone, it names the cache and how it is written again. Breaks if a
    /// refusal there says no desktop is installed, which sends a person to install what is installed.
    /// </summary>
    [Fact]
    public void On_linux_links_that_go_elsewhere_are_refused_in_words_that_say_where_and_how_back()
    {
        var deb = Executable(Path.Combine(Root, "opt", "Jason", "jason-desktop-app"));
        var other = new FakeXdg(Root);
        Executable(Path.Combine(other.Bin, "firefox"));
        other.System("jason-desktop-app.desktop", Entry($"Exec={deb} -- %U", "MimeType=x-scheme-handler/jason-desktop;")).SystemCache("jason-desktop-app.desktop")
            .Own("other.desktop", Entry("Exec=firefox %u", "MimeType=x-scheme-handler/jason-desktop;")).Default(other.ConfigHome, "other.desktop");

        var elsewhere = DesktopLinks.Absent(other.Reader(string.Empty))!;

        Assert.Equal(
            $"Nothing was opened: this account opens jason-desktop: links with other.desktop ({Path.Combine(other.ConfigHome, "mimeapps.list")}), not with Jason Desktop; xdg-mime default jason-desktop-app.desktop x-scheme-handler/jason-desktop gives them back to it.",
            elsewhere.Message);

        File.Delete(Path.Combine(other.ConfigHome, "mimeapps.list"));
        other.OwnCache("removed.desktop");
        var stale = DesktopLinks.Absent(other.Reader(string.Empty))!;

        Assert.Equal(
            $"Nothing was opened: {Path.Combine(other.DataHome, "applications", "mimeinfo.cache")} names removed.desktop for jason-desktop: links, and no menu entry of that name starts a program here; update-desktop-database {Path.Combine(other.DataHome, "applications")} writes it again.",
            stale.Message);

        // A desktop's own list comes before the account's plain one, which is all `xdg-mime default` writes: its line is named.
        File.Delete(Path.Combine(other.DataHome, "applications", "mimeinfo.cache"));
        other.Default(other.ConfigHome, "other.desktop", "hyprland-");
        var ownList = DesktopLinks.Absent(other.Reader("Hyprland"))!;

        Assert.Equal(
            $"Nothing was opened: this account opens jason-desktop: links with other.desktop ({Path.Combine(other.ConfigHome, "hyprland-mimeapps.list")}), not with Jason Desktop; its x-scheme-handler/jason-desktop line in that file, changed to jason-desktop-app.desktop or taken out, gives them back to it.",
            ownList.Message);
    }

    /// <summary>
    /// Jason Desktop's menu entry that starts its program by a quoted path — an AppImage in a folder whose path holds a space
    /// or a quote — is refused in words that say so and what to do, never as a Jason Desktop that is not installed.
    /// </summary>
    [Fact]
    public void On_linux_an_entry_xdg_open_cannot_start_for_its_quoted_path_is_refused_in_its_own_words()
    {
        var xdg = new FakeXdg(Root);
        var appImage = Executable(Path.Combine(Root, "home", "My Apps", "jason-desktop-linux-x64.AppImage"));
        xdg.Own("jason-desktop.desktop", Entry($"Exec=\"{appImage}\" -- %U", "MimeType=x-scheme-handler/jason-desktop;")).OwnCache("jason-desktop.desktop");

        var refused = DesktopLinks.Absent(xdg.Reader(string.Empty))!;

        Assert.Equal(CliErrors.DesktopLinksElsewhere, refused.Code);
        Assert.Equal(
            $"Nothing was opened: Jason Desktop's menu entry {Path.Combine(xdg.DataHome, "applications", "jason-desktop.desktop")} starts {appImage} by a quoted path, which xdg-open outside GNOME and KDE cannot start; with the AppImage in a folder whose path holds no space or quote, the install line writes an entry that can.",
            refused.Message);
    }

    /// <summary>An XDG tree under the run's folder: the account's config and data homes, one config and one data dir, and a search path.</summary>
    private sealed class FakeXdg(string root)
    {
        public string ConfigHome { get; } = Directory.CreateDirectory(Path.Combine(root, "home", ".config")).FullName;

        public string ConfigDir { get; } = Directory.CreateDirectory(Path.Combine(root, "etc", "xdg")).FullName;

        public string DataHome { get; } = Directory.CreateDirectory(Path.Combine(root, "home", ".local", "share")).FullName;

        public string DataDir { get; } = Directory.CreateDirectory(Path.Combine(root, "usr", "share")).FullName;

        public string Bin { get; } = Directory.CreateDirectory(Path.Combine(root, "xdg-bin")).FullName;

        public FakeXdg Own(string id, string text) => Write(Path.Combine(DataHome, "applications", id), text);

        public FakeXdg System(string id, string text) => Write(Path.Combine(DataDir, "applications", id), text);

        public FakeXdg OwnCache(string id) => Write(Path.Combine(DataHome, "applications", "mimeinfo.cache"), $"[MIME Cache]\nx-scheme-handler/jason-desktop={id};\n");

        public FakeXdg SystemCache(string id) => Write(Path.Combine(DataDir, "applications", "mimeinfo.cache"), $"[MIME Cache]\ntext/plain=editor.desktop;\nx-scheme-handler/jason-desktop={id};\n");

        public FakeXdg OwnDefaultsList(string id) => Write(Path.Combine(DataHome, "applications", "defaults.list"), $"[Default Applications]\nx-scheme-handler/jason-desktop={id}\n");

        public FakeXdg Default(string folder, string id, string prefix = "") =>
            Write(Path.Combine(folder, prefix + "mimeapps.list"), $"[Added Associations]\nx-scheme-handler/jason-desktop=nothing.desktop;\n[Default Applications]\ntext/html=other.desktop;\nx-scheme-handler/jason-desktop={id};\n");

        public LinuxDesktopRegistration Reader(string currentDesktop) =>
            new(new XdgPlaces(ConfigHome, [ConfigDir], DataHome, [DataDir], currentDesktop.Length == 0 ? [] : [currentDesktop], string.Empty), new Jason.Contracts.Programs.ProgramSearchPath(Bin, null));

        private FakeXdg Write(string path, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
            return this;
        }
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

    /// <summary>A program a search path finds, as <c>command -v</c> finds one: a file that may be run.</summary>
    private static string Executable(string path)
    {
        Program(path);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        return path;
    }

    /// <summary>A file that may not be run: a download a browser saved, which <c>command -v</c> refuses.</summary>
    private static void NotExecutable(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
        }
    }

    private static string Program(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "not a program");
        return path;
    }
}
