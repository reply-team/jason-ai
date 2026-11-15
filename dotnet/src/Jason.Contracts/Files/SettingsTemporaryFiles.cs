namespace Jason.Contracts.Files;

/// <summary>
/// The temporary files the settings file's writers make beside it, by their names: each writes its new bytes into one,
/// then renames it over the settings file, and a writer that stops in between leaves it there with the settings' secrets
/// in it. There are two shapes and only these two, each a writer's own:
/// the runtime's, <c>&lt;settings file&gt;.&lt;32 lowercase hex digits&gt;.tmp</c> (a new GUID, <c>JasonConfiguration.WriteTemporary</c>),
/// and the desktop's, <c>&lt;settings file&gt;.&lt;decimal process id&gt;.tmp</c> (<c>writeAtomically</c> in
/// <c>desktop/src/setup.ts</c>, and the demo's writers beside it). No other file of the folder is one.
/// </summary>
/// <remarks>
/// One rule, read where the runtime sweeps such files at its start and where <c>jason export</c> and <c>jason import</c>
/// leave them out, so the three cannot disagree about which files they are.
/// </remarks>
public static class SettingsTemporaryFiles
{
    /// <summary>The desktop's process id is at most ten decimal digits; a longer run of digits is no writer's.</summary>
    private const int MostProcessIdDigits = 10;

    /// <summary>The runtime's GUID, written with the <c>N</c> format: 32 lowercase hexadecimal digits.</summary>
    private const int GuidDigits = 32;

    /// <summary>The shapes in words, as a help text or a log line names them: <c>settings.json.&lt;id&gt;.tmp</c>.</summary>
    public static string Shape(string settingsFileName) => settingsFileName + ".<id>.tmp";

    /// <summary>
    /// Whether <paramref name="name"/>, a file's name in the settings file's folder, is one of the writers' temporary files
    /// for the settings file named <paramref name="settingsFileName"/>. Compared exactly, letter case included: the writers
    /// make the names, and a name none of them makes is never one.
    /// </summary>
    public static bool IsOne(string settingsFileName, string name)
    {
        ArgumentNullException.ThrowIfNull(settingsFileName);
        ArgumentNullException.ThrowIfNull(name);
        var prefix = settingsFileName + ".";
        const string Suffix = ".tmp";
        if (name.Length <= prefix.Length + Suffix.Length
            || !name.StartsWith(prefix, StringComparison.Ordinal)
            || !name.EndsWith(Suffix, StringComparison.Ordinal))
        {
            return false;
        }

        var id = name.AsSpan(prefix.Length, name.Length - prefix.Length - Suffix.Length);
        return (id.Length == GuidDigits && IsLowerHex(id)) || (id.Length <= MostProcessIdDigits && IsDecimal(id));
    }

    private static bool IsLowerHex(ReadOnlySpan<char> id)
    {
        foreach (var c in id)
        {
            if (!char.IsAsciiDigit(c) && c is not (>= 'a' and <= 'f'))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsDecimal(ReadOnlySpan<char> id)
    {
        foreach (var c in id)
        {
            if (!char.IsAsciiDigit(c))
            {
                return false;
            }
        }

        return true;
    }
}
