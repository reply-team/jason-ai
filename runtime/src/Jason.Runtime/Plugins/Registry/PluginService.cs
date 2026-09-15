using System.Globalization;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Json;
using Jason.Contracts.Plugins;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Microsoft.Extensions.Logging;

namespace Jason.Runtime.Plugins.Registry;

/// <summary>
/// The registry as an operation: read it, or reload it. A reload is explicit and atomic — every candidate is
/// validated, and the new snapshot replaces the old one only when the whole candidate set is loadable. There are
/// no filesystem watchers: what is installed becomes what is active because somebody said so.
/// </summary>
public sealed class PluginService(
    JasonDbContext db,
    JournalWriter journal,
    PluginRegistry registry,
    PluginLoader loader,
    ReloadGate gate,
    ILogger<PluginService> logger)
{
    public const int MaxReasonLength = 2000;

    private static readonly Action<ILogger, string, int, string?, Exception?> Activated = LoggerMessage.Define<string, int, string?>(
        LogLevel.Information,
        new EventId(1, nameof(Activated)),
        "Plugin reload activated snapshot {SnapshotId} with {Plugins} plugins; reason {Reason}");

    private static readonly Action<ILogger, int, Exception?> Rejected = LoggerMessage.Define<int>(
        LogLevel.Information,
        new EventId(2, nameof(Rejected)),
        "Plugin reload rejected with {Problems} problems; the previous snapshot stays active");

    public PluginRegistryDto List() =>
        PluginMapper.ToDto(registry.Snapshot, registry.LastReload, registry.LastReload?.Activated ?? true);

    public async Task<PluginRegistryDto> ReloadAsync(PluginReloadRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var actor = Actors.Resolve(request.Actor);

        var errors = new ValidationErrors();
        if (request.Reason is not null && request.Reason.Trim().Length > MaxReasonLength)
        {
            errors.Add("reason", "too_long", string.Create(CultureInfo.InvariantCulture, $"reason must be at most {MaxReasonLength} characters."));
        }

        errors.ThrowIfAny();
        var reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();

        await gate.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var load = await loader.LoadAsync(SnapshotSource.Reload, cancellationToken).ConfigureAwait(false);
            if (load.Snapshot is null)
            {
                // The previous snapshot stays: a partly working registry would hide the problem, and the fix is
                // always the same — repair the package and reload again.
                registry.Record(load.Report);
                var details = PluginMapper.ToDetails(load.Report);
                Rejected(logger, details.Count, null);
                throw DomainErrors.PluginReloadRejected(details);
            }

            // The record first, the swap second: the swap is in memory and cannot fail, the save can. A reload that
            // could not be written down leaves the previous snapshot active and is simply repeated, instead of a new
            // snapshot running with no trace of when it began.
            JournalActivation(journal, db, actor, load.Snapshot, reason);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            registry.Replace(load.Snapshot, load.Report);
            Activated(logger, load.Snapshot.Id, load.Snapshot.Plugins.Count, reason, null);
            return PluginMapper.ToDto(load.Snapshot, load.Report, activated: true);
        }
        finally
        {
            gate.Semaphore.Release();
        }
    }

    /// <summary>
    /// One global entry per activated snapshot, shared with the load at startup. It is the audit trail that
    /// invocation pinning leans on: which package, at which digest, was active when.
    /// </summary>
    public static void JournalActivation(JournalWriter journal, JasonDbContext db, ActorRef actor, PluginSnapshot snapshot, string? reason)
    {
        ArgumentNullException.ThrowIfNull(journal);
        ArgumentNullException.ThrowIfNull(snapshot);

        var plugins = new JsonArray();
        foreach (var plugin in snapshot.Plugins)
        {
            plugins.Add(new JsonObject
            {
                ["id"] = plugin.Manifest.Id,
                ["version"] = plugin.Manifest.Version,
                ["digest"] = plugin.Digest,
                ["status"] = SnakeCase.Convert(plugin.Status.ToString()),
            });
        }

        journal.Append(
            db,
            actor,
            JournalKinds.PluginsReloaded,
            campaign: null,
            key: snapshot.Id,
            updated: new JsonObject
            {
                ["snapshot_id"] = snapshot.Id,
                ["source"] = SnakeCase.Convert(snapshot.Source.ToString()),
                ["plugins"] = plugins,
            },
            reason: reason);
    }
}
