using System.Text.Json.Nodes;
using Jason.Contracts.Discovery;

namespace Jason.Runtime.Tests.Hosting;

/// <summary>
/// That a log carries none of the operating system's own account of a refusal over the endpoint descriptor, read in what
/// no language changes. The system words its refusal in the machine's own language, and every language's sentence names
/// the path it refused: the descriptor, or the temporary file it is written to beside it in the run directory. And the
/// line that says the refusal carries no exception: an exception attached there would be the refusal's own.
/// </summary>
/// <remarks>
/// An exception on another line is not read as the refusal's. A start refused after the host had started its services
/// disposes the host without stopping it, and the host may then log a background service it cancelled itself, with an
/// exception that names no path.
/// </remarks>
internal static class SystemRefusalInLog
{
    public static void AssertNone(string log, JasonPaths paths)
    {
        var lines = log
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => JsonNode.Parse(line)!.AsObject())
            .ToList();

        var withException = lines.Where(line => line.ContainsKey("Descriptor") && line.ContainsKey("@x")).Select(line => line.ToJsonString()).ToList();
        Assert.True(withException.Count == 0, $"A line that names the descriptor carries an exception:\n{string.Join('\n', withException)}");

        // Strict on purpose: the run directory is named by the descriptor's own property and nowhere else, so a line
        // added later that names it under another property, or inside a longer text, has to be added here knowingly.
        var elsewhere = lines
            .SelectMany(line => Strings(line, property: null))
            .Where(named => named.Value.Contains(paths.RunDirectory, StringComparison.OrdinalIgnoreCase)
                && !(named.Property == "Descriptor" && named.Value == paths.DescriptorFile))
            .Select(named => $"{named.Property}: {named.Value}")
            .ToList();
        Assert.True(elsewhere.Count == 0, $"The log names the run directory other than as the descriptor:\n{string.Join('\n', elsewhere)}");
    }

    /// <summary>Every string a line carries, with the property it is the value of, at any depth.</summary>
    private static IEnumerable<(string? Property, string Value)> Strings(JsonNode? node, string? property) => node switch
    {
        JsonObject properties => properties.SelectMany(pair => Strings(pair.Value, pair.Key)),
        JsonArray items => items.SelectMany(item => Strings(item, property)),
        JsonValue value when value.TryGetValue<string>(out var text) => [(property, text)],
        _ => [],
    };
}
