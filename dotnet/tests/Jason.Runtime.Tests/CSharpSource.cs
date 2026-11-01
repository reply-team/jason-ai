using System.Text;

namespace Jason.Runtime.Tests;

/// <summary>
/// The few things the source guards read in C# text: a string literal as the compiler reads it, where a literal or an
/// argument ends, and a call's arguments. One reading, shared by the guards that read templates and patterns, so the
/// two cannot come to read the same text differently.
/// </summary>
/// <remarks>
/// It reads only as far as those guards need: a literal's escapes are undone only for a backslash and a quote, which is
/// all a template's hole or a pattern's anchor is read through, and an interpolated literal is skipped and never read.
/// </remarks>
internal static class CSharpSource
{
    /// <summary>The line the index is on, counting from 1.</summary>
    public static int LineOf(string source, int index) => 1 + source.AsSpan(0, index).Count('\n');

    /// <summary>
    /// The text of the string literal that begins at the index — raw, verbatim or quoted — and the index just past it;
    /// null where none begins there, an interpolated one included. A quoted literal's escapes are undone only for a
    /// backslash and a quote; any other escape is kept as written.
    /// </summary>
    public static string? Literal(string source, int index, out int end)
    {
        end = index;
        var verbatim = source.AsSpan(index).StartsWith("@\"", StringComparison.Ordinal);
        var quote = verbatim ? index + 1 : index;
        if (quote >= source.Length || source[quote] is not '"')
        {
            return null;
        }

        var quotes = 0;
        while (quote + quotes < source.Length && source[quote + quotes] is '"')
        {
            quotes++;
        }

        if (quotes >= 3)
        {
            var close = source.IndexOf(new string('"', quotes), quote + quotes, StringComparison.Ordinal);
            if (close < 0)
            {
                return null;
            }

            end = close + quotes;
            return source[(quote + quotes)..close].Trim();
        }

        if (quotes == 2 && !verbatim)
        {
            end = quote + 2;
            return string.Empty;
        }

        var text = new StringBuilder();
        for (var at = quote + 1; at < source.Length; at++)
        {
            var character = source[at];
            if (verbatim && character is '"')
            {
                if (at + 1 < source.Length && source[at + 1] is '"')
                {
                    text.Append('"');
                    at++;
                    continue;
                }

                end = at + 1;
                return text.ToString();
            }

            if (!verbatim && character is '"')
            {
                end = at + 1;
                return text.ToString();
            }

            if (!verbatim && character is '\\' && at + 1 < source.Length)
            {
                text.Append(source[at + 1] is '\\' or '"' ? source.AsSpan(at + 1, 1) : source.AsSpan(at, 2));
                at++;
                continue;
            }

            if (!verbatim && character is '\n')
            {
                return null;
            }

            text.Append(character);
        }

        return null;
    }

    /// <summary>
    /// The index just past the string or character literal that begins at the index — an interpolated one included, raw
    /// or verbatim, which is never read but can stand among the arguments of a call that is — or null where none begins
    /// there.
    /// </summary>
    public static int? Skip(string source, int index)
    {
        if (source[index] is '\'')
        {
            var close = index + 1 < source.Length && source[index + 1] is '\\' ? source.IndexOf('\'', index + 3) : source.IndexOf('\'', index + 2);
            return close < 0 ? null : close + 1;
        }

        if (Literal(source, index, out var end) is not null)
        {
            return end;
        }

        var quote = index;
        while (quote < source.Length && source[quote] is '$' or '@')
        {
            quote++;
        }

        if (quote == index || quote >= source.Length || source[quote] is not '"')
        {
            return null;
        }

        var quotes = 0;
        while (quote + quotes < source.Length && source[quote + quotes] is '"')
        {
            quotes++;
        }

        if (quotes >= 3)
        {
            var close = source.IndexOf(new string('"', quotes), quote + quotes, StringComparison.Ordinal);
            return close < 0 ? null : close + quotes;
        }

        var verbatim = source.AsSpan(index, quote - index).Contains('@');
        for (var at = quote + 1; at < source.Length; at++)
        {
            if (!verbatim && source[at] is '\\')
            {
                at++;
            }
            else if (source[at] is '"')
            {
                if (verbatim && at + 1 < source.Length && source[at + 1] is '"')
                {
                    at++;
                    continue;
                }

                return at + 1;
            }
        }

        return null;
    }

    /// <summary>
    /// The index of the comma or closing bracket that ends the argument starting at the index, skipping nested brackets
    /// and string and character literals; the end of the text where none does.
    /// </summary>
    public static int ArgumentEnd(string source, int at)
    {
        var depth = 0;
        for (var index = at; index < source.Length; index++)
        {
            var character = source[index];
            if (character is '"' or '@' or '$' or '\'' && Skip(source, index) is { } after)
            {
                index = after - 1;
                continue;
            }

            switch (character)
            {
                case '(' or '[' or '{':
                    depth++;
                    break;
                case ')' or ']' or '}' when depth == 0:
                    return index;
                case ')' or ']' or '}':
                    depth--;
                    break;
                case ',' when depth == 0:
                    return index;
            }
        }

        return source.Length;
    }

    /// <summary>
    /// The arguments of the call whose opening parenthesis is at the index, split at the commas that separate them
    /// and nowhere inside a literal, a comment or a nested bracket, each without its comments; null when the call
    /// never closes.
    /// </summary>
    public static List<string>? Arguments(string source, int open)
    {
        var arguments = new List<string>();
        var current = new StringBuilder();
        var depth = 0;
        for (var at = open + 1; at < source.Length; at++)
        {
            var character = source[at];
            if (character is '"' or '@' or '$' or '\'' && Skip(source, at) is { } after)
            {
                current.Append(source, at, after - at);
                at = after - 1;
                continue;
            }

            if (character is '/' && at + 1 < source.Length && source[at + 1] is '/' or '*')
            {
                var close = source[at + 1] is '/' ? source.IndexOf('\n', at) : source.IndexOf("*/", at + 2, StringComparison.Ordinal) + 1;
                if (close <= 0)
                {
                    return null;
                }

                current.Append(' ');
                at = close;
                continue;
            }

            switch (character)
            {
                case '(' or '[' or '{':
                    depth++;
                    break;
                case ')' or ']' or '}' when depth == 0:
                    arguments.Add(current.ToString());
                    return arguments;
                case ')' or ']' or '}':
                    depth--;
                    break;
                case ',' when depth == 0:
                    arguments.Add(current.ToString());
                    current.Clear();
                    continue;
            }

            current.Append(character);
        }

        return null;
    }
}
