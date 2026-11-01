namespace Jason.App;

/// <summary>
/// What <c>jason runtime run</c> was asked for. Only the runtime-service mode reads these, and it reads them
/// straight from argv rather than through the CLI parser: the parser lives on the client side of the process
/// boundary and must not be loaded to start a server.
/// </summary>
/// <param name="Detached">Run as a background service: no console, standard streams on the null device.</param>
/// <param name="DataDirectory">
/// The data directory this runtime owns, when it was named here rather than left to the environment. Autostart
/// is why it can be named at all: a Windows logon task carries no environment, so a registration that must
/// bring its data directory with it has nowhere to put it but the command line.
/// </param>
public sealed record RuntimeRunArguments(bool Detached, string? DataDirectory = null)
{
    private const string DetachedFlag = "--detached";
    private const string DataDirectoryFlag = "--data-dir";

    /// <summary>Reads the flags that follow the <c>runtime run</c> verbs; the verbs themselves are never flags.</summary>
    /// <exception cref="RuntimeRunUsageException">The data directory flag was given with no path after it.</exception>
    public static RuntimeRunArguments Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var flags = args.Skip(2).ToArray();
        return new RuntimeRunArguments(
            flags.Contains(DetachedFlag, StringComparer.Ordinal),
            DataDirectoryIn(flags));
    }

    private static string? DataDirectoryIn(string[] flags)
    {
        var flag = Array.IndexOf(flags, DataDirectoryFlag);
        if (flag < 0)
        {
            return null;
        }

        // Refused rather than read as "the default". A runtime told nothing about its data directory takes
        // ~/.jason, and the one caller that names it - a logon task, which has no environment to say it in -
        // would get a background runtime on a database nobody meant, quietly, at every logon.
        var directory = flag + 1 < flags.Length ? flags[flag + 1] : null;
        return string.IsNullOrWhiteSpace(directory) || directory.StartsWith("--", StringComparison.Ordinal)
            ? throw new RuntimeRunUsageException($"{DataDirectoryFlag} needs the path of a data directory after it.")
            : directory;
    }
}

/// <summary>What was typed after <c>runtime run</c> cannot be read. The mode says so and starts nothing.</summary>
public sealed class RuntimeRunUsageException : Exception
{
    public RuntimeRunUsageException()
    {
    }

    public RuntimeRunUsageException(string message)
        : base(message)
    {
    }

    public RuntimeRunUsageException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
