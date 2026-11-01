using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Jason.Contracts.Preferences;

/// <summary>
/// The named preferences this build knows, read once from the documents under <c>docs/contracts/preferences/</c> embedded
/// into this assembly: the file a skill author reads is the file the runtime enforces. A family of its own, beside
/// <see cref="Operations.OperationCatalog"/>, <see cref="Notifications.DeliveryCatalog"/> and
/// <see cref="Events.EventsCatalog"/> and never inside any of them.
/// </summary>
public static class PreferenceCatalog
{
    private const string ResourcePrefix = "Jason.Contracts.Preferences.";
    private const string ResourceSuffix = ".json";

    private static readonly Dictionary<string, PreferenceContract> ByKey = Read();

    /// <summary>Every key, in ordinal order: the order <c>preference.list</c> answers in.</summary>
    public static IReadOnlyList<PreferenceContract> All { get; } =
        [.. ByKey.Values.OrderBy(contract => contract.Key, StringComparer.Ordinal)];

    public static IReadOnlyList<string> Keys { get; } = [.. All.Select(contract => contract.Key)];

    public static PreferenceContract? Find(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return ByKey.GetValueOrDefault(key);
    }

    public static bool Knows(string key) => Find(key) is not null;

    private static Dictionary<string, PreferenceContract> Read()
    {
        var assembly = typeof(PreferenceCatalog).GetTypeInfo().Assembly;
        var contracts = new Dictionary<string, PreferenceContract>(StringComparer.Ordinal);

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

            var contract = PreferenceContract.Parse(file, document);
            if (!contracts.TryAdd(contract.Key, contract))
            {
                throw new InvalidOperationException($"{file}: `{contract.Key}` is published twice, and a key has one document.");
            }
        }

        return contracts;
    }
}
