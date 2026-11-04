using Jason.Cli.Session;

namespace Jason.Cli.Tests.Session;

/// <summary>
/// The terminal a person's own agent host is started in, composed as plain strings for every operating system on every
/// operating system: which program is the terminal, the argument list it is given and the folder it starts in. Nothing
/// here starts anything; a stand-in file in a temporary folder is all a terminal is to these rows.
/// </summary>
public sealed class TerminalPlanTests : IDisposable
{
    private const string Item = "apr_01JBZ3X7Q9K2M4N6P8R0S2T4V6";

    /// <summary>A prompt holding every character a shell, a terminal's own parser or a host's flags could misread.</summary>
    private const string Prompt = "Jason is waiting; read \"this\" and 'that'.\n\n1. Run jason approval get <id> & tell me | $HOME `x` %PATH% \\;";

    private readonly string _root = Directory.CreateTempSubdirectory("jason-terminal-plan-").FullName;

    private static readonly string[] Host = ["C:/Tools/claude.exe", Prompt];

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Windows_starts_the_console_host_from_the_system_directory_with_the_host_after_its_end_of_options()
    {
        var system = Folder("System32");
        var conhost = StandIn(system, "conhost.exe");

        var found = TerminalPlan.Find(TerminalPlatform.Windows, system, terminalVariable: null, searchPath: null);
        Assert.NotNull(found);
        Assert.Equal("conhost", found.Name);
        Assert.Equal(conhost, found.Program);

        var launch = TerminalPlan.Compose(TerminalPlatform.Windows, found, "C:/Jason/campaigns/Spring launch/", Host, Item);

        Assert.Equal("conhost", launch.Terminal);
        Assert.Equal([conhost, "--", "C:/Tools/claude.exe", Prompt], launch.Argv);
        Assert.Equal("C:/Jason/campaigns/Spring launch/", launch.WorkingDirectory);
        Assert.Null(launch.Script);
        Assert.Null(launch.ScriptName);
    }

    /// <summary>
    /// The console host is never taken from the search path: a click runs under the person's PATH, and a program of that
    /// name planted earlier on it must not be what the click starts.
    /// </summary>
    [Fact]
    public void Windows_never_takes_the_console_host_from_the_search_path()
    {
        var planted = Folder("planted");
        StandIn(planted, "conhost.exe");
        var system = Folder("System32");

        Assert.Null(TerminalPlan.Find(TerminalPlatform.Windows, system, terminalVariable: null, searchPath: planted));
        Assert.Null(TerminalPlan.Find(TerminalPlatform.Windows, systemDirectory: null, terminalVariable: null, searchPath: planted));
        Assert.Null(TerminalPlan.Find(TerminalPlatform.Windows, systemDirectory: "", terminalVariable: null, searchPath: planted));
    }

    /// <summary>
    /// macOS's opener is named by its absolute path, never looked up, and Terminal is given a script, since
    /// <c>open -a Terminal</c> takes no command of its own; the script's folder is made only when it starts, so the plan
    /// names the script by its file name.
    /// </summary>
    [Fact]
    public void MacOs_opens_a_command_script_in_terminal_through_the_opener_by_its_absolute_path()
    {
        var planted = Folder("planted");
        StandIn(planted, "open");

        var found = TerminalPlan.Find(TerminalPlatform.MacOs, systemDirectory: null, terminalVariable: "kitty", searchPath: planted);
        Assert.NotNull(found);
        Assert.Equal("/usr/bin/open", found.Program);

        var launch = TerminalPlan.Compose(TerminalPlatform.MacOs, found, "/Users/marta/Jason/campaigns/spring", Host, Item);

        Assert.Equal("Terminal", launch.Terminal);
        Assert.Equal(["/usr/bin/open", "-a", "Terminal", "jason-" + Item + ".command"], launch.Argv);
        Assert.Equal("jason-" + Item + ".command", launch.ScriptName);
        Assert.Equal("/Users/marta/Jason/campaigns/spring", launch.WorkingDirectory);
        Assert.NotNull(launch.Script);
    }

