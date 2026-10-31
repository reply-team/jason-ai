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

    /// <summary>The reach of an operation that does something to or for a person: its document's own word.</summary>
    private const string ActReach = "act";

    private static readonly Dictionary<string, OperationContract> ByIdentifier = Read();

    /// <summary>The published operations, ordered by id so that anything rendered from them is stable.</summary>
    public static IReadOnlyList<OperationContract> All { get; } =
        [.. ByIdentifier.Values.OrderBy(contract => contract.Id, StringComparer.Ordinal)];

    /// <summary>
    /// The published operation with the largest budget — the one every budget an installation configures has to
    /// be able to hold, and the reason a setting can be refused for a number nobody wrote into it. Null only if
    /// a build published nothing at all. Ties go to the first id, because <see cref="All"/> is already settled.
    /// </summary>
    public static OperationContract? Slowest { get; } =
        All.OrderByDescending(contract => contract.TimeoutMs).FirstOrDefault();

    /// <summary>
    /// The operations whose work is claimed while its campaign is paused, from each document's
    /// <c>dispatch.in_paused_campaign</c>: an operation whose not running is the harm. Read once, as the documents are.
    /// </summary>
    public static IReadOnlyList<string> RunsInPausedCampaign { get; } =
        [.. All.Where(contract => contract.Dispatch.InPausedCampaign == InPausedCampaign.Runs).Select(contract => contract.Id)];

    /// <summary>
    /// The operations whose work a provider block holds, from each document's <c>dispatch.under_provider_block</c>: every
    /// one that declares <c>waits</c>. Every operation whose <c>reach</c> is <c>act</c> declares it, because a block is
    /// the provider refusing to let work reach a person; a control declares it where its one run must not end on that
    /// refusal. Every other operation, a read or a control, runs through a block and ends on its own answer, because a
    /// read tells the truth and a control may still succeed. Read once, as the documents are.
    /// </summary>
    public static IReadOnlySet<string> HeldByProviderBlock { get; } =
        new HashSet<string>(
            All.Where(contract => contract.Dispatch.UnderProviderBlock == UnderProviderBlock.Waits).Select(contract => contract.Id),
            StringComparer.Ordinal);

    /// <summary>
    /// The held operations whose answer decides a block: those whose <c>reach</c> is <c>act</c>. A block's trial is drawn
    /// from their work first, and only their call getting past a block clears it; a held control's answer decides
    /// nothing but that the provider still refuses it.
    /// </summary>
    public static IReadOnlySet<string> TriedByProviderBlock { get; } =
        new HashSet<string>(
            All.Where(contract => HeldByProviderBlock.Contains(contract.Id) && string.Equals(contract.Reach.Value, ActReach, StringComparison.Ordinal)).Select(contract => contract.Id),
            StringComparer.Ordinal);

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
