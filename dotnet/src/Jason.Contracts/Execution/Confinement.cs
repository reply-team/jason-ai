namespace Jason.Contracts.Execution;

/// <summary>
/// The variable that confines one call of the CLI. Set to any non-empty value, an option of an operation verb that
/// reads a file's content into a request takes only standard input, and <c>status --provider-cli</c> runs only a
/// program it finds by a bare name in a fully qualified entry of the search path. It confines the call it is set on and
/// nothing that call starts: a runtime the CLI starts, the role work a runtime launches, and the terminal
/// <c>session open</c> opens never carry it. The CLI and the runtime read the name from here, and nothing else in .NET
/// spells it; the desktop spells it once, in its permission model, held to this constant by a row of
/// <c>DesktopDenyListTests</c>.
/// </summary>
public static class Confinement
{
    /// <summary>The variable's name.</summary>
    public const string Variable = "JASON_CONFINED";

    /// <summary>
    /// Whether <paramref name="value"/>, read from the variable, confines the call: any value but none and empty.
    /// </summary>
    public static bool Confines(string? value) => !string.IsNullOrEmpty(value);
}
