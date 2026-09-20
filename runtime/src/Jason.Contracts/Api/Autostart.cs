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
public sealed record AutostartStatusResponse(
    bool Registered,
    string Platform,
    string? DataDirectory,
    string? Executable,
    bool? ExecutableExists,
    string? ArtifactPath);
