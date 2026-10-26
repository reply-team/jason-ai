using System.Text;
using System.Text.Json.Nodes;

namespace Jason.Contracts.Operations;

/// <summary>
/// The one example a person approving a template reads: the template rendered by the standard over the composed input.
/// A name is read where the table of names says the input holds it; one it does not hold — a custom field, the sending
/// account's names — is the provider's alone to fill, so it renders its fallback, or itself in brackets, and is listed.
/// A name the input holds with no value and no fallback renders the same way and is listed, since the provider will
/// not send the message. The rendering writes text and escapes nothing; a provider's own may differ.
/// </summary>
internal static class TemplateExample
{
    private const string Compose = "/args/message/compose";
    private const string Subject = "/args/message/subject";
    private const string Body = "/args/message/body";

    private static readonly Dictionary<string, PreviewPointer> Pointers = TemplateGrammar.Names
        .Where(row => row.Pointer is not null)
        .ToDictionary(row => row.Name, row => PreviewPointer.Parse(row.Pointer!), StringComparer.Ordinal);

    /// <summary>The example of the message <paramref name="input"/> writes as a template, or null when it writes none.</summary>
    internal static JsonObject? Of(JsonNode? input)
    {
        if (Text(input, Compose) != "template" || Text(input, Body) is not { } body)
        {
            return null;
        }

        // The schema refused a template the standard refuses when the item was written, so a refusal here is an
        // input that never passed it, and it is shown no example rather than half of one.
        var unfilled = new List<string>();
        var written = Text(input, Subject);
        var subject = written is null ? null : Render(written, input, unfilled);
        var text = Render(body, input, unfilled);
        if ((written is not null && subject is null) || text is null)
        {
            return null;
        }

        return new JsonObject
        {
            ["subject"] = subject,
            ["body"] = text,
            ["unfilled"] = new JsonArray([.. unfilled.Select(name => (JsonNode?)name)]),
        };
    }

    /// <summary>The template rendered, or null when the standard refuses it.</summary>
    private static string? Render(string template, JsonNode? input, List<string> unfilled)
    {
        var nodes = TemplateGrammar.Parse(template, out var refusal);
        if (refusal is not null)
        {
            return null;
        }

        var text = new StringBuilder();
        Write(nodes, input, unfilled, text);
        return text.ToString();
    }

    private static void Write(IReadOnlyList<TemplateGrammar.Node> nodes, JsonNode? input, List<string> unfilled, StringBuilder text)
    {
        foreach (var node in nodes)
        {
            switch (node)
            {
                case TemplateGrammar.Literal literal:
                    text.Append(literal.Text);
                    break;
                case TemplateGrammar.Value value:
                    var (held, read) = Read(value.Name, input);
                    if (HasValue(read))
                    {
                        text.Append(read);
                    }
                    else if (value.Fallback is not null)
                    {
                        text.Append(value.Fallback);
                        Note(held ? null : value.Name, unfilled);
                    }
                    else
                    {
                        text.Append('[').Append(value.Name).Append(']');
                        Note(value.Name, unfilled);
                    }

                    break;
                case TemplateGrammar.Condition condition:
                    var (known, tested) = Read(condition.Name, input);
                    Note(known ? null : condition.Name, unfilled);
                    var compared = HasValue(tested) ? tested! : string.Empty;
                    var holds = condition.Operator switch
                    {
                        null => HasValue(tested),
                        "==" => string.Equals(compared, condition.Text, StringComparison.Ordinal),
                        _ => !string.Equals(compared, condition.Text, StringComparison.Ordinal),
                    };
                    Write(holds ? condition.Then : condition.Otherwise ?? [], input, unfilled, text);
                    break;
            }
        }
    }

    /// <summary>Whether the input holds <paramref name="name"/>, and its value there as text when it is text.</summary>
    private static (bool Held, string? Value) Read(string name, JsonNode? input) =>
        Pointers.TryGetValue(name, out var pointer)
            ? (true, pointer.Resolve(input) is JsonValue value && value.TryGetValue<string>(out var text) ? text : null)
            : (false, null);

    private static bool HasValue(string? value) => !string.IsNullOrWhiteSpace(value);

    private static void Note(string? name, List<string> unfilled)
    {
        if (name is not null && !unfilled.Contains(name, StringComparer.Ordinal))
        {
            unfilled.Add(name);
        }
    }

    private static string? Text(JsonNode? input, string pointer) =>
        PreviewPointer.Parse(pointer).Resolve(input) is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
}
