using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Json;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Replies;
using Jason.Runtime.Routing;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Approvals;

/// <summary>What the automatic step decided about one send that nobody was ever asked about.</summary>
internal abstract record AutomaticDecision
{
    /// <summary>
    /// The mode in force for the contact is every message, or the send is not one the modes cover: the claim decides as it
    /// did before the modes. <paramref name="Chat"/> says the send was written in the chat, which no standing approval
    /// releases either.
    /// </summary>
    internal sealed record NotInForce(bool Chat) : AutomaticDecision;

    /// <summary>
    /// The send parks for the person with <paramref name="Why"/>, one of <see cref="SendModeReasons"/>, and the facts for the
    /// preview's <c>send_mode</c>. Where <paramref name="FallsThrough"/>, a standing approval that covers the send still
    /// decides by its own conditions; a doubt and a send from the chat never fall through.
    /// </summary>
    internal sealed record Park(string Why, bool FallsThrough, JsonObject Facts) : AutomaticDecision;

    /// <summary>The send goes in this claim, under a row the person who set <paramref name="Mode"/> decided.</summary>
    internal sealed record Release(ModeInForce Mode, Temperature Temperature, string Review, JsonObject Facts) : AutomaticDecision;
}

/// <summary>
/// The automatic mode at the claim: for a send a person decides each one of (<c>confirm_each</c>) that nobody was ever
/// asked about, whether the mode in force for its contact releases it, in one published order, and where it does not,
/// the reason it waits for the person. It reads facts and never a category, a sentence or the words of a reply.
/// </summary>
/// <remarks>
/// <para>
/// The order, each step asked only when every step above it let the send on:
/// the send was written in the chat or its input edited there, marked on the item's own lines, read for every send a
/// person decides each one of, with a contact or without, since a chat send parks in every mode and ahead of a standing
/// approval; the two modes are read, and where neither temperature resolves to automatic under a named person's
/// word nothing about warmth is read (with no preference set both are every message); the contact's warmth in the
/// campaign; the mode in force for it, every message ending the step; a send from the chat; a doubt that stands; a reply
/// that waits for a judgement; one that waits for its nature; for an answer, the verdict it answers, which must stand,
/// say a nature of a person and not say <c>escalate</c>; a pass of its current text by the critic or by a person; the
/// place for a limit on automatic releases; the release.
/// </para>
/// <para>
/// A mode in force releases in the name of the person who set it, so a mode whose setter is not a named person releases
/// nothing. The critic whose pass releases is the one the runtime made for the send (<see cref="CriticsAsync"/>): its
/// work created under the runtime's own actor, its <see cref="SendReview.Key"/> naming the send, its context changed by
/// nobody but the runtime; a critic somebody queued by hand, the runtime's critic of another send, or another role is
/// shown and releases nothing. A person's pass counts
/// where the line names a person and carries no attempt: a line that carries an attempt is the run's, whatever actor it
/// names, and a run's pass is the critic's only where the line's actor is the attempt it carries.
/// </para>
/// <para>
/// Every read is written SQL that names its index: the item's own lines through the journal's work item index, and a
/// campaign's verdicts on one thread through the verdicts' thread index.
/// </para>
/// </remarks>
public static class AutomaticRelease
{
    /// <summary>The role whose pass the automatic mode requires: the shipped critic, by its name.</summary>
    public const string CriticRole = "critic";

    /// <summary>The preview's member that carries the facts the step decided on.</summary>
    public const string PreviewKey = "send_mode";

