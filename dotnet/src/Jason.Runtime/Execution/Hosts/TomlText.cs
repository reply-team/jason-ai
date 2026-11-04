using System.Globalization;
using System.Text;

namespace Jason.Runtime.Execution.Hosts;

/// <summary>TOML text a host's configuration is written in.</summary>
internal static class TomlText
{
    /// <summary>
    /// A TOML basic string: the quote and the backslash escaped, so a word cannot end the string it is in, and every
    /// character the format forbids raw (U+0000 to U+001F and U+007F) written as its escape; anything else, a letter
    /// beyond ASCII included, is kept as it is.
    /// </summary>
    internal static string BasicString(string value)
    {
        var text = new StringBuilder("\"");
        foreach (var character in value)
        {
            switch (character)
            {
                case '"':
                    text.Append("\\\"");
                    break;
                case '\\':
                    text.Append("\\\\");
                    break;
                case < ' ' or '\u007F':
                    text.Append("\\u").Append(((int)character).ToString("X4", CultureInfo.InvariantCulture));
                    break;
                default:
                    text.Append(character);
                    break;
            }
        }

        return text.Append('"').ToString();
    }
}
