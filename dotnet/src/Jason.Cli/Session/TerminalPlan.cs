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
/// program first), the folder it starts in, on macOS the script Terminal runs and its file name, and on Windows the
/// variables the console host is started with beside the CLI's own. The script's folder is made only when the launch
/// starts, so <see cref="Argv"/> ends with <see cref="ScriptName"/> until then.
/// </summary>
public sealed record TerminalLaunch(string Terminal, IReadOnlyList<string> Argv, string WorkingDirectory, string? Script, string? ScriptName, IReadOnlyDictionary<string, string>? Variables = null);

/// <summary>
/// The installation the opened terminal's <c>jason</c> is to be: the data directory the CLI runs on, made absolute; the
/// folder of the executable that opens the terminal, or null where this jason is no one installed file (a build run
/// through <c>dotnet</c>); and the CLI's own search path, which that folder goes before where the terminal's PATH is
/// composed here (Windows and Linux; macOS puts it before Terminal's own).
/// </summary>
public sealed record OwnJason(string DataDirectory, string? ExecutableFolder, string? SearchPath);

/// <summary>
/// The terminal per operating system, as pure strings. Windows starts the console host from the system directory and gives
/// it the host after <c>--</c> (which window a person then sees is not yet measured on a machine); macOS opens a <c>.command</c> script in Terminal through <c>/usr/bin/open</c>, since
/// <c>open -a Terminal</c> takes no command; Linux starts the program the <c>TERMINAL</c> variable names, else
/// <c>x-terminal-emulator</c>, with the host as several arguments in the form that program takes, chosen by its file name
/// (<see cref="LinuxForm"/>). A terminal the
/// operating system keeps in a fixed place is never taken from the search path: a click runs under the person's PATH, and
/// a program of that name planted earlier on it must not be what the click starts. And the terminal opened runs this
/// installation's own <c>jason</c> (<see cref="OwnJason"/>): every system is handed the data directory by name and the
/// opening executable's folder first on PATH, so another <c>jason</c> earlier on the person's PATH does not win there.
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
    /// The program that sets variables and replaces itself with the command after them, no shell between, by the absolute
    /// path Linux systems keep it at: the variables a Linux terminal is given travel on its command line through it.
    /// </summary>
    public const string EnvProgram = "/usr/bin/env";

    /// <summary>The variable a search path is read from, as Jason writes it for the terminal.</summary>
    public const string PathVariable = "PATH";

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
    /// <param name="own">
    /// The installation the opened terminal's <c>jason</c> is to be, or null for a launch that carries nothing of one. Its
    /// data directory is passed by name and its executable's folder, where there is one, goes first on PATH, on every
    /// system the way that system's terminal can be given them, and nothing is added to the host's own words. On Windows
    /// the console host is the CLI's own child and starts the host as its own, so the two are variables of the console
    /// host's start (<see cref="TerminalLaunch.Variables"/>), PATH the folder and then the CLI's search path. On Linux an
    /// emulator may hand its command to a server process already running, which runs it in the server's environment, so
    /// the two travel on the command itself: <see cref="EnvProgram"/> with them, after the emulator's own form and before
    /// the host. On macOS Terminal is started by the system and its shell carries nothing of the CLI's environment, so the
    /// script exports the data directory and puts the folder before Terminal's own PATH, before the <c>exec</c>.
    /// </param>
    /// <exception cref="ArgumentException">
    /// The host's program cannot be carried (<see cref="Uncarried"/>), or on macOS the id names no script.
    /// </exception>
    public static TerminalLaunch Compose(TerminalPlatform os, TerminalFound terminal, string folder, IReadOnlyList<string> host, string itemId, OwnJason? own = null)
    {
        ArgumentNullException.ThrowIfNull(terminal);
        ArgumentException.ThrowIfNullOrEmpty(folder);
        ArgumentNullException.ThrowIfNull(host);
        if (host.Count == 0)
        {
            throw new ArgumentException("The host's program is missing.", nameof(host));
        }

        if (own is not null && Uncarried(os, host) is { } uncarried)
        {
            throw new ArgumentException(uncarried, nameof(host));
        }

        switch (os)
        {
            case TerminalPlatform.Windows:
                return new TerminalLaunch(terminal.Name, [terminal.Program, "--", .. host], folder, null, null, own is null ? null : Variables(os, own));
            case TerminalPlatform.Linux:
                IReadOnlyList<string> carried = own is null ? [] : [EnvProgram, .. Variables(os, own).Select(variable => variable.Key + "=" + variable.Value)];
                return new TerminalLaunch(terminal.Name, [terminal.Program, .. LinuxForm(terminal.Program, folder), .. carried, .. host], folder, null, null);
            case TerminalPlatform.MacOs:
                var name = ScriptNameFor(itemId);
                return new TerminalLaunch(terminal.Name, [terminal.Program, "-a", "Terminal", name], folder, Script(folder, host, own), name);
            default:
                throw new ArgumentOutOfRangeException(nameof(os), os, null);
        }
    }

    /// <summary>
    /// Why the host's program cannot be carried to the terminal with this installation's variables, worded to follow "did
    /// not start", or null where it can: on Linux <see cref="EnvProgram"/> reads every word holding <c>=</c> as a variable
    /// to set until the program it runs, so a program whose path holds one would be read as a variable and the next word
    /// run in its place. Only the program is read so; its arguments are passed as they are.
    /// </summary>
    public static string? Uncarried(TerminalPlatform os, IReadOnlyList<string> host)
    {
        ArgumentNullException.ThrowIfNull(host);
        return os == TerminalPlatform.Linux && host.Count > 0 && host[0].Contains('=', StringComparison.Ordinal)
            ? $"the host's program {host[0]} holds '=', which {EnvProgram} reads as a variable to set rather than a program to run"
            : null;
    }

    /// <summary>
    /// The search path the terminal is given: <paramref name="folder"/> first, then <paramref name="searchPath"/> after the
    /// separator of <paramref name="os"/>'s, or the folder alone where the search path is empty, since an empty entry would
    /// put the folder the terminal starts in on PATH.
    /// </summary>
    public static string SearchPathFirst(TerminalPlatform os, string folder, string? searchPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(folder);
        return string.IsNullOrEmpty(searchPath) ? folder : folder + (os == TerminalPlatform.Windows ? ";" : ":") + searchPath;
    }

    /// <summary>The variables the terminal composed here is given: the data directory, and PATH where there is a folder to put first.</summary>
    private static Dictionary<string, string> Variables(TerminalPlatform os, OwnJason own)
    {
        var variables = new Dictionary<string, string>(StringComparer.Ordinal) { [JasonPaths.DataDirectoryVariable] = own.DataDirectory };
        if (!string.IsNullOrEmpty(own.ExecutableFolder))
        {
            variables[PathVariable] = SearchPathFirst(os, own.ExecutableFolder, own.SearchPath);
        }

        return variables;
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
    /// shell, a space, a dollar sign and a line break included. The folder, the host's words, the data directory and the
    /// executable's folder are quoted by it and by nothing else.
    /// </summary>
    public static string ShellQuote(string word)
    {
        ArgumentNullException.ThrowIfNull(word);
        return "'" + word.Replace("'", "'\\''", StringComparison.Ordinal) + "'";
    }

    /// <summary>
    /// The script Terminal runs: it removes itself and its folder first, enters the folder or stops, exports the data
    /// directory and puts the executable's folder before Terminal's own PATH (adding no empty entry where that is empty),
    /// and replaces itself with the host.
    /// </summary>
    private static string Script(string folder, IReadOnlyList<string> host, OwnJason? own)
    {
        List<string> lines =
        [
            "#!/bin/sh",
            "rm -f -- \"$0\"",
            "rmdir -- \"${0%/*}\" 2>/dev/null",
            "cd -- " + ShellQuote(folder) + " || exit 1",
        ];
        if (own is not null)
        {
            lines.Add("export " + JasonPaths.DataDirectoryVariable + "=" + ShellQuote(own.DataDirectory));
            if (!string.IsNullOrEmpty(own.ExecutableFolder))
            {
                lines.Add("export " + PathVariable + "=" + ShellQuote(own.ExecutableFolder) + "\"${" + PathVariable + ":+:$" + PathVariable + "}\"");
            }
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