    /// <summary>
    /// The decision for <paramref name="item"/>, about to park under <paramref name="plan"/>, where nobody was ever asked
    /// about it; <paramref name="doubted"/> says a doubt stands, as the park's preview reads it.
    /// </summary>
    internal static async Task<AutomaticDecision> DecideAsync(JasonDbContext db, WorkItem item, ProviderOpPlan plan, bool doubted, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(plan);
        if (!ReviewSubject.IsReviewable(item))
        {
            return new AutomaticDecision.NotInForce(Chat: false);
        }

        // Read for every send a person decides each one of, a contact or none, since it parks a chat send in every mode.
        var chat = await Confined(db, item.PublicId).AnyAsync(ct).ConfigureAwait(false);
        if (item.ContactId is not { } contact)
        {
            return new AutomaticDecision.NotInForce(chat);
        }

        var reading = await ModeForAsync(db, item.Campaign, item.CampaignId, contact, ct).ConfigureAwait(false);
        if (!reading.InForce)
        {
            return new AutomaticDecision.NotInForce(chat);
        }

        var (state, temperature, mode) = (reading.State!.Value, reading.Temperature, reading.Mode);

        var facts = Facts(mode, temperature, review: null);
        if (chat)
        {
            return new AutomaticDecision.Park(SendModeReasons.SentFromChat, FallsThrough: false, facts);
        }

        if (doubted)
        {
            return new AutomaticDecision.Park(SendModeReasons.Doubted, FallsThrough: false, facts);
        }

        if (Awaiting(state) is { } awaiting)
        {
            return new AutomaticDecision.Park(awaiting, FallsThrough: true, facts);
        }

        if (await AnswerAsync(db, item, plan, ct).ConfigureAwait(false) is { } answer)
        {
            return new AutomaticDecision.Park(answer, FallsThrough: true, facts);
        }

        var (pass, why, shown) = await PassAsync(db, item, ct).ConfigureAwait(false);
        if (pass is null)
        {
            return new AutomaticDecision.Park(why!, FallsThrough: true, Facts(mode, temperature, shown));
        }

        // The place for a limit on automatic releases: after the pass that would release the send, before the release.
        return new AutomaticDecision.Release(mode!, temperature!.Value, pass, Facts(mode, temperature, pass));
    }

    /// <summary>
    /// The mode in force for a send to <paramref name="contactId"/> in <paramref name="campaign"/>, whose row id is
    /// <paramref name="campaignId"/>, as the claim decides it:
    /// both modes are read, and only where either releases is the contact's warmth read, which picks the mode of its
    /// temperature; a contact who waits on a verdict has no temperature yet. The one reading of a mode for a contact, asked
    /// by this step at the claim and by a create before the runtime creates the critic of a send.
    /// </summary>
    internal static async Task<ModeForContact> ModeForAsync(JasonDbContext db, Campaign? campaign, int campaignId, int contactId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        var installation = await SendModes.ReadAsync(db, ct).ConfigureAwait(false);
        var cold = Releasing(SendModes.Resolve(campaign, installation, Temperature.Cold));
        var warm = Releasing(SendModes.Resolve(campaign, installation, Temperature.Warm));
        if (cold is null && warm is null)
        {
            return new ModeForContact(cold, warm, State: null, Temperature: null, Mode: null);
        }

        var state = await Warmth.OfAsync(db, campaignId, contactId, ct).ConfigureAwait(false);
        Temperature? temperature = state switch
        {
            WarmthState.Cold => Temperature.Cold,
            WarmthState.Warm => Temperature.Warm,
            _ => null,
        };
        var mode = temperature switch
        {
            Temperature.Cold => cold,
            Temperature.Warm => warm,
            _ => null,
        };
        return new ModeForContact(cold, warm, state, temperature, mode);
    }

    /// <summary>
    /// At a retry under an automatic release's row, where the contact now waits on a verdict: the reason it parks for the
    /// person, with the facts; null where the retry goes under the row. A reply can land between a failure and the retry,
    /// and the release read the contact's warmth before it did; the pass, the count and the rest stay decided.
    /// </summary>
    internal static async Task<AutomaticDecision.Park?> RetryAsync(JasonDbContext db, WorkItem item, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(item);
        if (item.ContactId is not { } contact)
        {
            return null;
        }

        var state = await Warmth.OfAsync(db, item.CampaignId, contact, ct).ConfigureAwait(false);
        return Awaiting(state) is { } why
            ? new AutomaticDecision.Park(why, FallsThrough: false, Facts(mode: null, temperature: null, review: null))
            : null;
    }

    /// <summary>
    /// Whether the item was written in the desktop's chat: its <c>workitem_created</c> line says <c>confined</c>, or a
    /// <c>workitem_updated</c> line keyed <c>confined</c> marked it at an edit of its input there. Read through the item's
    /// own lines.
    /// </summary>
    internal static IQueryable<int> Confined(JasonDbContext db, string workItemId) =>
        db.Database.SqlQuery<int>($"""
            SELECT id AS "Value" FROM journal INDEXED BY ix_journal_work_item_id_public_id
            WHERE work_item_id = {workItemId}
              AND ((kind = 'workitem_created' AND json_extract(new_json, '$.confined') = 1)
                OR (kind = 'workitem_updated' AND key = 'confined'))
            LIMIT 1
            """);

