using Jason.Contracts.Discovery;

namespace Jason.Contracts.Files;

/// <summary>
/// The temporary files the settings' writers make beside the file they write: each writes its new bytes into one, then
/// renames it over that file, and a writer that stops in between leaves it there with the settings' secrets in it. There
/// are two shapes and only these two, each a writer's own (<see cref="Shapes"/>): the runtime's,
/// <c>&lt;file&gt;.&lt;32 lowercase hex digits&gt;.tmp</c> (a new GUID, <see cref="NameFor"/>, which
/// <c>JasonConfiguration.WriteTemporary</c> names its file by), and the desktop's, <c>&lt;file&gt;.&lt;decimal process
/// id&gt;.tmp</c> (<c>settingsTemporary</c> in <c>desktop/src/settings-temporary.ts</c>). No other file of the folder is
/// one.
/// </summary>
/// <remarks>
/// One rule, read where the runtime sweeps such files at its start and where <c>jason export</c> and <c>jason import</c>
/// leave them out, so the three cannot disagree about which files they are; <c>desktop/settings-temporary-files.json</c>
/// is <see cref="Shapes"/> as data, written from here by a test, and the desktop's test holds its writers to it.
/// </remarks>
public static class SettingsTemporaryFiles
{
    /// <summary>What every such name ends with.</summary>
    public const string Suffix = ".tmp";

    /// <summary>
    /// The shapes of the id between the written file's name and <see cref="Suffix"/>, one per writer: the runtime's GUID in
    /// the <c>N</c> format, 32 lowercase hexadecimal digits, and the desktop's process id, at most ten decimal digits.
    /// </summary>
    public static readonly IReadOnlyList<SettingsTemporaryShape> Shapes =
    [
        new("runtime", "0123456789abcdef", 32, 32),
        new("desktop", "0123456789", 1, 10),
    ];

    /// <summary>
    /// The files whose writes leave such names, in the layout <paramref name="paths"/> answer by: the settings file, and the
    /// secrets file where the layout keeps it apart (layout 2's <c>config/secrets.json</c>).
    /// </summary>
    public static IReadOnlyList<string> WrittenFiles(JasonPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return paths.UserSettingsFileHoldsSecrets ? [paths.UserSettingsFile] : [paths.UserSettingsFile, paths.SecretsFile];
    }

    /// <summary>The name the runtime's writer gives the temporary file of the write <paramref name="id"/> of <paramref name="fileName"/>.</summary>
    public static string NameFor(string fileName, Guid id)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        return fileName + "." + id.ToString("N") + Suffix;
    }

    /// <summary>The shapes in words, as a help text or a log line names them: <c>settings.json.&lt;id&gt;.tmp</c>.</summary>
    public static string Shape(string settingsFileName) => settingsFileName + ".<id>" + Suffix;

    /// <summary>
    /// Whether <paramref name="name"/>, a file's name in the written file's folder, is one of the writers' temporary files
    /// for the file named <paramref name="settingsFileName"/>. Compared exactly, letter case included: the writers make the
    /// names, and a name none of them makes is never one.
    /// </summary>
    public static bool IsOne(string settingsFileName, string name)
    {
        ArgumentNullException.ThrowIfNull(settingsFileName);
        ArgumentNullException.ThrowIfNull(name);
        var prefix = settingsFileName + ".";
        if (name.Length <= prefix.Length + Suffix.Length
            || !name.StartsWith(prefix, StringComparison.Ordinal)
            || !name.EndsWith(Suffix, StringComparison.Ordinal))
        {
            return false;
        }

        var id = name.Substring(prefix.Length, name.Length - prefix.Length - Suffix.Length);
        return Shapes.Any(shape => shape.Describes(id));
    }
}

/// <summary>One writer's shape of the id in its temporary file's name.</summary>
/// <param name="Writer">Whose shape it is: <c>runtime</c> or <c>desktop</c>.</param>
/// <param name="Digits">The characters the id is made of, and no others.</param>
/// <param name="FewestDigits">How few of them it has.</param>
/// <param name="MostDigits">How many of them it has at most.</param>
public sealed record SettingsTemporaryShape(string Writer, string Digits, int FewestDigits, int MostDigits)
{
    /// <summary>Whether <paramref name="id"/> is of this shape.</summary>
    public bool Describes(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return id.Length >= FewestDigits && id.Length <= MostDigits && id.All(c => Digits.Contains(c, StringComparison.Ordinal));
    }
}
