using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Jason.Contracts.Operations;

/// <summary>
/// Every canonical operation this build knows, read once from the documents published under
/// <c>docs/contracts/operations/</c> and embedded here. There is no second source: what a plugin author reads is
/// what the runtime enforces.
/// </summary>
/// <remarks>
/// A document that does not parse throws while the catalog is first touched, naming the file. That is deliberate: a
/// broken catalog is a mistake in this repository, caught by the first test that runs, and never a condition the
/// runtime has to carry on through.
/// </remarks>
public static class OperationCatalog
{
    private const string ResourcePrefix = "Jason.Contracts.Operations.";
    private const string ResourceSuffix = ".json";

    private static readonly Dictionary<string, OperationContract> ByIdentifier = Read();

    /// <summary>The published operations, ordered by id so that anything rendered from them is stable.</summary>
    public static IReadOnlyList<OperationContract> All { get; } =
        [.. ByIdentifier.Values.OrderBy(contract => contract.Id, StringComparer.Ordinal)];

    /// <summary>The contract for one operation, or null when nothing of that name is published.</summary>
    public static OperationContract? Find(string operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        return ByIdentifier.GetValueOrDefault(operation);
    }

    /// <summary>Whether this build publishes a contract for the named operation.</summary>
    public static bool Knows(string operation) => Find(operation) is not null;

    private static Dictionary<string, OperationContract> Read()
    {
        var assembly = typeof(OperationCatalog).GetTypeInfo().Assembly;
        var contracts = new Dictionary<string, OperationContract>(StringComparer.Ordinal);

        foreach (var resource in assembly.GetManifestResourceNames())
        {
            if (!resource.StartsWith(ResourcePrefix, StringComparison.Ordinal) || !resource.EndsWith(ResourceSuffix, StringComparison.Ordinal))
            {
                continue;
            }

            var file = resource[ResourcePrefix.Length..];
            var contract = OperationContract.Parse(file, ReadDocument(assembly, resource, file));
            if (!contracts.TryAdd(contract.Id, contract))
            {
                throw new InvalidOperationException($"{file}: `{contract.Id}` is published twice, and an operation has one contract.");
            }
        }

        return contracts;
    }

    private static JsonNode? ReadDocument(Assembly assembly, string resource, string file)
    {
        using var stream = assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"{file}: the embedded document could not be opened.");

        try
        {
            return JsonNode.Parse(stream);
        }
        catch (JsonException error)
        {
            throw new InvalidOperationException($"{file}: the document is not valid JSON.", error);
        }
    }
}