    /// <summary>The newest verdict of a campaign on one thread at one plugin, which stands by being the newest there.</summary>
    internal static IQueryable<JournalEntry> NewestOnThread(JasonDbContext db, int campaignId, string thread, string plugin) =>
        db.Journal.FromSqlInterpolated($"""
            SELECT * FROM journal INDEXED BY ix_journal_campaign_verdict_thread
            WHERE kind = 'reply_classified' AND campaign_id = {campaignId}
              AND json_extract(new_json, '$.thread') = {thread} AND json_extract(new_json, '$.plugin') = {plugin}
            ORDER BY id DESC LIMIT 1
            """).AsNoTracking();

    /// <summary>The newest verdict of a campaign under one key on one thread: the one that stands for that reply.</summary>
    internal static IQueryable<JournalEntry> StandingUnder(JasonDbContext db, int campaignId, string thread, string key) =>
        db.Journal.FromSqlInterpolated($"""
            SELECT * FROM journal INDEXED BY ix_journal_campaign_verdict_thread
            WHERE kind = 'reply_classified' AND campaign_id = {campaignId}
              AND json_extract(new_json, '$.thread') = {thread} AND key = {key}
            ORDER BY id DESC LIMIT 1
            """).AsNoTracking();

    /// <summary>Every review of one item, whatever text it read, oldest first, through the journal's work item index.</summary>
    internal static IQueryable<JournalEntry> ReviewLines(JasonDbContext db, string workItemId) =>
        db.Journal.FromSqlInterpolated($"""
            SELECT * FROM journal INDEXED BY ix_journal_work_item_id_public_id
            WHERE work_item_id = {workItemId} AND kind = 'workitem_reviewed'
            ORDER BY id
            """).AsNoTracking();

    /// <summary>
    /// Whether a review line is a pass that counts: a named person's outside any run, or the pass of a running attempt of
    /// the critic's work the runtime made for this send, whose attempt is among <paramref name="critics"/>.
    /// </summary>
    internal static bool Counts(JournalEntry line, IReadOnlySet<string> critics)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(critics);
        if (!string.Equals(Text(line.New?["result"]), "passed", StringComparison.Ordinal))
        {
            return false;
        }

