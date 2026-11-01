using System.Globalization;
using System.Text.Json;
using Jason.Contracts.Plugins;

namespace Jason.Contracts.Update;

/// <summary>One platform's download: the file to fetch, what it must hash to, and how large it should be.</summary>
public sealed record UpdateArtifact(string Asset, string Sha256, long Size);

/// <summary>
/// A plugin package a release publishes beside its archives: its version, the file it is published as, its content
/// digest as the runtime computes it (<c>sha256:</c> and 64 lowercase hexadecimal characters), and the digest of every
/// version of the package on record, its own among them.
/// </summary>
public sealed record UpdatePluginPackage(
    SemanticVersion Version,
    string Asset,
    string Digest,
    IReadOnlyDictionary<SemanticVersion, string> Recorded)
{
    /// <summary>
    /// The recorded version whose content has <paramref name="digest"/>, or null when no version on record has it — a
    /// package the list does not know: built or edited on that machine, or a version before the list's first line, 0.3.2.
    /// </summary>
    public SemanticVersion? VersionOf(string digest)
    {
        foreach (var (version, recorded) in Recorded)
        {
            if (string.Equals(recorded, digest, StringComparison.Ordinal))
            {
                return version;
            }
        }

        return null;
    }
}

/// <summary>
/// What a release says about itself, as the updater reads it: the version, when it was published, one artifact
/// per platform with its digest, where the notes are, where a release needs one the oldest version that may
/// upgrade to it directly, and the plugin packages it publishes with their content digests.
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
    SemanticVersion? MinUpgradeFrom,
    IReadOnlyDictionary<string, UpdatePluginPackage> Plugins)
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
        var plugins = ReadPlugins(root);

        return new UpdateManifest(schema, version, publishedAt, artifacts, notes, minimum, plugins);
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

        // Either case in, one case out. A digest is a number written in hexadecimal and the case of its letters
        // carries nothing, so a feed this project did not write is read rather than refused — and refusing one
        // would report a correct file as a corrupt one, which is the worst thing an updater can say. Everything
        // that compares a digest compares this lower-cased text, so no caller has to remember the rule.
        var digest = Text(element, "sha256");
        if (digest.Length != DigestLength || !digest.All(char.IsAsciiHexDigit))
        {
            throw Invalid($"the artifact for '{rid}' carries a digest that is not {DigestLength} hexadecimal characters.");
        }

        digest = digest.ToLowerInvariant();

        if (!element.TryGetProperty("size", out var size)
            || size.ValueKind != JsonValueKind.Number
            || !size.TryGetInt64(out var bytes)
            || bytes <= 0)
        {
            throw Invalid($"the artifact for '{rid}' carries no positive size.");
        }

        return new UpdateArtifact(asset, digest, bytes);
    }

    private static readonly IReadOnlyDictionary<string, UpdatePluginPackage> NoPlugins =
        new Dictionary<string, UpdatePluginPackage>(StringComparer.Ordinal);

    /// <summary>
    /// The packages, by plugin id. A release before 0.20.0 names none and a feed with no package names <c>null</c>; both
    /// are an empty set rather than a refusal.
    /// </summary>
    private static IReadOnlyDictionary<string, UpdatePluginPackage> ReadPlugins(JsonElement root)
    {
        if (!root.TryGetProperty("plugins", out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return NoPlugins;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            throw Invalid("the feed's 'plugins' is not an object.");
        }

        var plugins = new Dictionary<string, UpdatePluginPackage>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!PluginIds.IsWellFormed(property.Name))
            {
                throw Invalid($"the feed names a plugin package '{property.Name}', which is not a plugin's id.");
            }

            plugins[property.Name] = ReadPlugin(property.Name, property.Value);
        }

        return plugins;
    }

    /// <remarks>
    /// <c>recorded</c> is read both ways: from a version to its digest, and from an installed copy's digest back to its
    /// version (<see cref="UpdatePluginPackage.VersionOf"/>). The reverse is sound because the package's own version line
    /// is one of the files the digest covers, so two versions of one package cannot share a digest. A record that has
    /// them share one is not a record this project wrote, and it is refused with the rest.
    /// </remarks>
    private static UpdatePluginPackage ReadPlugin(string id, JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw Invalid($"the plugin package '{id}' is not an object.");
        }

        if (!element.TryGetProperty("version", out var named) || named.ValueKind == JsonValueKind.Null)
        {
            throw Invalid($"the plugin package '{id}' carries no version.");
        }

        if (named.ValueKind != JsonValueKind.String || !SemanticVersion.TryParse(named.GetString(), out var version))
        {
            throw Invalid($"the plugin package '{id}' carries a version that is not one.");
        }

        var asset = PackageText(id, element, "asset");
        if (!ReleaseAssets.IsWellFormedAssetName(asset))
        {
            throw Invalid($"the plugin package '{id}' names '{asset}', which is not a file name.");
        }

        var digest = PluginDigest(PackageText(id, element, "digest"), $"the plugin package '{id}' carries a digest");

        if (!element.TryGetProperty("recorded", out var lines) || lines.ValueKind != JsonValueKind.Object)
        {
            throw Invalid($"the plugin package '{id}' records no versions.");
        }

        // Build metadata takes no part in a version's identity, so 0.9.0 and 0.9.0+b are one key, and the second is a
        // version recorded twice rather than a line that quietly replaces the first.
        var recorded = new Dictionary<SemanticVersion, string>();
        foreach (var line in lines.EnumerateObject())
        {
            if (!SemanticVersion.TryParse(line.Name, out var recordedVersion))
            {
                throw Invalid($"the plugin package '{id}' records '{line.Name}', which is not a version.");
            }

            var lineDigest = PluginDigest(
                line.Value.ValueKind == JsonValueKind.String ? line.Value.GetString()! : string.Empty,
                $"the plugin package '{id}' records {recordedVersion} with a digest");

            if (!recorded.TryAdd(recordedVersion, lineDigest))
            {
                throw Invalid($"the plugin package '{id}' records {recordedVersion} twice.");
            }
        }

        if (!recorded.TryGetValue(version, out var own) || own != digest)
        {
            throw Invalid($"the plugin package '{id}' is {version}, and its record of {version} is not its digest.");
        }

        if (recorded.Values.Distinct(StringComparer.Ordinal).Count() != recorded.Count)
        {
            throw Invalid($"the plugin package '{id}' records one digest under two versions.");
        }

        return new UpdatePluginPackage(version, asset, digest, recorded);
    }

    /// <summary>
    /// A package's digest as the runtime writes one: <c>sha256:</c> and 64 hexadecimal characters, the hexadecimal read
    /// in either case and kept in lower case, as an artifact's is. <paramref name="holder"/> says whose digest it is, the
    /// package's own or a line of its record, and opens the refusal.
    /// </summary>
    private static string PluginDigest(string text, string holder)
    {
        if (!text.StartsWith(DigestPrefix, StringComparison.Ordinal)
            || text.Length != DigestPrefix.Length + DigestLength
            || !text[DigestPrefix.Length..].All(char.IsAsciiHexDigit))
        {
            throw Invalid($"{holder} that is not '{DigestPrefix}' and {DigestLength} hexadecimal characters.");
        }

        return DigestPrefix + text[DigestPrefix.Length..].ToLowerInvariant();
    }

    private const string DigestPrefix = "sha256:";

    /// <summary>Whether <paramref name="text"/> is a package's digest as this reader keeps one: <c>sha256:</c> and 64 lowercase hexadecimal characters.</summary>
    internal static bool IsPackageDigest(string text) =>
        text.StartsWith(DigestPrefix, StringComparison.Ordinal)
        && text.Length == DigestPrefix.Length + DigestLength
        && text[DigestPrefix.Length..].All(c => char.IsAsciiDigit(c) || c is >= 'a' and <= 'f');

    /// <summary>A text field of the plugin package <paramref name="id"/>, refused by the package's id when it is absent.</summary>
    private static string PackageText(string id, JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()!
            : throw Invalid($"the plugin package '{id}' carries no '{name}'.");

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

    /// <summary>
    /// The shapes a moment may arrive in: a date and a time to the second, optionally with a fraction, and
    /// <b>always</b> with a zone — <c>Z</c> or an offset.
    /// </summary>
    /// <remarks>
    /// An ordinary parse of <c>2026-09-19T08:00:00</c> takes the *reading* machine's offset, so one manifest
    /// would mean two instants three hours apart depending on who read it. A release is published at one moment;
    /// a document that cannot say which is not a manifest. The writer of this project's own manifests emits
    /// <c>Z</c>, so nothing this build publishes is refused by the rule.
    /// </remarks>
    private static readonly string[] MomentFormats =
    [
        "yyyy-MM-dd'T'HH:mm:ss'Z'",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'",
        "yyyy-MM-dd'T'HH:mm:sszzz",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz",
    ];

    private static DateTimeOffset Moment(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
        && DateTimeOffset.TryParseExact(
            value.GetString(),
            MomentFormats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var moment)
            ? moment
            : throw Invalid($"the feed's '{name}' is not a moment with a time zone.");

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
