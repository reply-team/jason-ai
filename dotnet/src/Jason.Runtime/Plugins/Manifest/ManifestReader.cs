using System.Globalization;
using System.Text.Json.Nodes;
using Jason.Contracts.Operations;
using Jason.Runtime.Plugins.Subscriptions;

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
    public static ManifestReadResult Read(string yamlText, string directoryName, string packageRoot, ManifestBounds bounds) =>
        Read(yamlText, directoryName, packageRoot, bounds, PatternTiming.Default);

    /// <summary>The same read, with the binding schema's patterns probed under <paramref name="timing"/>.</summary>
    public static ManifestReadResult Read(string yamlText, string directoryName, string packageRoot, ManifestBounds bounds, PatternTiming timing) =>
        Read(yamlText, directoryName, packageRoot, bounds, timing, probeTimeoutsHoldOnlyThePlugin: false);

    /// <summary>
    /// The same read, where a manifest whose only problems are binding probes that did not finish is a plugin held back by
    /// this machine rather than a broken package: with <paramref name="probeTimeoutsHoldOnlyThePlugin"/> those problems are
    /// read as <see cref="ProblemCodes.ProbeTimedOut"/>, environmental, and the manifest is kept. The start's second load
    /// reads this way; nothing else does.
    /// </summary>
    public static ManifestReadResult Read(string yamlText, string directoryName, string packageRoot, ManifestBounds bounds, PatternTiming timing, bool probeTimeoutsHoldOnlyThePlugin) =>
        Read(yamlText, directoryName, packageRoot, bounds, timing, probeTimeoutsHoldOnlyThePlugin, SubscribableKinds.Catalog);

    /// <summary>The same read, with the declared subscriptions read against <paramref name="kinds"/> rather than the catalog.</summary>
    public static ManifestReadResult Read(string yamlText, string directoryName, string packageRoot, ManifestBounds bounds, PatternTiming timing, bool probeTimeoutsHoldOnlyThePlugin, SubscribableKinds kinds)
    {
        ArgumentNullException.ThrowIfNull(yamlText);
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryName);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageRoot);
        ArgumentNullException.ThrowIfNull(bounds);
        ArgumentNullException.ThrowIfNull(timing);
        ArgumentNullException.ThrowIfNull(kinds);

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

        var rules = new ManifestRules(root, directoryName, packageRoot, bounds, timing, kinds);
        var manifest = rules.Build();
        if (probeTimeoutsHoldOnlyThePlugin && rules.Problems.Count > 0 && rules.Problems.All(problem => problem.Unmeasured))
        {
            return new ManifestReadResult(
                manifest,
                [.. rules.Problems.Select(problem => new ManifestProblem(ProblemCodes.ProbeTimedOut, problem.Path, problem.Message))]);
        }

        return new ManifestReadResult(rules.Problems.Count == 0 ? manifest : null, rules.Problems);
    }
}
