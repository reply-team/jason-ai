namespace Jason.Cli.Uninstall;

/// <summary>
/// How this account's PATH carries the install directory, and therefore how it stops carrying it.
/// </summary>
/// <remarks>
/// The two platforms put it there differently and the installer is the authority on both: on Unix a marker
/// line and an <c>export</c> line appended to a login profile, on Windows an entry in this account's own
/// <c>Path</c> value where "a directory is its own mark". So a removal names one or the other, never both.
/// </remarks>
/// <param name="Directory">The install directory that is on the PATH, or that this would take off it.</param>
/// <param name="Profiles">The login profiles carrying the line, on Unix. Empty on Windows.</param>
/// <param name="RegistryValue">This account's <c>Path</c> value, on Windows. Null elsewhere.</param>
/// <param name="Ours">
/// Whether the installer is what put it there. A directory that is on the PATH by somebody else's hand is
/// reported and left alone: this verb removes what Jason wrote, and a PATH is a person's own document.
/// </param>
public sealed record PathEntryPlan(string Directory, IReadOnlyList<string> Profiles, string? RegistryValue, bool Ours);

/// <summary>What removing it came to, so the report says what really happened rather than what was intended.</summary>
/// <param name="Removed">Whether anything was taken off the PATH at all.</param>
/// <param name="Touched">The files or values that changed. Empty when nothing needed changing.</param>
/// <param name="Note">Why nothing was removed, where nothing was.</param>
public sealed record PathEntryOutcome(bool Removed, IReadOnlyList<string> Touched, string? Note);
