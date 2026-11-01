using System.Text;

namespace Jason.Cli.Tests.Documentation;

/// <summary>
/// A printed command line, split the way a shell would hand it to the program: quoted runs stay together
/// whichever quote was used, an escape inside double quotes is understood, and the program's own name is
/// dropped. A JSON argument is the reason this exists — <c>--note '{"a":"two words"}'</c> is one argument
/// however many spaces it contains.
/// </summary>
/// <remarks>
/// One splitter for every guard that types what a page prints. There were two, and they disagreed: a regex
/// that knew only double quotes shattered a single-quoted JSON note into six arguments and reported the page
/// as documenting a command the CLI does not understand — a guard failing over its own reading rather than
/// over what it guards.
/// </remarks>
internal static class ShellWords
{
    public static IReadOnlyList<string> Split(string command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var arguments = new List<string>();
        var current = new StringBuilder();
        var quote = '\0';
        var started = false;
        var escaped = false;

        foreach (var character in command)
        {
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

        return [.. arguments.Skip(1)];
    }
}
