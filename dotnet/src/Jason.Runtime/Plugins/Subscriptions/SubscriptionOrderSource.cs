using Jason.Contracts.Api;
using Jason.Runtime.Configuration;
using Jason.Runtime.Plugins.Registry;
using Microsoft.Extensions.Options;

namespace Jason.Runtime.Plugins.Subscriptions;

/// <summary>
/// The one reader of <c>Subscriptions:Order</c>: the load at a start and the plugin reload, and nothing else, so an
/// edit to the order means nothing until the next reload, as an edit to a route does.
/// </summary>
public sealed class SubscriptionOrderSource(IOptionsMonitor<SubscriptionOrderOptions> options)
{
    /// <summary>The chains <paramref name="snapshot"/> would freeze under the order as the settings now hold it.</summary>
    public SubscriptionCandidate Build(PluginSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        SubscriptionOrderOptions order;
        try
        {
            order = options.CurrentValue;
        }
        catch (OptionsValidationException refused)
        {
            // The section was edited into something its validator will not hand over: a rejected load naming the
            // setting, as a mistyped route is, and never a fault of the runtime.
            return new SubscriptionCandidate(
                null,
                [.. refused.Failures.Select(failure => new ErrorDetail(
                    SettingsFailure.Name(SubscriptionOrderOptions.Section, failure),
                    SubscriptionProblemCodes.SettingsInvalid,
                    failure))]);
        }

        return SubscriptionChains.Build(snapshot, order);
    }
}
