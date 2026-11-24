using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Jason.Contracts.Api;
using Jason.Runtime.Configuration;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Routing;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Execution;

/// <summary>
/// What a read of a send says when it finds the send did not go: one <see cref="JournalKinds.MessageUnsentObserved"/>
/// line about the work item that made the send, for a send the one definition of a held send covers (<see cref="HeldSends"/>),
/// whoever made the read. A read that finds the send <c>failed</c> or <c>cancelled</c> says so at any moment; a read that
/// finds it still <c>queued</c> says it is <c>overdue</c> only where it is the last read the runtime makes of a deferred send
/// whose moment has passed (<see cref="DeferredReads"/>): it ends <c>Dispatcher:DeferredReadOverdueSeconds</c> or more
/// after the moment, no read the runtime made of the send is open, the reads it made since the moment have ended — the
/// most it makes that succeed, or one at least that span after the moment — and nothing yet says what became of the
/// send. Never earlier.
/// </summary>
/// <remarks>
/// Nothing is said of a send the campaign's own cancel took back: a succeeded <c>message.cancel</c> of the campaign naming
/// it is what took it back, and its line is the record. The line is said once per send, under the key of the item that
/// made it, and the database is what says so: the key is looked up here first, and a second line under it is refused by
/// <c>ix_journal_campaign_kind_key_unsent</c>. It holds identifiers, the state, the provider's own code where it is one
/// word and moments: never the provider's sentence for the failure, a subject or a body.
/// </remarks>
public sealed partial class SentFacts
{
    /// <summary>The state the line says of a send whose last read found it still held after it was due to go.</summary>
    public const string Overdue = "overdue";

    /// <summary>A provider's code the line carries: one word, as a bounce's is, never a server's text.</summary>
    private static readonly Regex OneWord = new(@"^[A-Za-z0-9][A-Za-z0-9._-]*\z", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>
    /// Adds the line to the context's change set (not saved here) where <paramref name="result"/> says the send did not
    /// go, or is the last read of a deferred send that found it still held, and answers whether it did.
    /// </summary>
    /// <param name="read">The work item that read the send, ended: its end is the moment the read is judged at.</param>
    /// <param name="pluginId">Which plugin answered the read, as the claim pinned it.</param>
    /// <param name="input">The input the claim composed: its <c>send</c> names the send's work item and its reference.</param>
    /// <param name="result">What the read answered, already held to the operation's schema.</param>
    public async Task<bool> SayUnsentAsync(
        JasonDbContext db,
        WorkItem read,
        string pluginId,
        JsonObject? input,
        JsonNode? result,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(read);
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);

        var state = result?["state"] is JsonValue said && said.TryGetValue<string>(out var text) ? text : null;
        if (state is not ("failed" or "cancelled" or "queued")
            || input?["send"] is not JsonObject named
            || named["work_item"]?.GetValue<string>() is not { } sendId
            || named["reference"]?.GetValue<string>() is not { } reference)
        {
            return false;
        }

        // Read, then written, as the sent line is: a campaign has at most one item scheduled or processing, so two reads of
        // one send never end at once, and the unique key on the campaign, the kind and the key is the floor beneath that.
        if (await db.Journal
                .AsNoTracking()
                .AnyAsync(
                    entry => entry.CampaignId == read.CampaignId && entry.Kind == JournalKinds.MessageUnsentObserved && entry.Key == sendId,
                    cancellationToken)
                .ConfigureAwait(false))
        {
            return false;
        }

        var held = state == "queued"
            ? await OverdueAsync(db, read, sendId, cancellationToken).ConfigureAwait(false)
            : await HeldSends.CoveredAsync(db, read.CampaignId, sendId, cancellationToken).ConfigureAwait(false);
        if (held is null)
        {
            return false;
        }

        var send = await db.WorkItems
            .AsNoTracking()
            .SingleAsync(item => item.PublicId == sendId && item.CampaignId == read.CampaignId, cancellationToken)
            .ConfigureAwait(false);
        var overdue = state == "queued";
        journal.Append(
            db,
            Actors.Dispatcher,
            JournalKinds.MessageUnsentObserved,
            campaign: null,
            key: send.PublicId,
            updated: new JsonObject
            {
                ["plugin"] = pluginId,
                ["reference"] = reference,
                ["channel"] = result!["channel"]?.DeepClone(),
                ["state"] = overdue ? Overdue : state,
                ["failure_code"] = overdue ? null : Word(result["failure_code"]),
                ["moment"] = InboundSync.Instant(held.Moment),
                ["cancelled_at"] = overdue ? null : result["cancelled_at"]?.DeepClone(),
                ["read_work_item"] = read.PublicId,
            },
            workItem: send);
        return true;
    }

    /// <summary>
    /// The deferred send <paramref name="read"/> is the last read of, where its end says the send is overdue; otherwise
    /// null. The read's own row is not saved yet, so it is counted here, as one that succeeded, where the runtime made it:
    /// the send is overdue where no read of the runtime's is still to come, its reads spent or the last past the span.
    /// </summary>
    private async Task<HeldSend?> OverdueAsync(JasonDbContext db, WorkItem read, string sendId, CancellationToken ct)
    {
        if (read.FinishedAt is not { } finished)
        {
            return null;
        }

        var ended = new DateTimeOffset(DateTime.SpecifyKind(finished, DateTimeKind.Utc));
        var passed = await HeldSends.ReadPassedAsync(db, ended, since: null, ct, read.CampaignId, sendId).ConfigureAwait(false);
        if (passed.Count == 0)
        {
            return null;
        }

        var send = passed[0];
        var due = send.Moment.AddSeconds((settings?.Current ?? Defaults).DeferredReadOverdueSeconds);
        if (ended < due)
        {
            return null;
        }

        if ((await SendReads.OpenAsync(db, read.CampaignId, [sendId], ct, except: read.PublicId).ConfigureAwait(false)).Count > 0)
        {
            return null;
        }

        var made = (await SendReads.MadeAsync(db, read.CampaignId, new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal) { [sendId] = send.Moment }, ct, except: read.PublicId)
            .ConfigureAwait(false)).GetValueOrDefault(sendId);
        if (read.CreatedByType == ActorType.System)
        {
            made = MadeReads.With(made, succeeded: true, ended);
        }

        return made is { } reads && (reads.Spent || reads.LastEnded >= due) ? send : null;
    }

    /// <summary>The provider's code where it is one word, as the line may carry it; null otherwise.</summary>
    private static string? Word(JsonNode? code) =>
        code is JsonValue value && value.GetValueKind() == JsonValueKind.String && value.GetValue<string>() is { Length: <= 128 } word && OneWord.IsMatch(word)
            ? word
            : null;

    /// <summary>The settings the runtime ships, for a composition that names none.</summary>
    private static DispatcherOptions Defaults { get; } = new();
}
