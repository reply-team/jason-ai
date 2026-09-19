using System.Globalization;
using System.Text.Json;

namespace Jason.Contracts.Update;

/// <summary>One platform's download: the file to fetch, what it must hash to, and how large it should be.</summary>
public sealed record UpdateArtifact(string Asset, string Sha256, long Size);

/// <summary>
/// What a release says about itself, as the updater reads it: the version, when it was published, one artifact
/// per platform with its digest, where the notes are, and — where a release needs one — the oldest version that
/// may upgrade to it directly.
/// </summary>
/// <remarks>
/// Read by hand rather than deserialized. Every field has a rule (a version that parses, a digest that is a
/// digest, an asset name that is a file name), and a document that fails any of them must not become a manifest
/// at all — a half-valid one would put the checking somewhere later, in each caller, differently. So the whole
/// document is validated here and a caller who holds one of these can use every field without looking at it
/// twice.
/// </remarks>
public sealed record UpdateManifest(
    int Schema,
    SemanticVersion Version,
    DateTimeOffset PublishedAt,
    IReadOnlyDictionary<string, UpdateArtifact> Artifacts,
    string? ReleaseNotesUrl,
    SemanticVersion? MinUpgradeFrom)
{
    /// <summary>The shape this build knows. A document that says more than this is one it must not guess at.</summary>
    public const int CurrentSchema = 1;

    /// <summary>
    /// As much of a feed as is ever read. A manifest for three platforms is a few hundred bytes; the bound is
    /// what stops a page that answers with a gigabyte from being a way to exhaust a runtime that asked a
    /// question once a day.
    /// </summary>
    public const int MaxBytes = 64 * 1024;

    private const int DigestLength = 64;

    /// <summary>Whether this release is worth telling somebody about.</summary>
    public bool IsNewerThan(SemanticVersion running) => Version > running;

    /// <exception cref="UpdateFeedException">The document is not a manifest this build can read.</exception>
    public static UpdateManifest Read(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        // Characters here, bytes where the bytes are: the reader stops at MaxBytes of them and this is the
        // backstop for a caller that arrived with a string already. A character is at most four bytes, so this
        // is the looser of the two bounds and says so rather than calling a character count a byte count.
        if (json.Length > MaxBytes)
        {
            throw Invalid($"the feed is longer than the {MaxBytes} characters a manifest is read to.");
        }

        using var document = Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw Invalid("the feed is not a JSON object.");
        }

        var schema = Integer(root, "schema");
        if (schema != CurrentSchema)
        {
            throw Invalid($"the feed says schema {schema}, and this build reads {CurrentSchema}.");
        }

        var version = ReadVersion(root, "version", required: true)!.Value;
        var publishedAt = Moment(root, "published_at");
        var artifacts = ReadArtifacts(root);
        var notes = Url(root, "release_notes_url");
        var minimum = ReadVersion(root, "min_upgrade_from", required: false);

        return new UpdateManifest(schema, version, publishedAt, artifacts, notes, minimum);
    }

    private static JsonDocument Parse(string json)
    {
        try
        {
            return JsonDocument.Parse(json);
        }
        catch (JsonException error)
        {
            throw Invalid("the feed is not JSON.", error);
        }
    }

    private static IReadOnlyDictionary<string, UpdateArtifact> ReadArtifacts(JsonElement root)
    {
        if (!root.TryGetProperty("artifacts", out var element) || element.ValueKind != JsonValueKind.Object)
        {
            throw Invalid("the feed names no artifacts.");
        }

        var artifacts = new Dictionary<string, UpdateArtifact>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            artifacts[property.Name] = ReadArtifact(property.Name, property.Value);
        }

        if (artifacts.Count == 0)
        {
            throw Invalid("the feed names no artifacts.");
        }

        return artifacts;
    }

    private static UpdateArtifact ReadArtifact(string rid, JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw Invalid($"the artifact for '{rid}' is not an object.");
        }

        var asset = Text(element, "asset");
        if (!ReleaseAssets.IsWellFormedAssetName(asset))
        {
            throw Invalid($"the artifact for '{rid}' names '{asset}', which is not a file name.");
        }

        var digest = Text(element, "sha256");
        if (digest.Length != DigestLength || !digest.All(char.IsAsciiHexDigitLower))
        {
            throw Invalid($"the artifact for '{rid}' carries a digest that is not {DigestLength} lowercase hexadecimal characters.");
        }

        if (!element.TryGetProperty("size", out var size)
            || size.ValueKind != JsonValueKind.Number
            || !size.TryGetInt64(out var bytes)
            || bytes <= 0)
        {
            throw Invalid($"the artifact for '{rid}' carries no positive size.");
        }

        return new UpdateArtifact(asset, digest, bytes);
    }

    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()!
            : throw Invalid($"the feed carries no '{name}'.");

    private static int Integer(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number
            : throw Invalid($"the feed carries no '{name}'.");

    private static SemanticVersion? ReadVersion(JsonElement element, string name, bool required)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return required ? throw Invalid($"the feed carries no '{name}'.") : null;
        }

        return SemanticVersion.TryParse(value.ValueKind == JsonValueKind.String ? value.GetString() : null, out var version)
            ? version
            : throw Invalid($"the feed's '{name}' is not a version.");
    }

    private static DateTimeOffset Moment(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
        && DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var moment)
            ? moment.ToUniversalTime()
            : throw Invalid($"the feed's '{name}' is not a moment.");

    /// <summary>
    /// A link a person may be shown, so it is held to the two schemes a link can safely be. Absent is fine; a
    /// release with no notes is a release.
    /// </summary>
    private static string? Url(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        var text = value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        return Uri.TryCreate(text, UriKind.Absolute, out var url) && url.Scheme is "https" or "http"
            ? text
            : throw Invalid($"the feed's '{name}' is not an http address.");
    }

    private static UpdateFeedException Invalid(string because, Exception? inner = null) =>
        new(UpdateFeedException.Invalid, $"The update feed could not be read: {because}", inner);
}
