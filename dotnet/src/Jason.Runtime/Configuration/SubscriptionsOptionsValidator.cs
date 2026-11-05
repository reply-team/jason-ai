using System.Globalization;
using Jason.Contracts.Configuration;
using Jason.Contracts.Plugins;
using Jason.Runtime.Plugins.Subscriptions;
using Microsoft.Extensions.Options;

namespace Jason.Runtime.Configuration;

/// <summary>
/// The section's numbers, each within its range, and the order of each kind's subscribers, checked for shape alone: each key a journal kind a plugin may subscribe to,
/// each entry a plugin id written once. Whether that plugin is installed, declares the kind and was granted it is a
/// reload question — settings are read before any package is loaded — and is answered there by
/// <see cref="SubscriptionProblemCodes.OrderUngranted"/>.
/// </summary>
/// <param name="installedPluginIds">
/// The plugins installed in this data directory, listed when there is a sentence to compose: an entry is a plugin id,
/// and a key travels into a sentence only when the code itself knows it.
/// </param>
/// <param name="kinds">Which kinds the runtime writes, and which of them a plugin may subscribe to.</param>
public sealed class SubscriptionsOptionsValidator(Func<IReadOnlySet<string>> installedPluginIds, SubscribableKinds kinds)
    : IValidateOptions<SubscriptionsOptions>
{
    /// <summary>A validator that knows of no installed plugin and reads the runtime's own list of kinds.</summary>
    public SubscriptionsOptionsValidator()
        : this(SafeBinding.NoInstalledPlugins, SubscribableKinds.Catalog)
    {
    }

    public ValidateOptionsResult Validate(string? name, SubscriptionsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<string>();
        var installed = new Lazy<IReadOnlySet<string>>(installedPluginIds);

        OptionRules.Range(failures, "Subscriptions:RunRecordsPerKind", options.RunRecordsPerKind, 1, SubscriptionsOptions.MaximumRunRecordsPerKind);
        OptionRules.Range(failures, "Subscriptions:EnvelopeBytes", options.EnvelopeBytes, 2, SubscriptionsOptions.MaximumEnvelopeBytes);
        OptionRules.Range(failures, "Subscriptions:TransientRetries", options.TransientRetries, 0, SubscriptionsOptions.MaximumTransientRetries);
        OptionRules.Range(failures, "Subscriptions:RetryBackoffSeconds", options.RetryBackoffSeconds, 1, SubscriptionsOptions.MaximumRetryBackoffSeconds);
        OptionRules.Range(failures, "Subscriptions:MaxParallel", options.MaxParallel, 1, SubscriptionsOptions.MaximumMaxParallel);

        foreach (var (kind, ids) in options.Order)
        {
            var setting = SubscriptionsOptions.Setting(kind);
            switch (kinds.Of(kind))
            {
                case KindStanding.Unknown:
                    failures.Add($"{SettingKey.ShownPath(setting, installed.Value)} must name a journal kind the runtime writes; {OptionRules.Written("key", kind, "is not one")}");
                    break;
                case KindStanding.NotSubscribable:
                    failures.Add($"{SettingKey.ShownPath(setting, installed.Value)} names a journal kind no plugin may subscribe to.");
                    break;
            }

            if (ids is null)
            {
                failures.Add($"{SettingKey.ShownPath(setting, installed.Value)} must be a list of plugin ids.");
                continue;
            }

            for (var index = 0; index < ids.Count; index++)
            {
                var id = ids[index];
                var entry = string.Create(CultureInfo.InvariantCulture, $"{SettingKey.ShownPath(setting, installed.Value)}[{index}]");
                if (!PluginIds.IsWellFormed(id))
                {
                    failures.Add($"{entry} must be a plugin id; {OptionRules.Written("value", id, "is not one")}");
                    continue;
                }

                var first = ids.FindIndex(earlier => string.Equals(earlier, id, StringComparison.Ordinal));
                if (first < index)
                {
                    failures.Add(string.Create(CultureInfo.InvariantCulture, $"{entry} repeats the id at [{first}]; a plugin is listed once."));
                }
            }
        }

        return OptionRules.Result(failures);
    }
}
