namespace Jason.Runtime.Routing;

/// <summary>
/// Everything that can be wrong with a route, spelled exactly once. A route problem is found while a candidate
/// route set is being built, before anything is swapped, and it refuses the whole reload: a route that cannot be
/// used is a mistake in the installation's configuration, and a registry that is only partly right hides it.
/// <para>
/// The seven codes are listed in the order they are checked, and the first one that fires is the one a route is
/// reported under. The order is not arbitrary: everything after <see cref="PluginUnknown"/> reads the plugin, and
/// <see cref="BindingSecretLike"/> comes before <see cref="BindingInvalid"/> because a plugin's schema will
/// usually refuse an unexpected field too, and "this looks like a credential" is the sentence the person who
/// wrote it needs to read.
/// </para>
/// </summary>
public static class RouteProblemCodes
{
    /// <summary>The route names a plugin the candidate set does not contain.</summary>
    public const string PluginUnknown = "route_plugin_unknown";

    /// <summary>The plugin is of a kind nothing invokes — a notification plugin implements no canonical operation.</summary>
    public const string PluginKindNotInvocable = "route_plugin_kind_not_invocable";

    /// <summary>The route is for an operation this build does not publish.</summary>
    public const string OperationUnknown = "route_operation_unknown";

    /// <summary>The plugin does not list the operation the route sends it.</summary>
    public const string OperationUnsupported = "route_operation_unsupported";

    /// <summary>The plugin's <c>contracts.operations</c> does not contain the version of the operation's contract.</summary>
    public const string ContractIncompatible = "route_contract_incompatible";

    /// <summary>The binding carries a field named like a credential, at any depth.</summary>
    public const string BindingSecretLike = "route_binding_secret_like";

    /// <summary>The binding does not satisfy the schema the plugin's manifest declares for a route to it.</summary>
    public const string BindingInvalid = "route_binding_invalid";

    /// <summary>
    /// Not one of the seven: this is the <c>Routes</c> section itself failing the validator that reads it, which
    /// happens when the file is edited into something no route could be built from. It is reported like a route
    /// problem — the same rejected reload, naming the same setting — because an operator who mistypes a route
    /// must get one answer, not a 500 that says the runtime broke rather than the edit.
    /// </summary>
    public const string SettingsInvalid = "routes_settings_invalid";
}
