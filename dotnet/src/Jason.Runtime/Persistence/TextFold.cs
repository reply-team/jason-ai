using System.Data.Common;
using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Jason.Runtime.Persistence;

/// <summary>
/// Text as a person types it on any keyboard, as a function the database calls: lower case in every script, accents
/// gone, and the few letters that carry no accent to strip spelled the way an English keyboard spells them (ł as l,
/// ø as o, ß as ss). SQLite's own <c>lower()</c> and <c>LIKE</c> fold only ASCII letters, so neither "álv" nor "alvaro"
/// would find "Álvaro" through them; through this, both sides compare like for like.
/// </summary>
public static class TextFold
{
    public const string FunctionName = "jason_fold";

    /// <summary>Translated to <see cref="FunctionName"/> in a query; there is nothing to run on this side.</summary>
    public static string? Fold(string? value) => throw new NotSupportedException($"{FunctionName} runs only inside a query.");

    internal static string? Apply(string? value) => value is null ? null : Text(value);

    /// <summary>
    /// A <c>LIKE</c> pattern, escaped with a backslash, that finds the folded text anywhere in a folded value: the
    /// characters a pattern would read as wildcards match as written.
    /// </summary>
    public static string Containing(string text) => "%" + Escaped(Text(text.Trim())) + "%";

    /// <summary>Text taken as written inside a <c>LIKE</c> pattern whose escape character is a backslash.</summary>
    public static string Escaped(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
    }

    /// <summary>The escape character <see cref="Containing"/> and <see cref="Escaped"/> use.</summary>
    public const string Escape = "\\";

    /// <summary>The fold itself, for the side of a comparison that never reaches the database.</summary>
    public static string Text(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var folded = new StringBuilder(decomposed.Length);
        foreach (var letter in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(letter) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            folded.Append(char.ToLowerInvariant(letter) switch
            {
                'ł' => "l",
                'ø' => "o",
                'đ' => "d",
                'ð' => "d",
                'þ' => "th",
                'ß' => "ss",
                'æ' => "ae",
                'œ' => "oe",
                'ı' => "i",
                var other => other.ToString(),
            });
        }

        return folded.ToString().Normalize(NormalizationForm.FormC);
    }
}

/// <summary>Registers <see cref="TextFold"/> on every connection as it opens, so every query of every context can call it.</summary>
internal sealed class TextFoldInterceptor : DbConnectionInterceptor
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
            sqlite.CreateFunction<string?, string?>(TextFold.FunctionName, TextFold.Apply, isDeterministic: true);
        }
    }
}
