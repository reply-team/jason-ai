using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Operations;
using Jason.Runtime.Configuration;
using Jason.Runtime.Domain;
using Jason.Runtime.Persistence;
using Jason.Runtime.Plugins.Registry;
using Jason.Runtime.WorkItems;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Jason.Runtime.Routing;

/// <summary>
/// Takes back what the provider still holds for people whose line in a campaign stopped: for each send of theirs there
/// that the provider holds for a moment still to come, by the runtime's one definition of such a send
/// (<see cref="HeldSends"/>), it queues one <c>message.cancel</c> naming it, as the dispatcher, unless an open or a
/// succeeded cancel already names it. A letter the provider already took cannot be held at the claim, only taken back.
/// <para>
/// Three acts stop a line, and each calls this in its own save: the hold a reply sets (the respond step), the end of a
/// line (<c>campaign.finish_contacts</c>) and a removal (<c>campaign.remove_contacts</c>). The cancels are the runtime's
/// own work, root work with no person on the row, as the bounce step's reads are: the cancel's contract uses no contact,
/// and system work takes nobody out of <c>new</c>.
/// </para>
/// <para>
/// A cancel goes only where the send ran (<see cref="RouteResolver.ForSend"/>). Where no route active now names that plugin
/// and account for <c>message.cancel</c>, or the plugin performs none, nothing is queued for that send, since the claim
/// would refuse it: the act that called goes on, and the log says so once per campaign, by the campaign and the code
/// <see cref="NotRouted"/>, never by anything a send says.
/// </para>
/// </summary>
public sealed class HeldSendCancels(
    WorkItemService items,
    LiveSettings<ResponderOptions> settings,
    RouteRegistry routes,
    PluginRegistry plugins,
    HeldSendCancelSkips skips,
    ILogger<HeldSendCancels> logger)
{
    /// <summary>Why a held send was not taken back: no route active now takes its cancel where it ran.</summary>
    public const string NotRouted = "held_send_cancel_not_routed";

    /// <summary>
    /// How far above <c>Responder:Priority</c> a take-back sits: the provider sends at its moment, so the cancel goes before
    /// the reply's read and its responder, and below the inbound read's detection.
    /// </summary>
    public const int AboveResponders = 1;

    private static readonly OperationContract CancelContract = OperationCatalog.Find(HeldSends.Cancel)!;

    private static readonly Action<ILogger, string, string, Exception?> Skipped = LoggerMessage.Define<string, string>(
        LogLevel.Warning,
        new EventId(1, nameof(Skipped)),
        "A send the provider holds for campaign {CampaignId} was not taken back: {Code}");

    /// <summary>
    /// Queues the cancels for the people named, by their rows, in the campaign, and answers how many it queued. Called
    /// inside the caller's transaction: the reads see the caller's writes and the cancels commit with them.
    /// </summary>
    public async Task<int> CancelForAsync(JasonDbContext db, int campaignId, string campaignPublicId, IReadOnlyCollection<int> contactIds, DateTimeOffset now, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(contactIds);
        if (contactIds.Count == 0)
        {
            return 0;
        }

        var held = await HeldSends.ReadForAsync(db, campaignId, contactIds, now, ct).ConfigureAwait(false);
        if (held.Count == 0)
        {
            return 0;
        }

        var named = await OpenCancelsAsync(db, campaignId, [.. held.Select(send => send.WorkItem)], ct).ConfigureAwait(false);
        var queued = 0;
        foreach (var send in held.Where(send => !named.Contains(send.WorkItem)))
        {
            if (!Routed(send))
            {
                if (skips.FirstTime(campaignId, NotRouted))
                {
                    Skipped(logger, campaignPublicId, NotRouted, null);
                }

                continue;
            }

            await items.CreateCoreAsync(
                new WorkItemCreateRequest(
                    campaignPublicId,
                    WorkItemKind.ProviderOp,
                    Role: null,
                    HeldSends.Cancel,
                    ContactId: null,
                    ExecutionProfile: null,
                    settings.Current.Priority + AboveResponders,
                    NotBefore: null,
                    DueAt: null,
                    TimeoutSeconds: null,
                    HeartbeatSeconds: null,
                    MaxAttempts: null,
                    new JsonObject { [WorkItemService.InputKey] = new JsonObject { ["send"] = send.WorkItem } },
                    ResultFormat: null,
                    Actor: null,
                    Reason: "a held send to take back"),
                Actors.Dispatcher,
                LineageRecord.Root,
                ct).ConfigureAwait(false);
            queued++;
        }

        return queued;
    }

    /// <summary>
    /// Whether the claim would take a cancel of the send where it ran: a route active now names the plugin and the account
    /// the send ran under, and that plugin performs the cancel. The send's channel only orders the walk, as at a route
    /// change, so it is not asked for.
    /// </summary>
    private bool Routed(HeldSend send) =>
        RouteResolver.ForSend(routes.Snapshot, send.Campaign, HeldSends.Cancel, send.Operation, sendChannel: null, send.PluginId, send.BindingIdentity) is { } found
        && string.Equals(found.Route.PluginId, send.PluginId, StringComparison.Ordinal)
        && string.Equals(found.Route.BindingIdentity, send.BindingIdentity, StringComparison.Ordinal)
        && ProviderOpPreflight.Routable(plugins.Snapshot, CancelContract, found.Route) is null;

    /// <summary>
    /// The sends among <paramref name="sends"/> an open cancel of the campaign already names, whoever made it. A succeeded
    /// one has already let the send go, by <see cref="HeldSends"/>' own reading; a failed one leaves it held, so it is
    /// taken back again.
    /// </summary>
    private static async Task<HashSet<string>> OpenCancelsAsync(JasonDbContext db, int campaignId, IReadOnlyList<string> sends, CancellationToken ct)
    {
        var open = await db.WorkItems
            .AsNoTracking()
            .Where(item => item.CampaignId == campaignId
                && item.Operation == HeldSends.Cancel
                && (item.Status == WorkItemStatus.Created
                    || item.Status == WorkItemStatus.AwaitingApproval
                    || item.Status == WorkItemStatus.Scheduled
                    || item.Status == WorkItemStatus.Processing
                    || item.Status == WorkItemStatus.Waiting))
            .Select(item => item.Context)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return [.. open
            .Select(context => context[WorkItemService.InputKey]?["send"] is JsonValue value && value.TryGetValue<string>(out var send) ? send : null)
            .OfType<string>()
            .Where(sends.Contains)];
    }
}

/// <summary>Which campaigns the take-back has said it left a held send for, and why, in the life of this process.</summary>
public sealed class HeldSendCancelSkips
{
    private readonly ConcurrentDictionary<(int Campaign, string Code), bool> _said = new();

    /// <summary>True the first time this campaign is passed over for this reason, and never again.</summary>
    public bool FirstTime(int campaign, string code) => _said.TryAdd((campaign, code), true);
}
