using System.Text.RegularExpressions;

namespace Jason.Contracts.Plugins;

/// <summary>
/// What a plugin's id has to be: a lowercase letter, then one to sixty-three lowercase letters, digits or dashes. A
/// package's directory is named by it, a manifest declares it, a route and a grant name it, and the command-line client
/// lists installed plugins by it; the rule is spelled once, here, so that none of them can come to mean another id.
/// </summary>
public static partial class PluginIds
{
    /// <summary>Whether <paramref name="text"/> could name a plugin. A name that starts with a dot never could.</summary>
    public static bool IsWellFormed(string? text) => text is not null && Shape().IsMatch(text);

    // The end is `\z` rather than `$`, which also matches just before a final line break: an id ends where the text does.
    [GeneratedRegex(@"^[a-z][a-z0-9-]{1,63}\z")]
    private static partial Regex Shape();
}
