using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Jason.Contracts.Journal;

/// <summary>
/// The kinds the runtime reserves for its own journal lines, read once from the documents under
/// <c>docs/contracts/journal/</c> embedded into this assembly: the file a plugin author reads is the file every line of
/// the kind is checked against. A family of its own, beside <see cref="Operations.OperationCatalog"/>,
/// <see cref="Notifications.DeliveryCatalog"/>, <see cref="Events.EventsCatalog"/> and
/// <see cref="Preferences.PreferenceCatalog"/> and never inside any of them.
/// </summary>
public static class JournalKindCatalog
{
    private const string ResourcePrefix = "Jason.Contracts.Journal.";
    private const string ResourceSuffix = ".json";

    private static readonly Dictionary<string, JournalKindContract> ByKind = Read();

    /// <summary>Every kind, in ordinal order.</summary>
    public static IReadOnlyList<JournalKindContract> All { get; } =
        [.. ByKind.Values.OrderBy(contract => contract.Kind, StringComparer.Ordinal)];

    public static IReadOnlyList<string> Kinds { get; } = [.. All.Select(contract => contract.Kind)];

    public static JournalKindContract? Find(string kind)
    {
        ArgumentNullException.ThrowIfNull(kind);
        return ByKind.GetValueOrDefault(kind);
    }

    public static bool Knows(string kind) => Find(kind) is not null;

    private static Dictionary<string, JournalKindContract> Read()
    {
        var assembly = typeof(JournalKindCatalog).GetTypeInfo().Assembly;
        var contracts = new Dictionary<string, JournalKindContract>(StringComparer.Ordinal);

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

            var contract = JournalKindContract.Parse(file, document);
            if (!contracts.TryAdd(contract.Kind, contract))
            {
                throw new InvalidOperationException($"{file}: `{contract.Kind}` is published twice, and a kind has one document.");
            }
        }

        return contracts;
    }
}
