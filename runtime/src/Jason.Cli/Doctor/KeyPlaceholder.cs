using System.Globalization;
using System.Text.RegularExpressions;
using Jason.Contracts.Configuration;

namespace Jason.Cli.Doctor;

/// <summary>
/// What a key the code does not know is written as where a settings file is described: <c>&lt;key&gt;</c>, and when an
/// object has several, <c>&lt;key 2&gt;</c>, <c>&lt;key 3&gt;</c> and so on in the order the file wrote them. The first
/// is <see cref="SettingKey.Hidden"/>, the placeholder the runtime's own sentences use, so one word means the same in
/// both places.
/// </summary>
internal static partial class KeyPlaceholder
{
    /// <summary>The placeholder for the key that is the <paramref name="number"/>th unknown one of its object, counting from 1.</summary>
    public static string For(int number) =>
        number <= 1 ? SettingKey.Hidden : string.Create(CultureInfo.InvariantCulture, $"<key {number}>");

    /// <summary>
    /// Whether a key is written like a placeholder. Such a key is never a name the code knows, whatever it is called:
    /// a person who typed one would otherwise be shown another key's place in the description, or take it.
    /// </summary>
    public static bool Is(string key) => Shape().IsMatch(key);

    [GeneratedRegex(@"^<key( [0-9]+)?>\z", RegexOptions.CultureInvariant)]
    private static partial Regex Shape();
}
