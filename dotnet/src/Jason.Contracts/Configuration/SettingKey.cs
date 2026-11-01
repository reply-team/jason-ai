using System.Collections.Frozen;
using Jason.Contracts.Ids;
using Jason.Contracts.Operations;

namespace Jason.Contracts.Configuration;

/// <summary>
/// How one key of a settings path is written where a person or a log will read it. A key is text a person typed,
/// and what gets typed into the wrong place is often a secret — a token pasted where its name belonged — so only
/// the names the code already knows travel as they were typed, and anything else is written <see cref="Hidden"/>.
/// A name the code already knows is never one anybody had to keep to themselves. A number travels only as an index,
/// under the list option's whole path.
/// </summary>
public static class SettingKey
{
    /// <summary>What a key the code does not know is written as.</summary>
    public const string Hidden = "<key>";

    /// <summary>
    /// The prefixes of the public ids the runtime mints. A key spelled like one of them is an id the runtime itself
    /// handed out; a prefix missing here only means such an id is written <see cref="Hidden"/>, which is the
    /// direction a mistake in this list should fall.
    /// </summary>
    private static readonly FrozenSet<string> PublicIdPrefixes = new[]
    {
        "apr", "att", "cmp", "cnt", "dec", "jrn", "ntf", "pin", "prf", "rol", "rpt", "rt", "rts", "snp", "sup", "wi",
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>
    /// How long an index into a list can be and still be shown. Nobody writes a settings list of ten thousand items by
    /// hand, so a number of five digits or more where a list stands is one somebody typed — a phone number, a PIN — and
    /// is written <see cref="Hidden"/> like any other key the code does not know.
    /// </summary>
    private const int MaxIndexDigits = 4;

    /// <summary>The key of a map, in <see cref="SettingsVocabulary.ListPaths"/>: any one key.</summary>
    private const string AnyKey = "*";

    /// <summary><see cref="SettingsVocabulary.ListPaths"/>, one key at a time.</summary>
    private static readonly string[][] ListPaths = [.. SettingsVocabulary.ListPaths.Select(path => path.Split(':'))];

    /// <summary>
    /// The key as it was typed when it is a settings name (<see cref="SettingsVocabulary.Names"/>), a published
    /// operation's id, the id of a plugin installed here, or a public id; otherwise <see cref="Hidden"/>. A key made
    /// of digits is none of these on its own: it is an index only where a list stands before it, which only
    /// <see cref="ShownPath"/> can see.
    /// </summary>
    /// <param name="installedPluginIds">
    /// The plugins installed in this data directory, as the caller lists them now — not as they were when it started.
    /// </param>
    public static string Shown(string key, IReadOnlySet<string> installedPluginIds)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(installedPluginIds);

        return SettingsVocabulary.Names.Contains(key)
            || OperationCatalog.Knows(key)
            || installedPluginIds.Contains(key)
            || IsPublicId(key)
            ? key
            : Hidden;
    }

    /// <summary>
    /// A whole path, <c>Section:Key:…</c> as the configuration system spells one, with each of its keys written the
    /// way <see cref="Shown"/> writes it — except that a key of up to four digits directly under the whole path of a list
    /// (<see cref="SettingsVocabulary.ListPaths"/>), as in <c>Manager:Triggers:0</c>, is that list's index and is shown
    /// too. Anywhere else a number is a key a person chose, and no map in the settings is keyed by one: a plugin's
    /// id and an operation's name both start with a letter, while a number typed where a name belonged can be a
    /// secret as easily as a word can. It is the path that counts and not the name before it: <c>Exec</c> and
    /// <c>Http</c> are also two objects of the plugin limits, and a binding can hold a key of any name at all.
    /// </summary>
    public static string ShownPath(string path, IReadOnlySet<string> installedPluginIds)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(installedPluginIds);

        var keys = path.Split(':');
        return string.Join(':', keys.Select((key, at) =>
            at > 0 && IsIndex(key) && IsListPath(keys.AsSpan(0, at)) ? key : Shown(key, installedPluginIds)));
    }

    /// <summary>
    /// Whether these keys, written as typed, are the whole path of a list. Keys are matched without regard to case, and
    /// a map's key in the path is any key at all, whether or not it is one that would be shown.
    /// </summary>
    private static bool IsListPath(ReadOnlySpan<string> keys)
    {
        foreach (var list in ListPaths)
        {
            if (list.Length != keys.Length)
            {
                continue;
            }

            var same = true;
            for (var at = 0; same && at < keys.Length; at++)
            {
                same = list[at] == AnyKey || string.Equals(list[at], keys[at], StringComparison.OrdinalIgnoreCase);
            }

            if (same)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsPublicId(string key) =>
        key.IndexOf('_', StringComparison.Ordinal) is var separator and > 0
        && PublicIdPrefixes.Contains(key[..separator])
        && Ulid.IsWellFormed(key.AsSpan(separator + 1));

    private static bool IsIndex(string key) => key.Length is > 0 and <= MaxIndexDigits && key.All(char.IsAsciiDigit);
}
