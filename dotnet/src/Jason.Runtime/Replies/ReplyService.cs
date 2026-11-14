using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Json;
using Jason.Contracts.Operations;
using Jason.Runtime.Campaigns;
using Jason.Runtime.Contacts;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Replies;

/// <summary>
/// A role's verdict on one reply, written as the reserved line <c>reply_classified</c> and by nothing else. The runtime
/// keeps the verdict and reads none of it here: the category is held to being a word, never to a list, and the reaction
/// and goal are the decision a reader of the line acts on, not something this service does. The nature is a word of a
/// closed list that <see cref="Warmth"/> reads later; it is never checked against the category or the reaction.
/// </summary>
/// <remarks>
/// One nature is acted on here, in the same act: a verdict that says <c>opt_out</c> puts every channel value the contact
/// has on the do-not-contact register, by <see cref="Actors.OptOut"/>, with the verdict as the reason, in the one save
/// that records the verdict; a value already registered gets no second entry, and nothing here lifts one. Nobody is asked.
/// The verdict's journal line names in its contact column the contact its body names, never the person of the run that
/// records it, so the claim's lock, which reads a contact's own lines, finds the person who asked.
/// </remarks>
/// <remarks>
/// What is checked against the database: the campaign (archived ones included, as the chronicle takes lines on an
/// archived campaign), the contact when one is named, the goal against the campaign's goals as they stand, an attempt
/// actor, which must be a known attempt of work in the same campaign, and a decision when one is named, whose answer the
/// verdict then carries and whose asker must have recorded a verdict on this reply. Everything else is checked by its shape. The call is not fenced by the attempt's lease: a
/// verdict is a fact about a reply, and one recorded late is still true.
/// </remarks>
public sealed class ReplyService(JasonDbContext db, JournalWriter journal)
{
    /// <summary>A category is a word or a short phrase from a skill's vocabulary, never a sentence about the reply.</summary>
    public const int MaxCategoryLength = 100;

    /// <summary>A provider's identifier for a plugin, a thread or a message; generous for any of them, short of a payload.</summary>
    public const int MaxIdentifierLength = 256;

    public async Task<JournalEntryDto> RecordAsync(ReplyRecordRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var actor = Actors.Resolve(request.Actor);

        var errors = new ValidationErrors();
        var category = Category(request.Category, errors);

        if (request.Confidence is not { } confidence)
        {
            errors.Add("confidence", "required", "confidence is required: a number from 0 to 1.");
        }
        else if (!double.IsFinite(confidence) || confidence < 0 || confidence > 1)
        {
            errors.Add("confidence", "invalid", "confidence must be a number from 0 to 1.");
        }

        if (request.Reaction is null)
        {
            errors.Add("reaction", "required", "reaction is required: act, draft, escalate or none.");
        }

        if (request.Nature == ReplyNature.OptOut && string.IsNullOrWhiteSpace(request.ContactId))
        {
            errors.Add("contact_id", "required", "contact_id is required with the nature opt_out: an opt-out stops a person.");
        }

        Identifier(errors, "plugin_id", request.PluginId);
        Identifier(errors, "thread_id", request.ThreadId);
        Identifier(errors, "message_id", request.MessageId);

        if (request.MessageAt is null)
        {
            errors.Add("message_at", "required", "message_at is required: the moment the reply arrived, in ISO-8601.");
        }

        if (request.GoalId is not null && request.GoalId.Trim().Length == 0)
        {
            errors.Add("goal_id", "invalid", "goal_id names one of the campaign's goals by its id, or is left out.");
        }

        if (request.DecisionId is not null && request.DecisionId.Trim().Length == 0)
        {
            errors.Add("decision_id", "invalid", "decision_id names the campaign's question whose answer this verdict carries, by its id, or is left out.");
        }

        if (request.Reason is not null && request.Reason.Trim().Length > CampaignService.MaxReasonLength)
        {
            errors.Add("reason", "too_long", string.Create(CultureInfo.InvariantCulture, $"reason must be at most {CampaignService.MaxReasonLength} characters."));
        }

        errors.ThrowIfAny();

        // An opt-out reads the contact's values and writes the register: BEGIN IMMEDIATE, so no other writer runs between
        // the read of what is registered and the commit, and two opt-outs of one person never race to the same entry.
        var optOut = request.Nature == ReplyNature.OptOut;
        await using var stop = optOut ? await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false) : null;

        var campaign = await CampaignService.LoadAsync(db, request.CampaignId, cancellationToken).ConfigureAwait(false);

