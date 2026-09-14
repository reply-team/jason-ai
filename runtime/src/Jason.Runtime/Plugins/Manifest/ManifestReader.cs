using System.Globalization;
using System.Text.Json.Nodes;

namespace Jason.Runtime.Plugins.Manifest;

/// <summary>
/// Reads one <c>plugin.yaml</c>: YAML to JSON, then every rule of the manifest schema. Nothing here touches the
/// machine beyond looking for the entry module inside the package — what the environment answers for a declared
/// executable is a separate question, asked later, and answered without invalidating the package.
/// </summary>
public static class ManifestReader
{
    /// <param name="yamlText">The manifest as it is written on disk.</param>
    /// <param name="directoryName">The package directory's name, which the manifest's id must equal.</param>
    /// <param name="packageRoot">The package directory, for the entry module's existence.</param>
    /// <param name="bounds">The ceilings this installation allows a manifest to ask for.</param>
    public static ManifestReadResult Read(string yamlText, string directoryName, string packageRoot, ManifestBounds bounds)
    {
        ArgumentNullException.ThrowIfNull(yamlText);
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryName);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageRoot);
        ArgumentNullException.ThrowIfNull(bounds);

        JsonNode? document;
        try
        {
            document = YamlToJson.Convert(yamlText);
        }
        catch (YamlInvalidException exception)
        {
            // The one problem whose place is not a field: the file and the position, spelled together here.
            var path = string.Create(CultureInfo.InvariantCulture, $"plugin.yaml#{exception.Line}:{exception.Column}");
            return new ManifestReadResult(null, [new ManifestProblem(ProblemCodes.YamlInvalid, path, exception.Message)]);
        }

        if (document is not JsonObject root)
        {
            return new ManifestReadResult(null, [new ManifestProblem(ProblemCodes.YamlInvalid, "(root)", "A manifest is one YAML mapping of fields.")]);
        }

        var rules = new ManifestRules(root, directoryName, packageRoot, bounds);
        var manifest = rules.Build();
        return new ManifestReadResult(rules.Problems.Count == 0 ? manifest : null, rules.Problems);
    }
}
