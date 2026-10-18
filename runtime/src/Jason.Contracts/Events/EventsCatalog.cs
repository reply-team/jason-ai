using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Jason.Contracts.Events;

/// <summary>
/// The events contracts this build publishes, read once from the documents under <c>docs/contracts/events/</c> embedded
/// into this assembly: the file a plugin author reads is the file the runtime enforces. A family of its own, beside
/// <see cref="Operations.OperationCatalog"/> and <see cref="Notifications.DeliveryCatalog"/> and never inside either.
/// </summary>
public static class EventsCatalog
{
    private const string ResourcePrefix = "Jason.Contracts.Events.";
    private const string ResourceSuffix = ".json";

    private static readonly Dictionary<string, EventsContract> ByIdentifier = Read();

    public static IReadOnlyList<EventsContract> All { get; } =
        [.. ByIdentifier.Values.OrderBy(contract => contract.Id, StringComparer.Ordinal)];

    public static EventsContract? Find(string operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return ByIdentifier.GetValueOrDefault(operation);
    }

    public static bool Knows(string operation) => Find(operation) is not null;

    private static Dictionary<string, EventsContract> Read()
    {
        var assembly = typeof(EventsCatalog).GetTypeInfo().Assembly;
        var contracts = new Dictionary<string, EventsContract>(StringComparer.Ordinal);

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

            var contract = EventsContract.Parse(file, document);
            if (!contracts.TryAdd(contract.Id, contract))
            {
                throw new InvalidOperationException($"{file}: `{contract.Id}` is published twice, and a read has one contract.");
            }
        }

        return contracts;
    }
}
