using Jason.Cli.Session;
using Jason.Cli.Tests.Process;
using Jason.Contracts.Execution;

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

    /// <summary>
    /// Terminal is started by the system, not by the CLI, so nothing of the CLI's environment reaches the script's shell:
    /// the script exports the data directory the CLI runs on and puts the folder of the executable that opened it first
    /// on Terminal's own PATH, in two lines before the <c>exec</c>, so the host's own <c>jason</c> commands are this
    /// installation's, run on its data.
    /// </summary>
    [Fact]
    public void The_macos_script_exports_the_data_directory_and_puts_the_executables_folder_first_before_the_exec()
    {
        var found = TerminalPlan.Find(TerminalPlatform.MacOs, null, null, null)!;

        var launch = TerminalPlan.Compose(TerminalPlatform.MacOs, found, "/Users/marta/Jason/campaigns/spring", ["/opt/homebrew/bin/claude", "Go."], Item, Own);

        Assert.Equal(
            MacScript("/Users/marta/Jason/campaigns/spring", "export JASON_DATA_DIR='/Users/marta/jason-test'", "export PATH='/Users/marta/.jason/bin'\"${PATH:+:$PATH}\""),
            launch.Script);
        Assert.Null(launch.Variables);
    }

    /// <summary>A launch composed with nothing of an installation's carries nothing: the script is the plain one, byte for byte.</summary>
    [Fact]
    public void The_macos_script_composed_with_no_installation_exports_nothing()
    {
        var found = TerminalPlan.Find(TerminalPlatform.MacOs, null, null, null)!;

        var launch = TerminalPlan.Compose(TerminalPlatform.MacOs, found, "/Users/marta/Jason/campaigns/spring", ["/opt/homebrew/bin/claude", "Go."], Item);

        Assert.Equal(MacScript("/Users/marta/Jason/campaigns/spring"), launch.Script);
    }

    /// <summary>
    /// The data directory and the executable's folder are paths a person gave, so each is quoted by the one function that
    /// quotes the folder and the host's words: a space, an apostrophe, a dollar sign and a line break each stay inside the
    /// single quotes.
    /// </summary>
    [Theory]
    [InlineData("/Users/marta/jason test", "'/Users/marta/jason test'")]
    [InlineData("/Users/marta/marta's jason", "'/Users/marta/marta'\\''s jason'")]
    [InlineData("/Users/marta/$HOME/jason", "'/Users/marta/$HOME/jason'")]
    [InlineData("/Users/marta/jason\nexit 7", "'/Users/marta/jason\nexit 7'")]
    public void The_exported_paths_are_quoted_like_every_other_word_of_the_script(string path, string quoted)
    {
        var found = TerminalPlan.Find(TerminalPlatform.MacOs, null, null, null)!;

        var launch = TerminalPlan.Compose(TerminalPlatform.MacOs, found, "/Users/marta/Jason/campaigns/spring", ["/opt/homebrew/bin/claude", "Go."], Item, new OwnJason(path, path, null));

        Assert.Equal(quoted, TerminalPlan.ShellQuote(path));
        Assert.Equal(MacScript("/Users/marta/Jason/campaigns/spring", "export JASON_DATA_DIR=" + quoted, "export PATH=" + quoted + "\"${PATH:+:$PATH}\""), launch.Script);
    }

    /// <summary>
    /// The data directory and PATH are all the script carries of the environment: no other variable is named in it, and the
    /// CLI's own search path is never written there, since the PATH the folder goes before is Terminal's.
    /// </summary>
    [Fact]
    public void The_macos_script_names_no_other_variable()
    {
        var found = TerminalPlan.Find(TerminalPlatform.MacOs, null, null, null)!;
        var without = TerminalPlan.Compose(TerminalPlatform.MacOs, found, "/Users/marta/Jason/campaigns/spring", ["/opt/homebrew/bin/claude", "Go."], Item).Script!;

        var with = TerminalPlan.Compose(TerminalPlatform.MacOs, found, "/Users/marta/Jason/campaigns/spring", ["/opt/homebrew/bin/claude", "Go."], Item, Own with { SearchPath = "/opt/cli-only/bin" }).Script!;

        var added = with.Split('\n').Except(without.Split('\n')).ToArray();
        Assert.Equal(["export JASON_DATA_DIR='/Users/marta/jason-test'", "export PATH='/Users/marta/.jason/bin'\"${PATH:+:$PATH}\""], added);
        Assert.Equal(without.Split('\n').Length + 2, with.Split('\n').Length);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(with, @"\bexport\b").Count);
        Assert.DoesNotContain("cli-only", with, StringComparison.Ordinal);
        Assert.DoesNotContain("JASON_", without, StringComparison.Ordinal);
    }

    /// <summary>
    /// Where no executable of this installation is known (a build run through <c>dotnet</c>), the data directory is still
    /// passed and PATH is left as it is, on every system.
    /// </summary>
    [Theory]
    [InlineData(TerminalPlatform.Windows)]
    [InlineData(TerminalPlatform.Linux)]
    [InlineData(TerminalPlatform.MacOs)]
    public void With_no_executables_folder_the_data_directory_is_passed_and_path_is_left_alone(TerminalPlatform os)
    {
        var own = Own with { ExecutableFolder = null };

        var launch = TerminalPlan.Compose(os, TerminalOf(os), "/home/marta/Jason", ["/usr/local/bin/codex"], Item, own);

        switch (os)
        {
            case TerminalPlatform.Windows:
                Assert.Equal(new Dictionary<string, string> { ["JASON_DATA_DIR"] = own.DataDirectory }, launch.Variables);
                break;
            case TerminalPlatform.Linux:
                Assert.Equal(["/usr/bin/kitty", "/usr/bin/env", "JASON_DATA_DIR=" + own.DataDirectory, "/usr/local/bin/codex"], launch.Argv);
                break;
            default:
                Assert.Equal(MacScript("/home/marta/Jason", "export JASON_DATA_DIR='/Users/marta/jason-test'").Replace("'/opt/homebrew/bin/claude' 'Go.'", "'/usr/local/bin/codex'", StringComparison.Ordinal), launch.Script);
                break;
        }
    }

    /// <summary>
    /// On Windows the console host is the CLI's own child, and it starts the host as its own: the launch hands it the data
    /// directory and a PATH with the executable's folder first, then the CLI's search path, in the environment it is
    /// started with. Its argument list is the plain one: nothing is added to what the host is given.
    /// </summary>
    [Fact]
    public void Windows_starts_the_console_host_with_the_data_directory_and_the_executables_folder_first()
    {
        var terminal = TerminalOf(TerminalPlatform.Windows);
        var own = new OwnJason("C:/Users/marta/jason-test", "C:/Users/marta/AppData/Local/Programs/Jason", "C:/Other/bin;C:/Windows/System32");

        var launch = TerminalPlan.Compose(TerminalPlatform.Windows, terminal, "C:/Jason", ["C:/Tools/claude.exe", Prompt], Item, own);

        Assert.Equal([terminal.Program, "--", "C:/Tools/claude.exe", Prompt], launch.Argv);
        Assert.Equal(
            new Dictionary<string, string>
            {
                ["JASON_DATA_DIR"] = "C:/Users/marta/jason-test",
                ["PATH"] = "C:/Users/marta/AppData/Local/Programs/Jason;C:/Other/bin;C:/Windows/System32",
            },
            launch.Variables);
        Assert.Null(launch.Script);
    }

    /// <summary>
    /// On Linux the emulator may hand the command to a server that is already running and runs it in that server's
    /// environment, so the variables travel on the command itself: <c>/usr/bin/env</c>, which sets them and replaces itself
    /// with the host, no shell between, after the emulator's own form and before the host's words, on every emulator the
    /// plan knows. Nothing is added to the host's own arguments.
    /// </summary>
    [Theory]
    [InlineData("kitty", new string[0])]
    [InlineData("foot", new string[0])]
    [InlineData("gnome-terminal", new[] { "--" })]
    [InlineData("xfce4-terminal", new[] { "-x" })]
    [InlineData("mate-terminal", new[] { "-x" })]
    [InlineData("wezterm", new[] { "start", "--cwd", "<folder>", "--" })]
    [InlineData("x-terminal-emulator", new[] { "-e" })]
    public void Linux_gives_each_terminal_the_data_directory_and_the_executables_folder_first_through_env(string name, string[] before)
    {
        var program = "/usr/bin/" + name;
        const string Campaign = "/home/marta/Jason/projects/acme corp";
        var own = new OwnJason("/home/marta/jason test", "/home/marta/.local/bin", "/usr/local/bin:/usr/bin");

        var launch = TerminalPlan.Compose(TerminalPlatform.Linux, new TerminalFound(name, program), Campaign, ["/usr/local/bin/codex", Prompt], Item, own);

        Assert.Equal(
            [
                program,
                .. before.Select(word => word == "<folder>" ? Campaign : word),
                "/usr/bin/env",
                "JASON_DATA_DIR=/home/marta/jason test",
                "PATH=/home/marta/.local/bin:/usr/local/bin:/usr/bin",
                "/usr/local/bin/codex",
                Prompt,
            ],
            launch.Argv);
        Assert.Null(launch.Variables);
        Assert.Null(launch.Script);
    }

    /// <summary>
    /// Another <c>jason</c> earlier on the CLI's search path loses in the opened terminal: the PATH the terminal is given
    /// finds the opening executable's folder first. Composed only; no terminal is started.
    /// </summary>
    [Theory]
    [InlineData(TerminalPlatform.Windows)]
    [InlineData(TerminalPlatform.Linux)]
    public void Another_jason_earlier_on_the_search_path_loses_in_the_opened_terminal(TerminalPlatform os)
    {
        Assert.SkipWhen(os == TerminalPlatform.Linux && OperatingSystem.IsWindows(), "A Windows path holds the drive's colon, the Linux separator; the Linux legs run this row.");
        var name = os == TerminalPlatform.Windows ? "jason.exe" : "jason";
        var planted = Folder("planted");
        StandIn(planted, name);
        var installed = Folder("installed");
        var own = StandIn(installed, name);
        var separator = os == TerminalPlatform.Windows ? ";" : ":";
        var cliPath = string.Join(separator, planted, Folder("system"));
        Assert.Equal(Path.Combine(planted, name), FirstOn(cliPath, separator, name));

        var launch = TerminalPlan.Compose(os, TerminalOf(os), "/home/marta/Jason", ["/usr/local/bin/codex"], Item, new OwnJason(_root, installed, cliPath));

        var given = os == TerminalPlatform.Windows
            ? launch.Variables!["PATH"]
            : launch.Argv.Single(word => word.StartsWith("PATH=", StringComparison.Ordinal))["PATH=".Length..];
        Assert.Equal(own, FirstOn(given, separator, name));
        Assert.Equal(installed + separator + cliPath, given);
    }

    /// <summary>
    /// The folder goes before the search path with one separator, and alone where the search path is empty: an empty entry
    /// would be the folder the terminal starts in, on PATH.
    /// </summary>
    [Theory]
    [InlineData(TerminalPlatform.Windows, "C:/bin;C:/other", "C:/jason;C:/bin;C:/other")]
    [InlineData(TerminalPlatform.Windows, null, "C:/jason")]
    [InlineData(TerminalPlatform.Windows, "", "C:/jason")]
    [InlineData(TerminalPlatform.Linux, "/usr/bin", "C:/jason:/usr/bin")]
    [InlineData(TerminalPlatform.Linux, "", "C:/jason")]
    public void The_executables_folder_goes_first_with_one_separator(TerminalPlatform os, string? searchPath, string expected)
    {
        Assert.Equal(expected, TerminalPlan.SearchPathFirst(os, "C:/jason", searchPath));
    }

    /// <summary>
    /// <c>/usr/bin/env</c> reads every word that holds <c>=</c> as a variable to set until the program it runs, so a host's
    /// program whose path holds one cannot be carried: it is refused before anything is started, never run as something
    /// else. Only Linux carries the variables on the command.
    /// </summary>
    [Fact]
    public void Linux_refuses_a_hosts_program_whose_path_holds_an_equals_sign()
    {
        string[] host = ["/opt/a=b/claude", "Go."];

        Assert.NotNull(TerminalPlan.Uncarried(TerminalPlatform.Linux, host));
        Assert.Null(TerminalPlan.Uncarried(TerminalPlatform.Windows, host));
        Assert.Null(TerminalPlan.Uncarried(TerminalPlatform.MacOs, host));
        Assert.Null(TerminalPlan.Uncarried(TerminalPlatform.Linux, ["/opt/claude", "a=b"]));
        Assert.ThrowsAny<ArgumentException>(() => TerminalPlan.Compose(TerminalPlatform.Linux, TerminalOf(TerminalPlatform.Linux), "/home/marta", host, Item, Own));
    }

    /// <summary>
    /// The opened terminal's PATH puts this jason's folder first, and a folder whose name holds the system's separator of
    /// PATH would be two entries there, neither of them the folder: refused on every system, by the system's separator
    /// alone, and never composed.
    /// </summary>
    [Theory]
    [InlineData(TerminalPlatform.Windows, @"C:\Tools\a;b", ';')]
    [InlineData(TerminalPlatform.Linux, "/opt/a:b", ':')]
    [InlineData(TerminalPlatform.MacOs, "/opt/a:b", ':')]
    public void A_jason_whose_folder_holds_the_separator_of_path_is_refused(TerminalPlatform os, string folder, char separator)
    {
        var own = Own with { ExecutableFolder = folder };
        string[] host = ["/opt/claude", "Go."];

        Assert.Equal(
            $"this jason's folder {folder} holds '{separator}', which separates the entries of PATH, so no PATH entry can name it, and the window's PATH could not put this jason first",
            TerminalPlan.Uncarried(os, host, own));
        Assert.ThrowsAny<ArgumentException>(() => TerminalPlan.Compose(os, TerminalOf(os), "/home/marta", host, Item, own));
    }

    /// <summary>The other system's separator is a character of the folder's own, and a build with no folder carries none.</summary>
    [Theory]
    [InlineData(TerminalPlatform.Windows, @"C:\Tools\a:b")]
    [InlineData(TerminalPlatform.Linux, "/opt/a;b")]
    [InlineData(TerminalPlatform.MacOs, "/opt/a;b")]
    [InlineData(TerminalPlatform.Windows, null)]
    public void A_folder_without_the_systems_separator_is_carried(TerminalPlatform os, string? folder)
    {
        Assert.Null(TerminalPlan.Uncarried(os, ["/opt/claude", "Go."], Own with { ExecutableFolder = folder }));
    }

    /// <summary>
    /// The start a terminal is given carries the launch's variables, and on Windows PATH replaces the variable whatever its
    /// spelling there (Windows' own is <c>Path</c>), so there is one. Making a start info starts nothing.
    /// </summary>
    [Fact]
    public void The_terminals_start_carries_the_launchs_variables()
    {
        // Every child is started by its full path, so the console host is named by one this machine reads as full.
        var start = TerminalStartInfo.For(
            TerminalPlatform.Windows,
            [Path.Combine(Path.GetTempPath(), "conhost.exe"), "--", "C:/Tools/claude.exe"],
            "C:/Jason",
            new Dictionary<string, string> { ["JASON_DATA_DIR"] = "C:/data", ["PATH"] = "C:/jason;C:/bin" });

        Assert.Equal("C:/data", start.Environment["JASON_DATA_DIR"]);
        Assert.Equal("C:/jason;C:/bin", Assert.Single(start.Environment, pair => string.Equals(pair.Key, "PATH", StringComparison.OrdinalIgnoreCase)).Value);
    }

    [Theory]
    [InlineData("plain", "'plain'")]
    [InlineData("", "''")]
    [InlineData("'", "''\\'''")]
    [InlineData("a b", "'a b'")]
    [InlineData("$(exit 7)", "'$(exit 7)'")]
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
    /// Each terminal is given the host in the form its vendor documents, by the program's file name: kitty and foot take
    /// the host's words after the program, gnome-terminal after <c>--</c>, xfce4-terminal and mate-terminal after
    /// <c>-x</c>, wezterm after <c>start --cwd &lt;folder&gt; --</c> (it takes the program's folder from <c>--cwd</c>, never
    /// from its own), and every other name, Debian's <c>x-terminal-emulator</c> included, after <c>-e</c> as separate
    /// arguments (Debian Policy 11.8.3). A <c>&lt;folder&gt;</c> in a row is the campaign's folder, one argument.
    /// </summary>
    [Theory]
    [InlineData("kitty", new string[0])]
    [InlineData("foot", new string[0])]
    [InlineData("gnome-terminal", new[] { "--" })]
    [InlineData("xfce4-terminal", new[] { "-x" })]
    [InlineData("mate-terminal", new[] { "-x" })]
    [InlineData("wezterm", new[] { "start", "--cwd", "<folder>", "--" })]
    [InlineData("x-terminal-emulator", new[] { "-e" })]
    public void Linux_gives_each_terminal_the_host_in_the_form_it_takes(string name, string[] before)
    {
        var program = "/usr/bin/" + name;
        const string Campaign = "/home/marta/Jason/projects/acme corp";

        var launch = TerminalPlan.Compose(TerminalPlatform.Linux, new TerminalFound(name, program), Campaign, ["/usr/local/bin/codex", Prompt], Item);

        Assert.Equal([program, .. before.Select(word => word == "<folder>" ? Campaign : word), "/usr/local/bin/codex", Prompt], launch.Argv);
        Assert.Equal(Campaign, launch.WorkingDirectory);
    }

    /// <summary>The form is chosen by the program's file name, wherever the program is and whatever the variable called it.</summary>
    [Fact]
    public void Linux_chooses_the_form_by_the_programs_file_name()
    {
        var launch = TerminalPlan.Compose(TerminalPlatform.Linux, new TerminalFound("term", "/opt/wezterm/bin/wezterm"), "/home/marta/Jason", ["/usr/local/bin/codex"], Item);

        Assert.Equal(["/opt/wezterm/bin/wezterm", "start", "--cwd", "/home/marta/Jason", "--", "/usr/local/bin/codex"], launch.Argv);
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

    /// <summary>
    /// An entry of the search path that is not a full path, a relative folder, names wherever the process happens to
    /// stand and is not searched: a terminal there is never found, and the system's emulator is taken from the full
    /// entries alone.
    /// </summary>
    [Fact]
    public void Linux_never_searches_an_entry_of_the_search_path_that_is_not_a_full_path()
    {
        var relative = "jason-terminal-" + Guid.NewGuid().ToString("N");
        var decoys = Directory.CreateDirectory(Path.Combine(Directory.GetCurrentDirectory(), relative)).FullName;
        try
        {
            StandIn(decoys, "kitty");
            StandIn(decoys, "x-terminal-emulator");
            var emulator = StandIn(Folder("bin"), "x-terminal-emulator");

            Assert.Null(TerminalPlan.Find(TerminalPlatform.Linux, null, "kitty", relative));
            Assert.Null(TerminalPlan.Find(TerminalPlatform.Linux, null, null, relative));
            Assert.Equal(emulator, TerminalPlan.Find(TerminalPlatform.Linux, null, "kitty", string.Join(Path.PathSeparator, relative, Folder("bin")))!.Program);
        }
        finally
        {
            Directory.Delete(decoys, recursive: true);
        }
    }

    /// <summary>
    /// Every terminal found is named by its full path on the system it is found for: the console host in the system folder
    /// on Windows, the opener on macOS, and on Linux the program the terminal variable names by its full path, or the one
    /// the search path's full entries give for its name or for the system's emulator. The terminal starter hands that
    /// path to the door as it is.
    /// </summary>
    [Fact]
    public void Every_terminal_found_is_named_by_its_full_path()
    {
        var system = Folder("System32");
        StandIn(system, "conhost.exe");
        var bin = Folder("bin");
        var kitty = StandIn(bin, "kitty");
        StandIn(bin, "x-terminal-emulator");

        Assert.True(Path.IsPathFullyQualified(TerminalPlan.Find(TerminalPlatform.Windows, system, null, null)!.Program));
        Assert.StartsWith("/", TerminalPlan.Find(TerminalPlatform.MacOs, null, null, null)!.Program, StringComparison.Ordinal);
        foreach (var variable in new[] { null, "kitty", kitty })
        {
            Assert.True(Path.IsPathFullyQualified(TerminalPlan.Find(TerminalPlatform.Linux, null, variable, bin)!.Program));
        }
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
    public void On_linux_and_macos_the_terminal_is_given_a_start_on_pipes_of_its_own(TerminalPlatform os, bool redirected)
    {
        // Every child is started by its full path, so the terminal is named by one this machine reads as full.
        var terminal = Path.Combine(Path.GetTempPath(), "x-terminal-emulator");
        var start = TerminalStartInfo.For(os, [terminal, "-e", "/usr/local/bin/codex", Prompt], "/home/marta/Jason");

        Assert.Equal(redirected, start.RedirectStandardInput);
        Assert.Equal(redirected, start.RedirectStandardOutput);
        Assert.Equal(redirected, start.RedirectStandardError);
        Assert.False(start.UseShellExecute);
        Assert.Equal(terminal, start.FileName);
        Assert.Equal(["-e", "/usr/local/bin/codex", Prompt], start.ArgumentList);
        Assert.Equal("/home/marta/Jason", start.WorkingDirectory);
    }

    /// <summary>The installation a row's launch carries: its data directory, its executable's folder and the CLI's search path.</summary>
    private static readonly OwnJason Own = new("/Users/marta/jason-test", "/Users/marta/.jason/bin", "/usr/local/bin:/usr/bin");

    /// <summary>The macOS script for <c>/opt/homebrew/bin/claude 'Go.'</c> at <paramref name="folder"/>, with the export lines before the <c>exec</c>.</summary>
    private static string MacScript(string folder, params string[] exportLines) => string.Join(
        "\n",
        [
            "#!/bin/sh",
            "rm -f -- \"$0\"",
            "rmdir -- \"${0%/*}\" 2>/dev/null",
            "cd -- '" + folder + "' || exit 1",
            .. exportLines,
            "exec '/opt/homebrew/bin/claude' 'Go.'",
            string.Empty,
        ]);

    private static TerminalFound TerminalOf(TerminalPlatform os) => os switch
    {
        TerminalPlatform.Windows => new TerminalFound("conhost", "C:/Windows/System32/conhost.exe"),
        TerminalPlatform.Linux => new TerminalFound("kitty", "/usr/bin/kitty"),
        _ => new TerminalFound("Terminal", TerminalPlan.MacOpener),
    };

    /// <summary>The first file of <paramref name="name"/> in the folders of <paramref name="searchPath"/>, in its order, as a lookup by name finds it.</summary>
    private static string? FirstOn(string searchPath, string separator, string name) =>
        searchPath.Split(separator, StringSplitOptions.RemoveEmptyEntries).Select(folder => Path.Combine(folder, name)).FirstOrDefault(File.Exists);

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

/// <summary>
/// <c>JASON_CONFINED</c> confines the CLI call it is set on and nothing that call starts: the terminal a person's agent
/// host is opened in never carries it, on any operating system, even where this process holds it. The row sets it in this
/// process's own environment, so it runs apart from every other test in the assembly.
/// </summary>
[Collection(ConfinementEnvironmentCollection.Name)]
public sealed class TerminalStartConfinementTests
{
    [Theory]
    [InlineData(TerminalPlatform.Linux)]
    [InlineData(TerminalPlatform.MacOs)]
    [InlineData(TerminalPlatform.Windows)]
    public void The_terminal_does_not_carry_the_variable(TerminalPlatform os)
    {
        var original = Environment.GetEnvironmentVariable(Confinement.Variable);
        var turn = Environment.GetEnvironmentVariable(Confinement.ChatTurnVariable);
        bool carried;
        try
        {
            Environment.SetEnvironmentVariable(Confinement.Variable, "1");
            Environment.SetEnvironmentVariable(Confinement.ChatTurnVariable, "1");

            // Every child is started by its full path, so the terminal is named by one this machine reads as full.
            var start = TerminalStartInfo.For(os, [Path.Combine(Path.GetTempPath(), "x-terminal-emulator"), "-e", "/usr/local/bin/codex"], "/home/marta/Jason");

            // The start's environment is read from this process when it is first asked for, so it is asked while the variable is set.
            carried = start.Environment.ContainsKey(Confinement.Variable) || start.Environment.ContainsKey(Confinement.ChatTurnVariable);
        }
        finally
        {
            Environment.SetEnvironmentVariable(Confinement.Variable, original);
            Environment.SetEnvironmentVariable(Confinement.ChatTurnVariable, turn);
        }

        Assert.False(carried, "The terminal was handed the variable.");
    }
}
