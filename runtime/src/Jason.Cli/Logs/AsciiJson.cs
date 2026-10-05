using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Jason.Contracts.Json;

namespace Jason.Cli.Logs;

/// <summary>
/// JSON as this CLI writes it wherever what the runtime logged can stand in it: the project's dialect, with <c>'</c>,
/// <c>&lt;</c>, <c>&gt;</c>, <c>&amp;</c> and <c>+</c> left as they are and every character above <c>~</c> written as
/// <c>\uXXXX</c>. One writer for <c>jason logs</c> and for the diagnostic bundle, so the two never disagree about what a
/// line of the log looks like once it has been written out again.
/// </summary>
internal static class AsciiJson
{
    /// <summary>
    /// The relaxed encoder leaves <c>'</c>, <c>&lt;</c>, <c>&gt;</c>, <c>&amp;</c> and <c>+</c> as they are, which is how
    /// the runtime writes them and how a reader matches a template verbatim. What it also leaves as it is — text above
    /// ASCII — is taken out afterwards by <see cref="Escape"/>.
    /// </summary>
    private static readonly JsonSerializerOptions Output = JasonJson.Apply(new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

    /// <summary>The value as compact JSON, plain ASCII.</summary>
    public static string Serialize<T>(T value) => Escape(JsonSerializer.Serialize(value, Output));

    /// <summary>
    /// The JSON with every character above <c>~</c> written as <c>\uXXXX</c>. Nothing sets the encoding of the console
    /// this is written to, so what a console makes of a character it cannot show is the console's own business — and
    /// code page 437 turns Cyrillic and CJK into question marks. Outside a string the document is ASCII already, so
    /// every such character is inside one, where the escape is the same text: a parser reads it back whole.
    /// </summary>
    private static string Escape(string json)
    {
        var ascii = new StringBuilder(json.Length);
        foreach (var character in json)
        {
            if (character <= '~')
            {
                ascii.Append(character);
            }
            else
            {
                ascii.Append("\\u").Append(((int)character).ToString("X4", CultureInfo.InvariantCulture));
            }
        }

        return ascii.ToString();
    }
}
