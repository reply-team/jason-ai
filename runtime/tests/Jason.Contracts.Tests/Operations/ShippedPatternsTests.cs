using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Jason.Contracts.Operations;

namespace Jason.Contracts.Tests.OperationContracts;

/// <summary>
/// Every pattern the repository ships — in the operation and notification documents, their fixtures, and the plugin
/// manifests — is one the published grammar admits, and means under it what it meant before the grammar was published:
/// none writes a construct the grammar rewrites other than the end anchor and the dot. A pattern added later in a
/// construct the table refuses fails here, by its file, before it fails a package's load.
/// </summary>
public partial class ShippedPatternsTests
{
    [Fact]
    public void Every_shipped_pattern_is_admitted_and_means_what_it_meant()
    {
        var shipped = Shipped().ToList();
        Assert.True(shipped.Count >= 6, $"Only {shipped.Count} patterns were found; the walk has stopped finding them.");

        var problems = new List<string>();
        foreach (var (file, pattern) in shipped)
        {
            var form = PatternGrammar.Translate(pattern, out var refusal);
            if (refusal is not null)
            {
                problems.Add($"{file}: `{refusal.Construct}` at character {refusal.Position} is refused ({refusal.Row.Name}).");
                continue;
            }

            // Undo the two rewrites every pattern always had; anything else the grammar changed changes its meaning.
            var unchanged = form
                .Replace(@"\z", "$", StringComparison.Ordinal)
                .Replace(PatternGrammar.AnyButLineTerminator, ".", StringComparison.Ordinal);
            if (!string.Equals(unchanged, pattern, StringComparison.Ordinal))
            {
                problems.Add($"{file}: the grammar rewrites it to mean something else.");
            }
        }

        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    private static IEnumerable<(string File, string Pattern)> Shipped()
    {
        foreach (var path in Directory.EnumerateFiles(ContractFiles.Package, "*.json", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            foreach (var pattern in SchemaPatterns(JsonNode.Parse(File.ReadAllText(path))))
            {
                yield return (ContractFiles.Relative(path), pattern);
            }
        }

        foreach (var path in Directory.EnumerateFiles(Path.Combine(ContractFiles.Root, "plugins"), "plugin.yaml", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            foreach (Match match in YamlPattern().Matches(File.ReadAllText(path)))
            {
                yield return (ContractFiles.Relative(path), match.Groups["pattern"].Value);
            }
        }
    }

    /// <summary>The value of every `pattern` that sits in a schema of a string — beside a `"type": "string"`.</summary>
    private static IEnumerable<string> SchemaPatterns(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject schema:
                if (schema["pattern"] is JsonValue value && value.TryGetValue<string>(out var pattern)
                    && schema["type"] is JsonValue type && type.TryGetValue<string>(out var kind) && kind == "string")
                {
                    yield return pattern;
                }

                foreach (var (_, child) in schema)
                {
                    foreach (var found in SchemaPatterns(child))
                    {
                        yield return found;
                    }
                }

                break;
            case JsonArray array:
                foreach (var item in array)
                {
                    foreach (var found in SchemaPatterns(item))
                    {
                        yield return found;
                    }
                }

                break;
        }
    }

    /// <summary>A manifest's `pattern:` written as a double-quoted YAML string, the only way the manifests write one.</summary>
    [GeneratedRegex("""^\s*pattern:\s*"(?<pattern>(?:[^"\\]|\\.)*)"\s*$""", RegexOptions.Multiline)]
    private static partial Regex YamlPattern();
}