        return line.ActorType switch
        {
            ActorType.Human => !string.IsNullOrWhiteSpace(line.ActorId) && line.AttemptId is null,
            ActorType.Attempt => line.AttemptId is { } attempt
                && string.Equals(line.ActorId, attempt, StringComparison.Ordinal)
                && string.Equals(Text(line.New?["role"]), CriticRole, StringComparison.Ordinal)
                && critics.Contains(attempt),
            _ => false,
        };
    }

    /// <summary>
    /// Of the attempts the item's review lines carry, those of the critic's work the runtime made for this send: the work
    /// was created by the runtime's own actor, as its created line says; its <see cref="SendReview.Key"/> names this send
    /// and its operation; and no line says that anybody but the runtime changed its context since. Read by the attempts the
    /// lines carry, and by each such work item's own lines, through the journal's work item index.
    /// </summary>
    internal static async Task<IReadOnlySet<string>> CriticsAsync(JasonDbContext db, WorkItem send, IEnumerable<JournalEntry> lines, CancellationToken ct)
    {
        var carried = lines.Where(line => line.ActorType == ActorType.Attempt && line.AttemptId is not null)
            .Select(line => line.AttemptId!)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var critics = new HashSet<string>(StringComparer.Ordinal);
        if (carried.Count == 0)
        {
            return critics;
        }

        var runs = await db.Attempts.AsNoTracking()
            .Where(attempt => carried.Contains(attempt.PublicId))
            .Select(attempt => new { attempt.PublicId, Work = attempt.WorkItem!.PublicId, attempt.WorkItem.Role, attempt.WorkItem.Context })
            .ToListAsync(ct)
            .ConfigureAwait(false);
        foreach (var run in runs)
        {
            if (!string.Equals(run.Role, CriticRole, StringComparison.Ordinal)
                || !string.Equals(SendReview.Named(run.Context), send.PublicId, StringComparison.Ordinal)
                || !string.Equals(SendReview.Operation(run.Context), send.Operation, StringComparison.Ordinal))
            {
                continue;
            }

            var own = await CriticLines(db, run.Work).ToListAsync(ct).ConfigureAwait(false);
            var created = own.Where(line => line.Kind == JournalKinds.WorkItemCreated).ToList();
            if (created.Count == 1
                && created[0].ActorType == ActorType.System
                && string.Equals(created[0].ActorId, Actors.Runtime.Id, StringComparison.Ordinal)
                && own.All(line => line.ActorType == ActorType.System))
            {
                critics.Add(run.PublicId);
            }
        }

        return critics;
    }

    /// <summary>A work item's created line and its context's changes, through the journal's work item index.</summary>
    internal static IQueryable<JournalEntry> CriticLines(JasonDbContext db, string workItemId) =>
        db.Journal.FromSqlInterpolated($"""
            SELECT * FROM journal INDEXED BY ix_journal_work_item_id_public_id
            WHERE work_item_id = {workItemId} AND kind IN ('workitem_created', 'workitem_context_updated')
            """).AsNoTracking();

    /// <summary>
    /// The mode, where it releases: automatic, set by a named person. A create asks it too, before the runtime creates the
    /// critic of a send.
    /// </summary>
    internal static ModeInForce? Releasing(ModeInForce mode) =>
        mode is { Mode: SendMode.Automatic, SetBy: { Type: ActorType.Human, Id: { } id } } && !string.IsNullOrWhiteSpace(id) ? mode : null;

    private static string? Awaiting(WarmthState state) => state switch
    {
        WarmthState.AwaitingJudgement => SendModeReasons.AwaitingJudgement,
        WarmthState.AwaitingNature => SendModeReasons.AwaitingNature,
        _ => null,
    };

    /// <summary>
    /// For an answer — a <c>conversation.reply</c>, or a <c>message.send</c> that names the verdict it answers — the reason
    /// it waits for the person, or null where the verdict it answers stands, says a person wrote the reply and does not say
    /// <c>escalate</c>. Work that is no answer is null.
    /// </summary>
    private static async Task<string?> AnswerAsync(JasonDbContext db, WorkItem item, ProviderOpPlan plan, CancellationToken ct)
    {
        var named = item.Context[StandingApprovals.VerdictKey];
        var reply = string.Equals(plan.Contract.Id, "conversation.reply", StringComparison.Ordinal);
        if (!reply && named is null)
        {
            return null;
        }

        var verdict = named is JsonValue value && value.TryGetValue<string>(out var cited)
            ? await NamedAsync(db, item.CampaignId, cited, ct).ConfigureAwait(false)
            : named is null && Text(plan.Input["args"]?["thread"]?["external_id"]) is { } thread
                ? await NewestOnThread(db, item.CampaignId, thread, plan.Plugin.Manifest.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false)
                : null;
        if (verdict?.New is not JsonObject said)
        {
            return SendModeReasons.AnswersNoVerdict;
        }

        switch (Text(said["nature"]))
        {
            case null:
                return SendModeReasons.AwaitingNature;
            case not "person":
                return SendModeReasons.ReplyNotFromAPerson;
        }

        return Escalates(said) || await EscalatedSinceAsync(db, item.CampaignId, said, ct).ConfigureAwait(false)
            ? SendModeReasons.Escalated
            : null;
    }

    private static bool Escalates(JsonObject said) => string.Equals(Text(said["reaction"]), "escalate", StringComparison.Ordinal);

    /// <summary>
    /// Whether a verdict that stands on the answered verdict's thread and plugin, about a reply at or after the one answered,
    /// says escalate: the conversation was marked for the person, whichever reply of it the answer names. A moment that is
    /// not known holds, since nothing tells the escalation is earlier.
    /// </summary>
    internal static async Task<bool> EscalatedSinceAsync(JasonDbContext db, int campaignId, JsonObject answered, CancellationToken ct)
    {
        if (Text(answered["thread"]) is not { } thread || Text(answered["plugin"]) is not { } plugin)
        {
            return true;
        }

        var from = Moment(answered["message_at"]);
        var verdicts = await OnThread(db, campaignId, thread, plugin).ToListAsync(ct).ConfigureAwait(false);
        return verdicts
            .GroupBy(line => line.Key, StringComparer.Ordinal)
            .Select(under => under.MaxBy(line => line.Id)!)
            .Where(standing => standing.New is JsonObject said && Escalates(said))
            .Any(standing => from is not { } since || Moment(standing.New!["message_at"]) is not { } at || at >= since);
    }

    private static DateTimeOffset? Moment(JsonNode? node) =>
        Text(node) is { } text && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at) ? at : null;

    /// <summary>Every verdict of a campaign on one thread through one plugin, through the verdicts' thread index.</summary>
    internal static IQueryable<JournalEntry> OnThread(JasonDbContext db, int campaignId, string thread, string plugin) =>
        db.Journal.FromSqlInterpolated($"""
            SELECT * FROM journal INDEXED BY ix_journal_campaign_verdict_thread
            WHERE kind = 'reply_classified' AND campaign_id = {campaignId}
              AND json_extract(new_json, '$.thread') = {thread} AND json_extract(new_json, '$.plugin') = {plugin}
            """).AsNoTracking();

    /// <summary>The verdict that stands for the reply the named line judged: the newest of the campaign under its key.</summary>
    private static async Task<JournalEntry?> NamedAsync(JasonDbContext db, int campaignId, string cited, CancellationToken ct)
    {
        var line = await db.Journal.AsNoTracking().FirstOrDefaultAsync(entry => entry.PublicId == cited, ct).ConfigureAwait(false);
        if (line is not { Kind: JournalKinds.ReplyClassified, Key: { } key } || line.CampaignId != campaignId
            || Text((line.New as JsonObject)?["thread"]) is not { } thread)
        {
            return null;
        }

        return await StandingUnder(db, campaignId, thread, key).FirstOrDefaultAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// The pass that releases the send, the newest that counts under its current review hash; or why there is none, with the
    /// line shown: a pass that counts under an older hash only, which an edit since voided, or none that counts at all, with
    /// the newest pass of the current text by anybody else where one stands.
    /// </summary>
    private static async Task<(string? Pass, string? Why, string? Shown)> PassAsync(JasonDbContext db, WorkItem item, CancellationToken ct)
    {
        var hash = ReviewSubject.Hash(item);
        var lines = await ReviewLines(db, item.PublicId).ToListAsync(ct).ConfigureAwait(false);
        var current = lines.Where(line => string.Equals(line.Key, hash, StringComparison.Ordinal)).ToList();
        var critics = await CriticsAsync(db, item, lines, ct).ConfigureAwait(false);
        if (current.LastOrDefault(line => Counts(line, critics)) is { } pass)
        {
            return (pass.PublicId, null, null);
        }

        if (lines.LastOrDefault(line => !string.Equals(line.Key, hash, StringComparison.Ordinal) && Counts(line, critics)) is { } older)
        {
            return (null, SendModeReasons.EditedAfterReview, older.PublicId);
        }

        var other = current.LastOrDefault(line => string.Equals(Text(line.New?["result"]), "passed", StringComparison.Ordinal));
        return (null, SendModeReasons.NotReviewed, other?.PublicId);
    }

    /// <summary>
    /// What the preview's <c>send_mode</c> says the step decided on: the mode and where it came from, the contact's
    /// temperature, and the review it read; the first three null while the contact waits on a verdict, the review null where
    /// none was read. Never the reason, which is the row's.
    /// </summary>
    internal static JsonObject Facts(ModeInForce? mode, Temperature? temperature, string? review) => new()
    {
        ["mode"] = mode is null ? null : JsonSerializer.SerializeToNode(mode.Mode, JasonJson.Options),
        ["source"] = mode is null ? null : JsonSerializer.SerializeToNode(mode.Source, JasonJson.Options),
        ["temperature"] = temperature is { } known ? Word(known) : null,
        ["review"] = review,
    };

    /// <summary>The word a temperature is said by: <c>cold</c> or <c>warm</c>.</summary>
    internal static string Word(Temperature temperature) => temperature == Temperature.Cold ? "cold" : "warm";

    private static string? Text(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
}

/// <summary>
/// The modes that release for a campaign, each null where it does not, and, only where either does, the contact's warmth,
/// its temperature and the mode of that temperature (<see cref="AutomaticRelease.ModeForAsync"/>).
/// </summary>
internal sealed record ModeForContact(ModeInForce? Cold, ModeInForce? Warm, WarmthState? State, Temperature? Temperature, ModeInForce? Mode)
{
    /// <summary>
    /// Whether a mode is in force for a send to this contact: the mode of the contact's temperature releases, or, for a
    /// contact who waits on a verdict, either mode does.
    /// </summary>
    public bool InForce => State is not null && (Temperature is null || Mode is not null);
}
