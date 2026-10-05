using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Jason.Cli.Logs;

/// <summary>
/// Puts a line of the runtime's log back together: the template it was written from, with the values that went into it.
/// </summary>
/// <remarks>
/// <para>
/// A line carries its template (<c>@mt</c>) and its properties separately, because that is what makes it searchable by
/// what it says rather than by what it happened to say this time. A person reading wants the sentence. A hole is
/// <c>{Name}</c>, optionally with the operator <c>@</c> or <c>$</c> in front (<c>{@Name}</c>, <c>{$Name}</c>), an
/// alignment (<c>{Name,12}</c>, <c>{Name,-12}</c>) and a format (<c>{Name:000}</c>); <c>{{</c> and <c>}}</c> are literal
/// braces.
/// </para>
/// <para>
/// The value goes in plainly. The operator, the alignment and the format are recognised so that the hole is found, and
/// are not applied: a string is written without quotes, a number or a boolean as the JSON it is, <c>null</c> as
/// <c>null</c>, a structure as compact JSON. Padding a value into a column or formatting a number is what a person
/// would want from a renderer of their own program's output, and this is not that: it is a reader of lines another
/// program wrote, where the value as written is the evidence.
/// </para>
/// <para>
/// It forgives. A hole whose property the line does not carry is left exactly as written, a brace that does not open a
/// hole is text, and a value that itself contains braces is inserted as it is and never read for holes of its own. A
/// line that cannot be put back together is more use shown as it is than hidden.
/// </para>
/// </remarks>
public static partial class MessageTemplate
{
    /// <summary>
    /// One hole, matched where it starts. A name is letters, digits and underscore, which is why <c>@t</c> — the format's
    /// own key for the time — can never be named by a hole: <c>{@t}</c> is the operator and the name <c>t</c>.
    /// </summary>
    [GeneratedRegex(@"\G\{[@$]?(?<name>[\p{L}\p{Nd}_]+)(?:,-?[0-9]+)?(?::[^}]*)?\}", RegexOptions.CultureInvariant)]
    private static partial Regex Hole();

    private static readonly JsonSerializerOptions Readable = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>The template with every hole the properties can fill replaced by its value.</summary>
    /// <param name="template">The template as the line carries it in <c>@mt</c>.</param>
    /// <param name="properties">The line's properties by name: the event itself will do, since a hole cannot name a key that begins with <c>@</c>.</param>
    public static string Render(string template, JsonObject properties)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(properties);

        var text = new StringBuilder(template.Length);
        var at = 0;
        while (at < template.Length)
        {
            var next = template[at];
            var following = at + 1 < template.Length ? template[at + 1] : '\0';

            if (next == '{' && following == '{')
            {
                text.Append('{');
                at += 2;
            }
            else if (next == '}' && following == '}')
            {
                text.Append('}');
                at += 2;
            }
            else if (next == '{' && Hole().Match(template, at) is { Success: true } hole)
            {
                text.Append(properties.TryGetPropertyValue(hole.Groups["name"].Value, out var value) ? Plain(value) : hole.Value);
                at += hole.Length;
            }
            else
            {
                text.Append(next);
                at++;
            }
        }

        return text.ToString();
    }

    private static string Plain(JsonNode? value) => value switch
    {
        null => "null",
        JsonValue scalar when scalar.TryGetValue<string>(out var text) => text,
        _ => value.ToJsonString(Readable),
    };
}
