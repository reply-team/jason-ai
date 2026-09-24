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

    /// <summary>The profile the running shell reads at login: the one file a repair may write into.</summary>
    /// <remarks>
    /// The last of <see cref="ProfileFiles"/>, and telling the two apart is the whole point of them. A removal
    /// has to look in every profile the installer may have written to; a repair has to write into the one this
    /// shell will actually read, and zsh does not read <c>~/.profile</c> at all — so a repair that named it
    /// for somebody on zsh would be advice that appeared to have worked.
    /// </remarks>
    public static string LoginProfile(string home, string? shell) => ProfileFiles(home, shell)[^1];

    /// <summary>The <c>printf</c> format <c>install.sh</c> appends with, and therefore the one a repair uses.</summary>
    public const string AppendFormat = @"\n%s\n%s\n";

    /// <summary>
    /// The command that puts the directory on this account's PATH on Unix, spelled as <c>install.sh</c>
    /// spells it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both halves, because the installer has both: the marked line appended to the login profile, which is
    /// what survives the shell, and an export for the shell it is typed in, which is what lets the next
    /// <c>jason status</c> say something different. <see cref="ExportLine"/> on its own lasts exactly one
    /// shell — printed as a repair it is advice that appears to have worked, and the Windows half of the same
    /// repair writes the registry and persists, so the two platforms differed in kind with neither saying so.
    /// </para>
    /// <para>
    /// Written the way the installer writes it, marker and all, so that <see cref="WithoutEntry"/> takes back
    /// out what this puts in. A repair whose line <c>jason uninstall</c> could not find again would leave a
    /// profile carrying Jason after Jason was gone.
    /// </para>
    /// <para>
    /// And typed twice, it changes nothing the second time — which is the installer's own rule, the whole line
    /// looked for before it is appended. The repair appended unconditionally, so an agent that ran it in a
    /// shell the installer had already served got a second block, and the export half put the directory on
    /// the running PATH twice.
    /// </para>
    /// </remarks>
    public static string AppendCommand(string installDirectory, string profile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(profile);

        var line = ExportLine(installDirectory);
        return $"(grep -qxF {Word(line)} {Word(profile)} 2>/dev/null || printf '{AppendFormat}' {Word(Marker)} {Word(line)} >> {Word(profile)})"
            + $" && case \":$PATH:\" in *:{Word(installDirectory)}:*) ;; *) {line} ;; esac";
    }

    /// <summary>
    /// The command that puts the directory on this account's PATH on Windows: <see cref="RegistryStatements"/>,
    /// on one line, in a script block of its own so that nothing it names is left in the caller's session.
    /// </summary>
    /// <param name="installDirectory">The directory to put on it.</param>
    /// <param name="subKey">
    /// The key under <c>HKEY_CURRENT_USER</c> whose <c>Path</c> it edits. Always <c>Environment</c>, except in
    /// a test that runs this very text against a key of its own.
    /// </param>
    public static string RegistryCommand(string installDirectory, string subKey = UserPathValue.EnvironmentKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(subKey);

        return "& { " + string.Join("; ", RegistryStatements(Literal(installDirectory.TrimEnd('\\')), Literal(subKey))) + " }";
    }

    /// <summary>
    /// What puts a directory on this account's PATH on Windows, one statement a line: the text
    /// <c>install.ps1</c> runs and the repair <c>jason status</c> prints, which a test holds to each other.
    /// </summary>
    /// <param name="directory">A PowerShell expression for the directory: a literal, or the installer's variable.</param>
    /// <param name="subKey">A PowerShell expression for the key under <c>HKEY_CURRENT_USER</c>.</param>
    /// <remarks>
    /// <para>
    /// <b>The value is read unexpanded and written back with its own kind.</b> Through
    /// <c>[Environment]::GetEnvironmentVariable</c> and its setter, every <c>%USERPROFILE%\…</c> entry came
    /// back expanded and the whole value went back as <c>REG_SZ</c>: one install turned an ordinary account's
    /// <c>REG_EXPAND_SZ</c> Path into fixed strings. A Path that does not exist yet is created as
    /// <c>ExpandString</c>, the kind Windows gives one.
    /// </para>
    /// <para>
    /// <b>Nothing is appended that is already there</b>, compared the way Windows will read it — expanded,
    /// ignoring case and a trailing separator — so a second run, or a repair typed after the installer, changes
    /// nothing. The empty entries are dropped before the directory is appended, the installer's rule, because
    /// an empty PATH entry is the current directory.
    /// </para>
    /// <para>
    /// <b>Then running programs are told</b>, which the setter used to do for free: without the broadcast, a
    /// terminal opened from the Start menu inherits Explorer's environment from logon and does not find
    /// <c>jason</c> until the person signs out. And the session it is typed in learns it too, once.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<string> RegistryStatements(string directory, string subKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(subKey);

        return
        [
            $"$entry = {directory}",
            $"$key = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey({subKey})",
            "$stored = [string]$key.GetValue('Path', '', 'DoNotExpandEnvironmentNames')",
            "$kind = if ($key.GetValueNames() -contains 'Path') { $key.GetValueKind('Path') } else { 'ExpandString' }",
            "if (-not @($stored -split ';' | Where-Object { [Environment]::ExpandEnvironmentVariables($_).TrimEnd('\\') -ieq $entry })) { $key.SetValue('Path', ((@($stored -split ';' | Where-Object { $_ }) + $entry) -join ';'), $kind) }",
            "$key.Dispose()",
            "if (-not ('Jason.UserEnvironment' -as [type])) { Add-Type -Namespace Jason -Name UserEnvironment -MemberDefinition '[DllImport(\"user32.dll\", CharSet = CharSet.Unicode)] public static extern IntPtr SendMessageTimeout(IntPtr window, uint message, UIntPtr wParam, string lParam, uint flags, uint timeout, out UIntPtr result);' }",
            "$answer = [UIntPtr]::Zero",
            "[void][Jason.UserEnvironment]::SendMessageTimeout([IntPtr]0xffff, 0x1a, [UIntPtr]::Zero, 'Environment', 2, 5000, [ref]$answer)",
            "if (-not @($env:Path -split ';' | Where-Object { $_.TrimEnd('\\') -ieq $entry })) { $env:Path += ';' + $entry }",
        ];
    }

    /// <summary>
    /// A string as one shell word: single quotes, with the one escape POSIX has for a single quote inside
    /// them.
    /// </summary>
    /// <remarks>
    /// A directory with an apostrophe in it would otherwise end the word and turn the repair into a syntax
    /// error — a command that looks like it ran, printed by the verb whose whole job is to be trusted.
    /// </remarks>
    private static string Word(string word) => "'" + word.Replace("'", @"'\''", StringComparison.Ordinal) + "'";

    /// <summary>A string as one PowerShell literal, where the only escape is doubling the quote.</summary>
    private static string Literal(string word) => "'" + word.Replace("'", "''", StringComparison.Ordinal) + "'";

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

            // And carry on above what was just taken out. Without this the walk stepped down into a list
            // three shorter than the one it was measured against and read past its end — which is the shape
            // `install.sh` leaves behind whenever nothing follows the block it appended, so `jason uninstall`
            // threw on the ordinary profile rather than the unusual one. Found by asking, for the first time,
            // whether what a repair writes is what the removal takes back out.
            index = first;
        }

        return removed ? string.Join(newline, lines) : profileText;
    }

    /// <summary>
    /// This account's <c>Path</c> value with that directory taken out of it, keeping every other entry
    /// verbatim and in order, or <b>the same string</b> when it is not in there.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>install.ps1</c> writes no marker on Windows, because "the user's PATH lives in the registry, where a
    /// directory is its own mark". So the directory is what is matched — ignoring case, as the file system
    /// does, and ignoring a trailing separator, which is the same directory written differently.
    /// </para>
    /// <para>
    /// The value is the one the registry stores, <c>%VARIABLE%</c>s and all, so each entry is compared as
    /// Windows will read it — expanded — and every entry that is kept is kept as it was written. Reading it
    /// expanded is what turned a whole Path's <c>%USERPROFILE%</c> entries into fixed strings on the way back.
    /// </para>
    /// </remarks>
    public static string WithoutDirectory(string registryValue, string installDirectory, Func<string, string>? expand = null)
    {
        ArgumentNullException.ThrowIfNull(registryValue);
        ArgumentException.ThrowIfNullOrWhiteSpace(installDirectory);

        var entries = registryValue.Split(';');
        var kept = entries.Where(entry => !Names(entry, installDirectory, expand)).ToList();

        // Every other entry as it was, in the order it was in: this edit is about one directory, and a PATH
        // rewritten "equivalently" is still a PATH somebody did not ask to have rewritten.
        return kept.Count == entries.Length ? registryValue : string.Join(';', kept);
    }

    /// <summary>Whether that value carries the directory at all, by the same rule.</summary>
    public static bool Carries(string registryValue, string installDirectory, Func<string, string>? expand = null)
    {
        ArgumentNullException.ThrowIfNull(registryValue);
        ArgumentException.ThrowIfNullOrWhiteSpace(installDirectory);

        return registryValue.Split(';').Any(entry => Names(entry, installDirectory, expand));
    }

    /// <summary>Whether one entry, as Windows would read it, is that directory.</summary>
    private static bool Names(string entry, string installDirectory, Func<string, string>? expand) =>
        string.Equals(
            Normalise((expand ?? Environment.ExpandEnvironmentVariables)(entry)),
            Normalise(installDirectory),
            StringComparison.OrdinalIgnoreCase);

    private static string Normalise(string entry) => entry.Trim().TrimEnd('\\', '/');
}
