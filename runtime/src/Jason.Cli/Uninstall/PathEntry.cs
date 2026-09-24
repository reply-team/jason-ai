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
/// Whether it is Jason's to take off: the installer put it there, and its directory holds nothing but what the
/// installer writes. A directory that is on the PATH by somebody else's hand is reported and left alone, and so is
/// one other programs are on the PATH through: this verb removes what Jason wrote, and a PATH is a person's own
/// document.
/// </param>
/// <param name="Persisted">
/// Where this account keeps the directory on its PATH for shells not yet started — this account's Path value on
/// Windows, the login profile its shell reads on Unix — or null where nothing does. Not the same question as
/// <paramref name="Ours"/>: a directory can be on the PATH by somebody else's hand, and a new shell finds
/// <c>jason</c> through it all the same, which is what <c>jason status</c> asks.
/// </param>
public sealed record PathEntryPlan(string Directory, IReadOnlyList<string> Profiles, string? RegistryValue, bool Ours, string? Persisted = null);

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
    /// Every login profile the installer or the repair may have written into, whatever this account's shell is
    /// now: the removal looks in all of them, because the shell somebody uses today is not the one they installed
    /// under.
    /// </summary>
    public static IReadOnlyList<string> ProfileFiles(string home)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(home);
        return [.. new[] { ".profile", ".bash_profile", ".bash_login", ".zprofile" }.Select(name => Path.Combine(home, name))];
    }

    /// <summary>
    /// The one file a login shell of this account reads, and so the one file the installer, a repair and the
    /// <c>path</c> check all mean: zsh reads <c>~/.zprofile</c>; bash reads the first of <c>~/.bash_profile</c>,
    /// <c>~/.bash_login</c> and <c>~/.profile</c> that exists; every other shell reads <c>~/.profile</c>.
    /// </summary>
    /// <param name="home">This account's home directory.</param>
    /// <param name="shell">The account's shell, as <c>SHELL</c> names it.</param>
    /// <param name="exists">Whether a file exists; the file system's own answer, except in a test.</param>
    /// <remarks>
    /// <para>
    /// This was "<c>~/.profile</c>, and <c>~/.zprofile</c> for zsh", and it is false for bash wherever
    /// <c>~/.bash_profile</c> exists — every Fedora, RHEL and Arch account's skeleton, and a macOS account on
    /// bash — because bash then never reads <c>~/.profile</c>. The installer wrote there, the repair wrote there,
    /// and the check read there, so all three agreed about a file no login shell would open, and the check said
    /// <c>ok</c> about it.
    /// </para>
    /// <para>
    /// <c>install.sh</c> carries the same rule as <see cref="LoginProfileFunction"/>, and a test runs that
    /// function against this one.
    /// </para>
    /// </remarks>
    public static string LoginProfile(string home, string? shell, Func<string, bool>? exists = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(home);
        exists ??= File.Exists;

        switch (Path.GetFileName((shell ?? string.Empty).TrimEnd('/')))
        {
            case "zsh":
                return Path.Combine(home, ".zprofile");
            case "bash":
                foreach (var candidate in new[] { ".bash_profile", ".bash_login" })
                {
                    if (exists(Path.Combine(home, candidate)))
                    {
                        return Path.Combine(home, candidate);
                    }
                }

                return Path.Combine(home, ".profile");
            default:
                return Path.Combine(home, ".profile");
        }
    }

    /// <summary>
    /// How a Unix account's login profiles carry that directory, read from the files under its home.
    /// </summary>
    /// <param name="directory">The install directory.</param>
    /// <param name="home">The account's home directory.</param>
    /// <param name="shell">The account's shell, as <c>SHELL</c> names it.</param>
    /// <remarks>
    /// <para>
    /// Two questions, answered separately. Which profiles carry the block this installer appends — every one it
    /// or a repair may have written into, whatever the shell is now — is what the uninstall removes. Whether the
    /// one file a login shell of this account reads carries the line at all is what the <c>path</c> check asks,
    /// and a profile no login shell opens is not an answer to it.
    /// </para>
    /// <para>
    /// <b>And the block is Jason's only where its directory is</b> — holding nothing but what <c>install.sh</c>
    /// writes there, the rule the Windows entry and the install directory itself are removed by. The marker made
    /// the block ours on its own, and <c>install.sh</c>'s default is <c>~/.local/bin</c>, where Claude Code,
    /// <c>uv</c> and <c>pipx</c> install as well: once the line was there they were found through it, and the
    /// uninstall took them off the PATH along with Jason.
    /// </para>
    /// </remarks>
    public static PathEntryPlan ReadProfiles(string directory, string home, string? shell)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(home);

        var profiles = ProfileFiles(home).Where(profile => Holds(profile, directory, marked: true)).ToList();
        var login = LoginProfile(home, shell);
        return new PathEntryPlan(
            directory,
            profiles,
            null,
            profiles.Count > 0 && JasonsOwn(directory, windows: false),
            Holds(login, directory, marked: false) ? login : null);
    }

    /// <summary>
    /// Whether a profile carries the line for that directory: as the installer's marked block, or — where
    /// <paramref name="marked"/> is false — as a whole line of its own, however it got there.
    /// </summary>
    private static bool Holds(string profile, string directory, bool marked)
    {
        try
        {
            if (!File.Exists(profile))
            {
                return false;
            }

            var bytes = File.ReadAllBytes(profile);
            return marked ? !ReferenceEquals(WithoutEntry(bytes, directory), bytes) : CarriesLine(bytes, directory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// <see cref="LoginProfile"/>, as the function <c>install.sh</c> defines and calls. The script carries this text
    /// verbatim, and a test runs it in <c>sh</c> against every case of the rule above.
    /// </summary>
    public const string LoginProfileFunction =
        """
        login_profile() {
            case "${SHELL##*/}" in
                zsh) echo "$HOME/.zprofile" ;;
                bash)
                    for candidate in .bash_profile .bash_login; do
                        if [ -f "$HOME/$candidate" ]; then
                            echo "$HOME/$candidate"
                            return 0
                        fi
                    done
                    echo "$HOME/.profile" ;;
                *) echo "$HOME/.profile" ;;
            esac
        }
        """;

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
    /// <b>Nothing is appended that is already there</b>, compared the way Windows will read it — ignoring case
    /// and a trailing separator, and expanded only where the value is <c>REG_EXPAND_SZ</c>: Windows expands the
    /// variables of that kind alone, so a <c>%VARIABLE%</c> entry in a <c>REG_SZ</c> Path names no directory at
    /// all — so a second run, or a repair typed after the installer, changes nothing. The empty entries are dropped before the directory is appended, the installer's rule, because
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
            "if (-not @($stored -split ';' | Where-Object { $(if ($kind -eq 'ExpandString') { [Environment]::ExpandEnvironmentVariables($_) } else { $_ }).TrimEnd('\\') -ieq $entry })) { $key.SetValue('Path', ((@($stored -split ';' | Where-Object { $_ }) + $entry) -join ';'), $kind) }",
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
    /// A profile with the blocks the installer appended taken out, or <b>the same string</b> when it carries
    /// none.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The same instance on purpose: it lets the caller tell "nothing to do" from "rewritten identically" and
    /// never open the file for writing at all. A profile is somebody's own document with years of their work
    /// in it, and an uninstall that rewrote one would be remembered for that rather than for removing Jason.
    /// </para>
    /// <para>
    /// <b>A block is the marker and the line under it</b> — with the blank line above them where there is one,
    /// which is what both the installer and the repair append. A line nobody marked is somebody's own, written
    /// by hand, and stays: this verb removes only what this installer wrote, on this platform as on the other,
    /// where an entry the installer did not write is left on the Path too.
    /// </para>
    /// <para>
    /// <b>Every other byte stays as it was.</b> The text is split on <c>\n</c> alone and each line keeps its own
    /// <c>\r</c>: a profile with a single CRLF in it used to come back all CRLF, which puts a carriage return at
    /// the end of every line a shell then reads. <see cref="WithoutEntry(byte[], string)"/> does this over the
    /// file's own bytes.
    /// </para>
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

        var lines = profileText.Split('\n').ToList();

        var removed = false;
        for (var index = lines.Count - 1; index >= 0; index--)
        {
            if (!string.Equals(lines[index].TrimEnd(), line, StringComparison.Ordinal)
                || index == 0
                || !string.Equals(lines[index - 1].Trim(), Marker, StringComparison.Ordinal))
            {
                continue;
            }

            // Exactly what the script appended, and in that order: a blank line, the marker, the line.
            var first = index - 1;
            if (first > 0 && lines[first - 1].Trim().Length == 0)
            {
                first--;
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

        return removed ? string.Join('\n', lines) : profileText;
    }

    /// <summary>
    /// The same, over a profile's own bytes: every byte that is not part of a block comes back as it was,
    /// whatever encoding the file is in — or <b>the same array</b> when it carries none.
    /// </summary>
    /// <remarks>
    /// Read as UTF-8 and written back, a byte that was not UTF-8 came back as U+FFFD. Each byte is mapped to the
    /// one character of the same value instead, and the line looked for is the directory's UTF-8 bytes mapped the
    /// same way, so a directory with non-ASCII in its name is still found.
    /// </remarks>
    public static byte[] WithoutEntry(byte[] profile, string installDirectory)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var text = System.Text.Encoding.Latin1.GetString(profile);
        var without = WithoutEntry(text, AsBytes(installDirectory));
        return ReferenceEquals(without, text) ? profile : System.Text.Encoding.Latin1.GetBytes(without);
    }

    /// <summary>
    /// Whether a profile carries the line that puts that directory on the PATH, as a whole line of its own and
    /// not commented out — marked or not, because a login shell runs it either way.
    /// </summary>
    /// <remarks>
    /// Whole lines, by the rule <see cref="WithoutEntry(string, string)"/> keeps. This was a substring search, so
    /// <c># export PATH=…</c> — a line somebody had commented out — made the <c>path</c> check say a new
    /// login shell would find <c>jason</c>.
    /// </remarks>
    public static bool CarriesLine(byte[] profile, string installDirectory)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var line = ExportLine(AsBytes(installDirectory));
        return System.Text.Encoding.Latin1.GetString(profile).Split('\n').Any(candidate => string.Equals(candidate.TrimEnd(), line, StringComparison.Ordinal));
    }

    /// <summary>A directory as the characters its UTF-8 bytes map to one for one, which is how a profile is read here.</summary>
    private static string AsBytes(string installDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installDirectory);
        return System.Text.Encoding.Latin1.GetString(System.Text.Encoding.UTF8.GetBytes(installDirectory));
    }

    /// <summary>
    /// What <c>install.ps1</c> ever writes into an install directory: the executable, and the one an upgrade
    /// replaced while it was running.
    /// </summary>
    public static IReadOnlyList<string> InstallerFiles { get; } = ["jason.exe", "jason.previous.exe"];

    /// <summary>
    /// Whether a directory holding those entries is Jason's own: nothing in it but what the installer writes.
    /// </summary>
    /// <remarks>
    /// On Windows there is no marker — the Path is in the registry, "where a directory is its own mark" — so being
    /// on the Path is not enough to make an entry Jason's. <c>install.ps1 -InstallDir</c> into a directory already
    /// on the Path writes nothing there, and a build copied "somewhere on your PATH" lands in a directory
    /// everything else is on the Path through: this account's <c>WindowsApps</c>, say, on every account's default
    /// Path. Taking that entry off would take everything in it off with Jason. So the entry is Jason's only where
    /// the directory is, by the same rule the install directory itself is removed by.
    /// </remarks>
    public static bool OnlyInstallerFiles(IEnumerable<string> entries, bool windows)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var (names, comparer) = windows ? (InstallerFiles, StringComparer.OrdinalIgnoreCase) : (UnixInstallerFiles, StringComparer.Ordinal);
        return entries.All(entry => names.Contains(Path.GetFileName(entry), comparer));
    }

    /// <summary>Whether a directory is there and holds nothing but what that platform's installer writes.</summary>
    public static bool JasonsOwn(string directory, bool windows)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        try
        {
            return System.IO.Directory.Exists(directory) && OnlyInstallerFiles(System.IO.Directory.EnumerateFileSystemEntries(directory), windows);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Why an entry that is not Jason's was left, said as what was found: a directory that holds more than Jason,
    /// a line nobody marked, or nothing there at all.
    /// </summary>
    public static string WhyLeft(PathEntryPlan plan, bool windows)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (windows)
        {
            return plan.Persisted is not null
                ? $"'{plan.Directory}' is on this account's Path, but it holds more than this installer writes there, so the entry is not Jason's to take off: whatever else is in it is on the Path through the same entry."
                : "That directory is not on this account's Path, so there was nothing to take off it.";
        }

        if (plan.Profiles.Count > 0)
        {
            return $"'{plan.Directory}' holds more than this installer writes there, so the block it wrote in {string.Join(", ", plan.Profiles)} "
                + "was left: whatever else is in that directory is on the PATH through the same line.";
        }

        return plan.Persisted is not null
            ? "The line that puts that directory on the PATH is in a login profile without the mark this installer writes above it, so it is somebody's own and was left."
            : "No login profile carries the block this installer writes for that directory, so nothing was taken off the PATH.";
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

    /// <summary>What <c>install.sh</c> ever leaves in an install directory: the executable, and nothing else.</summary>
    public static IReadOnlyList<string> UnixInstallerFiles { get; } = ["jason"];

    /// <summary>
    /// How the entries of a Path value of that kind are read by Windows: expanded where it is
    /// <c>REG_EXPAND_SZ</c>, and exactly as written where it is <c>REG_SZ</c>, which Windows never expands.
    /// </summary>
    /// <param name="entries">What the directory holds.</param>
    /// <param name="windows">
    /// Whose installer: <c>install.ps1</c>'s names, compared as Windows compares them, or <c>install.sh</c>'s,
    /// compared byte for byte.
    /// </param>
    /// <remarks>
    /// Always expanding counted <c>%VARIABLE%\bin</c> in a <c>REG_SZ</c> Path as the directory it would expand to,
    /// so the check said a new shell would find <c>jason</c> through an entry no shell ever resolves.
    /// </remarks>
    public static Func<string, string> ReadAs(bool expandable) =>
        expandable ? Environment.ExpandEnvironmentVariables : static entry => entry;

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