    /// <summary>
    /// The script removes itself and its folder before anything else, enters the folder or stops, and replaces itself with
    /// the host; every word is single-quoted, with each apostrophe written as the four characters that close the quote,
    /// write one apostrophe and reopen it, so nothing inside is special to the shell Terminal runs it with.
    /// </summary>
    [Fact]
    public void The_macos_script_quotes_every_word_so_the_shell_reads_nothing_in_it()
    {
        var found = TerminalPlan.Find(TerminalPlatform.MacOs, null, null, null)!;

        var launch = TerminalPlan.Compose(TerminalPlatform.MacOs, found, "/Users/marta/Jason/it's here", ["/opt/homebrew/bin/claude", Prompt], Item);

        var expected = string.Join(
            "\n",
            "#!/bin/sh",
            "rm -f -- \"$0\"",
            "rmdir -- \"${0%/*}\" 2>/dev/null",
            "cd -- '/Users/marta/Jason/it'\\''s here' || exit 1",
            "exec '/opt/homebrew/bin/claude' 'Jason is waiting; read \"this\" and '\\''that'\\''.\n\n1. Run jason approval get <id> & tell me | $HOME `x` %PATH% \\;'",
            string.Empty);
        Assert.Equal(expected, launch.Script);
    }

    [Theory]
    [InlineData("plain", "'plain'")]
    [InlineData("", "''")]
    [InlineData("'", "''\\'''")]
    [InlineData("a b", "'a b'")]
    [InlineData("$(rm -rf ~)", "'$(rm -rf ~)'")]
    public void A_word_is_single_quoted_with_each_apostrophe_closed_and_reopened(string word, string quoted)
    {
        Assert.Equal(quoted, TerminalPlan.ShellQuote(word));
    }

    /// <summary>kitty takes the program to run as its positional arguments and has no <c>-e</c>, so the host follows its name.</summary>
    [Fact]
    public void Linux_starts_the_terminal_the_terminal_variable_names_with_the_host_in_its_own_form()
    {
        var bin = Folder("bin");
        var kitty = StandIn(bin, "kitty");
        StandIn(bin, "x-terminal-emulator");

        var found = TerminalPlan.Find(TerminalPlatform.Linux, systemDirectory: null, terminalVariable: "kitty", searchPath: bin);
        Assert.NotNull(found);
        Assert.Equal("kitty", found.Name);
        Assert.Equal(kitty, found.Program);

        var launch = TerminalPlan.Compose(TerminalPlatform.Linux, found, "/home/marta/Jason/campaigns/spring launch", ["/usr/local/bin/codex", Prompt], Item);

        Assert.Equal("kitty", launch.Terminal);
        Assert.Equal([kitty, "/usr/local/bin/codex", Prompt], launch.Argv);
        Assert.Equal("/home/marta/Jason/campaigns/spring launch", launch.WorkingDirectory);
        Assert.Null(launch.Script);
    }

    /// <summary>
    /// Each terminal is given the host in the form it takes, by the program's file name: kitty and foot take the program
    /// as positional arguments, gnome-terminal after <c>--</c>, wezterm after <c>start --</c>, and every other name after
    /// <c>-e</c>, as xterm, konsole, alacritty, xfce4-terminal, urxvt and Debian's <c>x-terminal-emulator</c> (Debian
    /// Policy 11.8.3) take it.
    /// </summary>
    [Theory]
    [InlineData("kitty", new string[0])]
    [InlineData("foot", new string[0])]
    [InlineData("gnome-terminal", new[] { "--" })]
    [InlineData("wezterm", new[] { "start", "--" })]
    [InlineData("xterm", new[] { "-e" })]
    [InlineData("konsole", new[] { "-e" })]
    [InlineData("alacritty", new[] { "-e" })]
    [InlineData("xfce4-terminal", new[] { "-e" })]
    [InlineData("urxvt", new[] { "-e" })]
    [InlineData("x-terminal-emulator", new[] { "-e" })]
    public void Linux_gives_each_terminal_the_host_in_the_form_it_takes(string name, string[] before)
    {
        var program = "/usr/bin/" + name;

        var launch = TerminalPlan.Compose(TerminalPlatform.Linux, new TerminalFound(name, program), "/home/marta/Jason", ["/usr/local/bin/codex", Prompt], Item);

        Assert.Equal([program, .. before, "/usr/local/bin/codex", Prompt], launch.Argv);
    }

    /// <summary>The form is chosen by the program's file name, wherever the program is and whatever the variable called it.</summary>
    [Fact]
    public void Linux_chooses_the_form_by_the_programs_file_name()
    {
        var launch = TerminalPlan.Compose(TerminalPlatform.Linux, new TerminalFound("term", "/opt/wezterm/bin/wezterm"), "/home/marta/Jason", ["/usr/local/bin/codex"], Item);

        Assert.Equal(["/opt/wezterm/bin/wezterm", "start", "--", "/usr/local/bin/codex"], launch.Argv);
    }

