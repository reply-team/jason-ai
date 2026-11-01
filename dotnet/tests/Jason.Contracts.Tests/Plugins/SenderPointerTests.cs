using System.Text.Json.Nodes;
using Jason.Contracts.Plugins;
using Jason.Contracts.Tests.OperationContracts;

namespace Jason.Contracts.Tests.Plugins;

/// <summary>
/// A block of scope <c>sender</c> names one sending account by the provider's identifier, and an item meets that block
/// only through the sender its own input names. So there is one place in an input where a sender's identifier is read,
/// and it is held here over the operation documents themselves: every operation whose input refers to the sender
/// definition carries it at that one pointer, and no other operation carries a sender there at all.
/// </summary>
public sealed class SenderPointerTests
{
    private const string Reference = "#/$defs/sender";

    [Fact]
    public void Every_operation_that_takes_a_sender_carries_its_identifier_at_the_one_pointer()
    {
        var taking = 0;
        foreach (var file in Directory.EnumerateFiles(ContractFiles.Operations, "*.json").Order(StringComparer.Ordinal))
        {
            var input = JsonNode.Parse(File.ReadAllText(file))!["input_schema"]!.AsObject();
            var referenced = References(input, "").ToList();
            var name = ContractFiles.Relative(file);

            // The walk guards its own reading: a reference it did not reach is one the pointer would never be held over.
            var written = input.ToJsonString().Split("\"" + Reference + "\"").Length - 1;
            Assert.True(written == referenced.Count, $"{name} refers to the sender {written} times, and the walk reached {referenced.Count}.");

            if (referenced.Count == 0)
            {
                Assert.True(input["$defs"]?["sender"] is null, $"{name} defines a sender its input never refers to.");
                Assert.True(input["properties"]?["args"]?["properties"]?["sender"] is null, $"{name} carries a sender outside the shared definition.");
                continue;
            }

            taking++;
            Assert.True(
                referenced.SequenceEqual(["/args/sender"]),
                $"{name} refers to the sender at {string.Join(", ", referenced)}, where the pointer reads only /args/sender.");

            var sender = input["$defs"]!["sender"]!;
            Assert.Contains("external_id", sender["required"]!.AsArray().Select(required => (string?)required));
            Assert.Equal("string", (string?)sender["properties"]!["external_id"]!["type"]);
            Assert.Equal("/args/sender" + "/external_id", ProviderBlockContract.SenderPointer);
        }

        Assert.True(taking > 0, "No operation takes a sender, so the pointer would be held over nothing.");
    }

    /// <summary>Where in an instance each reference to the sender sits, read through the schema's properties.</summary>
    private static IEnumerable<string> References(JsonObject schema, string at)
    {
        if ((string?)schema["$ref"] == Reference)
        {
            yield return at;
        }

        if (schema["properties"] is JsonObject properties)
        {
            foreach (var (key, value) in properties)
            {
                if (value is JsonObject property)
                {
                    foreach (var found in References(property, at + "/" + key))
                    {
                        yield return found;
                    }
                }
            }
        }

        foreach (var keyword in new[] { "items", "additionalProperties" })
        {
            if (schema[keyword] is JsonObject nested)
            {
                foreach (var found in References(nested, at + "/*"))
                {
                    yield return found;
                }
            }
        }

        foreach (var keyword in new[] { "oneOf", "anyOf", "allOf" })
        {
            if (schema[keyword] is JsonArray branches)
            {
                foreach (var branch in branches.OfType<JsonObject>())
                {
                    foreach (var found in References(branch, at))
                    {
                        yield return found;
                    }
                }
            }
        }
    }
}
