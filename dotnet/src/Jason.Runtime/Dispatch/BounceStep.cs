using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Operations;
using Jason.Runtime.Configuration;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Logging;
using Jason.Runtime.Persistence;
using Jason.Runtime.Plugins.Registry;
using Jason.Runtime.Routing;
using Jason.Runtime.WorkItems;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Jason.Runtime.Dispatch;

/// <summary>
/// The step of a scan right after respond: a bounce a provider's events said of one of a campaign's people asks for the
/// detail of the campaign's own sends that could be the one. For each <c>provider_event_observed</c> line of kind
/// <c>bounce</c> above the campaign's own watermark it queues a read of each such send (<c>message.status</c>): the
/// campaign's emails to that contact through the plugin the bounce was read at, succeeded with a reference, in the 45
/// days before the bounce, with no bounce said of them yet, newest first, at most three, and never a second read for a
/// send that already has an open read the runtime made.
/// <para>
/// It writes no line and no register entry: each read decides alone, at its own end (<see cref="Execution.SentFacts"/>).
/// A campaign with no send the bounce could be of does nothing with its line: a bounce of a provider's own sequence, of
/// a mail written by hand or of a send older than the window stops nothing, and the event's own line stays the record.
/// </para>
/// <para>
/// One line, one short transaction, as in the respond step: the reads and the watermark's guarded move commit together.
/// A paused campaign holds its watermark until it resumes, so its lines wait, and so do its reads.
/// </para>
/// </summary>
public sealed class BounceStep(
    WorkItemService items,
    LiveSettings<ResponderOptions> settings,
    RouteRegistry routes,
    PluginRegistry plugins,
    BounceSkips skips,
    ILogger<BounceStep> logger)
{
    /// <summary>The kind of provider event this step reads.</summary>
    public const string EventKind = "bounce";

    /// <summary>How many lines one scan reads of one campaign at most; the rest are read by the scans after it.</summary>
    public const int LinesPerScan = 100;

    private static readonly OperationContract StatusContract = OperationCatalog.Find(BounceCandidates.StatusOperation)!;

    private static readonly Action<ILogger, string, int, Exception?> Queued = LoggerMessage.Define<string, int>(
        LogLevel.Information,
        new EventId(1, nameof(Queued)),
        "The bounce step queued reads of sends for campaign {CampaignId}: {Count}");

    private static readonly Action<ILogger, string, string, Exception?> Skipped = LoggerMessage.Define<string, string>(
        LogLevel.Information,
        new EventId(2, nameof(Skipped)),
        "The bounce step queued no read for campaign {CampaignId}: {Code}");

    private static readonly Action<ILogger, string, string, Exception?> PassedOver = LoggerMessage.Define<string, string>(
        LogLevel.Warning,
        new EventId(3, nameof(PassedOver)),
        "The bounce step passed over a line of campaign {CampaignId} it could not act on: {Code}");

    private static readonly Action<ILogger, string, Exception?> Failed = LoggerMessage.Define<string>(
        LogLevel.Warning,
        new EventId(4, nameof(Failed)),
        "The bounce step for campaign {CampaignId} failed; the rest of the scan continues without it");

    private static readonly Action<ILogger, string, string, Exception?> FailedToParse = LoggerMessage.Define<string, string>(
        LogLevel.Warning,
        new EventId(4, nameof(Failed)),
        "The bounce step for campaign {CampaignId} failed; the rest of the scan continues without it: {Failure}");

    private static readonly Action<ILogger, string, Exception?> Moved = LoggerMessage.Define<string>(
        LogLevel.Information,
        new EventId(5, nameof(Moved)),
        "Campaign {CampaignId} was accounted for by another writer while the bounce step was reading it");

    /// <summary>Reads every active campaign's new bounces, and answers how many reads it queued.</summary>
    public async Task<int> BounceAsync(JasonDbContext db, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        var options = settings.Current;
        var queued = 0;
        foreach (var campaign in await ActiveAsync(db, ct).ConfigureAwait(false))
        {
            try
            {
                queued += await BounceOneAsync(db, campaign, options, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (ParseFailure.Is(ex))
                {
                    FailedToParse(logger, campaign.PublicId, ParseFailure.Describe(ex), null);
                }
                else
                {
                    Failed(logger, campaign.PublicId, ex);
                }

                db.ChangeTracker.Clear();
            }
        }

        return queued;
    }

    private async Task<int> BounceOneAsync(JasonDbContext db, Active campaign, ResponderOptions options, CancellationToken ct)
    {
        var before = campaign.Watermark;
        var lines = await db.Journal
            .AsNoTracking()
            .Where(entry => entry.CampaignId == campaign.Id && entry.Id > before && entry.Kind == JournalKinds.ProviderEventObserved)
            .OrderBy(entry => entry.Id)
            .Take(LinesPerScan)
            .Select(entry => new Line(entry.Id, entry.PublicId, entry.New))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (lines.Count == 0)
        {
            await LevelAsync(db, campaign, before, ct).ConfigureAwait(false);
            return 0;
        }

        var queued = 0;
        foreach (var line in lines)
        {
            var acted = await OneLineAsync(db, campaign, before, line, options, ct).ConfigureAwait(false);
            if (acted.Moved)
            {
                Moved(logger, campaign.PublicId, null);
                return queued;
            }

            before = line.Id;
            foreach (var code in acted.Skips)
            {
                if (skips.FirstTime(campaign.Id, code))
                {
                    Skipped(logger, campaign.PublicId, code, null);
                }
            }

            if (acted.Queued > 0)
            {
                Queued(logger, campaign.PublicId, acted.Queued, null);
                queued += acted.Queued;
            }
        }

        return queued;
    }

    private async Task<Acted> OneLineAsync(JasonDbContext db, Active campaign, int before, Line line, ResponderOptions options, CancellationToken ct)
    {
        await using (var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false))
        {
            try
            {
                if (await AccountForAsync(db, campaign.Id, before, line.Id, ct).ConfigureAwait(false) == 0)
                {
                    await transaction.RollbackAsync(ct).ConfigureAwait(false);
                    return Acted.Elsewhere;
                }

                var outcome = Bounce.Read(line) is { } bounce
                    ? await ActAsync(db, campaign, bounce, options, ct).ConfigureAwait(false)
                    : Acted.Nothing;
                await transaction.CommitAsync(ct).ConfigureAwait(false);
                db.ChangeTracker.Clear();
                return outcome;
            }
            catch (Exception ex) when (ex is DomainException || Refused(ex))
            {
                await transaction.RollbackAsync(ct).ConfigureAwait(false);
                db.ChangeTracker.Clear();
                var code = ex is DomainException domain ? domain.Code : "write_refused";
                if (skips.FirstTime(campaign.Id, code))
                {
                    PassedOver(logger, campaign.PublicId, code, null);
                }
            }
        }

        // Past the poisoned line alone, as in the respond step.
        await using var passing = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        if (await AccountForAsync(db, campaign.Id, before, line.Id, ct).ConfigureAwait(false) == 0)
        {
            await passing.RollbackAsync(ct).ConfigureAwait(false);
            return Acted.Elsewhere;
        }

        await passing.CommitAsync(ct).ConfigureAwait(false);
        return Acted.Nothing;
    }

    /// <summary>The reads of the campaign's own sends the bounce could be of, each where the send ran.</summary>
    private async Task<Acted> ActAsync(JasonDbContext db, Active campaign, Bounce bounce, ResponderOptions options, CancellationToken ct)
    {
        var contactId = bounce.ContactId is null
            ? null
            : await db.Contacts.AsNoTracking()
                .Where(contact => contact.PublicId == bounce.ContactId)
                .Select(contact => (int?)contact.Id)
                .SingleOrDefaultAsync(ct)
                .ConfigureAwait(false);
        if (contactId is not { } contact)
        {
            return new Acted(0, [BounceSkipCodes.NoContact]);
        }

        var candidates = await BounceCandidates.ReadAsync(db, campaign.Id, contact, bounce.Plugin, bounce.OccurredAt, ct).ConfigureAwait(false);
        if (candidates.Count == 0)
        {
            return Acted.Nothing;
        }

        var open = await BounceCandidates.ReadOpenAsync(db, campaign.Id, [.. candidates.Select(send => send.WorkItem)], ct).ConfigureAwait(false);
        var queued = 0;
        var skipped = new List<string>();
        foreach (var send in candidates.Where(send => !open.Contains(send.WorkItem)))
        {
            if (!Routed(campaign, send))
            {
                skipped.Add(BounceSkipCodes.ReadNotRouted);
                continue;
            }

            await items.CreateCoreAsync(
                new WorkItemCreateRequest(
                    campaign.PublicId,
                    WorkItemKind.ProviderOp,
                    Role: null,
                    BounceCandidates.StatusOperation,
                    ContactId: null,
                    ExecutionProfile: null,
                    options.Priority,
                    NotBefore: null,
                    DueAt: null,
                    TimeoutSeconds: null,
                    HeartbeatSeconds: null,
                    MaxAttempts: null,
                    new JsonObject { [WorkItemService.InputKey] = new JsonObject { ["send"] = send.WorkItem } },
                    ResultFormat: null,
                    Actor: null,
                    Reason: "a bounce to read"),
                Actors.Dispatcher,
                LineageRecord.Root,
                ct).ConfigureAwait(false);
            queued++;
        }

        return new Acted(queued, skipped);
    }

    /// <summary>
    /// Whether the claim would take a read of the send where it ran: a route active now names the plugin and the account
    /// the send ran under (<see cref="RouteResolver.ForSend"/>), and that plugin performs the read. Otherwise the claim
    /// would refuse it, so none is made.
    /// </summary>
    private bool Routed(Active campaign, BounceCandidate send) =>
        RouteResolver.ForSend(routes.Snapshot, campaign.PublicId, BounceCandidates.StatusOperation, send.Operation, "email", send.PluginId, send.BindingIdentity) is { } found
        && string.Equals(found.Route.PluginId, send.PluginId, StringComparison.Ordinal)
        && string.Equals(found.Route.BindingIdentity, send.BindingIdentity, StringComparison.Ordinal)
        && ProviderOpPreflight.Routable(plugins.Snapshot, StatusContract, found.Route) is null;

    private static async Task LevelAsync(JasonDbContext db, Active campaign, int before, CancellationToken ct)
    {
        var last = await OwnLastLineAsync(db, campaign.Id, ct).ConfigureAwait(false);
        if (last <= before)
        {
            return;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        var said = await db.Journal
            .AnyAsync(entry => entry.CampaignId == campaign.Id && entry.Id > before && entry.Kind == JournalKinds.ProviderEventObserved, ct)
            .ConfigureAwait(false);
        last = await OwnLastLineAsync(db, campaign.Id, ct).ConfigureAwait(false);
        if (said || await AccountForAsync(db, campaign.Id, before, last, ct).ConfigureAwait(false) == 0)
        {
            await transaction.RollbackAsync(ct).ConfigureAwait(false);
            return;
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }

    private static bool Refused(Exception exception) =>
        exception is DbUpdateException { InnerException: SqliteException { SqliteErrorCode: 19 } };

    private static async Task<int> OwnLastLineAsync(JasonDbContext db, int campaignId, CancellationToken ct) =>
        await db.Journal
            .Where(entry => entry.CampaignId == campaignId)
            .MaxAsync(entry => (int?)entry.Id, ct)
            .ConfigureAwait(false) ?? 0;

    private static Task<int> AccountForAsync(JasonDbContext db, int campaignId, int before, int after, CancellationToken ct) =>
        db.Campaigns
            .Where(campaign => campaign.Id == campaignId && campaign.BounceWatermark == before)
            .ExecuteUpdateAsync(set => set.SetProperty(campaign => campaign.BounceWatermark, after), ct);

    private static async Task<IReadOnlyList<Active>> ActiveAsync(JasonDbContext db, CancellationToken ct) =>
        await db.Campaigns
            .AsNoTracking()
            .Where(campaign => campaign.Status == CampaignStatus.Active)
            .OrderBy(campaign => campaign.Id)
            .Select(campaign => new Active(campaign.Id, campaign.PublicId, campaign.BounceWatermark))
            .ToListAsync(ct)
            .ConfigureAwait(false);

    private sealed record Active(int Id, string PublicId, int Watermark);

    private sealed record Line(int Id, string PublicId, JsonNode? New);

    private sealed record Acted(int Queued, IReadOnlyList<string> Skips, bool Moved = false)
    {
        public static Acted Nothing { get; } = new(0, []);

        public static Acted Elsewhere { get; } = new(0, [], Moved: true);
    }

    /// <summary>What a provider event line says of a bounce: the plugin, the contact it names and the bounce's moment.</summary>
    private sealed record Bounce(string Plugin, string? ContactId, DateTime OccurredAt)
    {
        public static Bounce? Read(Line line)
        {
            if (line.New is not JsonObject said
                || Text(said, "kind") != EventKind
                || Text(said, "plugin") is not { } plugin
                || Text(said, "occurred_at") is not { } moment
                || !DateTime.TryParse(moment, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var at))
            {
                return null;
            }

            return new Bounce(plugin, Text(said, "contact_id"), at);
        }

        private static string? Text(JsonObject said, string key) =>
            said[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
    }
}

/// <summary>Why a bounce queued no read of a send, one code per reason. A campaign with no send it could be of is no skip.</summary>
public static class BounceSkipCodes
{
    /// <summary>The bounce names no contact the installation holds, so no send of the campaign can be found for it.</summary>
    public const string NoContact = "bounce_names_no_contact";

    /// <summary>No route active now takes a read of the send where it ran, so the claim would refuse one.</summary>
    public const string ReadNotRouted = "bounce_read_not_routed";
}

/// <summary>Which campaigns the bounce step has said it queued no read for, and why, in the life of this process.</summary>
public sealed class BounceSkips
{
    private readonly ConcurrentDictionary<(int Campaign, string Code), bool> _said = new();

    /// <summary>True the first time this campaign is passed over for this reason, and never again.</summary>
    public bool FirstTime(int campaign, string code) => _said.TryAdd((campaign, code), true);
}
