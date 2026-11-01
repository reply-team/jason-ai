using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Jason.Contracts.Notifications;

/// <summary>
/// The delivery contracts this build publishes, read once from the documents under <c>docs/contracts/notifications/</c>
/// embedded into this assembly: the file a plugin author reads is the file the runtime enforces. A family of its own,
/// beside <see cref="Operations.OperationCatalog"/> and never inside it.
/// </summary>
public static class DeliveryCatalog
{
    private const string ResourcePrefix = "Jason.Contracts.Notifications.";
    private const string ResourceSuffix = ".json";

    private static readonly Dictionary<string, DeliveryContract> ByIdentifier = Read();

    public static IReadOnlyList<DeliveryContract> All { get; } =
        [.. ByIdentifier.Values.OrderBy(contract => contract.Id, StringComparer.Ordinal)];

    public static DeliveryContract? Find(string operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return ByIdentifier.GetValueOrDefault(operation);
    }

    public static bool Knows(string operation) => Find(operation) is not null;

    private static Dictionary<string, DeliveryContract> Read()
    {
        var assembly = typeof(DeliveryCatalog).GetTypeInfo().Assembly;
        var contracts = new Dictionary<string, DeliveryContract>(StringComparer.Ordinal);

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

            var contract = DeliveryContract.Parse(file, document);
            if (!contracts.TryAdd(contract.Id, contract))
            {
                throw new InvalidOperationException($"{file}: `{contract.Id}` is published twice, and a delivery has one contract.");
            }
        }

        return contracts;
    }
}
