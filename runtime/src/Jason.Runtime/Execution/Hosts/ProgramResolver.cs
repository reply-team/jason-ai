using Jason.Contracts.Discovery;
using Jason.Runtime.Plugins.Manifest;
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
    /// </summary>
    public IReadOnlyList<string>? Resolve(string program)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(program);

        if (program.Contains(Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || program.Contains(Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
        {
            return Runnable(program) && Full(program) is { } path ? [path] : null;
        }

        // The same walk, the same order and the same reading of a shim that a plugin's declared program gets.
        // Two answers to "where is this program" would be one answer too many.
        var resolved = new ExecutableResolver(searchPath).Resolve(new ExecutableRequest(program, null, null), 0);
        return resolved is { Problem: null, Path: { } found } ? [found, .. resolved.Launch] : null;
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
