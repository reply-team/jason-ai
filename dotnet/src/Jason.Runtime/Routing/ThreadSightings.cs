using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Routing;

/// <summary>
/// Where a campaign's own lines say a thread was seen: the plugin and the account of every <c>inbound_reply_observed</c>
/// line of the campaign naming it, newest first. A line names its plugin itself; its account is the one the read that
/// found it ran under — the inbound read's attempt, whose provenance recorded the binding at the claim, or the events
/// source the stored event was read from. All of it is the runtime's own record; nothing is asked of the provider, and
/// nothing a person wrote is read.
/// </summary>
internal static class ThreadSightings
{
    /// <summary>The operation whose route reads a campaign's threads: the one a thread's read asks after its own.</summary>
    public const string ListOperation = "conversation.list";

    /// <summary>
    /// One read of the lines, each with what its read recorded of its account: the attempt it names, for a line the inbound
    /// read wrote, and the event and its source, for a line the events store wrote.
    /// </summary>
    private const string Select = """
        SELECT j.id AS Line,
               json_extract(j.new_json, '$.plugin') AS PluginId,
               json_type(j.new_json, '$.event_id') IS 'text' AS ByEvent,
               json_extract(a.provenance_json, '$.plugin_id') AS ReadPluginId,
               json_extract(a.provenance_json, '$.binding_identity') AS ReadIdentity,
               src.plugin_id AS SourcePluginId,
               src.binding_identity AS SourceIdentity
        FROM journal AS j
        LEFT JOIN attempts AS a ON a.public_id = j.attempt_id
        LEFT JOIN events AS e ON e.public_id = json_extract(j.new_json, '$.event_id')
        LEFT JOIN event_sources AS src ON src.id = e.source_id
        """;

    internal const string OfThread = Select + """

        WHERE j.campaign_id = {0} AND j.kind = 'inbound_reply_observed' AND json_extract(j.new_json, '$.thread') = {1}
        ORDER BY j.id DESC
        """;

    private const string OfLine = Select + """

        WHERE j.id = {0} AND j.kind = 'inbound_reply_observed'
        """;

    /// <summary>
    /// Every plugin and account the campaign's lines say the thread was seen at, newest first and each once; empty where
    /// no line of the campaign names it. Read by the campaign's index on its lines of this kind.
    /// </summary>
    public static async Task<IReadOnlyList<ThreadSeen>> InCampaignAsync(JasonDbContext db, int campaignId, string thread, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(thread);

        var rows = await db.Database.SqlQueryRaw<SightingRow>(OfThread, campaignId, thread).ToListAsync(ct).ConfigureAwait(false);
        return [.. rows.Select(Seen).OfType<ThreadSeen>().Distinct()];
    }

    /// <summary>Where the one line says its thread was seen, or null where it is no such line.</summary>
    public static async Task<ThreadSeen?> OfLineAsync(JasonDbContext db, int lineId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);

        var rows = await db.Database.SqlQueryRaw<SightingRow>(OfLine, lineId).ToListAsync(ct).ConfigureAwait(false);
        return rows.Select(Seen).OfType<ThreadSeen>().FirstOrDefault();
    }

    /// <summary>
    /// The plugin and account one line says. A line whose read left no record of its account — no attempt of the plugin it
    /// names, no source of the event it names, which only a row no build of this runtime writes could be — is known by its
    /// plugin alone: a sighting of that plugin all the same, which names no account a read could prefer.
    /// </summary>
    private static ThreadSeen? Seen(SightingRow row)
    {
        if (row.PluginId is not { } plugin)
        {
            return null;
        }

        if (row.ByEvent)
        {
            return string.Equals(row.SourcePluginId, plugin, StringComparison.Ordinal) && row.SourceIdentity is { } identity
                ? new ThreadSeen(plugin, string.Equals(identity, EventSource.NoBinding, StringComparison.Ordinal) ? null : identity, AccountKnown: true)
                : new ThreadSeen(plugin, null, AccountKnown: false);
        }

        return string.Equals(row.ReadPluginId, plugin, StringComparison.Ordinal)
            ? new ThreadSeen(plugin, row.ReadIdentity, AccountKnown: true)
            : new ThreadSeen(plugin, null, AccountKnown: false);
    }

    /// <summary>One line, before its account is told.</summary>
    internal sealed record SightingRow(
        int Line,
        string? PluginId,
        bool ByEvent,
        string? ReadPluginId,
        string? ReadIdentity,
        string? SourcePluginId,
        string? SourceIdentity);
}

/// <summary>
/// A plugin and an account a thread was seen at: the plugin's id and the identity of the binding its read ran under, null
/// for a route that names no binding. <paramref name="AccountKnown"/> is false only for a line whose read left no record
/// of its account. The claim refuses on the plugin alone (<see cref="NamesThePlugin"/>); the account only orders the
/// routes a read prefers (<see cref="Names"/>).
/// </summary>
public sealed record ThreadSeen(string PluginId, string? BindingIdentity, bool AccountKnown)
{
    /// <summary>
    /// Whether <paramref name="route"/> names this plugin and this account exactly: the plugin's id, and the binding by its
    /// identity. A sighting whose account is not known names no route so.
    /// </summary>
    public bool Names(Route route)
    {
        ArgumentNullException.ThrowIfNull(route);

        return AccountKnown
            && NamesThePlugin(route)
            && string.Equals(route.BindingIdentity, BindingIdentity, StringComparison.Ordinal);
    }

    /// <summary>Whether <paramref name="route"/> names this plugin, under any binding.</summary>
    public bool NamesThePlugin(Route route)
    {
        ArgumentNullException.ThrowIfNull(route);

        return string.Equals(route.PluginId, PluginId, StringComparison.Ordinal);
    }
}
