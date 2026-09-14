using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Json;

namespace Jason.Cli;

/// <summary>
/// Composes an operation's JSON body. The body starts either empty or as the JSON object read from
/// <c>--file &lt;path&gt;</c> (or <c>--file -</c> for standard input); scalar options are then laid over it and win.
/// The CLI never inspects the fields — shape and meaning are the API's business.
/// </summary>
public static class RequestBody
{
    public static JsonObject Empty() => new();

    /// <summary>
    /// Reads the file, or standard input when the path is <c>-</c>. A JSON object is the body itself; a JSON
    /// array is wrapped under <paramref name="arrayProperty"/> when the verb accepts one (add-contacts holds a
    /// list of items); anything else, and anything unreadable, is a usage error.
    /// </summary>
    public static async Task<JsonObject> FromFileAsync(string path, TextReader stdin, string? arrayProperty, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(stdin);

        var isStdin = string.Equals(path, "-", StringComparison.Ordinal);
        var text = isStdin
            ? await stdin.ReadToEndAsync(cancellationToken).ConfigureAwait(false)
            : await ReadFileAsync(path, cancellationToken).ConfigureAwait(false);
        var source = isStdin ? "Standard input" : $"'{path}'";

        JsonNode? node;
        try
        {
            node = JsonNode.Parse(text);
        }
        catch (JsonException ex)
        {
            throw new UsageException($"{source} does not contain valid JSON (line {ex.LineNumber ?? 0}, position {ex.BytePositionInLine ?? 0}).");
        }

        return node switch
        {
            JsonObject body => body,
            JsonArray items when arrayProperty is not null => new JsonObject { [arrayProperty] = items },
            _ when arrayProperty is not null => throw new UsageException($"{source} must hold a JSON object or a JSON array of {arrayProperty}."),
            _ => throw new UsageException($"{source} must hold a JSON object."),
        };
    }

    /// <summary>Lays one option over the body. An option that was not given is null and leaves the body alone.</summary>
    public static JsonObject Set(this JsonObject body, string property, JsonNode? value, bool onlyIfNotNull = true)
    {
        ArgumentNullException.ThrowIfNull(body);

        if (value is null && onlyIfNotNull)
        {
            return body;
        }

        body[property] = value;
        return body;
    }

    /// <summary>Writes the claimed actor, or leaves the body alone when no <c>--actor</c> was given.</summary>
    public static JsonObject SetActor(this JsonObject body, JsonObject? actor) => body.Set("actor", actor);

    public static string Serialize(JsonObject body)
    {
        ArgumentNullException.ThrowIfNull(body);
        return body.ToJsonString(JasonJson.Options);
    }

    private static async Task<string> ReadFileAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            return await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (FileNotFoundException)
        {
            throw new UsageException($"No such file: '{path}'.");
        }
        catch (DirectoryNotFoundException)
        {
            throw new UsageException($"No such file: '{path}'.");
        }
        catch (IOException ex)
        {
            throw new UsageException($"'{path}' could not be read: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new UsageException($"'{path}' could not be read: {ex.Message}");
        }
    }
}

/// <summary>
/// An argument the CLI itself can tell is wrong, thrown from inside a command action. The root command turns
/// it into the usage exit code with the message on stderr.
/// </summary>
public sealed class UsageException : Exception
{
    public UsageException()
    {
    }

    public UsageException(string message)
        : base(message)
    {
    }

    public UsageException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
