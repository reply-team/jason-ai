using System.Globalization;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Configuration;
using Jason.Contracts.Ids;
using Jason.Contracts.Operations;
using Jason.Contracts.Plugins;
using Jason.Runtime.Audiences;
using Jason.Runtime.Configuration;
using Jason.Runtime.Persistence;
using Jason.Runtime.Plugins.Registry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Jason.Runtime.Routing;

/// <summary>
/// A candidate route set and what stands in its way. The snapshot is null whenever there is a problem: a route
/// set is activated whole or not at all, exactly as a plugin set is.
/// </summary>
public sealed record RouteCandidate(RouteSnapshot? Snapshot, IReadOnlyList<ErrorDetail> Problems)
{
    /// <summary>
    /// Whether nothing stands in the way but bindings no pattern could finish measuring. Then nothing is wrong with the
    /// set — only unanswered — and whoever asked for the activation is told to ask again rather than told it was refused.
    /// </summary>
    public bool Unmeasured => Problems.Count > 0 && Problems.All(problem => problem.Code == SchemaValidator.PatternTimedOut);

    /// <summary>
    /// What the set is refused for: every problem but a binding no pattern could finish measuring. A refusal names what
    /// is wrong, and an unmeasured binding beside a real problem is not wrong — only unanswered, and asked again by the
    /// next activation once the real problem is repaired.
    /// </summary>
    public IReadOnlyList<ErrorDetail> Refusals => [.. Problems.Where(problem => problem.Code != SchemaValidator.PatternTimedOut)];

    /// <summary>
    /// How many sends the refusals hold back, across every campaign and account they name: a refusal names the sends of one
    /// campaign at one account as one problem, so the count of problems is not the count of sends.
    /// </summary>
    public int StrandedSends { get; init; }
}

/// <summary>
/// Where the global half of a candidate comes from. A reload and the load at startup read
/// <see cref="FromSettings"/>, because freezing what the settings file now says is what a reload is for;
/// <c>route.set</c> and <c>route.unset</c> read <see cref="FromActiveSnapshot"/>, because changing one campaign's
/// route must not quietly pick up a global edit nobody asked to activate.
/// </summary>
public enum GlobalRouteSource
{
    FromSettings,
    FromActiveSnapshot,
}

