namespace Jason.App;

/// <summary>
/// What <c>jason runtime run</c> was asked for. Only the runtime-service mode reads these, and it reads them
/// straight from argv rather than through the CLI parser: the parser lives on the client side of the process
/// boundary and must not be loaded to start a server.
/// </summary>
/// <param name="Detached">Run as a background service: no console, standard streams on the null device.</param>
public sealed record RuntimeRunArguments(bool Detached)
{
    private const string DetachedFlag = "--detached";

    /// <summary>Reads the flags that follow the <c>runtime run</c> verbs; the verbs themselves are never flags.</summary>
    public static RuntimeRunArguments Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        return new RuntimeRunArguments(args.Skip(2).Contains(DetachedFlag, StringComparer.Ordinal));
    }
}
