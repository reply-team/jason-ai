namespace Jason.Contracts.Api;

/// <summary>
/// What this account has registered to start at logon, printed by all three autostart verbs so that a script
/// reads one shape and can check what <c>enable</c> did with the same parser it checks <c>status</c> with.
/// </summary>
/// <remarks>
/// It is read from the machine rather than composed again: the registration a machine holds may have been made
/// by another installation, under another data directory, which is exactly the thing worth seeing.
/// </remarks>
/// <param name="Registered">Whether anything is registered for this account.</param>
/// <param name="Platform">How this machine registers things: <c>windows</c>, <c>macos</c>, <c>linux</c>, or <c>unsupported</c>.</param>
/// <param name="DataDirectory">The data directory the registration names, which need not be this session's.</param>
/// <param name="Executable">The program the registration runs.</param>
/// <param name="ExecutableExists">Whether that program is still where the registration says it is.</param>
/// <param name="ArtifactPath">Where the document that is the registration lives, where the platform keeps one.</param>
/// <param name="Path">
/// The search path the registered runtime starts with, as the registration carries it: on macOS and Linux, the PATH of the
/// process that registered it, null for a LaunchAgent or a user unit that carries none, which launchd or the systemd user
/// manager starts on its own; null on Windows.
/// </param>
public sealed record AutostartStatusResponse(
    bool Registered,
    string Platform,
    string? DataDirectory,
    string? Executable,
    bool? ExecutableExists,
    string? ArtifactPath,
    string? Path = null);