/// <summary>
/// Builds the route set a load would activate, checks every route in it against the plugin set that load
/// produced, and — only when nothing objected — swaps it in.
/// <para>
/// The order is the whole point: the candidate is built and validated <b>before</b> either registry is replaced,
/// so a route naming a plugin the new set does not have leaves both snapshots exactly where they were. Nothing
/// becomes active until everything that could refuse has refused.
/// </para>
/// </summary>
public sealed class RouteActivator(
    JasonDbContext db,
    IOptionsMonitor<RoutesOptions> options,
    RouteRegistry registry,
    TimeProvider clock,
    PatternTiming? timing = null,
    ILogger<RouteActivator>? logger = null)
{
    private static readonly Action<ILogger, string, string, string, Exception?> SearchUnrouted = LoggerMessage.Define<string, string, string>(
        LogLevel.Warning,
        new EventId(1, nameof(SearchUnrouted)),
        "Search {WorkItemId} of campaign {CampaignId} ran at {PluginId} under an account no route names: its reads and its stop are refused until a route names that account again. Put such a route back, then stop the search or let it end");

    private static readonly Action<ILogger, string, string, string, Exception?> SendUnrouted = LoggerMessage.Define<string, string, string>(
        LogLevel.Warning,
        new EventId(2, nameof(SendUnrouted)),
        "Send {WorkItemId} of campaign {CampaignId} ran at {PluginId} under an account no route names, and the provider still holds it for a later moment: its cancel and its status read are refused until a route names that account again. Put such a route back, then cancel it or let it go");

    /// <summary>How a binding is matched against its plugin's patterns: the runtime's one default, which a test replaces.</summary>
    private readonly PatternTiming _timing = timing ?? PatternTiming.Default;

    /// <summary>The route an operator edits under <c>Routes:Default</c>, named the way they wrote it.</summary>
    public const string GlobalDefaultField = RoutesOptions.Section + ":Default";

    /// <summary>What a campaign route with no operation is called in a problem: the campaign's default.</summary>
    public const string CampaignDefaultName = "default";

    /// <summary>
    /// The candidate set for this plugin snapshot: the global half from wherever <paramref name="source"/> says,
    /// the campaign half from the rows of every campaign that is not archived. Problems are collected rather than
    /// thrown, because the caller — a reload — has to keep the previous snapshots and answer with all of them.
    /// </summary>
    public async Task<RouteCandidate> BuildAsync(PluginSnapshot snapshot, GlobalRouteSource source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var problems = new List<ErrorDetail>();
        var global = source == GlobalRouteSource.FromSettings
            ? FromSettings(snapshot, problems)
            : Rechecked(registry.Snapshot.Global, snapshot, _timing, problems);

        var campaigns = await CampaignsAsync(snapshot, problems, cancellationToken).ConfigureAwait(false);
        if (problems.Count > 0)
        {
            return new RouteCandidate(null, problems);
        }

        var built = new RouteSnapshot(PublicId.New(RouteSnapshot.IdPrefix), clock.GetUtcNow(), snapshot.Id, global, campaigns);
        await KeepSearchesAsync(built, problems, cancellationToken).ConfigureAwait(false);
        var stranded = await KeepSendsAsync(built, problems, cancellationToken).ConfigureAwait(false);
        return problems.Count > 0 ? new RouteCandidate(null, problems) { StrandedSends = stranded } : new RouteCandidate(built, []);
    }

    /// <summary>
    /// A route is where a binding is kept, and a search is read and stopped only at the plugin and account it ran under
    /// (<see cref="RouteResolver.ForSearch"/>). So a search still followed — running, held at the provider, or of an
    /// archived campaign whose stop has not ended it yet — that the routes active now reach and <paramref name="candidate"/>
    /// would not is a problem of the set: its reads and its stop would have nowhere to go while it goes on at the
    /// provider. A search the active routes do not reach either is not this change's doing — the start, which has no
    /// active routes, or an edit made while the runtime was stopped — so it refuses nothing, and is said in the log at
    /// every build while it lasts.
    /// </summary>
    private async Task KeepSearchesAsync(RouteSnapshot candidate, List<ErrorDetail> problems, CancellationToken cancellationToken)
    {
        var active = registry.Snapshot;
        var searches = await AudienceSearches.Followed(db).ToListAsync(cancellationToken).ConfigureAwait(false);

        foreach (var search in searches)
        {
            var plugin = search.Ran?.PluginId ?? search.PluginId;
            var identity = search.Ran?.BindingIdentity;
            if (Reaches(candidate, search.Campaign, plugin, identity))
            {
                continue;
            }

            if (!Reaches(active, search.Campaign, plugin, identity))
            {
                if (logger is not null)
                {
                    SearchUnrouted(logger, search.WorkItem, search.Campaign, plugin, null);
                }

                continue;
            }

            problems.Add(new ErrorDetail(SearchField(search.Campaign, search.WorkItem), RouteProblemCodes.OrphansSearch, Stranded(search, plugin)));
        }
    }

    /// <summary>
    /// What a search a change would leave without its account is told as, and the way out its campaign's state leaves: a
    /// paused campaign's searches are not read, so one ends only once the campaign runs again, and an archived campaign's
    /// is ended only by a stop, the one the archive made or, where that one failed and is not made again, one a person
    /// makes.
    /// </summary>
    private static string Stranded(FollowedSearch search, string plugin)
    {
        var account = $"which ran at '{plugin}' under an account neither the campaign's other routes nor the global ones would name";
        return search.Status switch
        {
            CampaignStatus.Archived =>
                $"campaign '{search.Campaign}' is archived, and the stop it made of search {search.WorkItem}, {account}, has not ended the search yet, so a stop would have nowhere to go while the search goes on at the provider. Let that stop end it or, if it failed, stop the search with audience.search_cancel; then change the route.",
            CampaignStatus.Paused =>
                $"campaign '{search.Campaign}' is paused and still follows search {search.WorkItem}, {account}, so its reads and its stop would have nowhere to go while it goes on at the provider. Stop it with audience.search_cancel, or start the campaign and let the search end, then change the route.",
            _ =>
                $"campaign '{search.Campaign}' still follows search {search.WorkItem}, {account}, so its reads and its stop would have nowhere to go while it goes on at the provider. Stop it with audience.search_cancel, or let it end, then change the route.",
        };
    }

    /// <summary>Whether a read and a stop of the search would both find the very plugin and binding it ran under.</summary>
    private static bool Reaches(RouteSnapshot routes, string campaign, string plugin, string? identity) =>
        new[] { SearchLanding.Status, SearchLanding.Stop }.All(operation =>
            RouteResolver.ForSearch(routes, campaign, operation, SearchLanding.Search, plugin, identity) is { } found
            && string.Equals(found.Route.PluginId, plugin, StringComparison.Ordinal)
            && string.Equals(found.Route.BindingIdentity, identity, StringComparison.Ordinal));

    /// <summary>
    /// The same rule for a send (<see cref="RouteResolver.ForSend"/>): a cancel and a status read of a send go only to the
    /// plugin and account it ran under. So a send the provider still holds for a moment to come, with no succeeded cancel
    /// (<see cref="HeldSends"/>), that the routes active now reach and <paramref name="candidate"/> would not is a problem
    /// of the set: it would go out at its moment with nowhere to cancel it. The sends of one campaign held at one plugin
    /// and account are one problem, named by the latest of them, saying how many there are, the latest moment among them
    /// and the two ways out; it names them by their work items and says nothing a send says to a person. Answers how
    /// many sends the problems hold back.
    /// <para>
    /// A send the active routes do not reach either is not this change's doing, and refuses nothing. After the start it
    /// cannot be new either: every door a change comes through afterwards refuses one that would strand a send, so a send
    /// no route reaches was left so by the start, which has no routes in force to keep, or by settings edited while the
    /// runtime was stopped. So the start is where it is said, once, in the log, by its ids alone. The read is made under
    /// the writer's lock at <c>route.set</c> and <c>route.unset</c>, so it is skipped where it can find nothing to refuse
    /// or to say: after the start, while the routes in force name nothing.
    /// </para>
    /// </summary>
    private async Task<int> KeepSendsAsync(RouteSnapshot candidate, List<ErrorDetail> problems, CancellationToken cancellationToken)
    {
        var active = registry.Snapshot;
        var atStart = !registry.Activated;
        if (!atStart && !HoldsAnyRoute(active))
        {
            return 0;
        }

        var sends = await HeldSends.ReadAsync(db, clock.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        var unreached = sends.Where(send => !Reaches(candidate, send)).ToList();
        if (atStart)
        {
            if (logger is not null)
            {
                foreach (var send in unreached)
                {
                    SendUnrouted(logger, send.WorkItem, send.Campaign, send.PluginId, null);
                }
            }

            return 0;
        }

        var stranded = unreached
            .Where(send => Reaches(active, send))
            .GroupBy(send => (send.Campaign, send.PluginId, send.BindingIdentity))
            .ToList();

        foreach (var held in stranded)
        {
            var ordered = held.OrderBy(send => send.Moment).ThenBy(send => send.WorkItem, StringComparer.Ordinal).ToList();
            var latest = ordered[^1];
            var until = SecondAfter(latest.Moment).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
            var named = ordered.Count <= ListedSends
                ? string.Join(", ", ordered.Select(send => send.WorkItem))
                : string.Join(", ", ordered.TakeLast(ListedSends).Select(send => send.WorkItem)) + string.Create(CultureInfo.InvariantCulture, $" and {ordered.Count - ListedSends} more");
            problems.Add(new ErrorDetail(
                SendField(held.Key.Campaign, latest.WorkItem),
                RouteProblemCodes.OrphansSend,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"campaign '{held.Key.Campaign}' holds {ordered.Count} send(s) ({named}) that ran at '{held.Key.PluginId}' under an account neither the campaign's other routes nor the global ones would name, and the provider holds the latest of them until {until}, so their cancels and status reads would have nowhere to go before they go out. Cancel them with {HeldSends.Cancel}, or change the route after {until}.")));
        }

        return stranded.Sum(held => held.Count());
    }

    /// <summary>
    /// The moment rounded up to its second, in UTC: what a refusal names as the time after which the change goes through,
    /// so a person who asks again at that second is not refused again by the fraction the second did not show.
    /// </summary>
    private static DateTime SecondAfter(DateTimeOffset moment)
    {
        var utc = moment.UtcDateTime;
        var fraction = utc.Ticks % TimeSpan.TicksPerSecond;
        return fraction == 0 ? utc : utc.AddTicks(TimeSpan.TicksPerSecond - fraction);
    }

    /// <summary>Whether <paramref name="routes"/> names any plugin at all, globally or for a campaign.</summary>
    private static bool HoldsAnyRoute(RouteSnapshot routes) =>
        Holds(routes.Global) || routes.Campaigns.Values.Any(Holds);

    private static bool Holds(RouteSet set) => set.Default is not null || set.Operations.Count > 0 || set.ChannelRoutes.Any();

    /// <summary>How many of the stranded sends a problem names by their work items: the latest ones, the rest counted.</summary>
    private const int ListedSends = 5;

    /// <summary>
    /// Whether a cancel and a status read of the send would both find the very plugin and binding it ran under. The send's
    /// own channel is not asked for: it only orders the walk, and whether any route names the account does not depend on
    /// which is asked first.
    /// </summary>
    private static bool Reaches(RouteSnapshot routes, HeldSend send) =>
        new[] { HeldSends.Cancel, HeldSends.Status }.All(operation =>
            RouteResolver.ForSend(routes, send.Campaign, operation, send.Operation, sendChannel: null, send.PluginId, send.BindingIdentity) is { } found
            && string.Equals(found.Route.PluginId, send.PluginId, StringComparison.Ordinal)
            && string.Equals(found.Route.BindingIdentity, send.BindingIdentity, StringComparison.Ordinal));

    /// <summary>
    /// Makes the candidate the active route snapshot. Called under the reload gate, after the plugin registry it
    /// was validated against has been swapped — never before anything that could still refuse.
    /// </summary>
    public void Activate(RouteSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        registry.Replace(snapshot);
    }

    /// <summary>
    /// What is wrong with one route, or null when nothing is. A pure function over the candidate plugin set and
    /// the catalog: no options, no database, no clock, so the caller can decide what a problem is called and
    /// where it is, and a rule can be tested without a running runtime.
    /// <para>
    /// Only the first failing check is answered. The checks after the first one read what it established — there
    /// is no operation to support without a plugin to support it — and an operator fixing a route fixes it once.
    /// </para>
    /// <para>
    /// The questions are <see cref="RouteChecks"/>, which the claim asks again of the route an item resolved to;
    /// what belongs here is the order they are asked in and the words an operator is answered with.
    /// </para>
    /// </summary>
    public static RouteProblem? Check(PluginSnapshot plugins, string? operation, string? pluginId, JsonObject? binding) =>
        Check(plugins, operation, pluginId, binding, PatternTiming.Default);

    /// <summary>The same check, with the binding's patterns matched under <paramref name="timing"/>.</summary>
    public static RouteProblem? Check(PluginSnapshot plugins, string? operation, string? pluginId, JsonObject? binding, PatternTiming timing) =>
        Check(plugins, operation, channel: null, pluginId, binding, timing, out _);

    /// <summary>
    /// The same check of a route that names <paramref name="channel"/> under <paramref name="operation"/>: the same
    /// questions in the same order, and the two about the channel asked as soon as the operation is found in the catalog
    /// (<see cref="ChannelProblem"/>). Null is the operation's own route, or the default, exactly as above. The campaign
    /// half and the settings half of a reload, and <c>route.set</c>, all ask this one.
    /// </summary>
    public static RouteProblem? Check(PluginSnapshot plugins, string? operation, string? channel, string? pluginId, JsonObject? binding, PatternTiming timing) =>
        Check(plugins, operation, channel, pluginId, binding, timing, out _);

    /// <summary>
    /// The same check, also answering what the binding is called — the identity the route will be frozen with.
    /// It falls out of the size check, which has to write the canonical form anyway, so a route is serialised
    /// once rather than once to bound it and again to name it.
    /// </summary>
    private static RouteProblem? Check(
        PluginSnapshot plugins,
        string? operation,
        string? channel,
        string? pluginId,
        JsonObject? binding,
        PatternTiming timing,
        out string? bindingIdentity)
    {
        ArgumentNullException.ThrowIfNull(plugins);
        ArgumentNullException.ThrowIfNull(timing);
        bindingIdentity = null;

        if (RouteChecks.Loaded(plugins, pluginId) is not { } plugin)
        {
            return new RouteProblem(
                RouteProblemCodes.PluginUnknown,
                $"no plugin with id '{pluginId}' is in the candidate set, so nothing would perform this work.");
        }

        if (RouteChecks.KindNotInvocable(plugin))
        {
            return new RouteProblem(
                RouteProblemCodes.PluginKindNotInvocable,
                $"'{plugin.Manifest.Id}' is a notification plugin, and a notification plugin performs no canonical operation.");
        }

        var incompatible = operation is null
            ? channel is null ? AnyOperationIncompatible(plugin) : DefaultChannelProblem(channel)
            : OperationProblem(plugin, operation, channel);
        if (incompatible is not null)
        {
            return incompatible;
        }

        if (binding is not null && FirstSecretLikeName(binding) is { } named)
        {
            // Before the schema check on purpose. A plugin's schema will usually refuse an unexpected field too,
            // and "additionalProperties" is not the sentence whoever pasted a credential here needs to read.
            return new RouteProblem(
                RouteProblemCodes.BindingSecretLike,
                $"a binding must not carry '{named}'. A binding selects an identity the plugin's own credential store already holds; it is journaled and listed, so it never carries the credential itself.");
        }

        if (BindingIdentity.Measure(binding) is { } measured)
        {
            // Bounded where it is written, not only where it is used. The invocation refuses an oversized
            // binding too, but by then the route has been journaled into an append-only table, listed and
            // answered as usable, and every item through it dies with nothing naming the route that carried it.
            if (measured.Bytes > PluginProtocol.MaxBindingBytes)
            {
                return new RouteProblem(
                    RouteProblemCodes.BindingTooLarge,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"a binding is at most {PluginProtocol.MaxBindingBytes} bytes of canonical JSON, and this one is {measured.Bytes}. It is refused where it is written, because a route is journaled, listed and resolved long before anything tries to carry it to a plugin."));
            }

            bindingIdentity = measured.Identity;
        }

        // A plugin that loaded unavailable because its binding probe never finished at the start has a binding schema no
        // binding can be measured against on this machine now; the binding is frozen as written and left unmeasured. Nothing
        // acts on it: every claim asks availability at check 4, before it measures a binding at check 7, and the next load
        // measures it as it measures everything. Only this plugin's routes, and only the measurement: what is checked above
        // still refuses. A route to any other unavailable plugin is measured, because its patterns can answer.
        if (ProbeTimedOut(plugin))
        {
            return null;
        }

        var failures = RouteChecks.BindingProblems(plugin, binding, timing);
        if (SchemaValidator.Verdicts(failures) is { Count: > 0 } wrong)
        {
            return new RouteProblem(
                RouteProblemCodes.BindingInvalid,
                binding is null
                    ? $"the route carries no binding, and '{plugin.Manifest.Id}' declares that a route to it must carry one: {Where(wrong[0].Pointer)}{wrong[0].Message}"
                    : $"the binding does not satisfy what '{plugin.Manifest.Id}' declares a route to it must carry: {Where(wrong[0].Pointer)}{wrong[0].Message}");
        }

        // Not one of the route problems: a pattern that did not finish says nothing about the binding, so the answer
        // carries the dialect's own word for it, and whoever asked for the activation is told to ask again.
        return SchemaValidator.Unmeasured(failures)
            ? new RouteProblem(
                SchemaValidator.PatternTimedOut,
                $"the binding could not be measured against what '{plugin.Manifest.Id}' declares: {Where(failures[0].Pointer)}{failures[0].Message}")
            : null;
    }

    /// <summary>How a campaign's route is named in a problem, so the operator can find the row they wrote.</summary>
    public static string CampaignField(string campaignId, string? operation) =>
        $"campaign:{campaignId}/routes/{operation ?? CampaignDefaultName}";

    /// <summary>How a campaign's route is named in a problem when it may name a channel: the channel after its operation.</summary>
    public static string CampaignField(string campaignId, string? operation, string? channel) =>
        channel is null ? CampaignField(campaignId, operation) : $"{CampaignField(campaignId, operation)}/{channel}";

    /// <summary>How a global operation override is named in a problem: the setting, exactly as it is spelled.</summary>
    public static string GlobalOperationField(string operation) => $"{RoutesOptions.Section}:Operations:{operation}";

    /// <summary>How a global channel route is named in a problem: the setting under its operation, exactly as it is spelled.</summary>
    public static string GlobalChannelField(string operation, string channel) => $"{GlobalOperationField(operation)}:Channels:{channel}";

    /// <summary>How a search a change would leave without a route is named in a problem: its campaign and its work item.</summary>
    public static string SearchField(string campaignId, string searchWorkItem) => $"campaign:{campaignId}/searches/{searchWorkItem}";

    /// <summary>How a send a change would leave without a route is named in a problem: its campaign and its work item.</summary>
    public static string SendField(string campaignId, string sendWorkItem) => $"campaign:{campaignId}/sends/{sendWorkItem}";

    private RouteSet FromSettings(PluginSnapshot plugins, List<ErrorDetail> problems)
    {
        RoutesOptions settings;
        try
        {
            settings = options.CurrentValue;
        }
        catch (OptionsValidationException refused)
        {
            // The section was edited into something the validator will not hand over at all, which is a mistyped
            // route like any other: the same rejected reload, naming the same setting. What it must never be is a
            // 500, which says the runtime broke rather than the edit.
            foreach (var failure in refused.Failures)
            {
                problems.Add(new ErrorDetail(Setting(failure), RouteProblemCodes.SettingsInvalid, failure));
            }

            return RouteSet.Empty;
        }

        foreach (var path in settings.MisplacedChannels)
        {
            problems.Add(MisplacedChannelProblem(path));
        }

        var operations = new Dictionary<string, Route>(StringComparer.Ordinal);
        var channels = new Dictionary<string, Dictionary<string, Route>>(StringComparer.Ordinal);
        foreach (var (operation, entry) in settings.Operations)
        {
            // An operation routed by its channels alone names no plugin of its own, and has no route of its own.
            if (entry is null || entry.Plugin is not null || entry.Channels.Count == 0)
            {
                var route = Accept(plugins, GlobalOperationField(operation), operation, channel: null, entry?.Plugin, entry?.Binding as JsonObject, _timing, problems);
                if (route is not null)
                {
                    operations[operation] = route;
                }
            }

            foreach (var (channel, channelEntry) in entry?.Channels ?? [])
            {
                var route = Accept(plugins, GlobalChannelField(operation, channel), operation, channel, channelEntry?.Plugin, channelEntry?.Binding as JsonObject, _timing, problems);
                if (route is not null)
                {
                    Under(channels, operation)[channel] = route;
                }
            }
        }

        var fallback = settings.Default is null
            ? null
            : Accept(plugins, GlobalDefaultField, null, channel: null, settings.Default.Plugin, settings.Default.Binding as JsonObject, _timing, problems);

        return new RouteSet(fallback, operations, Frozen(channels));
    }

    /// <summary>
    /// A channel route written where no operation stands above it, under <c>Routes:Default</c> or at the top of the
    /// section: refused as a channel on no operation, whatever plugin it names, rather than read as nothing while the
    /// default's plugin takes every send on that channel. The path is shown the way a refused setting's is, so a key that
    /// is no channel is written as a placeholder.
    /// </summary>
    private static ErrorDetail MisplacedChannelProblem(string path)
    {
        var field = SettingKey.ShownPath(path, NoInstalledPlugins);
        var channel = field[(field.LastIndexOf(':') + 1)..];
        var problem = path.StartsWith(GlobalDefaultField + ":", StringComparison.Ordinal)
            ? path.EndsWith(":" + nameof(OperationRouteEntry.Channels), StringComparison.Ordinal)
                ? DefaultChannelsProblem
                : DefaultChannelProblem(channel)
            : SectionChannelsProblem;
        return new ErrorDetail(field, problem.Code, problem.Message);
    }

    /// <summary>A map of channels under the default, written as text: no channel to name, and still no operation to stand under.</summary>
    private static readonly RouteProblem DefaultChannelsProblem = new(
        RouteProblemCodes.ChannelNotRoutable,
        "a default route names no operation, so it takes no Channels: a channel route is a route of one operation, written under Routes:Operations:<operation>:Channels:<channel>.");

    /// <summary>Channels at the top of the section, beside the default and the operations rather than under an operation.</summary>
    private static readonly RouteProblem SectionChannelsProblem = new(
        RouteProblemCodes.ChannelNotRoutable,
        "Channels at the top of Routes names no operation: a channel route is a route of one operation, written under Routes:Operations:<operation>:Channels:<channel>.");

    /// <summary>No plugin is known to be installed where a misplaced channel's path is shown: a channel is shown, and nothing else that is not a settings name.</summary>
    private static readonly IReadOnlySet<string> NoInstalledPlugins = new HashSet<string>();

    /// <summary>The channel routes of one operation while a set is being built, made the first time one is added.</summary>
    private static Dictionary<string, Route> Under(Dictionary<string, Dictionary<string, Route>> channels, string operation)
    {
        if (!channels.TryGetValue(operation, out var routes))
        {
            channels[operation] = routes = new Dictionary<string, Route>(StringComparer.Ordinal);
        }

        return routes;
    }

    /// <summary>The channel routes as a set holds them: none at all where none was built, the set made before channel routes existed.</summary>
    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, Route>>? Frozen(Dictionary<string, Dictionary<string, Route>> channels) =>
        channels.Count == 0
            ? null
            : channels.ToDictionary(entry => entry.Key, entry => (IReadOnlyDictionary<string, Route>)entry.Value, StringComparer.Ordinal);

    /// <summary>
    /// The global half of the snapshot that is already active, checked again against the new plugin set. It was
    /// valid against the set it was frozen with, and <c>route.set</c> runs against that same set — but checking
    /// is cheap and the alternative is a rule that holds only because of where it is called from. The channel routes
    /// are part of the global half: a campaign's route write that copied the rest and left them out would take every
    /// global channel route away without anybody asking.
    /// </summary>
    private static RouteSet Rechecked(RouteSet active, PluginSnapshot plugins, PatternTiming timing, List<ErrorDetail> problems)
    {
        var operations = new Dictionary<string, Route>(StringComparer.Ordinal);
        foreach (var (operation, route) in active.Operations)
        {
            var rechecked = Accept(plugins, GlobalOperationField(operation), operation, channel: null, route.PluginId, route.Binding, timing, problems);
            if (rechecked is not null)
            {
                operations[operation] = rechecked;
            }
        }

        var channels = new Dictionary<string, Dictionary<string, Route>>(StringComparer.Ordinal);
        foreach (var (operation, channel, route) in active.ChannelRoutes)
        {
            var rechecked = Accept(plugins, GlobalChannelField(operation, channel), operation, channel, route.PluginId, route.Binding, timing, problems);
            if (rechecked is not null)
            {
                Under(channels, operation)[channel] = rechecked;
            }
        }

        var fallback = active.Default is null
            ? null
            : Accept(plugins, GlobalDefaultField, null, channel: null, active.Default.PluginId, active.Default.Binding, timing, problems);

        return new RouteSet(fallback, operations, Frozen(channels));
    }

    private async Task<IReadOnlyDictionary<string, RouteSet>> CampaignsAsync(
        PluginSnapshot plugins,
        List<ErrorDetail> problems,
        CancellationToken cancellationToken)
    {
        // An archived campaign's routes are history, and history must never stand between an operator and an
        // uninstall: the campaign will never dispatch again, so what its route named no longer has to exist. Until a search
        // of it ends, though, its stop still runs and goes where the search ran, so its routes stay in the set while one is
        // followed — each one that no longer holds left out, and refusing nothing. Channel routes are a table of their own
        // and are read by the same rule into the same set.
        var operationRows = await db.CampaignRoutes
            .AsNoTracking()
            .Where(route => route.Campaign!.ArchivedAt == null
                || db.AudienceSearches.Any(search => search.CampaignId == route.CampaignId && search.EndedAs == null && search.Reference != null))
            .Select(route => new StoredRoute(
                route.CampaignId,
                route.Campaign!.PublicId,
                route.Campaign.ArchivedAt != null,
                route.Operation,
                null,
                route.PluginId,
                route.Binding))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var channelRows = await db.CampaignChannelRoutes
            .AsNoTracking()
            .Where(route => route.Campaign!.ArchivedAt == null
                || db.AudienceSearches.Any(search => search.CampaignId == route.CampaignId && search.EndedAs == null && search.Reference != null))
            .Select(route => new StoredRoute(
                route.CampaignId,
                route.Campaign!.PublicId,
                route.Campaign.ArchivedAt != null,
                route.Operation,
                route.Channel,
                route.PluginId,
                route.Binding))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var rows = operationRows
            .Concat(channelRows)
            .OrderBy(route => route.CampaignId)
            .ThenBy(route => route.Operation, StringComparer.Ordinal)
            .ThenBy(route => route.Channel, StringComparer.Ordinal);

        var campaigns = new Dictionary<string, CampaignRoutes>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var route = Accept(
                plugins,
                CampaignField(row.Campaign, row.Operation, row.Channel),
                row.Operation,
                row.Channel,
                row.PluginId,
                row.Binding,
                _timing,
                row.Archived ? [] : problems);
            if (route is null)
            {
                continue;
            }

            if (!campaigns.TryGetValue(row.Campaign, out var set))
            {
                campaigns[row.Campaign] = set = new CampaignRoutes();
            }

            if (row.Operation is null)
            {
                set.Default = route;
            }
            else if (row.Channel is null)
            {
                set.Operations[row.Operation] = route;
            }
            else
            {
                if (!set.Channels.TryGetValue(row.Operation, out var channels))
                {
                    set.Channels[row.Operation] = channels = new Dictionary<string, Route>(StringComparer.Ordinal);
                }

                channels[row.Channel] = route;
            }
        }

        return campaigns.ToDictionary(entry => entry.Key, entry => entry.Value.Frozen(), StringComparer.Ordinal);
    }

    /// <summary>One stored campaign route as the reload reads it, from either table: a channel only from the channel table.</summary>
    private sealed record StoredRoute(int CampaignId, string Campaign, bool Archived, string? Operation, string? Channel, string PluginId, JsonObject? Binding);

    /// <summary>
    /// One campaign's routes while the rows are still being read: a default, the overrides of it, and the channel routes
    /// that override an operation's own route for one channel. A campaign without channel routes freezes into a set that
    /// holds none, the set it froze into before channel routes existed.
    /// </summary>
    private sealed class CampaignRoutes
    {
        public Route? Default { get; set; }

        public Dictionary<string, Route> Operations { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, Dictionary<string, Route>> Channels { get; } = new(StringComparer.Ordinal);

        public RouteSet Frozen() => new(
            Default,
            Operations,
            Channels.Count == 0
                ? null
                : Channels.ToDictionary(
                    entry => entry.Key,
                    entry => (IReadOnlyDictionary<string, Route>)entry.Value,
                    StringComparer.Ordinal));
    }

    /// <summary>The route as the snapshot will hold it, or nothing — with the problem recorded against its name.</summary>
    private static Route? Accept(
        PluginSnapshot plugins,
        string field,
        string? operation,
        string? channel,
        string? pluginId,
        JsonObject? binding,
        PatternTiming timing,
        List<ErrorDetail> problems)
    {
        if (Check(plugins, operation, channel, pluginId, binding, timing, out var identity) is { } problem)
        {
            problems.Add(new ErrorDetail(field, problem.Code, problem.Message));
            return null;
        }

        // A copy, because the snapshot outlives the settings instance and the database row it was read from, and
        // a JsonNode belongs to exactly one parent. The identity is the one the check already measured: a clone
        // has the same canonical form, so writing it out a second time would only be a chance to disagree.
        var owned = binding?.DeepClone().AsObject();
        return new Route(pluginId!, owned, identity);
    }

    private static RouteProblem? OperationProblem(LoadedPlugin plugin, string operation, string? channel)
    {
        if (OperationCatalog.Find(operation) is not { } contract)
        {
            return new RouteProblem(
                RouteProblemCodes.OperationUnknown,
                $"'{operation}' is not an operation this build publishes.");
        }

        if (channel is not null && ChannelProblem(contract, channel) is { } unroutable)
        {
            return unroutable;
        }

        if (RouteChecks.OperationUnsupported(plugin, operation))
        {
            return new RouteProblem(
                RouteProblemCodes.OperationUnsupported,
                $"'{plugin.Manifest.Id}' does not implement '{operation}'; work is never quietly handed to a plugin that did not claim it.");
        }

        if (Incompatible(plugin, contract) is { } incompatible)
        {
            return incompatible;
        }

        // Every operation that takes channel routes is about what is sent on the channel — message.send and
        // conversation.reply send it, sender.list names the accounts that will — and what is sent on a channel route goes
        // out beside the provider's own campaigns, so a reply to it reaches the campaign only through the events of the
        // account it went from. A plugin that reads none would leave that reply unseen while the campaign went on writing
        // to a person who answered. Asked last of the operation's questions: a plugin that does not perform the operation
        // at all is told that first.
        return channel is not null && !plugin.Manifest.ReadsEvents
            ? new RouteProblem(
                RouteProblemCodes.ChannelPluginReadsNoEvents,
                $"'{plugin.Manifest.Id}' reads no events, and a channel route needs a plugin that does: a reply to a send on the channel '{channel}' reaches the campaign only through the events of the account the send went from. Route the channel to a plugin whose manifest declares contracts.events, or route the operation without a channel.")
            : null;
    }

    /// <summary>
    /// What is wrong with naming <paramref name="channel"/> under the operation <paramref name="contract"/> publishes, or
    /// null when nothing is: the operation's document must name an argument a route reads its channel from, and the
    /// channel must be one that argument allows. Both are questions about the document alone, the same for every plugin,
    /// so they are asked before anything about the plugin the route names.
    /// </summary>
    private static RouteProblem? ChannelProblem(OperationContract contract, string channel)
    {
        if (contract.RouteChannels is not { } channels)
        {
            return new RouteProblem(
                RouteProblemCodes.ChannelNotRoutable,
                $"'{contract.Id}' takes no channel routes: its document names no argument a route reads its channel from, so every call of it goes by the operation's own route.");
        }

        return channels.Contains(channel, StringComparer.Ordinal)
            ? null
            : new RouteProblem(
                RouteProblemCodes.ChannelUnknown,
                $"'{channel}' is not a channel '{contract.Id}' routes by; its argument '{contract.RouteChannelArgument}' allows {string.Join(", ", channels.Select(allowed => $"'{allowed}'"))}.");
    }

    /// <summary>
    /// The same two questions about the document, for a caller that names a route without a plugin — <c>route.unset</c> —
    /// so it refuses a channel no route could name exactly as <c>route.set</c> does. An operation the catalog does not
    /// publish is the caller's to have refused first; here it is answered as such.
    /// </summary>
    internal static RouteProblem? ChannelProblem(string operation, string channel) =>
        OperationCatalog.Find(operation) is { } contract
            ? ChannelProblem(contract, channel)
            : new RouteProblem(RouteProblemCodes.OperationUnknown, $"'{operation}' is not an operation this build publishes.");

    /// <summary>A default route that names a channel: the default names no operation, so there is no argument to read one from.</summary>
    private static RouteProblem DefaultChannelProblem(string channel) =>
        new(
            RouteProblemCodes.ChannelNotRoutable,
            $"a default route names no operation, so it cannot name the channel '{channel}': a channel route is a route of one operation.");

    /// <summary>
    /// A default route carries every operation, so what it must be compatible with is everything the plugin
    /// itself claims to implement: the first of those the plugin could not be handed is the problem.
    /// </summary>
    private static RouteProblem? AnyOperationIncompatible(LoadedPlugin plugin)
    {
        foreach (var operation in plugin.Manifest.Operations)
        {
            if (OperationCatalog.Find(operation) is { } contract && Incompatible(plugin, contract) is { } problem)
            {
                return problem;
            }
        }

        return null;
    }

    private static RouteProblem? Incompatible(LoadedPlugin plugin, OperationContract contract) =>
        RouteChecks.ContractIncompatible(plugin, contract)
            ? new RouteProblem(
                RouteProblemCodes.ContractIncompatible,
                $"'{plugin.Manifest.Id}' speaks operation contract {string.Join(", ", plugin.Manifest.Contracts.Operations)}, and '{contract.Id}' is published at version {contract.Version}.")
            : null;

    /// <summary>
    /// The first field anywhere in the binding whose name reads as a credential. The walk carries its own stack
    /// rather than the thread's, so a binding built in memory — the only kind that can be deeper than a parser
    /// allows — is answered rather than thrown over.
    /// </summary>
    private static string? FirstSecretLikeName(JsonNode binding)
    {
        var work = new Stack<JsonNode?>();
        work.Push(binding);

        while (work.Count > 0)
        {
            switch (work.Pop())
            {
                case JsonObject members:
                    foreach (var (name, value) in members)
                    {
                        if (SecretLikeNames.Matches(name))
                        {
                            return name;
                        }

                        work.Push(value);
                    }

                    break;

                case JsonArray entries:
                    foreach (var entry in entries)
                    {
                        work.Push(entry);
                    }

                    break;

                default:
                    break;
            }
        }

        return null;
    }

    /// <summary>The setting an options failure is about: every message in the house style begins with its name.</summary>
    private static string Setting(string failure) => SettingsFailure.Name(RoutesOptions.Section, failure);

    private static string Where(string pointer) => pointer.Length == 0 ? string.Empty : pointer + ": ";

    /// <summary>
    /// Whether the plugin loaded unavailable because a binding probe did not finish at the start. The plugin's own problem
    /// is the mark, and the snapshot that carries it is immutable, so a route and the reason its binding went unmeasured
    /// cannot come apart.
    /// </summary>
    private static bool ProbeTimedOut(LoadedPlugin plugin) =>
        plugin.Status == PluginStatus.Unavailable
        && plugin.Problems.Any(problem => problem.Code == Plugins.Manifest.ProblemCodes.ProbeTimedOut);
}

/// <summary>Why one route cannot be used, in the vocabulary of <see cref="RouteProblemCodes"/>.</summary>
public sealed record RouteProblem(string Code, string Message);
