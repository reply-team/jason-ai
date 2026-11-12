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
/// Under <c>JASON_CONFINED</c> (<see cref="Confinement"/>), set to any value but empty, such an option takes only
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

        if (env.Confined())
        {
            throw new ConfinedRefusal(
                $"{option} '{path}' was not read: with {Confinement.Variable} set, this CLI reads a request's content from "
                + $"standard input only. Give it as {option} - < <path>, or in a here-document.");
        }

        return (await ReadFileAsync(option, path, namesOption, cancellationToken).ConfigureAwait(false), $"'{path}'");
    }

    private static async Task<string> ReadFileAsync(string option, string path, bool namesOption, CancellationToken cancellationToken)
    {
        try
        {
            return await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
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
