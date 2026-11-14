using System.Globalization;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Plugins;
using Jason.Runtime.Configuration;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Plugins.Manifest;
using Jason.Runtime.Routing;

namespace Jason.Runtime.Plugins.Registry;

/// <summary>
/// A plugin's grants from the command line: <c>plugin.grant</c> adds one entry to one list under
/// <c>Plugins:Grants:&lt;id&gt;</c>, and <c>plugin.revoke</c> takes one out. Each writes the settings file through the
/// runtime's one writer of it, which keeps every byte it does not own, and runs the plugin reload in the same act, so the
/// grant is in force when the request answers; a reload that refuses puts the file back.
/// </summary>
/// <remarks>
/// <para>
/// <b>Stricter than the file.</b> A grant written by hand that the manifest does not request is a warning at the load and
/// grants nothing; this verb refuses one (<c>grant_unrequested</c>), since a verb that writes what the load will ignore
/// misleads whoever ran it. The rule is the resolver's own, run over the one entry, so <c>any_granted_host</c> and the
/// meaning of <c>*</c> are what the load makes of them.
/// </para>
/// <para>
/// <b>One entry, never the person's list.</b> A grant already held and a revoke of what is not granted write nothing and
/// answer success: each is the state asked for. An entry a <c>*</c> covers is not taken back, since that would mean
/// rewriting the star into the names it stands for today (<c>grant_held_by_star</c>).
/// </para>
/// <para>
/// <b>Names, never secrets.</b> A grant is a program's name, a host, a variable's name, an API family, a directory or a
/// journal kind; a variable's value stays in the runtime's environment, and <c>Plugins:Config</c> is never touched.
/// </para>
/// </remarks>
public sealed class PluginGrantService(
    JasonDbContext db,
    JournalWriter journal,
    PluginRegistry plugins,
    PluginService reload,
    PluginLoader loader,
    ReloadGate gate,
    SettingsWriter writer,
    SettingsFile settingsFile)
{
    /// <summary>What each capability is called where a person reads it, and the flag the CLI gives it.</summary>
    private static readonly Dictionary<PluginGrantCapability, (string Noun, string Plural, string Flag)> Words = new()
    {
        [PluginGrantCapability.Exec] = ("program", "programs", "exec"),
        [PluginGrantCapability.Http] = ("host", "hosts", "http"),
        [PluginGrantCapability.Env] = ("variable", "variables", "env"),
        [PluginGrantCapability.Api] = ("API family", "API families", "api"),
        [PluginGrantCapability.Files] = ("directory", "directories", "files"),
        [PluginGrantCapability.Subscriptions] = ("journal kind", "journal kinds", "subscription"),
    };

    public async Task<PluginRegistryDto> GrantAsync(PluginGrantRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var (actor, reason, id, capability, entry) = Read(request);

        var comparer = Comparer(capability);
        return await WriteAsync(id, capability, actor, reason, before =>
        {
            // The manifest is the one the write reads under the gate, so no reload between the check and the write can
            // replace it with one that does not ask for the entry.
            var loaded = before.Find(id) ?? throw DomainErrors.PluginNotFound(id);
            var written = Requested(loaded.Manifest, capability, entry);
            if (capability == PluginGrantCapability.Files && loader.FileGrantRefusal(entry) is { } refusal)
            {
                throw new ValidationException([new ErrorDetail("entry", ProblemCodes.GrantPathRefused, refusal)]);
            }

            return held => Covers(loaded.Manifest, capability, held, written, comparer) ? held : [.. held, written];
        }, takenBack: false, cancellationToken).ConfigureAwait(false);
    }

    public async Task<PluginRegistryDto> RevokeAsync(PluginGrantRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var (actor, reason, id, capability, entry) = Read(request);

        var comparer = Comparer(capability);
        return await WriteAsync(id, capability, actor, reason, before =>
        {
            // Taking a grant back needs no manifest: a package taken away leaves grants nobody can use, and they may go.
            var manifest = before.Find(id)?.Manifest;
            return held =>
            {
                if (!string.Equals(entry, GrantResolver.Everything, StringComparison.Ordinal)
                    && held.Contains(GrantResolver.Everything, StringComparer.Ordinal)
                    && manifest is not null
                    && Resolved(manifest, capability, [GrantResolver.Everything]).Contains(entry, comparer))
                {
                    throw DomainErrors.GrantHeldByStar(Path(id, capability), entry, id, Words[capability].Flag);
                }

                return [.. held.Where(name => !comparer.Equals(name, entry))];
            };
        }, takenBack: true, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Whether a list already grants the entry: a star by a star, a directory by itself, and a name where what the list
    /// resolves to holds it, so a star covers a declared host and never one only <c>any_granted_host</c> admits.
    /// </summary>
    private static bool Covers(PluginManifest manifest, PluginGrantCapability capability, IReadOnlyList<string> list, string entry, StringComparer comparer) =>
        entry == GrantResolver.Everything || capability == PluginGrantCapability.Files
            ? list.Contains(entry, comparer)
            : Resolved(manifest, capability, list).Contains(entry, comparer);

    /// <summary>
    /// The one shape of both: under the reload gate, the request is checked against the snapshot the gate holds, the
    /// settings writer edits the list from each read of the file, and the reload freezes it with a
    /// <c>plugin_grants_updated</c> line in the same save as its <c>plugins_reloaded</c>, written only where what the plugin
    /// may use of the capability changed. A file the edit left as it was is reloaded only where the grants in force are not
    /// already what the file says, so a grant written by hand and never reloaded is put in force by the same request;
    /// otherwise nothing is activated, and the answer is <c>plugin.list</c>'s with <c>activated</c> false.
    /// </summary>
    private async Task<PluginRegistryDto> WriteAsync(
        string id,
        PluginGrantCapability capability,
        ActorRef actor,
        string? reason,
        Func<PluginSnapshot, Func<IReadOnlyList<string>, IReadOnlyList<string>>> checkedAgainst,
        bool takenBack,
        CancellationToken cancellationToken)
    {
        var path = Path(id, capability);
        IReadOnlyList<string> inFile = [];
        await gate.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var before = plugins.Snapshot;
            var next = checkedAgainst(before);
            return await writer.WriteAsync(
                held =>
                {
                    var list = List(held, path);
                    var after = next(list);
                    inFile = after;
                    return after.SequenceEqual(list, StringComparer.Ordinal) ? [] : Edits(id, path, after);
                },
                [path],
                PluginsOptions.Section,
                async (wrote, token) =>
                {
                    var loaded = before.Find(id);
                    if (!wrote && (loaded is null || Granted(loaded, capability).SequenceEqual(Resolved(loaded.Manifest, capability, inFile), StringComparer.Ordinal)))
                    {
                        return reload.List() with { Activated = false };
                    }

                    var (answer, _) = await reload.ReloadHeldAsync(
                        actor,
                        reason,
                        wrote ? null : settingsFile.Read(),
                        recordRefusal: !wrote,
                        routeRefusal: null,
                        besides: (now, _) =>
                        {
                            var old = Recorded(id, capability, before.Find(id));
                            var updated = Recorded(id, capability, now.Find(id));
                            if (!JsonNode.DeepEquals(old, updated))
                            {
                                journal.Append(db, actor, JournalKinds.PluginGrantsUpdated, campaign: null, key: $"{id}/{Word(capability)}", old: old, updated: updated, reason: reason);
                            }
                        },
                        token).ConfigureAwait(false);
                    return answer;
                },
                (refused, putBack, _) => RouteService.PutBackSaid(refused, putBack, takenBack ? GlobalRouteWrite.TakenAway : GlobalRouteWrite.Added, GrantNouns),
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Semaphore.Release();
        }
    }

    /// <summary>The request, checked: the actor, the reason, the plugin's id, the capability and the entry, each required.</summary>
    private static (ActorRef Actor, string? Reason, string Id, PluginGrantCapability Capability, string Entry) Read(PluginGrantRequest request)
    {
        var actor = Actors.Resolve(request.Actor);
        var errors = new ValidationErrors();
        if (request.Reason is not null && request.Reason.Trim().Length > PluginService.MaxReasonLength)
        {
            errors.Add("reason", "too_long", string.Create(CultureInfo.InvariantCulture, $"reason must be at most {PluginService.MaxReasonLength} characters."));
        }

        if (string.IsNullOrWhiteSpace(request.PluginId))
        {
            errors.Add("plugin_id", "required", "plugin_id is required: the plugin whose grant this is.");
        }

        if (request.Capability is null)
        {
            errors.Add("capability", "required", "capability is required: exec, http, env, api, files or subscriptions.");
        }

        if (string.IsNullOrWhiteSpace(request.Entry))
        {
            errors.Add("entry", "required", "entry is required: the program, host, variable, API family, directory or journal kind the grant is about, or *.");
        }
        else if (request.Capability == PluginGrantCapability.Files && request.Entry.Trim() == GrantResolver.Everything)
        {
            errors.Add("entry", "not_allowed", "'*' cannot be granted for files: a manifest names no directory, so there is nothing for it to stand for; name an absolute directory.");
        }

        errors.ThrowIfAny();
        return (actor, string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim(), request.PluginId!.Trim(), request.Capability!.Value, request.Entry!.Trim());
    }

    /// <summary>
    /// The entry as it is written to the file, or the refusal of one the manifest does not request: the resolver's own
    /// rule over the one entry, so what the verb grants is what the load would grant. A name is written as the manifest
    /// spells it, a directory as the person wrote it, a star as a star.
    /// </summary>
    private static string Requested(PluginManifest manifest, PluginGrantCapability capability, string entry)
    {
        var resolved = GrantResolver.Resolve(manifest, Grant(capability, [entry]));
        var granted = Granted(resolved, capability);
        if (resolved.Warnings.Count > 0 || granted.Count == 0)
        {
            throw DomainErrors.GrantUnrequested(Unrequested(manifest, capability, entry));
        }

        return entry == GrantResolver.Everything || capability == PluginGrantCapability.Files ? entry : granted[0];
    }

    /// <summary>The sentence of a grant the manifest does not request, saying what it does request where it requests anything.</summary>
    private static string Unrequested(PluginManifest manifest, PluginGrantCapability capability, string entry)
    {
        var (noun, plural, _) = Words[capability];
        var requested = Resolved(manifest, capability, [GrantResolver.Everything]);
        var anyHost = capability == PluginGrantCapability.Http && manifest.Capabilities.Http?.AnyGrantedHost == true;
        if (capability == PluginGrantCapability.Files)
        {
            return $"{manifest.Id}'s manifest does not ask to write files, so no directory was granted and nothing was written.";
        }

        if (requested.Count == 0 && !anyHost)
        {
            return $"{manifest.Id}'s manifest requests no {plural}, so '{entry}' was not granted and nothing was written.";
        }

        if (entry == GrantResolver.Everything)
        {
            return $"{manifest.Id}'s manifest names no {plural}, so '*' grants nothing and was not written; name the {noun} itself, which its manifest lets you grant.";
        }

        return anyHost
            ? $"'{entry}' is not a host name, so it was not granted and nothing was written."
            : $"{manifest.Id}'s manifest does not request the {noun} '{entry}', so it was not granted and nothing was written; it requests: {string.Join(", ", requested)}.";
    }

    /// <summary>What a manifest makes of a list of entries for one capability, as the load would resolve it.</summary>
    private static IReadOnlyList<string> Resolved(PluginManifest manifest, PluginGrantCapability capability, IReadOnlyList<string> entries) =>
        Granted(GrantResolver.Resolve(manifest, Grant(capability, entries)), capability);

    private static PluginGrant Grant(PluginGrantCapability capability, IReadOnlyList<string> entries)
    {
        var grant = new PluginGrant();
        List(grant, capability).AddRange(entries);
        return grant;
    }

    private static List<string> List(PluginGrant grant, PluginGrantCapability capability) => capability switch
    {
        PluginGrantCapability.Exec => grant.Exec,
        PluginGrantCapability.Http => grant.Http,
        PluginGrantCapability.Env => grant.Env,
        PluginGrantCapability.Api => grant.Api,
        PluginGrantCapability.Files => grant.Files,
        PluginGrantCapability.Subscriptions => grant.Subscriptions,
        _ => throw new ArgumentOutOfRangeException(nameof(capability), capability, "a capability with no list."),
    };

    private static IReadOnlyList<string> Granted(ResolvedGrants grants, PluginGrantCapability capability) => capability switch
    {
        PluginGrantCapability.Exec => grants.Exec,
        PluginGrantCapability.Http => grants.Http,
        PluginGrantCapability.Env => grants.Env,
        PluginGrantCapability.Api => grants.Api,
        PluginGrantCapability.Files => grants.Files,
        PluginGrantCapability.Subscriptions => grants.Subscriptions,
        _ => throw new ArgumentOutOfRangeException(nameof(capability), capability, "a capability with no list."),
    };

    private static IReadOnlyList<string> Granted(LoadedPlugin plugin, PluginGrantCapability capability) => Granted(plugin.Grants, capability);

    /// <summary>Hosts are compared the way hosts are; programs, variables, families, kinds and directories exactly.</summary>
    private static StringComparer Comparer(PluginGrantCapability capability) =>
        capability == PluginGrantCapability.Http ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>The list's path in the settings file, its key spelled as the documentation spells it.</summary>
    private static string Path(string id, PluginGrantCapability capability) =>
        $"{PluginsOptions.Section}:{nameof(PluginsOptions.Grants)}:{id}:{capability}";

    /// <summary>The capability as the request and the journal name it.</summary>
    private static string Word(PluginGrantCapability capability) => capability.ToString().ToLowerInvariant();

    /// <summary>
    /// The list the file holds at the path, in its order, from the file as the configuration system flattens it. A value
    /// there that is no list is the person's, written in a form this writer will not guess at, so it is refused.
    /// </summary>
    private static IReadOnlyList<string> List(IReadOnlyDictionary<string, string?> held, string path)
    {
        if (held.TryGetValue(path, out var scalar) && scalar is not null)
        {
            throw new SettingsFileRefusedException(SettingsRefusal.CheckRefused, path);
        }

        var prefix = path + ":";
        return [.. held
            .Where(pair => pair.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && pair.Value is not null)
            .Select(pair => (Index: int.TryParse(pair.Key.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var index) ? index : int.MaxValue, pair.Value!))
            .OrderBy(item => item.Index)
            .Select(item => item.Item2)];
    }

    /// <summary>
    /// The list written whole, or, where nothing is left in it, taken away with the objects it leaves empty: the
    /// configuration reads an empty list as a key without a value, and an absent list grants nothing already.
    /// </summary>
    private static IReadOnlyList<SettingsEdit> Edits(string id, string path, IReadOnlyList<string> list) =>
        list.Count > 0
            ? [SettingsEdit.Set(path, new JsonArray([.. list.Select(name => (JsonNode)JsonValue.Create(name))]))]
            :
            [
                SettingsEdit.Remove(path),
                SettingsEdit.RemoveIfEmpty($"{PluginsOptions.Section}:{nameof(PluginsOptions.Grants)}:{id}"),
                SettingsEdit.RemoveIfEmpty($"{PluginsOptions.Section}:{nameof(PluginsOptions.Grants)}"),
                SettingsEdit.RemoveIfEmpty(PluginsOptions.Section),
            ];

    /// <summary>What the plugin may use of the capability in one snapshot, as its line records it: nothing where it is not loaded.</summary>
    private static JsonObject Recorded(string id, PluginGrantCapability capability, LoadedPlugin? plugin) => new()
    {
        ["plugin"] = id,
        ["capability"] = Word(capability),
        ["granted"] = new JsonArray([.. (plugin is null ? [] : Granted(plugin, capability)).Select(name => (JsonNode)JsonValue.Create(name))]),
    };

    /// <summary>A grant, as <c>plugin grant</c> and <c>plugin revoke</c> write it, in the put-back's sentences.</summary>
    internal static PutBackNouns GrantNouns { get; } = new("grant", "grants", "took back", "the grant was not changed");
}
