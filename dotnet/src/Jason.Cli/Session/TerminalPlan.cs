namespace Jason.Cli.Session;

/// <summary>The three operating systems a terminal is composed for; a parameter of the plan, never read in it.</summary>
public enum TerminalPlatform
{
    Windows,
    MacOs,
    Linux,
}

/// <summary>A terminal program found on this machine: the name a person knows it by and the file that is started.</summary>
public sealed record TerminalFound(string Name, string Program);

/// <summary>
/// What is started to open a person's own agent host in a new window: the terminal's name, the whole argument list (the
/// program first), the folder it starts in, and on macOS the script Terminal runs and its file name. The script's folder
/// is made only when the launch starts, so <see cref="Argv"/> ends with <see cref="ScriptName"/> until then.
/// </summary>
public sealed record TerminalLaunch(string Terminal, IReadOnlyList<string> Argv, string WorkingDirectory, string? Script, string? ScriptName);

/// <summary>
/// The terminal per operating system, as pure strings. Windows starts the console host from the system directory, which
/// hands the console to the person's default terminal (Windows Terminal where it is the default), and gives it the host
/// after <c>--</c>; macOS opens a <c>.command</c> script in Terminal through <c>/usr/bin/open</c>, since
/// <c>open -a Terminal</c> takes no command; Linux starts the program the <c>TERMINAL</c> variable names, else
/// <c>x-terminal-emulator</c>, with the host as several arguments in the form that program takes, chosen by its file name
/// (<see cref="LinuxForm"/>). A terminal the
/// operating system keeps in a fixed place is never taken from the search path: a click runs under the person's PATH, and
/// a program of that name planted earlier on it must not be what the click starts.
/// </summary>
public static class TerminalPlan
{
    /// <summary>The console host's file, in the system directory and nowhere else.</summary>
    public const string ConsoleHost = "conhost.exe";

    /// <summary>macOS's opener, by its absolute path.</summary>
    public const string MacOpener = "/usr/bin/open";

    /// <summary>The program Debian's alternatives name for the system's terminal emulator.</summary>
    public const string TerminalEmulator = "x-terminal-emulator";

    /// <summary>
    /// The terminal this machine starts, or null when there is none: on Windows the console host in
    /// <paramref name="systemDirectory"/>; on macOS the opener; on Linux <paramref name="terminalVariable"/> when it names
    /// one program found by its absolute path or on <paramref name="searchPath"/>, else <c>x-terminal-emulator</c> there.
    /// </summary>
    public static TerminalFound? Find(TerminalPlatform os, string? systemDirectory, string? terminalVariable, string? searchPath) => os switch
    {
        TerminalPlatform.Windows => string.IsNullOrWhiteSpace(systemDirectory) || !File.Exists(Path.Combine(systemDirectory, ConsoleHost))
            ? null
            : new TerminalFound("conhost", Path.Combine(systemDirectory, ConsoleHost)),
        TerminalPlatform.MacOs => new TerminalFound("Terminal", MacOpener),
        TerminalPlatform.Linux => FindOnLinux(terminalVariable, searchPath),
        _ => throw new ArgumentOutOfRangeException(nameof(os), os, null),
    };

    /// <summary>
    /// The launch of <paramref name="host"/> (its program, then its arguments) in <paramref name="terminal"/> at
    /// <paramref name="folder"/>. The host's words are passed as they are, one argument each, with no shell of Jason's
    /// between; only macOS's script is read by a shell, Terminal's, and every word in it is single-quoted.
    /// </summary>
    public static TerminalLaunch Compose(TerminalPlatform os, TerminalFound terminal, string folder, IReadOnlyList<string> host, string itemId)
    {
        ArgumentNullException.ThrowIfNull(terminal);
        ArgumentException.ThrowIfNullOrEmpty(folder);
        ArgumentNullException.ThrowIfNull(host);
        if (host.Count == 0)
        {
            throw new ArgumentException("The host's program is missing.", nameof(host));
        }

        switch (os)
        {
            case TerminalPlatform.Windows:
                return new TerminalLaunch(terminal.Name, [terminal.Program, "--", .. host], folder, null, null);
            case TerminalPlatform.Linux:
                return new TerminalLaunch(terminal.Name, [terminal.Program, .. LinuxForm(terminal.Program), .. host], folder, null, null);
            case TerminalPlatform.MacOs:
                var name = ScriptNameFor(itemId);
                return new TerminalLaunch(terminal.Name, [terminal.Program, "-a", "Terminal", name], folder, Script(folder, host), name);
            default:
                throw new ArgumentOutOfRangeException(nameof(os), os, null);
        }
    }

