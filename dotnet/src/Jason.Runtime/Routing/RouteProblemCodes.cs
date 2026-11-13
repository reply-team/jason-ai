namespace Jason.Runtime.Routing;

/// <summary>
/// Everything that can be wrong with a route, spelled exactly once. A route problem is found while a candidate
/// route set is being built, before anything is swapped, and it refuses the whole reload: a route that cannot be
/// used is a mistake in the installation's configuration, and a registry that is only partly right hides it.
/// <para>
/// The eleven codes are listed in the order they are checked, and the first one that fires is the one a route is
/// reported under. The two channel codes are asked only of a route that names a channel, straight after the operation is
/// found in the catalog, since they are questions about the operation's document rather than about the plugin, and
/// <see cref="ChannelPluginReadsNoEvents"/> only of such a route, once the plugin is found to perform it. The order
/// is not arbitrary: everything after <see cref="PluginUnknown"/> reads the plugin, and
/// <see cref="BindingSecretLike"/> comes before <see cref="BindingInvalid"/> because a plugin's schema will
/// usually refuse an unexpected field too, and "this looks like a credential" is the sentence the person who
/// wrote it needs to read. <see cref="BindingTooLarge"/> sits between them for the same reason read the other
/// way: a credential is the more urgent sentence, and how big a binding is is a fact about the binding rather
/// than about the plugin whose schema is asked next.
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

    /// <summary>
    /// The route names a channel under an operation whose document names no argument a route reads its channel from — or
    /// under no operation at all, the default route, which has none to be under.
    /// </summary>
    public const string ChannelNotRoutable = "route_channel_not_routable";

    /// <summary>The route names a channel the argument the operation's document names for routes does not allow.</summary>
    public const string ChannelUnknown = "route_channel_unknown";

    /// <summary>The plugin does not list the operation the route sends it.</summary>
    public const string OperationUnsupported = "route_operation_unsupported";

    /// <summary>The plugin's <c>contracts.operations</c> does not contain the version of the operation's contract.</summary>
    public const string ContractIncompatible = "route_contract_incompatible";

    /// <summary>
    /// The route names a channel, and the plugin it names reads no events (its manifest declares no <c>contracts.events</c>).
    /// Every operation that takes channel routes is about what is sent on the channel (<c>message.send</c> and
    /// <c>conversation.reply</c> send it, <c>sender.list</c> names the accounts that will), and a reply to what is sent on a
    /// channel route reaches a campaign only through the events of the account it went from, so such a route would leave
    /// the reply unseen. Asked of a channel route only, after the plugin is found to perform the
    /// operation and speak its contract, and asked again of every channel route at every reload, so a package that stops
    /// reading events while a channel route names it refuses the reload.
    /// </summary>
    public const string ChannelPluginReadsNoEvents = "route_channel_plugin_reads_no_events";

    /// <summary>The binding carries a field named like a credential, at any depth.</summary>
    public const string BindingSecretLike = "route_binding_secret_like";

    /// <summary>
    /// The binding's canonical JSON is larger than the protocol carries to a plugin. It is its own code rather
    /// than a second meaning for <see cref="BindingInvalid"/>, which says a plugin's own schema refused the
    /// binding: that is a different repair, read against a different document, and the operator who has to make
    /// one of them must not be handed the other. The number is the protocol's, so this reads as the same rule
    /// the invocation states in its own vocabulary.
    /// </summary>
    public const string BindingTooLarge = "route_binding_too_large";

    /// <summary>The binding does not satisfy the schema the plugin's manifest declares for a route to it.</summary>
    public const string BindingInvalid = "route_binding_invalid";

    /// <summary>
    /// Not one of the eleven: this is the <c>Routes</c> section itself failing the validator that reads it, which
    /// happens when the file is edited into something no route could be built from. It is reported like a route
    /// problem — the same rejected reload, naming the same setting — because an operator who mistypes a route
    /// must get one answer, not a 500 that says the runtime broke rather than the edit.
    /// </summary>
    public const string SettingsInvalid = "routes_settings_invalid";

    /// <summary>
    /// Not one of the eleven either: a problem of the whole set rather than of one route. The set would take the last
    /// active route naming the plugin and account a search still followed ran under away from it, so the search's reads
    /// and its stop would have nowhere to go while it goes on spending at the provider.
    /// </summary>
    public const string OrphansSearch = "route_orphans_search";
}