        Contact? contact = null;
        if (!string.IsNullOrWhiteSpace(request.ContactId))
        {
            var contactId = request.ContactId.Trim();
            IQueryable<Contact> contacts = optOut ? db.Contacts.Include(c => c.Channels) : db.Contacts;
            contact = await contacts.SingleOrDefaultAsync(c => c.PublicId == contactId, cancellationToken).ConfigureAwait(false)
                ?? throw DomainErrors.ContactNotFound(contactId);
        }

        string? goalId = null;
        string? goalKind = null;
        if (request.GoalId is not null)
        {
            goalId = request.GoalId.Trim();
            goalKind = GoalKind(campaign.Context, goalId)
                ?? throw new ValidationException([new ErrorDetail("goal_id", "unknown", "goal_id must name a goal the campaign's goals carry.")]);
        }

        var plugin = request.PluginId!.Trim();
        var thread = request.ThreadId!.Trim();
        var message = request.MessageId!.Trim();
        var key = InboundSync.VerdictKey(plugin, thread, message);

        var answered = request.DecisionId is null
            ? null
            : await AnswerOfAsync(request.DecisionId.Trim(), campaign, category!, key, cancellationToken).ConfigureAwait(false);

        var (workItem, attempt) = await AttemptOfAsync(actor, campaign, cancellationToken).ConfigureAwait(false);

        var line = new JsonObject
        {
            ["plugin"] = plugin,
            ["thread"] = thread,
            ["message_id"] = message,
            ["message_at"] = InboundSync.Instant(request.MessageAt!.Value),
            ["contact_id"] = contact?.PublicId,
            ["category"] = category,
            ["confidence"] = request.Confidence!.Value,
            ["reaction"] = JsonSerializer.SerializeToNode(request.Reaction!.Value, JasonJson.Options),
            ["goal_id"] = goalId,
            ["goal_kind"] = goalKind,
            ["decision_id"] = answered?.PublicId,
            ["answered_by"] = answered is null
                ? null
                : JsonSerializer.SerializeToNode(new ActorRef(answered.AnsweredByType!.Value, answered.AnsweredById), JasonJson.Options),
            ["nature"] = request.Nature is { } nature ? JsonSerializer.SerializeToNode(nature, JasonJson.Options) : null,
        };

        var entry = journal.Append(
            db,
            actor,
            JournalKinds.ReplyClassified,
            campaign,
            key: key,
            updated: line,
            reason: string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim(),
            workItem: workItem,
            attempt: attempt,
            contact: contact);
        if (optOut)
        {
            await SuppressionService.StageAsync(
                db,
                journal,
                entry.Ts,
                Actors.OptOut,
                contact!.Channels.Select(channel => (channel.Channel, channel.Value)),
                OptOutReason(entry.PublicId, campaign.PublicId),
                cancellationToken).ConfigureAwait(false);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        if (stop is not null)
        {
            await stop.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

        return JournalWriter.ToDto(entry, campaign.PublicId) with { ContactId = contact?.PublicId };
    }

    /// <summary>
    /// The reason a register entry an opt-out made carries: the verdict that made it, by its id, and its campaign. No word of
    /// the reply or the verdict's own reason is repeated, and a reader looking for the entries one verdict made finds them by
    /// its id.
    /// </summary>
    internal static string OptOutReason(string verdict, string campaign) =>
        $"Asked not to be written to: opt-out verdict {verdict} in campaign {campaign}.";

    /// <summary>
    /// A category as every verb that takes one reads it: trimmed, one line, at most <see cref="MaxCategoryLength"/>
    /// characters, and otherwise compared exactly as written. A standing approval names its category by this same rule, so
    /// the word it was set for and the word a verdict carries are compared as the same kind of string.
    /// </summary>
    internal static string? Category(string? value, ValidationErrors errors)
    {
        var category = value?.Trim();
        if (string.IsNullOrEmpty(category))
        {
            errors.Add("category", "required", "category is required: a word that names what kind of reply this is.");
        }
        else if (category.Length > MaxCategoryLength)
        {
            errors.Add("category", "too_long", string.Create(CultureInfo.InvariantCulture, $"category must be at most {MaxCategoryLength} characters."));
        }
        else if (category.EnumerateRunes().Any(PreviewTemplate.ChangesTheLine))
        {
            errors.Add("category", "invalid", "category is one line of text, without control, format or line-break characters.");
        }

        return category;
    }

    private static void Identifier(ValidationErrors errors, string field, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add(field, "required", $"{field} is required.");
        }
        else if (value.Trim().Length > MaxIdentifierLength)
        {
            errors.Add(field, "too_long", string.Create(CultureInfo.InvariantCulture, $"{field} must be at most {MaxIdentifierLength} characters."));
        }
    }

