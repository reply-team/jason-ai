using System.Globalization;
using Jason.Contracts.Api;
using Jason.Runtime.Configuration;
using Jason.Runtime.Plugins.Registry;

namespace Jason.Runtime.Plugins.Subscriptions;

/// <summary>What a load would freeze as the chains, or why it cannot: never both.</summary>
public sealed record SubscriptionCandidate(SubscriptionChains? Chains, IReadOnlyList<ErrorDetail> Problems);

/// <summary>
/// Which plugins handle each journal kind, and in what order, frozen by the load on the plugin snapshot it belongs to:
/// plugins, grants and chains change together, by the one swap, and nothing else changes them.
/// <para>
/// A chain is every plugin of the set granted the kind — whatever its kind and whether it is available or not, since
/// availability changes only at a load and the plugin is still the one the person granted the kind to. The ids
/// <c>Subscriptions:Order:&lt;kind&gt;</c> lists come first, in the order written; every other plugin granted the kind
/// follows in plugin-id order. Only kinds with at least one such plugin have a chain.
/// </para>
/// </summary>
public sealed class SubscriptionChains
{
    private readonly IReadOnlyDictionary<string, IReadOnlyList<string>> _chains;

    private SubscriptionChains(IReadOnlyDictionary<string, IReadOnlyList<string>> chains)
    {
        _chains = chains;
        Kinds = [.. chains.Keys.Order(StringComparer.Ordinal)];
    }

    /// <summary>No kind has a chain: the chains of a set no load has built.</summary>
    public static SubscriptionChains Empty { get; } = new(new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal));

    /// <summary>The kinds some plugin handles, in ordinal order.</summary>
    public IReadOnlyList<string> Kinds { get; }

    /// <summary>The plugin ids that handle <paramref name="kind"/>, in the order they run; empty when none does.</summary>
    public IReadOnlyList<string> For(string kind) =>
        _chains.TryGetValue(kind, out var chain) ? chain : [];

    /// <summary>
    /// The chains of <paramref name="snapshot"/> under <paramref name="order"/>, or every id the order lists for a kind
    /// that plugin is not granted. Such an id is refused rather than passed over: the person meant something this set
    /// does not hold, and a chain run without it would carry out an order nobody wrote.
    /// </summary>
    public static SubscriptionCandidate Build(PluginSnapshot snapshot, SubscriptionOrderOptions order)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(order);

        var granted = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var plugin in snapshot.Plugins.OrderBy(plugin => plugin.Manifest.Id, StringComparer.Ordinal))
        {
            foreach (var kind in plugin.Grants.Subscriptions)
            {
                if (!granted.TryGetValue(kind, out var ids))
                {
                    ids = [];
                    granted[kind] = ids;
                }

                ids.Add(plugin.Manifest.Id);
            }
        }

        var problems = new List<ErrorDetail>();
        foreach (var (kind, listed) in order.Order.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            var ids = granted.GetValueOrDefault(kind) ?? [];
            for (var index = 0; index < listed.Count; index++)
            {
                if (!ids.Contains(listed[index], StringComparer.Ordinal))
                {
                    problems.Add(new ErrorDetail(
                        string.Create(CultureInfo.InvariantCulture, $"{SubscriptionOrderOptions.Setting(kind)}[{index}]"),
                        SubscriptionProblemCodes.OrderUngranted,
                        Ungranted(snapshot, kind, listed[index])));
                }
            }
        }

        if (problems.Count > 0)
        {
            return new SubscriptionCandidate(null, problems);
        }

        var chains = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var (kind, ids) in granted)
        {
            var listed = order.Order.GetValueOrDefault(kind) ?? [];
            chains[kind] = [.. listed, .. ids.Where(id => !listed.Contains(id, StringComparer.Ordinal))];
        }

        return new SubscriptionCandidate(new SubscriptionChains(chains), []);
    }

    /// <summary>Which of the three causes it is, since each has a repair of its own.</summary>
    private static string Ungranted(PluginSnapshot snapshot, string kind, string id)
    {
        var plugin = snapshot.Find(id);
        if (plugin is null)
        {
            return $"no plugin with id '{id}' is in the candidate set, so it cannot be ordered among the plugins that handle {kind}.";
        }

        return plugin.Manifest.Subscriptions.Contains(kind, StringComparer.Ordinal)
            ? $"plugin '{id}' declares {kind} but Plugins:Grants:{id}:Subscriptions does not grant it, so it cannot be ordered among the plugins that handle it."
            : $"plugin '{id}' does not declare {kind} under subscriptions in its manifest, so it cannot be ordered among the plugins that handle it.";
    }
}
