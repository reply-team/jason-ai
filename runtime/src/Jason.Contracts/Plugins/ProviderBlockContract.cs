using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Operations;

namespace Jason.Contracts.Plugins;

/// <summary>
/// What a provider plugin says beside a failure of class <see cref="FailureClass.Blocked"/>: what is blocked, why, where a
/// person goes to clear it and, where the provider says so, when it clears by itself. One document, written once under
/// <c>docs/contracts/block/</c> and embedded into this assembly, so the file a plugin author reads is the schema the runtime
/// checks a block against. Every operation declares the one code of the class, <c>provider_blocked</c>, as an ordinary
/// failure code; the block is the same for all of them.
/// </summary>
/// <remarks>
/// Parsed as strictly as the other families: every field is required and a field this build does not know is refused. The
/// schema is held to the dialect by the tests rather than as the document loads, as the other families' are: checking the
/// dialect compiles every pattern a schema writes, and the command-line client loads this assembly too.
/// </remarks>
public sealed record ProviderBlockContract(string Id, int Version, string Summary, JsonObject Schema)
{
    /// <summary>
    /// What one block may carry, measured as an error's details are: the UTF-8 bytes of its JSON text. The schema bounds
    /// each field; this bounds the whole, and an outcome whose block is larger is not an outcome.
    /// </summary>
    public const int MaxBytes = 8 * 1024;

    /// <summary>
    /// Where an operation's input names the sending account it goes from, as a pointer from the input's root: the one
    /// place a block of scope <c>sender</c> is matched against an item. Every operation that takes a sender takes it
    /// through the same definition, at this pointer, and the tests hold that over the operation documents.
    /// </summary>
    public const string SenderPointer = "/args/sender/external_id";

    private const string ResourceName = "Jason.Contracts.Blocks.provider_block.json";
    private const string Contract = "the provider block";

    private static readonly string[] Fields = ["id", "version", "summary", "schema"];

    /// <summary>The block this build publishes.</summary>
    public static ProviderBlockContract Published { get; } = Read();

    /// <summary>The document, as published under <c>docs/contracts/block/provider_block.json</c>.</summary>
    public static ProviderBlockContract Parse(string source, JsonNode? document)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        if (document is not JsonObject fields)
        {
            throw new InvalidOperationException($"{source}: {Contract} is a JSON object.");
        }

        foreach (var (name, _) in fields)
        {
            if (!Fields.Contains(name, StringComparer.Ordinal))
            {
                throw new InvalidOperationException($"{source}: `{name}` is not part of {Contract}.");
            }
        }

        JsonNode Required(string name) =>
            fields.TryGetPropertyValue(name, out var node) && node is not null
                ? node
                : throw new InvalidOperationException($"{source}: `{name}` is missing, and absence is not a default.");

        string Text(string name) =>
            Required(name) is JsonValue value && value.GetValueKind() == JsonValueKind.String && value.TryGetValue(out string? text) && text.Trim().Length > 0
                ? text
                : throw new InvalidOperationException($"{source}: `{name}` is a string with something in it.");

        var id = Text("id");
        var version = Required("version") is JsonValue number && number.GetValueKind() == JsonValueKind.Number && number.TryGetValue(out int whole) && whole >= 1
            ? whole
            : throw new InvalidOperationException($"{source}: `version` is a whole number from 1.");

        var summary = Text("summary");

        var schema = Required("schema") is JsonObject described
            && described["type"] is JsonValue type && type.TryGetValue(out string? word) && word == "object"
            ? described
            : throw new InvalidOperationException($"{source}: `schema` describes a JSON object, which every block is.");

        return new ProviderBlockContract(id, version, summary, schema);
    }

    /// <summary>What is wrong with a block a plugin reported, against the published schema.</summary>
    public IReadOnlyList<SchemaProblem> Check(JsonNode? block) => Check(block, PatternTiming.Default);

    /// <summary>The same check, with the schema's patterns matched under <paramref name="timing"/>.</summary>
    public IReadOnlyList<SchemaProblem> Check(JsonNode? block, PatternTiming timing) => SchemaValidator.Validate(block, Schema, timing);

    private static ProviderBlockContract Read()
    {
        var assembly = typeof(ProviderBlockContract).GetTypeInfo().Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"{ResourceName}: the embedded document is missing.");

        JsonNode? document;
        try
        {
            document = JsonNode.Parse(stream);
        }
        catch (JsonException error)
        {
            throw new InvalidOperationException("provider_block.json: the document is not valid JSON.", error);
        }

        return Parse("provider_block.json", document);
    }
}
