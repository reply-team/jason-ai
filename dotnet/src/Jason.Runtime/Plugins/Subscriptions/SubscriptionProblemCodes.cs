namespace Jason.Runtime.Plugins.Subscriptions;

/// <summary>
/// Everything that can be wrong with the order of a kind's subscribers, spelled exactly once. Like a route problem, an
/// order problem is found while the candidate snapshot is being built, before anything is swapped, and it refuses the
/// whole reload with both snapshots kept: an order that names a plugin which will never run for that kind is a mistake
/// in the installation's configuration, and a chain that is only partly what the person wrote hides it.
/// </summary>
public static class SubscriptionProblemCodes
{
    /// <summary>
    /// The order of a kind names a plugin that holds no granted subscription to it: the plugin is not installed, its
    /// manifest does not declare the kind, or the settings do not grant it. The message says which.
    /// </summary>
    public const string OrderUngranted = "subscription_order_ungranted";

    /// <summary>
    /// The <c>Subscriptions:Order</c> setting itself fails the validator that reads it. It is reported like an order
    /// problem — the same rejected reload, naming the same setting — because an operator who mistypes the order must
    /// get one answer, not a 500 that says the runtime broke rather than the edit.
    /// </summary>
    public const string SettingsInvalid = "subscriptions_settings_invalid";
}
