using System.Data.Common;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Jason.Runtime.Persistence;

/// <summary>
/// The values of a person's custom fields as one text a search can look into, as a function the database calls: every
/// text and every number at any depth, inside an object or a list, each folded as <see cref="TextFold"/> folds, in the
/// order they are written and with <see cref="Separator"/> between two of them. Never a key, never the JSON's own
/// punctuation, never a flag or a null: a pattern over the stored text would find a field by its name, would find
/// nobody by a letter the stored text escapes, and would find everybody by a quote or a brace. The text is read by the
/// JSON reader, so a letter is the same letter however the stored text spells it.
/// </summary>
public static class CustomValues
{
    public const string FunctionName = "jason_custom_values";

    /// <summary>
    /// What stands between two values, and never inside one: a value's own is read as a space. A text holding it could
    /// only find a person by running from one value into the next, so a search never looks for one in the values.
    /// </summary>
    public const char Separator = '\u001f';

    /// <summary>Translated to <see cref="FunctionName"/> in a query; there is nothing to run on this side.</summary>
    public static string? Of(JsonObject custom) => throw new NotSupportedException($"{FunctionName} runs only inside a query.");

    internal static string? Apply(string? json)
    {
        if (json is null)
        {
            return null;
        }

        using var document = JsonDocument.Parse(json);
        var values = new StringBuilder();
        Collect(document.RootElement, values);
        return values.ToString();
    }

    private static void Collect(JsonElement element, StringBuilder values)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var field in element.EnumerateObject())
                {
                    Collect(field.Value, values);
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    Collect(item, values);
                }

                break;
            case JsonValueKind.String:
                Add(values, TextFold.Text(element.GetString()!));
                break;
            case JsonValueKind.Number:
                Add(values, element.GetRawText());
                break;
            default:
                // A flag or a null says nothing a person would type to find someone.
                break;
        }
    }

    private static void Add(StringBuilder values, string value)
    {
        if (values.Length > 0)
        {
            values.Append(Separator);
        }

        values.Append(value.Replace(Separator, ' '));
    }
}

/// <summary>Registers <see cref="CustomValues"/> on every connection as it opens, so every query of every context can call it.</summary>
internal sealed class CustomValuesInterceptor : DbConnectionInterceptor
{
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData) => Register(connection);

    public override Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        Register(connection);
        return Task.CompletedTask;
    }

    private static void Register(DbConnection connection)
    {
        if (connection is SqliteConnection sqlite)
        {
            sqlite.CreateFunction<string?, string?>(CustomValues.FunctionName, CustomValues.Apply, isDeterministic: true);
        }
    }
}
