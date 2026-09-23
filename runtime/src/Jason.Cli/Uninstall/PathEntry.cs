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

/// <summary>
/// The one definition of what the install scripts write onto a PATH, read backwards.
/// </summary>
/// <remarks>
/// <para>
/// Here rather than in the remover, and pure text in and text out, because that is what makes it testable
/// without a machine: a rule about somebody's login profile that could only be exercised by editing somebody's
/// login profile would be exercised by nobody.
/// </para>
/// <para>
/// And here rather than restated in three places. <c>install.sh</c> writes the marker, this reads it, and
/// <c>InstallScriptTests</c> asserts the script still spells it this way — so the script and the verb that
/// undoes it cannot drift apart without a red test.
/// </para>
/// </remarks>
public static class PathEntry
{
    /// <summary>The English sentence <c>install.sh</c> writes above the line it appends.</summary>
    /// <remarks>
    /// Not what a removal keys on. A person's own profile could carry this sentence, and a run with a
    /// different <c>--install-dir</c> writes a second marker of its own; the script had the same problem in
    /// reverse and answered it the same way. <b>The line names the directory, so the line is the thing to
    /// match</b> — whole, and literally.
    /// </remarks>
    public const string Marker = "# added by jason install";

    /// <summary>The line itself, spelled exactly as <c>install.sh</c> spells it.</summary>
    public static string ExportLine(string installDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installDirectory);
        return $"export PATH=\"{installDirectory}:$PATH\"";
    }

    /// <summary>
    /// The login profiles the installer may have written into: <c>~/.profile</c>, which <c>sh</c> and
    /// <c>bash</c> read at login, and <c>~/.zprofile</c> as well for somebody whose shell is zsh.
    /// </summary>
    public static IReadOnlyList<string> ProfileFiles(string home, string? shell)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(home);

        var profiles = new List<string> { Path.Combine(home, ".profile") };
        if (shell is { Length: > 0 } && Path.GetFileName(shell.TrimEnd('/')) == "zsh")
        {
            profiles.Add(Path.Combine(home, ".zprofile"));
        }

        return profiles;
    }

    /// <summary>
    /// A profile with the three lines the installer appended taken out, or <b>the same string</b> when it
    /// carries none.
    /// </summary>
    /// <remarks>
    /// The same instance on purpose: it lets the caller tell "nothing to do" from "rewritten identically" and
    /// never open the file for writing at all. A profile is somebody's own document with years of their work
    /// in it, and an uninstall that rewrote one would be remembered for that rather than for removing Jason.
    /// </remarks>
    public static string WithoutEntry(string profileText, string installDirectory)
    {
        ArgumentNullException.ThrowIfNull(profileText);
        ArgumentException.ThrowIfNullOrWhiteSpace(installDirectory);

        var line = ExportLine(installDirectory);
        if (!profileText.Contains(line, StringComparison.Ordinal))
        {
            return profileText;
        }

        var newline = profileText.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = profileText.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').ToList();

        var removed = false;
        for (var index = lines.Count - 1; index >= 0; index--)
        {
            if (!string.Equals(lines[index].TrimEnd(), line, StringComparison.Ordinal))
            {
                continue;
            }

            // Exactly what the script appended, and in that order: a blank line, the marker, the line. Each
            // one is taken only if it is really there, so a hand-written line without a marker above it loses
            // the line and nothing else.
            var first = index;
            if (first > 0 && string.Equals(lines[first - 1].Trim(), Marker, StringComparison.Ordinal))
            {
                first--;
                if (first > 0 && lines[first - 1].Trim().Length == 0)
                {
                    first--;
                }
            }

            lines.RemoveRange(first, index - first + 1);
            removed = true;
        }

        return removed ? string.Join(newline, lines) : profileText;
    }

    /// <summary>
    /// This account's <c>Path</c> value with that directory taken out of it, keeping every other entry
    /// verbatim and in order, or <b>the same string</b> when it is not in there.
    /// </summary>
    /// <remarks>
    /// <c>install.ps1</c> writes no marker on Windows, because "the user's PATH lives in the registry, where a
    /// directory is its own mark". So the directory is what is matched — ignoring case, as the file system
    /// does, and ignoring a trailing separator, which is the same directory written differently.
    /// </remarks>
    public static string WithoutDirectory(string registryValue, string installDirectory)
    {
        ArgumentNullException.ThrowIfNull(registryValue);
        ArgumentException.ThrowIfNullOrWhiteSpace(installDirectory);

        var wanted = Normalise(installDirectory);
        var entries = registryValue.Split(';');
        var kept = entries.Where(entry => !string.Equals(Normalise(entry), wanted, StringComparison.OrdinalIgnoreCase)).ToList();

        // Every other entry as it was, in the order it was in: this edit is about one directory, and a PATH
        // rewritten "equivalently" is still a PATH somebody did not ask to have rewritten.
        return kept.Count == entries.Length ? registryValue : string.Join(';', kept);
    }

    /// <summary>Whether that value carries the directory at all, by the same rule.</summary>
    public static bool Carries(string registryValue, string installDirectory)
    {
        ArgumentNullException.ThrowIfNull(registryValue);
        ArgumentException.ThrowIfNullOrWhiteSpace(installDirectory);

        var wanted = Normalise(installDirectory);
        return registryValue.Split(';').Any(entry => string.Equals(Normalise(entry), wanted, StringComparison.OrdinalIgnoreCase));
    }

    private static string Normalise(string entry) => entry.Trim().TrimEnd('\\', '/');
}
