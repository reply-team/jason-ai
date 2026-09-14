using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Jason.Cli.Commands;

/// <summary>
/// The options every business verb shares and the parsing they all repeat. The CLI composes a JSON body and
/// nothing more: what a field means is the API's business, so nothing here inspects a value beyond the shape
/// the body itself needs.
/// </summary>
internal static class VerbOptions
{
    public static Option<bool> Human() => new("--human")
    {
        Description = "Render for people instead of printing the JSON response.",
    };

    /// <summary><paramref name="shape"/> names the fields the object may hold, so the help page is the reference for the body.</summary>
    public static Option<string?> File(string shape) => new("--file")
    {
        Description = $"Request body as JSON — a path, or - for standard input. {shape} Options given on the command line win.",
    };

    public static Option<string?> Reason() => new("--reason")
    {
        Description = "Why this is done; recorded in the journal.",
    };

    public static Option<int?> Limit() => new("--limit")
    {
        Description = "How many items to return (the runtime defaults to 100, at most 1000).",
    };

    public static Option<string?> Cursor() => new("--cursor")
    {
        Description = "Continue a listing: the next_cursor of the previous page, verbatim.",
    };

    /// <summary>A repeatable option: users write it once per value.</summary>
    public static Option<string[]> Repeatable(string name, string description) => new(name)
    {
        Description = description,
        AllowMultipleArgumentsPerToken = false,
    };

    /// <summary>The body a verb starts from: the file or standard input when <c>--file</c> was given, an empty object otherwise.</summary>
    public static Task<JsonObject> BodyAsync(CliEnvironment env, string? file, string? arrayProperty, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(env);

        return file is null
            ? Task.FromResult(RequestBody.Empty())
            : RequestBody.FromFileAsync(file, env.In ?? TextReader.Null, arrayProperty, cancellationToken);
    }

    /// <summary>Any JSON value given on the command line; an absent option stays absent from the body.</summary>
    public static JsonNode? Json(string? text, string option)
    {
        if (text is null)
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(text);
        }
        catch (JsonException)
        {
            throw new UsageException($"{option} must be a JSON value.");
        }
    }

    /// <summary>A JSON object given on the command line, for the options that carry structured values.</summary>
    public static JsonObject? Object(string? text, string option)
    {
        if (text is null)
        {
            return null;
        }

        return Json(text, option) as JsonObject ?? throw new UsageException($"{option} must be a JSON object.");
    }

    /// <summary>The values of a repeatable option as a JSON array, or nothing when it was never given.</summary>
    public static JsonArray? Strings(string[]? values) =>
        values is { Length: > 0 } ? new JsonArray([.. values.Select(value => (JsonNode?)JsonValue.Create(value))]) : null;
}
