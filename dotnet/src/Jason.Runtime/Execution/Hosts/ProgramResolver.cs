using Jason.Contracts.Discovery;
using Jason.Contracts.Programs;
using Jason.Runtime.Plugins.Registry;

namespace Jason.Runtime.Execution.Hosts;

/// <summary>
/// Where a profile's program is on this machine, and what the agent it starts calls home with. A profile names a
/// program the way a person would — an absolute path, or a bare name the search path answers for — and this is
/// the one place that turns either into the file that is actually started. Nothing found is
/// <see cref="AttemptErrors.HostNotAvailable"/>: a host that is not installed is a fact about the machine, and it
/// is decided before a child exists rather than discovered by failing to start one.
/// </summary>
public sealed class ProgramResolver(ISearchPath searchPath)
{
    /// <summary>
    /// The bare word a launched agent calls home with when a profile names none: this executable's own name.
    /// One binary serves both the runtime and the CLI, so the name this process runs under is the name an agent
    /// types — and the launcher puts its directory within the child's reach so that the bare word resolves.
    /// </summary>
    public static string DefaultCliCommand { get; } = Path.GetFileNameWithoutExtension(SelfExecutable.Command[^1]);

    /// <summary>
    /// The directory the command word is found in, or null where this process publishes no path of its own. It
    /// is the directory of the file that carries this build rather than of the muxer that may be running it:
    /// prepending the muxer's own directory would put <c>dotnet</c> within a child's reach and not this Jason.
    /// </summary>
    public static string? ExecutableDirectory { get; } = DirectoryOf(SelfExecutable.Command[^1]);

    /// <summary>The word the allow rule is built from and the envelope carries: the profile's, or this one.</summary>
    public static string CliCommandFor(string? configured) =>
        string.IsNullOrWhiteSpace(configured) ? DefaultCliCommand : configured.Trim();

    /// <summary>
    /// How this machine starts the program a profile names, or null where it has no way to. A value with a
    /// directory in it is a path and is answered for by the file system alone. A bare name is looked up the way
    /// the plugin registry looks one up — the machine's own <c>PATHEXT</c> in order, and, where the search path
    /// yields only the shim npm writes, the interpreter and entry script that shim names. Nothing is ever
    /// resolved against the runtime's own working directory, which is not a place a profile can mean.
    /// <para>
    /// A list rather than a path, because starting an npm-installed host means starting its interpreter with
    /// its entry script: reading the shim is how a command stays an argument array instead of becoming a line
    /// for <c>cmd.exe</c> to interpret.
    /// </para>
    /// <para>
    /// A path to a script is answered with null even where the file exists: CreateProcess would start it through
    /// <c>cmd.exe</c>. A profile stored before its program was held to that rule keeps its program, so the rule is
    /// held here as well, and <see cref="NamesShellScript"/> says why there was no answer.
    /// </para>
    /// <para>
    /// A script is the one exception, and only where <paramref name="host"/> says it is its own launcher: a host that is
    /// installed as a script which starts an interpreter knows which script that is and what it starts, so the answer
    /// for the script is that interpreter and entry script — never the script. Without a host, or for a script that is
    /// not the host's, the answer is as it always was.
    /// </para>
    /// </summary>
    public IReadOnlyList<string>? Resolve(string program, IAgentHost? host = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(program);

        if (program.Contains(Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || program.Contains(Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
        {
            if (Full(program) is not { } path)
            {
                return null;
            }

            if (ProgramLookup.IsShellScript(path))
            {
                // Only a full path names a launcher: a relative one would be read against whatever directory the runtime
                // happens to run in.
                return Path.IsPathFullyQualified(program.Trim()) ? LayoutOf(host, path) : null;
            }

            return Runnable(program) ? [path] : null;
        }

        // The same walk, the same order and the same reading of a shim that a plugin's declared program gets.
        // Two answers to "where is this program" would be one answer too many.
        var onPath = new ProgramSearchPath(searchPath.Path, searchPath.PathExt);
        var found = ProgramLookup.Find(program, onPath, onPath);
        return found switch
        {
            { Outcome: ProgramLookupOutcome.Program or ProgramLookupOutcome.NpmShim, Path: { } started } => [started, .. found.Launch],
            { Outcome: ProgramLookupOutcome.RefusedShim, File: { } shim } => LayoutOf(host, shim),
            _ => null,
        };
    }

    /// <summary>What a host's own launcher script starts, or null where the file is not one the host owns.</summary>
    private static IReadOnlyList<string>? LayoutOf(IAgentHost? host, string script) =>
        host is not null && host.NamesItsLauncher(Path.GetFileName(script)) && host.ProgramLayout(script) is { Count: > 0 } layout ? layout : null;

    /// <summary>
    /// Whether a program names a file only a shell could start, read both as written and as this operating system
    /// reads the path: Windows trims the dots and spaces that end one, so <c>claude.cmd.</c> is the script
    /// <c>claude.cmd</c> although its extension, as written, is none.
    /// </summary>
    public static bool NamesShellScript(string program)
    {
        ArgumentNullException.ThrowIfNull(program);

        return ProgramLookup.IsShellScript(program)
            || (!string.IsNullOrWhiteSpace(program) && Full(program.Trim()) is { } path && ProgramLookup.IsShellScript(path));
    }

    /// <summary>A file that exists and that this operating system would start; a directory is neither.</summary>
    private static bool Runnable(string file)
    {
        try
        {
            if (!File.Exists(file))
            {
                return false;
            }

            if (OperatingSystem.IsWindows())
            {
                return true;
            }

            const UnixFileMode anyExecuteBit = UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
            return (File.GetUnixFileMode(file) & anyExecuteBit) != 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or PathTooLongException or PlatformNotSupportedException)
        {
            return false;
        }
    }

    private static string? Full(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static string? DirectoryOf(string path)
    {
        var directory = Full(path) is { } full ? Path.GetDirectoryName(full) : null;
        return string.IsNullOrEmpty(directory) ? null : directory;
    }

}
