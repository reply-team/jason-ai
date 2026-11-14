namespace Jason.Contracts.Execution;

/// <summary>
/// The variable that confines one call of the CLI. Set to any non-empty value, an option of an operation verb that
/// reads a file's content into a request takes only standard input, and <c>status --provider-cli</c> runs only a
/// program it finds by a bare name in a fully qualified entry of the search path. It also refuses, before any request, every command of <c>Jason.Cli</c>'s <c>ConfinedVerbs</c>: a person's own word and the
/// acts that change an installation, a higher bar and not a boundary. It confines the call it is set on and
/// nothing that call starts: three starts drop it, a runtime the CLI starts, the role work a runtime launches and the
/// terminal <c>session open</c> opens; every other child inherits it, and none reads it. The CLI and the runtime read
/// the name from here, and nothing else in .NET
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

    /// <summary>
    /// The header the CLI sends on every request while the call is confined, valued <see cref="HeaderValue"/>: the desktop's
    /// chat marker as the runtime reads it. The runtime records it on a work item made, or whose input is edited, under it, so
    /// a send written in the chat waits for the person whatever the send mode. A higher bar, not a boundary: the runtime
    /// cannot tell a person from a program that holds the token, and a request made around the CLI carries no header.
    /// </summary>
    public const string Header = "Jason-Confined";

    /// <summary>The one value <see cref="Header"/> is sent with.</summary>
    public const string HeaderValue = "1";
}
