using System.Text;
using System.CommandLine;
using Jason.Contracts.Execution;

namespace Jason.Cli;

/// <summary>
/// An option of an operation verb that reads a file's content into the request — a path, or <c>-</c> for standard input.
/// What it names is read by <see cref="RequestFile"/> alone; a test reads the command tree and holds every option so
/// spelt to this type, so a file option cannot arrive outside the rule that reader keeps.
/// </summary>
public sealed class RequestFileOption(string name) : Option<string?>(name);

/// <summary>
/// The one reader of what a <see cref="RequestFileOption"/> names: the file, or standard input for <c>-</c>.
/// </summary>
/// <remarks>
/// <para>
/// Under <c>JASON_CONFINED</c> or <c>JASON_CHAT_TURN</c> (<see cref="Confinement"/>), set to any value but empty, such an option takes only
/// <c>-</c>. Anything else is refused before the file is opened and before any request, exit 1, naming the cure:
/// <c>--file - &lt; &lt;path&gt;</c>, or a here-document. With the content on standard input, what reads the file is
/// the caller's shell, which the caller's host already checks, and never this CLI. Unset or empty, nothing changes.
/// </para>
/// <para>
/// The rule covers every option the command tree gives an operation verb to read a file's content into a request
/// (<c>--file</c>, <c>--value-file</c>, <c>--note-file</c>, <c>--result-file</c>) and no other path: <c>project link
/// --path</c> names a folder the runtime adopts and sends no file's content; <c>doctor --output</c> writes its bundle
/// and lists what it writes before it writes; <c>export</c> and <c>import</c> are refused whole under the variable
/// (<see cref="ConfinedVerbs"/>), as is every verb that takes a file option and is a person's word or changes this
/// installation, before this rule is reached; and <c>logs</c> takes no path.
/// </para>
/// </remarks>
public static class RequestFile
{
    /// <summary>The value that names standard input.</summary>
    public const string StandardInput = "-";

    /// <summary>
    /// The text <paramref name="path"/> names for <paramref name="option"/>, and how a message names where it came from.
    /// <paramref name="namesOption"/> keeps the words each reader said before there was one: a request body's file is
    /// named by its path alone, a result's file by the option and its path.
    /// </summary>
    public static async Task<(string Text, string Source)> ReadAsync(
        CliEnvironment env, string option, string path, bool namesOption, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(option);
        ArgumentNullException.ThrowIfNull(path);

        if (string.Equals(path, StandardInput, StringComparison.Ordinal))
        {
            return (await (env.In ?? TextReader.Null).ReadToEndAsync(cancellationToken).ConfigureAwait(false), "Standard input");
        }

        if (env.FileHalfVariable() is { } variable)
        {
            throw new ConfinedRefusal(
                $"{option} '{path}' was not read: with {variable} set, this CLI reads a request's content from "
                + $"standard input only. Give it as {option} - < <path>, or in a here-document.");
        }

        return (await ReadFileAsync(option, path, namesOption, cancellationToken).ConfigureAwait(false), $"'{path}'");
    }

    /// <summary>What a refusal of a file says, as the usage error's message: the file named, the cure given.</summary>
    public static string NotUtf8(string path) =>
        $"The file {path} must be UTF-8, and it is not, so none of it was used. Save it as UTF-8.";

    /// <summary>
    /// The bytes as text: UTF-8, or the encoding a leading byte-order mark names (UTF-32 or UTF-16, either order), the
    /// mark dropped. A byte that does not fit the encoding is refused, never stored as U+FFFD in a person's words.
    /// </summary>
    private static string Decode(string path, byte[] bytes)
    {
        var (encoding, mark) = bytes switch
        {
            [0xFF, 0xFE, 0x00, 0x00, ..] => ((Encoding)new UTF32Encoding(false, true, true), 4),
            [0x00, 0x00, 0xFE, 0xFF, ..] => (new UTF32Encoding(true, true, true), 4),
            [0xEF, 0xBB, 0xBF, ..] => (new UTF8Encoding(false, true), 3),
            [0xFF, 0xFE, ..] => (new UnicodeEncoding(false, true, true), 2),
            [0xFE, 0xFF, ..] => (new UnicodeEncoding(true, true, true), 2),
            _ => (new UTF8Encoding(false, true), 0),
        };

        try
        {
            return encoding.GetString(bytes, mark, bytes.Length - mark);
        }
        catch (DecoderFallbackException cause)
        {
            throw new UsageException(NotUtf8(path), cause);
        }
    }

    private static async Task<string> ReadFileAsync(string option, string path, bool namesOption, CancellationToken cancellationToken)
    {
        try
        {
            return Decode(path, await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false));
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            throw new UsageException(namesOption ? $"{option}: no such file: '{path}'." : $"No such file: '{path}'.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new UsageException(namesOption ? $"{option} '{path}' could not be read: {ex.Message}" : $"'{path}' could not be read: {ex.Message}");
        }
    }
}

/// <summary>A local act the CLI refused under <c>JASON_CONFINED</c>: exit 1, its message on standard error.</summary>
public sealed class ConfinedRefusal(string message) : Exception(message);
