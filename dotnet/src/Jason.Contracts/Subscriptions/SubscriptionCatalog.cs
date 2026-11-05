using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Jason.Contracts.Subscriptions;

/// <summary>
/// The subscription contracts this build publishes, read once from the documents under <c>docs/contracts/subscriptions/</c>
/// embedded into this assembly: the file a plugin author reads is the file the runtime enforces. A family of its own,
/// beside <see cref="Operations.OperationCatalog"/>, <see cref="Notifications.DeliveryCatalog"/> and
/// <see cref="Events.EventsCatalog"/> and never inside any of them.
/// </summary>
public static class SubscriptionCatalog
{
    private const string ResourcePrefix = "Jason.Contracts.Subscriptions.";
    private const string ResourceSuffix = ".json";

    private static readonly Dictionary<string, SubscriptionContract> ByIdentifier = Read();

    public static IReadOnlyList<SubscriptionContract> All { get; } =
        [.. ByIdentifier.Values.OrderBy(contract => contract.Id, StringComparer.Ordinal)];

    public static SubscriptionContract? Find(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return ByIdentifier.GetValueOrDefault(id);
    }

    public static bool Knows(string id) => Find(id) is not null;

    private static Dictionary<string, SubscriptionContract> Read()
    {
        var assembly = typeof(SubscriptionCatalog).GetTypeInfo().Assembly;
        var contracts = new Dictionary<string, SubscriptionContract>(StringComparer.Ordinal);

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

            var contract = SubscriptionContract.Parse(file, document);
            if (!contracts.TryAdd(contract.Id, contract))
            {
                throw new InvalidOperationException($"{file}: `{contract.Id}` is published twice, and a handler has one contract.");
            }
        }

        return contracts;
    }
}
