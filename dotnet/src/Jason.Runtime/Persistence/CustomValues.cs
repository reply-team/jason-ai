using System.Data.Common;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Jason.Runtime.Persistence;

/// <summary>
/// The values of a person's custom fields as one text a search can look into, as a function the database calls: every
/// text and every number at any depth, inside an object or a list, each text folded as <see cref="TextFold"/> folds and
/// each number as it is written, in the order they are written and with <see cref="Separator"/> between two of them.
/// Never a key, never the JSON's own punctuation, never a flag or a null: a pattern over the stored text would find a
/// field by its name, would find nobody by a letter the stored text escapes, and would find everybody by a quote or a
/// brace. The text is read by the JSON reader, so a letter is the same letter however the stored text spells it. Never
/// the provider's record an audience search keeps beside the fields (<see cref="Contact.ProviderRecordKey"/>) either:
/// it is the provider's, up to the whole of what a contact may keep, and nothing a person sees on the page.
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

    /// <summary>
    /// What a value's own <see cref="Separator"/> and NUL become: the one would join two values, and the other would
    /// end the text for the pattern, hiding every value after it.
    /// </summary>
    private const char Blank = ' ';

    /// <summary>
    /// The values, or null where the stored text gives no object to read them from: text the reader refuses, an object
    /// nested past its depth or a text that is an escaped half of a surrogate pair (both of which the column's check
    /// lets through), or JSON that is no object. Null matches no pattern, so such a person is still found by everything
    /// else, and one of them never stops a search of everybody: whatever this throws, the database's call turns into
    /// the failure of the whole query. The text is read forward once; the provider's record under
    /// <see cref="Contact.ProviderRecordKey"/> at the top of the object is passed over, never unescaped or folded.
    /// </summary>
    internal static string? Apply(string? json)
    {
        if (json is null)
        {
            return null;
        }

        try
        {
            var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                return null;
            }

            var values = new StringBuilder();
            while (reader.Read())
            {
                switch (reader.TokenType)
                {
                    case JsonTokenType.PropertyName when reader.CurrentDepth == 1 && reader.ValueTextEquals(Contact.ProviderRecordKey):
                        reader.Skip();
                        break;
                    case JsonTokenType.String:
                        Add(values, TextFold.Text(reader.GetString()!));
                        break;
                    case JsonTokenType.Number:
                        Add(values, Encoding.UTF8.GetString(reader.ValueSpan));
                        break;
                    default:
                        // A key, the JSON's punctuation, a flag or a null says nothing a person would type to find someone.
                        break;
                }
            }

            return values.ToString();
        }
        catch (Exception unreadable) when (unreadable is JsonException or InvalidOperationException)
        {
            // Found by its other fields only; the row's own read names the column. Nothing is logged here.
            return null;
        }
    }

    private static void Add(StringBuilder values, string value)
    {
        if (values.Length > 0)
        {
            values.Append(Separator);
        }

        values.Append(value.Replace(Separator, Blank).Replace((char)0, Blank));
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
