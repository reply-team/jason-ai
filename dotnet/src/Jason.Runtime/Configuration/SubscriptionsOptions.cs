using System.Globalization;
using Jason.Contracts.Configuration;
using Microsoft.Extensions.Configuration;

namespace Jason.Runtime.Configuration;

/// <summary>
/// The section <c>Subscriptions</c>: the order each journal kind's subscribers run in, and the numbers the runner of
/// their chains works to.
/// <para>
/// <c>Subscriptions:Order:&lt;kind&gt;</c> is a list of plugin ids. The ids listed come first, in the order written;
/// every other plugin granted that kind follows in plugin-id order, and a kind with no list, or an empty one, runs in
/// plugin-id order alone.
/// </para>
/// <para>
/// One class holds both, and when each is read is its readers' doing, not the class's: the order is frozen as routes
/// are, read by the plugin load at a start and at the reload and never while a chain runs; the numbers are live, read
/// through the section's seam by the runner at each chain and each handler, so an edit applies without a restart. A
/// mistake anywhere in the section refuses the section: a start fails naming the setting, a reload is rejected, and the
/// runner keeps the last numbers that validated.
/// </para>
/// </summary>
public sealed class SubscriptionsOptions
{
    public const string Section = "Subscriptions";

    /// <summary>The widest bounds of the numbers: what the validator allows.</summary>
    public const int MaximumRunRecordsPerKind = 100_000;

    public const int MaximumEnvelopeBytes = 524_288;

    public const int MaximumTransientRetries = 10;

    public const int MaximumRetryBackoffSeconds = 3_600;

    public const int MaximumMaxParallel = 16;

    public Dictionary<string, List<string>> Order { get; set; } = new(StringComparer.Ordinal);

    /// <summary>
    /// How many ended runs of one kind are kept, succeeded and failed alike; the oldest beyond it are deleted when the
    /// kind's next run starts. A run in flight is never deleted. The lasting record of a failure is its journal line.
    /// </summary>
    public int RunRecordsPerKind { get; set; } = 1_000;

    /// <summary>
    /// The largest envelope a handler may return, in bytes of its compact JSON as UTF-8. Below the input's own limit,
    /// so the line travels beside the largest envelope allowed.
    /// </summary>
    public int EnvelopeBytes { get; set; } = 65_536;

    /// <summary>How many times a handler whose invocation ended transient is run again before the chain ends.</summary>
    public int TransientRetries { get; set; } = 3;

    /// <summary>
    /// The wait before the first repeat of a transient handler, doubled at each repeat and held to an hour; and the wait
    /// before a kind whose turn gave out for a reason of the runtime's own is offered again, doubled at each give-out in a
    /// row and held to the same hour.
    /// </summary>
    public int RetryBackoffSeconds { get; set; } = 30;

    /// <summary>
    /// How many handler invocations, one child process each, run at once across every kind. A chain waiting out a
    /// backoff holds none.
    /// </summary>
    public int MaxParallel { get; set; } = 2;

    /// <summary>The settings path of one kind's order, as a refusal names it.</summary>
    public static string Setting(string kind) => $"{Section}:{nameof(Order)}:{kind}";

    /// <summary>
    /// The numbers are bound as any section's are, into a holder that has no <c>Order</c>, so the binder never reaches
    /// the order and a number it refuses is said by its path once. The order is read rather than bound: the binder
    /// appends to a list it finds already filled, which would turn a narrowed order into a lengthened one, and it cannot
    /// say where a list was written as something else. Text or an object where a
    /// list belongs is recorded against the instance by its path and what has to be there, for the section's
    /// <see cref="BindingFailureValidator{TOptions}"/> to refuse: a start then fails and a reload is rejected, naming the
    /// setting and never what was written.
    /// </summary>
    internal static void Fill(IConfiguration section, SubscriptionsOptions options)
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

        var numbers = new Numbers
        {
            RunRecordsPerKind = options.RunRecordsPerKind,
            EnvelopeBytes = options.EnvelopeBytes,
            TransientRetries = options.TransientRetries,
            RetryBackoffSeconds = options.RetryBackoffSeconds,
            MaxParallel = options.MaxParallel,
        };

        // A section written as text has been said to be that already; there is nothing under it to bind.
        if (section is IConfigurationSection { Value: null or "" } numbersSection)
        {
            SafeBinding.Bind(numbersSection, numbers, SafeBinding.NoInstalledPlugins);
            if (SafeBinding.TryGetFailures(numbers, out var refused))
            {
                passedOver.AddRange(refused);
            }
        }

        options.RunRecordsPerKind = numbers.RunRecordsPerKind;
        options.EnvelopeBytes = numbers.EnvelopeBytes;
        options.TransientRetries = numbers.TransientRetries;
        options.RetryBackoffSeconds = numbers.RetryBackoffSeconds;
        options.MaxParallel = numbers.MaxParallel;

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

    /// <summary>The section's numbers alone, for the binder: the order is never handed to it.</summary>
    private sealed class Numbers
    {
        public int RunRecordsPerKind { get; set; }

        public int EnvelopeBytes { get; set; }

        public int TransientRetries { get; set; }

        public int RetryBackoffSeconds { get; set; }

        public int MaxParallel { get; set; }
    }
}