    /// <summary>The kind of the goal the campaign's goals give this id, or null when they carry none by it.</summary>
    private static string? GoalKind(JsonObject context, string goalId)
    {
        if (context["goals"] is not JsonArray goals)
        {
            return null;
        }

        foreach (var goal in goals)
        {
            if (goal is JsonObject entry
                && entry["id"] is JsonValue id && id.TryGetValue<string>(out var value) && string.Equals(value, goalId, StringComparison.Ordinal)
                && entry["kind"] is JsonValue kind && kind.TryGetValue<string>(out var named))
            {
                return named;
            }
        }

        return null;
    }

    /// <summary>
    /// The decision whose answer the verdict carries, checked here and only here: a question of the same campaign, answered,
    /// by a person who gave their name, who chose the very category recorded, raised by a run that recorded a verdict on
    /// this reply. The line names the decision and the person, and what reads it later trusts it for that — nothing reads
    /// the decision again.
    /// </summary>
    /// <remarks>
    /// The asker's verdict is any <c>reply_classified</c> line of the campaign under the reply's key that its attempt wrote,
    /// not only the latest, so the manager may record the same answer on the same reply again; one decision may back many
    /// such records, which supersede each other with the same word. The lookup reads the journal's own columns — campaign,
    /// kind, key and attempt — and never what a line says.
    /// </remarks>
    private async Task<Decision> AnswerOfAsync(string id, Campaign campaign, string category, string key, CancellationToken cancellationToken)
    {
        var decision = await db.Decisions.AsNoTracking()
            .Include(row => row.Attempt)
            .SingleOrDefaultAsync(row => row.PublicId == id, cancellationToken).ConfigureAwait(false);
        if (decision is null || decision.CampaignId != campaign.Id)
        {
            throw DomainErrors.VerdictDecisionNotFound(id);
        }

        if (decision.Status != DecisionStatus.Answered)
        {
            throw DomainErrors.VerdictDecisionNotAnswered(decision.PublicId, decision.Status);
        }

        if (decision.AnsweredByType != ActorType.Human || string.IsNullOrWhiteSpace(decision.AnsweredById))
        {
            throw DomainErrors.VerdictDecisionNotHuman(decision.PublicId);
        }

        if (!string.Equals(decision.ChosenOption, category, StringComparison.Ordinal))
        {
            throw DomainErrors.VerdictDecisionOptionDiffers(decision.PublicId);
        }

        // The decision holds its attempt by the row's key, the chronicle by the attempt's public id.
        var asker = decision.Attempt!.PublicId;
        var aboutThisReply = await AskersVerdict(db, campaign.Id, key, asker).AnyAsync(cancellationToken).ConfigureAwait(false);
        if (!aboutThisReply)
        {
            throw DomainErrors.VerdictDecisionNotAboutReply(decision.PublicId);
        }

        return decision;
    }

    /// <summary>
    /// A verdict the asking attempt recorded on the reply, in the campaign: any such line, superseded or not. Read through
    /// the kind's own index, as a standing approval reads a later verdict, so it is bounded by the verdicts and never walks
    /// the chronicle; no index of its own is kept for it.
    /// </summary>
    internal static IQueryable<int> AskersVerdict(JasonDbContext db, int campaignId, string key, string attemptId) =>
        db.Database.SqlQuery<int>($"""
            SELECT id AS "Value" FROM journal INDEXED BY ix_journal_kind
            WHERE kind = 'reply_classified' AND campaign_id = {campaignId} AND key = {key} AND attempt_id = {attemptId}
            LIMIT 1
            """);

    /// <summary>
    /// An attempt claims a verdict as the run that decided it, so the line follows back to its work; the claim is checked
    /// to be an attempt this runtime holds, of work in the same campaign.
    /// </summary>
    private async Task<(WorkItem? Item, Attempt? Attempt)> AttemptOfAsync(ActorRef actor, Campaign campaign, CancellationToken cancellationToken)
    {
        if (actor.Type != ActorType.Attempt)
        {
            return (null, null);
        }

        await ActorVerification.VerifyAsync(db, actor, cancellationToken).ConfigureAwait(false);
        var attempt = await db.Attempts.Include(a => a.WorkItem)
            .SingleAsync(a => a.PublicId == actor.Id, cancellationToken).ConfigureAwait(false);
        if (attempt.WorkItem is not { } item || item.CampaignId != campaign.Id)
        {
            throw new ValidationException([new ErrorDetail("actor.id", "invalid", "actor.id must name an attempt of work in the same campaign.")]);
        }

        return (item, attempt);
    }
}
