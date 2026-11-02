using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Jason.Contracts.Context;

/// <summary>
/// The reserved keys of a campaign's context this build knows, read once from the documents under
/// <c>docs/contracts/context/</c> embedded into this assembly: the file a skill author reads is the file the runtime
/// enforces. A family of its own, beside <see cref="Operations.OperationCatalog"/>,
/// <see cref="Notifications.DeliveryCatalog"/>, <see cref="Events.EventsCatalog"/> and
/// <see cref="Preferences.PreferenceCatalog"/> and never inside any of them.
/// </summary>
public static class ContextKeyCatalog
{
    private const string ResourcePrefix = "Jason.Contracts.Context.";
    private const string ResourceSuffix = ".json";

    private static readonly Dictionary<string, ContextKeyContract> ByKey = Read();

    /// <summary>Every reserved key, in ordinal order.</summary>
    public static IReadOnlyList<ContextKeyContract> All { get; } =
        [.. ByKey.Values.OrderBy(contract => contract.Key, StringComparer.Ordinal)];

    public static IReadOnlyList<string> Keys { get; } = [.. All.Select(contract => contract.Key)];

    public static bool Knows(string key) => Find(key) is not null;

    public static ContextKeyContract? Find(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return ByKey.GetValueOrDefault(key);
    }

    private static Dictionary<string, ContextKeyContract> Read()
    {
        var assembly = typeof(ContextKeyCatalog).GetTypeInfo().Assembly;
        var contracts = new Dictionary<string, ContextKeyContract>(StringComparer.Ordinal);

        foreach (var resource in assembly.GetManifestResourceNames())
        {
            if (!resource.StartsWith(ResourcePrefix, StringComparison.Ordinal) || !resource.EndsWith(ResourceSuffix, StringComparison.Ordinal))
            {
                continue;
            }

            var file = resource[ResourcePrefix.Length..];
            using var stream = assembly.GetManifestResourceStream(resource)
                ?? throw new InvalidOperationException($"{file}: the embedded document could not be opened.");

            JsonNode? document;
            try
            {
                document = JsonNode.Parse(stream);
            }
            catch (JsonException error)
            {
                throw new InvalidOperationException($"{file}: the document is not valid JSON.", error);
            }

            var contract = ContextKeyContract.Parse(file, document);
            if (!contracts.TryAdd(contract.Key, contract))
            {
                throw new InvalidOperationException($"{file}: `{contract.Key}` is published twice, and a key has one document.");
            }
        }

        return contracts;
    }
}