    [Fact]
    public void Linux_takes_a_terminal_named_by_its_absolute_path()
    {
        var elsewhere = Folder("elsewhere");
        var foot = StandIn(elsewhere, "foot");

        var found = TerminalPlan.Find(TerminalPlatform.Linux, null, terminalVariable: foot, searchPath: Folder("bin"));

        Assert.NotNull(found);
        Assert.Equal(foot, found.Program);
        Assert.Equal("foot", found.Name);
    }

    /// <summary>
    /// With no terminal variable, a blank one, one holding a space (a command line, not a program), one named by a relative
    /// path, or one that is not on the search path, the terminal is the system's <c>x-terminal-emulator</c>.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("kitty -1")]
    [InlineData("bin/kitty")]
    [InlineData("alacritty")]
    public void Linux_falls_to_the_systems_terminal_emulator(string? variable)
    {
        var bin = Folder("bin");
        StandIn(bin, "kitty");
        StandIn(bin, "kitty -1");
        var emulator = StandIn(bin, "x-terminal-emulator");

        var found = TerminalPlan.Find(TerminalPlatform.Linux, null, variable, bin);

        Assert.NotNull(found);
        Assert.Equal("x-terminal-emulator", found.Name);
        Assert.Equal(emulator, found.Program);
    }

    [Fact]
    public void Linux_finds_the_first_terminal_on_the_search_path_in_its_order()
    {
        var first = Folder("first");
        var second = Folder("second");
        StandIn(second, "x-terminal-emulator");
        var winner = StandIn(first, "x-terminal-emulator");

        var found = TerminalPlan.Find(TerminalPlatform.Linux, null, null, string.Join(Path.PathSeparator, Folder("empty"), first, second));

        Assert.Equal(winner, found!.Program);
    }

    [Fact]
    public void Linux_with_no_terminal_anywhere_finds_none()
    {
        Assert.Null(TerminalPlan.Find(TerminalPlatform.Linux, null, "kitty", Folder("bin")));
        Assert.Null(TerminalPlan.Find(TerminalPlatform.Linux, null, null, searchPath: null));
    }

    /// <summary>The script's name is made from the item's id, so an id that is not one is refused before it names a file.</summary>
    [Theory]
    [InlineData("apr_../../x")]
    [InlineData("apr_a b")]
    [InlineData("")]
    public void A_script_is_named_only_from_a_well_formed_id(string id)
    {
        var found = TerminalPlan.Find(TerminalPlatform.MacOs, null, null, null)!;

        Assert.ThrowsAny<ArgumentException>(() => TerminalPlan.Compose(TerminalPlatform.MacOs, found, "/tmp", Host, id));
    }

    /// <summary>
    /// On Linux and macOS the terminal holds none of the CLI's own streams: a caller that captures the CLI's output reads
    /// its end when the CLI exits, not when the person closes the window. Windows' console host is started as it was.
    /// Making a start info starts nothing.
    /// </summary>
    [Theory]
    [InlineData(TerminalPlatform.Linux, true)]
    [InlineData(TerminalPlatform.MacOs, true)]
    [InlineData(TerminalPlatform.Windows, false)]
    public void On_linux_and_macos_the_terminal_is_started_on_pipes_of_its_own(TerminalPlatform os, bool redirected)
    {
        var start = TerminalStartInfo.For(os, ["/usr/bin/x-terminal-emulator", "-e", "/usr/local/bin/codex", Prompt], "/home/marta/Jason");

        Assert.Equal(redirected, start.RedirectStandardInput);
        Assert.Equal(redirected, start.RedirectStandardOutput);
        Assert.Equal(redirected, start.RedirectStandardError);
        Assert.False(start.UseShellExecute);
        Assert.Equal("/usr/bin/x-terminal-emulator", start.FileName);
        Assert.Equal(["-e", "/usr/local/bin/codex", Prompt], start.ArgumentList);
        Assert.Equal("/home/marta/Jason", start.WorkingDirectory);
    }

    private string Folder(string name) => Directory.CreateDirectory(Path.Combine(_root, name)).FullName;

    private static string StandIn(string folder, string name)
    {
        var path = Path.Combine(folder, name);
        File.WriteAllBytes(path, []);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        return path;
    }
}
