namespace Jason.Cli.Autostart;

/// <summary>Which of the three ways of starting something at logon this machine has, if it has one at all.</summary>
public enum AutostartPlatform
{
    /// <summary>A logon task, registered with <c>schtasks</c> from a task document.</summary>
    Windows,

    /// <summary>A LaunchAgent plist under the account's own <c>Library</c>.</summary>
    MacOs,

    /// <summary>A <c>systemd --user</c> unit under the account's own configuration.</summary>
    Linux,

    /// <summary>Nothing here knows how to start anything at logon, and the verb says so rather than guessing.</summary>
    Unsupported,
}

/// <summary>
/// Everything registering this machine's autostart consists of, composed before anything is touched: the
/// document that <em>is</em> the registration, where it belongs, and the commands that put it in place, take it
/// away and ask after it.
/// </summary>
/// <remarks>
/// Composing is a pure function of its arguments, so every artifact this product can write is asserted on every
/// machine the tests run on — including the two that cannot be registered there. What is left for a machine is
/// the running of these commands, which is the one part a test never does.
/// </remarks>
/// <param name="Platform">Which shape this is, and therefore how <see cref="Artifact"/> is read back.</param>
/// <param name="Name">What the registration is called on this platform: a task name, a label, a unit name.</param>
/// <param name="ArtifactPath">Where the document belongs. On Windows it is a file the registration is made from rather than one the system reads afterwards.</param>
/// <param name="Artifact">The document itself.</param>
/// <param name="Apply">The commands that register it, in order; each is a program followed by its arguments.</param>
/// <param name="Remove">The commands that unregister it, in order.</param>
/// <param name="Query">The command that says whether it is registered.</param>
/// <param name="Run">The command line that gets registered: the runtime itself, and never something that starts one.</param>
public sealed record AutostartRegistration(
    AutostartPlatform Platform,
    string Name,
    string ArtifactPath,
    string Artifact,
    IReadOnlyList<IReadOnlyList<string>> Apply,
    IReadOnlyList<IReadOnlyList<string>> Remove,
    IReadOnlyList<string> Query,
    IReadOnlyList<string> Run);

/// <summary>What a machine says is registered: whether anything is, and the command line it names.</summary>
/// <param name="Registered">Whether this account has a registration at all.</param>
/// <param name="Run">The command line the registration names, or nothing where there is no registration.</param>
/// <param name="ArtifactPath">Where the document lives, for a person who wants to look at it.</param>
public sealed record AutostartState(bool Registered, IReadOnlyList<string> Run, string? ArtifactPath);
