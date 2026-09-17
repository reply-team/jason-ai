using System.Globalization;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Ids;
using Jason.Contracts.Operations;
using Jason.Contracts.Plugins;
using Jason.Runtime.Configuration;
using Jason.Runtime.Persistence;
using Jason.Runtime.Plugins.Registry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Jason.Runtime.Routing;

/// <summary>
/// A candidate route set and what stands in its way. The snapshot is null whenever there is a problem: a route
/// set is activated whole or not at all, exactly as a plugin set is.
/// </summary>
public sealed record RouteCandidate(RouteSnapshot? Snapshot, IReadOnlyList<ErrorDetail> Problems);

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
    TimeProvider clock)
{
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
            : Rechecked(registry.Snapshot.Global, snapshot, problems);

        var campaigns = await CampaignsAsync(snapshot, problems, cancellationToken).ConfigureAwait(false);

        return problems.Count > 0
            ? new RouteCandidate(null, problems)
            : new RouteCandidate(
                new RouteSnapshot(PublicId.New(RouteSnapshot.IdPrefix), clock.GetUtcNow(), snapshot.Id, global, campaigns),
                []);
    }

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
        Check(plugins, operation, pluginId, binding, out _);

    /// <summary>
    /// The same check, also answering what the binding is called — the identity the route will be frozen with.
    /// It falls out of the size check, which has to write the canonical form anyway, so a route is serialised
    /// once rather than once to bound it and again to name it.
    /// </summary>
    private static RouteProblem? Check(
        PluginSnapshot plugins,
        string? operation,
        string? pluginId,
        JsonObject? binding,
        out string? bindingIdentity)
    {
        ArgumentNullException.ThrowIfNull(plugins);
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

        var incompatible = operation is null ? AnyOperationIncompatible(plugin) : OperationProblem(plugin, operation);
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

        return RouteChecks.BindingProblems(plugin, binding) is { Count: > 0 } failures
            ? new RouteProblem(
                RouteProblemCodes.BindingInvalid,
                $"the binding does not satisfy what '{plugin.Manifest.Id}' declares a route to it must carry: {Where(failures[0].Pointer)}{failures[0].Message}")
            : null;
    }

    /// <summary>How a campaign's route is named in a problem, so the operator can find the row they wrote.</summary>
    public static string CampaignField(string campaignId, string? operation) =>
        $"campaign:{campaignId}/routes/{operation ?? CampaignDefaultName}";

    /// <summary>How a global operation override is named in a problem: the setting, exactly as it is spelled.</summary>
    public static string GlobalOperationField(string operation) => $"{RoutesOptions.Section}:Operations:{operation}";

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

        var operations = new Dictionary<string, Route>(StringComparer.Ordinal);
        foreach (var (operation, entry) in settings.Operations)
        {
            var route = Accept(plugins, GlobalOperationField(operation), operation, entry?.Plugin, entry?.Binding as JsonObject, problems);
            if (route is not null)
            {
                operations[operation] = route;
            }
        }

        var fallback = settings.Default is null
            ? null
            : Accept(plugins, GlobalDefaultField, null, settings.Default.Plugin, settings.Default.Binding as JsonObject, problems);

        return new RouteSet(fallback, operations);
    }

    /// <summary>
    /// The global half of the snapshot that is already active, checked again against the new plugin set. It was
    /// valid against the set it was frozen with, and <c>route.set</c> runs against that same set — but checking
    /// is cheap and the alternative is a rule that holds only because of where it is called from.
    /// </summary>
    private static RouteSet Rechecked(RouteSet active, PluginSnapshot plugins, List<ErrorDetail> problems)
    {
        var operations = new Dictionary<string, Route>(StringComparer.Ordinal);
        foreach (var (operation, route) in active.Operations)
        {
            var rechecked = Accept(plugins, GlobalOperationField(operation), operation, route.PluginId, route.Binding, problems);
            if (rechecked is not null)
            {
                operations[operation] = rechecked;
            }
        }

        var fallback = active.Default is null
            ? null
            : Accept(plugins, GlobalDefaultField, null, active.Default.PluginId, active.Default.Binding, problems);

        return new RouteSet(fallback, operations);
    }

    private async Task<IReadOnlyDictionary<string, RouteSet>> CampaignsAsync(
        PluginSnapshot plugins,
        List<ErrorDetail> problems,
        CancellationToken cancellationToken)
    {
        // An archived campaign's routes are history, and history must never stand between an operator and an
        // uninstall: the campaign will never dispatch again, so what its route named no longer has to exist.
        var rows = await db.CampaignRoutes
            .AsNoTracking()
            .Where(route => route.Campaign!.ArchivedAt == null)
            .OrderBy(route => route.CampaignId)
            .ThenBy(route => route.Operation)
            .Select(route => new { Campaign = route.Campaign!.PublicId, route.Operation, route.PluginId, route.Binding })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var campaigns = new Dictionary<string, CampaignRoutes>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var route = Accept(plugins, CampaignField(row.Campaign, row.Operation), row.Operation, row.PluginId, row.Binding, problems);
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
            else
            {
                set.Operations[row.Operation] = route;
            }
        }

        return campaigns.ToDictionary(entry => entry.Key, entry => entry.Value.Frozen(), StringComparer.Ordinal);
    }

    /// <summary>One campaign's routes while the rows are still being read: a default, and the overrides of it.</summary>
    private sealed class CampaignRoutes
    {
        public Route? Default { get; set; }

        public Dictionary<string, Route> Operations { get; } = new(StringComparer.Ordinal);

        public RouteSet Frozen() => new(Default, Operations);
    }

    /// <summary>The route as the snapshot will hold it, or nothing — with the problem recorded against its name.</summary>
    private static Route? Accept(
        PluginSnapshot plugins,
        string field,
        string? operation,
        string? pluginId,
        JsonObject? binding,
        List<ErrorDetail> problems)
    {
        if (Check(plugins, operation, pluginId, binding, out var identity) is { } problem)
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

    private static RouteProblem? OperationProblem(LoadedPlugin plugin, string operation)
    {
        if (OperationCatalog.Find(operation) is not { } contract)
        {
            return new RouteProblem(
                RouteProblemCodes.OperationUnknown,
                $"'{operation}' is not an operation this build publishes.");
        }

        if (RouteChecks.OperationUnsupported(plugin, operation))
        {
            return new RouteProblem(
                RouteProblemCodes.OperationUnsupported,
                $"'{plugin.Manifest.Id}' does not implement '{operation}'; work is never quietly handed to a plugin that did not claim it.");
        }

        return Incompatible(plugin, contract);
    }

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
}

/// <summary>Why one route cannot be used, in the vocabulary of <see cref="RouteProblemCodes"/>.</summary>
public sealed record RouteProblem(string Code, string Message);
