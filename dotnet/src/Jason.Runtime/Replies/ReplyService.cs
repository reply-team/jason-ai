using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Json;
using Jason.Contracts.Operations;
using Jason.Runtime.Campaigns;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Replies;

/// <summary>
/// A role's verdict on one reply, written as the reserved line <c>reply_classified</c> and by nothing else. The runtime
/// keeps the verdict and reads none of it: the category is held to being a word, never to a list, and the reaction and
/// goal are the decision a reader of the line acts on, not something this service does.
/// </summary>
/// <remarks>
/// What is checked against the database: the campaign (archived ones included, as the chronicle takes lines on an
/// archived campaign), the contact when one is named, the goal against the campaign's goals as they stand, and an attempt
/// actor, which must be a known attempt of work in the same campaign. Everything else is checked by its shape. The call
/// is not fenced by the attempt's lease: a verdict is a fact about a reply, and one recorded late is still true.
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

        if (request.Reason is not null && request.Reason.Trim().Length > CampaignService.MaxReasonLength)
        {
            errors.Add("reason", "too_long", string.Create(CultureInfo.InvariantCulture, $"reason must be at most {CampaignService.MaxReasonLength} characters."));
        }

        errors.ThrowIfAny();

        var campaign = await CampaignService.LoadAsync(db, request.CampaignId, cancellationToken).ConfigureAwait(false);

        Contact? contact = null;
        if (!string.IsNullOrWhiteSpace(request.ContactId))
        {
            var contactId = request.ContactId.Trim();
            contact = await db.Contacts.SingleOrDefaultAsync(c => c.PublicId == contactId, cancellationToken).ConfigureAwait(false)
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

        var (workItem, attempt) = await AttemptOfAsync(actor, campaign, cancellationToken).ConfigureAwait(false);

        var plugin = request.PluginId!.Trim();
        var thread = request.ThreadId!.Trim();
        var message = request.MessageId!.Trim();
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
        };

        var entry = journal.Append(
            db,
            actor,
            JournalKinds.ReplyClassified,
            campaign,
            key: InboundSync.VerdictKey(plugin, thread, message),
            updated: line,
            reason: string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim(),
            workItem: workItem,
            attempt: attempt,
            contact: contact);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return JournalWriter.ToDto(entry, campaign.PublicId) with { ContactId = contact?.PublicId };
    }

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
