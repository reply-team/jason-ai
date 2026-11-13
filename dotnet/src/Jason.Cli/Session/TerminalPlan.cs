using Jason.Contracts.Discovery;

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
    /// The system's plain shell for <c>jason session open --shell</c>, as the words a terminal is given, or null where it is
    /// not there: on Windows, Windows PowerShell in its fixed place under <paramref name="systemDirectory"/>, never from the
    /// search path, for the reason the console host is not taken from it; on macOS a <c>/bin/sh</c> that replaces itself
    /// with the account's login shell, read from <c>SHELL</c> in Terminal's own environment, since the script Terminal runs
    /// carries nothing of the CLI's; on Linux <paramref name="shellVariable"/> where it names a program by its absolute
    /// path, else <c>/bin/sh</c>. No argument follows but macOS's: the shell starts with no first input.
    /// </summary>
    public static IReadOnlyList<string>? Shell(TerminalPlatform os, string? systemDirectory, string? shellVariable)
    {
        switch (os)
        {
            case TerminalPlatform.Windows:
                if (string.IsNullOrWhiteSpace(systemDirectory))
                {
                    return null;
                }

                var powerShell = Path.Combine(systemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
                return File.Exists(powerShell) ? [powerShell] : null;
            case TerminalPlatform.MacOs:
                return [PosixShell, "-c", "exec \"${SHELL:-/bin/sh}\" -l"];
            case TerminalPlatform.Linux:
                return !string.IsNullOrWhiteSpace(shellVariable) && !shellVariable.Any(char.IsWhiteSpace)
                    && Path.IsPathFullyQualified(shellVariable) && IsProgram(shellVariable)
                    ? [shellVariable]
                    : [PosixShell];
            default:
                throw new ArgumentOutOfRangeException(nameof(os), os, null);
        }
    }

    /// <summary>The POSIX shell every macOS and Linux system keeps at this path.</summary>
    private const string PosixShell = "/bin/sh";

    /// <summary>
    /// The launch of <paramref name="host"/> (its program, then its arguments) in <paramref name="terminal"/> at
    /// <paramref name="folder"/>. The host's words are passed as they are, one argument each, with no shell of Jason's
    /// between; only macOS's script is read by a shell, Terminal's, and every word in it is single-quoted.
    /// </summary>
    /// <param name="dataDirectory">
    /// The data directory the CLI runs with when <c>JASON_DATA_DIR</c> names one, else null (empty and blank are none, as
    /// they are to the CLI). On Windows and Linux the terminal process is the CLI's own child and inherits the variable,
    /// so the launch there does not change, though a Linux emulator that hands the command to a server process already
    /// running may run the host in that server's environment instead; on macOS Terminal is started by the system and its
    /// shell carries nothing of the CLI's environment, so the script exports this one variable before the <c>exec</c>, and
    /// nothing else.
    /// </param>
    public static TerminalLaunch Compose(TerminalPlatform os, TerminalFound terminal, string folder, IReadOnlyList<string> host, string itemId, string? dataDirectory = null)
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
                return new TerminalLaunch(terminal.Name, [terminal.Program, .. LinuxForm(terminal.Program, folder), .. host], folder, null, null);
            case TerminalPlatform.MacOs:
                var name = ScriptNameFor(itemId);
                return new TerminalLaunch(terminal.Name, [terminal.Program, "-a", "Terminal", name], folder, Script(folder, host, dataDirectory), name);
            default:
                throw new ArgumentOutOfRangeException(nameof(os), os, null);
        }
    }

    /// <summary>
    /// What a Linux terminal is given before the program it runs, by the program's file name, in the form each vendor
    /// documents: kitty and foot take the host's words after the program; gnome-terminal after <c>--</c>; xfce4-terminal
    /// and mate-terminal after <c>-x</c>, which executes the remainder of the command line; wezterm after
    /// <c>start --cwd &lt;folder&gt; --</c>, since wezterm takes the program's folder from <c>--cwd</c> (else its
    /// <c>default_cwd</c>, else the home folder) and never from the folder it is started in; every other name,
    /// <c>x-terminal-emulator</c> included, after <c>-e</c> as separate arguments
    /// (Debian Policy 11.8.3). An emulator whose <c>-e</c> takes one string and that documents no remainder form, tilix
    /// among them, is outside the table: it is given <c>-e</c> too, and is used through <c>x-terminal-emulator</c>.
    /// </summary>
    public static IReadOnlyList<string> LinuxForm(string program, string folder)
    {
        ArgumentException.ThrowIfNullOrEmpty(program);
        ArgumentException.ThrowIfNullOrEmpty(folder);
        // Either separator: the plan's Linux rows run on Windows too, where a stand-in's path is written with backslashes.
        return program[(program.AsSpan().LastIndexOfAny('/', '\\') + 1)..] switch
        {
            "kitty" or "foot" => [],
            "gnome-terminal" => ["--"],
            "xfce4-terminal" or "mate-terminal" => ["-x"],
            "wezterm" => ["start", "--cwd", folder, "--"],
            _ => ["-e"],
        };
    }

    /// <summary>
    /// One word in single quotes, each apostrophe written as <c>'\''</c>: nothing inside single quotes is special to a POSIX
    /// shell, a space, a dollar sign and a line break included. The folder, the host's words and the data directory are
    /// quoted by it and by nothing else.
    /// </summary>
    public static string ShellQuote(string word)
    {
        ArgumentNullException.ThrowIfNull(word);
        return "'" + word.Replace("'", "'\\''", StringComparison.Ordinal) + "'";
    }

    /// <summary>
    /// The script Terminal runs: it removes itself and its folder first, enters the folder or stops, exports the data
    /// directory where the CLI runs with one, and replaces itself with the host.
    /// </summary>
    private static string Script(string folder, IReadOnlyList<string> host, string? dataDirectory)
    {
        List<string> lines =
        [
            "#!/bin/sh",
            "rm -f -- \"$0\"",
            "rmdir -- \"${0%/*}\" 2>/dev/null",
            "cd -- " + ShellQuote(folder) + " || exit 1",
        ];
        if (!string.IsNullOrWhiteSpace(dataDirectory))
        {
            lines.Add("export " + JasonPaths.DataDirectoryVariable + "=" + ShellQuote(dataDirectory));
        }

        lines.Add("exec " + string.Join(' ', host.Select(ShellQuote)));
        lines.Add(string.Empty);
        return string.Join("\n", lines);
    }

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
