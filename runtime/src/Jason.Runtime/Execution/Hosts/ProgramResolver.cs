using Jason.Contracts.Discovery;
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
    /// What Windows starts without a shell. A name is looked up through the machine's own <c>PATHEXT</c>, and
    /// through these two when it says nothing, because they are the extensions that are a program rather than a
    /// script something else has to interpret.
    /// </summary>
    private static readonly string[] ExecutableExtensions = [".exe", ".com"];

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
    /// The program as it will be started, or null when this machine has none. A value with a directory in it is
    /// a path and is answered for by the file system alone; a bare name is looked up on the search path in
    /// order, through the platform's executable extensions. Nothing is ever resolved against the runtime's own
    /// working directory, which is not a place a profile can mean.
    /// </summary>
    public string? Resolve(string program)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(program);

        if (program.Contains(Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || program.Contains(Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
        {
            return Runnable(program) ? Full(program) : null;
        }

        foreach (var directory in Directories())
        {
            foreach (var candidate in Candidates(program))
            {
                var file = Path.Combine(directory, candidate);
                if (Runnable(file))
                {
                    return file;
                }
            }
        }

        return null;
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

    /// <summary>
    /// The name itself everywhere, and on Windows the name through the extensions that are a program. The other
    /// extensions <c>PATHEXT</c> names are scripts a shell would have to interpret, and a command this runtime
    /// composed argument by argument never goes through one, so a name that only a <c>.cmd</c> answers for is a
    /// host this machine does not have.
    /// </summary>
    private static IEnumerable<string> Candidates(string program)
    {
        if (!OperatingSystem.IsWindows())
        {
            yield return program;
            yield break;
        }

        if (ExecutableExtensions.Any(extension => program.EndsWith(extension, StringComparison.OrdinalIgnoreCase)))
        {
            yield return program;
            yield break;
        }

        foreach (var extension in ExecutableExtensions)
        {
            yield return program + extension;
        }
    }

    private IEnumerable<string> Directories()
    {
        foreach (var entry in (searchPath.Path ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var directory = entry.Trim('"');
            if (directory.Length == 0)
            {
                continue;
            }

            if (Full(directory) is { } full)
            {
                yield return full;
            }
        }
    }
}
