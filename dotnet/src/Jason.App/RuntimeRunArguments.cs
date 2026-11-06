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
/// <param name="Help">The usage was asked for (<c>--help</c>, <c>-h</c> or <c>-?</c>): it is printed and nothing is started.</param>
public sealed record RuntimeRunArguments(bool Detached, string? DataDirectory = null, bool Help = false)
{
    private const string DetachedFlag = "--detached";
    private const string DataDirectoryFlag = "--data-dir";

    /// <summary>The spellings that ask for the usage.</summary>
    private static readonly string[] HelpFlags = ["--help", "-h", "-?"];

    /// <summary>What <c>jason runtime run --help</c> prints: the line, what each flag does, and what a person types instead.</summary>
    public const string Usage =
        "usage: jason runtime run [--detached] [--data-dir <path>]\n"
        + "  --detached         run as a background service: no console, standard streams on the null device.\n"
        + "  --data-dir <path>  the data directory this runtime owns, instead of JASON_DATA_DIR or ~/.jason.\n"
        + "  --help, -h, -?     print this and exit.\n"
        + "This is the verb a logon registration or a service manager runs; a person types `jason runtime start`.";

    /// <summary>The usage's first line, which an error is printed beside.</summary>
    public const string UsageLine = "usage: jason runtime run [--detached] [--data-dir <path>]";

    /// <summary>Reads the flags that follow the <c>runtime run</c> verbs; the verbs themselves are never flags.</summary>
    /// <exception cref="RuntimeRunUsageException">The data directory flag was given with no path after it, or an argument is not one this verb knows.</exception>
    public static RuntimeRunArguments Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var flags = args.Skip(2).ToArray();
        var detached = false;
        var help = false;
        string? directory = null;
        for (var at = 0; at < flags.Length; at++)
        {
            var flag = flags[at];
            if (string.Equals(flag, DetachedFlag, StringComparison.Ordinal))
            {
                detached = true;
            }
            else if (string.Equals(flag, DataDirectoryFlag, StringComparison.Ordinal))
            {
                // Refused rather than read as "the default". A runtime told nothing about its data directory takes
                // ~/.jason, and the one caller that names it - a logon task, which has no environment to say it in -
                // would get a background runtime on a database nobody meant, quietly, at every logon.
                var value = at + 1 < flags.Length ? flags[at + 1] : null;
                if (string.IsNullOrWhiteSpace(value) || value.StartsWith("--", StringComparison.Ordinal))
                {
                    throw new RuntimeRunUsageException($"{DataDirectoryFlag} needs the path of a data directory after it.");
                }

                directory ??= value;
                at++;
            }
            else if (HelpFlags.Contains(flag, StringComparer.Ordinal))
            {
                help = true;
            }
            else
            {
                // Refused rather than ignored: a flag this verb never read would otherwise start a runtime that was
                // not asked for the thing the flag names.
                throw new RuntimeRunUsageException($"'{flag}' is not a flag of this verb.");
            }
        }

        return new RuntimeRunArguments(detached, directory, help);
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
