using System.Text.Json;
using System.Text.Json.Serialization;
using Jason.Contracts.Json;

namespace Jason.Contracts.Ui;

public sealed class UiManifestException(string message) : Exception(message);

/// <summary>
/// The UI build's counterpart of a plugin's manifest: which version it is, and which API version it was built
/// against. A directory without one is not a UI, whatever else it holds.
/// </summary>
public sealed record UiManifest(string Version, string ApiVersion)
{
    private static readonly JsonSerializerOptions Strict = Make();

    private static JsonSerializerOptions Make()
    {
        var options = JasonJson.Apply(new JsonSerializerOptions());
        options.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
        return options;
    }

    /// <summary>The manifest in <paramref name="directory"/>, null when there is none.</summary>
    /// <exception cref="UiManifestException">The file is there and is not a manifest.</exception>
    public static UiManifest? Read(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        var file = Path.Combine(directory, UiAssetLayout.ManifestFile);
        if (!File.Exists(file))
        {
            return null;
        }

        UiManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<UiManifest>(File.ReadAllText(file), Strict);
        }
        catch (JsonException exception)
        {
            throw new UiManifestException($"'{file}' is not a UI manifest: {exception.Message}");
        }

        if (manifest is null || string.IsNullOrWhiteSpace(manifest.Version) || string.IsNullOrWhiteSpace(manifest.ApiVersion))
        {
            throw new UiManifestException($"'{file}' is not a UI manifest: it must carry a non-empty version and api_version.");
        }

        return manifest;
    }
}