    /// <summary>
    /// What a Linux terminal is given before the program it runs, by the program's file name: kitty and foot take the
    /// program as positional arguments and have no <c>-e</c>; gnome-terminal takes it after <c>--</c>; wezterm after
    /// <c>start --</c>; every other name after <c>-e</c>, as xterm, konsole, alacritty, xfce4-terminal, urxvt and
    /// <c>x-terminal-emulator</c> (Debian Policy 11.8.3) take it.
    /// </summary>
    public static IReadOnlyList<string> LinuxForm(string program)
    {
        ArgumentException.ThrowIfNullOrEmpty(program);
        // Either separator: the plan's Linux rows run on Windows too, where a stand-in's path is written with backslashes.
        return program[(program.AsSpan().LastIndexOfAny('/', '\\') + 1)..] switch
        {
            "kitty" or "foot" => [],
            "gnome-terminal" => ["--"],
            "wezterm" => ["start", "--"],
            _ => ["-e"],
        };
    }

    /// <summary>One word in single quotes, each apostrophe written as <c>'\''</c>: nothing inside single quotes is special to a POSIX shell.</summary>
    public static string ShellQuote(string word)
    {
        ArgumentNullException.ThrowIfNull(word);
        return "'" + word.Replace("'", "'\\''", StringComparison.Ordinal) + "'";
    }

    /// <summary>
    /// The script Terminal runs: it removes itself and its folder first, enters the folder or stops, and replaces itself
    /// with the host.
    /// </summary>
    private static string Script(string folder, IReadOnlyList<string> host) => string.Join(
        "\n",
        "#!/bin/sh",
        "rm -f -- \"$0\"",
        "rmdir -- \"${0%/*}\" 2>/dev/null",
        "cd -- " + ShellQuote(folder) + " || exit 1",
        "exec " + string.Join(' ', host.Select(ShellQuote)),
        string.Empty);

    private static string ScriptNameFor(string itemId)
    {
        ArgumentException.ThrowIfNullOrEmpty(itemId);
        if (!itemId.All(character => char.IsAsciiLetterOrDigit(character) || character == '_'))
        {
            throw new ArgumentException("A script is named only from a well-formed id.", nameof(itemId));
        }

        return "jason-" + itemId + ".command";
    }

    private static TerminalFound? FindOnLinux(string? terminalVariable, string? searchPath)
    {
        if (!string.IsNullOrWhiteSpace(terminalVariable) && !terminalVariable.Any(char.IsWhiteSpace) && Locate(terminalVariable, searchPath) is { } named)
        {
            return new TerminalFound(Path.GetFileName(named), named);
        }

        return Locate(TerminalEmulator, searchPath) is { } emulator ? new TerminalFound(TerminalEmulator, emulator) : null;
    }

    /// <summary>A program by its absolute path, or a bare name on the search path in its order; a relative path is neither.</summary>
    /// <remarks>
    /// A second lookup beside <c>ProgramLookup</c>, which reads the real operating system (its extensions and its shims):
    /// the plan's Linux rows run on Windows too, so the Linux rule is applied here whatever the machine is, and only the
    /// executable bit, which Windows does not keep, is read from the machine.
    /// </remarks>
    private static string? Locate(string program, string? searchPath)
    {
        if (program.Contains('/', StringComparison.Ordinal) || program.Contains(Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            return Path.IsPathFullyQualified(program) && IsProgram(program) ? program : null;
        }

        foreach (var directory in (searchPath ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var candidate = Path.Combine(directory, program);
            if (IsProgram(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static bool IsProgram(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        if (OperatingSystem.IsWindows())
        {
            return true;
        }

        const UnixFileMode Execute = UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
        return (File.GetUnixFileMode(path) & Execute) != 0;
    }
}
