using System.Text;

namespace Jason.Cli.Tests.Documentation;

/// <summary>What a printed line hands the program on standard input: a here-document, or a file.</summary>
internal enum ShellRedirectionKind
{
    /// <summary><c>&lt;&lt;'DELIM'</c>, <c>&lt;&lt;"DELIM"</c> or <c>&lt;&lt;DELIM</c>: the lines after it, up to the delimiter.</summary>
    HereDocument,

    /// <summary><c>&lt; file</c>: the file's content.</summary>
    File,
}

/// <summary>
/// The redirection a printed line ends with. <paramref name="Target"/> is the here-document's delimiter, unquoted, or the
/// file's name.
/// </summary>
internal sealed record ShellRedirection(ShellRedirectionKind Kind, string Target);

/// <summary>A printed line, split: the words the program is handed, and what it reads on standard input, if anything.</summary>
internal sealed record ShellCommand(IReadOnlyList<string> Words, ShellRedirection? Input);

/// <summary>
/// A printed command line, split the way a shell would hand it to the program: quoted runs stay together
/// whichever quote was used, an escape inside double quotes is understood, and the program's own name is
/// dropped. A JSON argument is the reason this exists — <c>--note '{"a":"two words"}'</c> is one argument
/// however many spaces it contains.
/// </summary>
/// <remarks>
/// <para>
/// One splitter for every guard that types what a page prints. There were two, and they disagreed: a regex
/// that knew only double quotes shattered a single-quoted JSON note into six arguments and reported the page
/// as documenting a command the CLI does not understand — a guard failing over its own reading rather than
/// over what it guards.
/// </para>
/// <para>
/// A line may end by handing the program its standard input: a here-document (<c>&lt;&lt;'EOF'</c>, with either quote
/// or none) or a file (<c>&lt; brief.json</c>). A shell never passes that to the program, so the words stop there and the
/// redirection is returned apart. Only a <c>&lt;&lt;</c> that opens a delimiter and a <c>&lt;</c> that stands alone
/// redirect: a placeholder a page prints as <c>&lt;campaign-id&gt;</c> is a word, as is anything quoted. A word printed
/// after the redirection would reach the program in a shell and never in this reading, so such a line is refused.
/// </para>
/// </remarks>
internal static class ShellWords
{
    public static IReadOnlyList<string> Split(string command) => Parse(command).Words;

    public static ShellCommand Parse(string command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var arguments = new List<string>();
        var current = new StringBuilder();
        var quote = '\0';
        var started = false;
        var escaped = false;

        for (var index = 0; index < command.Length; index++)
        {
            var character = command[index];
            if (quote != '\0')
            {
                // A quoted JSON argument carries quotes of its own, escaped the way a shell requires — so the
                // escape has to be understood here too, or every such example would look like a broken command.
                if (escaped)
                {
                    current.Append(character);
                    escaped = false;
                }
                else if (character == '\\' && quote == '"')
                {
                    escaped = true;
                }
                else if (character == quote)
                {
                    quote = '\0';
                }
                else
                {
                    current.Append(character);
                }

                continue;
            }

            if (!started && character == '<' && Redirection(command, index) is { } redirection)
            {
                return new ShellCommand([.. arguments.Skip(1)], redirection);
            }

            switch (character)
            {
                case '\'' or '"':
                    quote = character;
                    started = true;
                    break;

                case ' ':
                    if (started)
                    {
                        arguments.Add(current.ToString());
                        current.Clear();
                        started = false;
                    }

                    break;

                default:
                    current.Append(character);
                    started = true;
                    break;
            }
        }

        if (started)
        {
            arguments.Add(current.ToString());
        }

        return new ShellCommand([.. arguments.Skip(1)], null);
    }

    /// <summary>
    /// The redirection that begins at <paramref name="at"/>, which opens a word, or null where the angle bracket only
    /// starts a word, as a placeholder's does. A redirection that is not the end of the line is refused.
    /// </summary>
    private static ShellRedirection? Redirection(string command, int at)
    {
        var rest = command[at..];
        if (rest.StartsWith("<<", StringComparison.Ordinal))
        {
            var delimiter = rest[2..];
            if (delimiter.Length == 0 || !(delimiter[0] is '\'' or '"' || IsDelimiterCharacter(delimiter[0])))
            {
                return null;
            }

            var (target, after) = delimiter[0] is '\'' or '"'
                ? Quoted(command, delimiter, delimiter[0])
                : Bare(delimiter, IsDelimiterCharacter);
            return End(command, new ShellRedirection(ShellRedirectionKind.HereDocument, target), after);
        }

        if (rest.Length > 1 && rest[1] != ' ')
        {
            return null;
        }

        var file = rest[1..].TrimStart(' ');
        if (file.Length == 0)
        {
            throw new FormatException($"'{command}' redirects standard input from no file.");
        }

        var (name, remainder) = file[0] is '\'' or '"'
            ? Quoted(command, file, file[0])
            : Bare(file, character => character != ' ');
        return End(command, new ShellRedirection(ShellRedirectionKind.File, name), remainder);
    }

    private static bool IsDelimiterCharacter(char character) => char.IsAsciiLetterOrDigit(character) || character == '_';

    private static (string Target, string After) Quoted(string command, string text, char quote)
    {
        var close = text.IndexOf(quote, 1);
        if (close < 0)
        {
            throw new FormatException($"'{command}' opens a quote in its redirection and never closes it.");
        }

        return (text[1..close], text[(close + 1)..]);
    }

    private static (string Target, string After) Bare(string text, Func<char, bool> belongs)
    {
        var length = 0;
        while (length < text.Length && belongs(text[length]))
        {
            length++;
        }

        return (text[..length], text[length..]);
    }

    private static ShellRedirection End(string command, ShellRedirection redirection, string after)
    {
        if (redirection.Target.Length == 0 || after.Trim().Length > 0)
        {
            throw new FormatException(
                $"'{command}' prints something after its redirection of standard input, which a shell would hand the "
                + "program and this reading never would: the redirection ends the line.");
        }

        return redirection;
    }
}
