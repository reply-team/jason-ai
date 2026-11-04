using System.Globalization;
using Jason.Contracts.Configuration;
using Microsoft.Extensions.Configuration;

namespace Jason.Runtime.Configuration;

/// <summary>
/// The order the subscribers of a journal kind run in: <c>Subscriptions:Order:&lt;kind&gt;</c>, a list of plugin ids.
/// The ids listed come first, in the order written; every other plugin granted that kind follows in plugin-id order,
/// and a kind with no list, or an empty one, runs in plugin-id order alone.
/// <para>
/// Frozen as routes are: read at a start and at the plugin reload, never while a chain runs. Only the <c>Order</c>
/// child of the section is read here; every other setting of <c>Subscriptions</c> belongs to another class and is
/// neither read nor refused by this one, so a mistake in the order can never stop a setting beside it.
/// </para>
/// </summary>
public sealed class SubscriptionOrderOptions
{
    public const string Section = "Subscriptions";

    public Dictionary<string, List<string>> Order { get; set; } = new(StringComparer.Ordinal);

    /// <summary>The settings path of one kind's order, as a refusal names it.</summary>
    public static string Setting(string kind) => $"{Section}:{nameof(Order)}:{kind}";

    /// <summary>
    /// Read rather than bound: the binder appends to a list it finds already filled, which would turn a narrowed order
    /// into a lengthened one, and it cannot say where a list was written as something else. Text or an object where a
    /// list belongs is recorded against the instance by its path and what has to be there, for the section's
    /// <see cref="BindingFailureValidator{TOptions}"/> to refuse: a start then fails and a reload is rejected, naming the
    /// setting and never what was written.
    /// </summary>
    internal static void Fill(IConfiguration section, SubscriptionOrderOptions options)
    {
        ArgumentNullException.ThrowIfNull(section);
        ArgumentNullException.ThrowIfNull(options);

        var passedOver = new List<string>();
        if (section is IConfigurationSection { Value: { Length: > 0 } })
        {
            passedOver.Add($"{Section} could not be read as an object.");
        }

        var order = section.GetSection(nameof(Order));
        if (order.Value is { Length: > 0 })
        {
            passedOver.Add($"{Section}:{nameof(Order)} could not be read as a map of journal kinds to lists of plugin ids.");
        }

        options.Order.Clear();
        foreach (var kind in order.GetChildren())
        {
            var ids = List(kind);
            if (ids is null)
            {
                passedOver.Add($"{SettingKey.ShownPath(Setting(kind.Key), SafeBinding.NoInstalledPlugins())} could not be read as a list of plugin ids.");
                continue;
            }

            options.Order[kind.Key] = ids;
        }

        if (passedOver.Count > 0)
        {
            SafeBinding.Refuse(options, passedOver);
        }
    }

    /// <summary>
    /// A kind's list as it was written: its entries in index order, or none for an empty list. Null when what is there
    /// is not a list of text — a piece of text itself, an object, or a list holding a list or an object.
    /// </summary>
    private static List<string>? List(IConfigurationSection kind)
    {
        var entries = kind.GetChildren().ToList();
        if (entries.Count == 0)
        {
            return kind.Value is { Length: > 0 } ? null : [];
        }

        var ids = new List<string>(entries.Count);
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries.Find(child => string.Equals(child.Key, index.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal));
            if (entry is null || entry.GetChildren().Any())
            {
                return null;
            }

            ids.Add(entry.Value ?? string.Empty);
        }

        return ids;
    }
}
