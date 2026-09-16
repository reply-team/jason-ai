using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Operations;
using Jason.Contracts.Plugins;
using Jason.Runtime.Configuration;
using Jason.Runtime.Domain;
using Jason.Runtime.Persistence;
using Jason.Runtime.Plugins;
using Jason.Runtime.Plugins.Invocation;
using Jason.Runtime.Plugins.Registry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Jason.Runtime.Execution;

/// <summary>
/// What a plugin answered, turned into what the work item is. There is exactly one of these, and it ends the
/// attempt through <see cref="AttemptOutcomes"/> like every other kind of work: a provider operation is a kind
/// of work item, not a second execution engine.
/// </summary>
/// <remarks>
/// <para>
/// Validate first, act second. An answer the operation's own schema does not recognise is not a result, so it
/// never becomes one — the item fails <c>result_invalid</c> and the failing pointers say where, in the words
/// the validator used.
/// </para>
/// <para>
/// Whether a failure is worth another attempt is never decided here: the class comes from the plugin or from how
/// far the protocol got, and <see cref="OutcomeContract.Retriable"/> reads the operation's own contract.
/// </para>
/// </remarks>
public sealed class ProviderOutcomeRecorder(
    AttemptOutcomes outcomes,
    ExternalIdStore identifiers,
    IOptionsMonitor<PluginsOptions> plugins)
{
    /// <summary>
    /// Ends <paramref name="attempt"/> by what came back from the plugin. Nothing is saved: the outcome joins the
    /// caller's change set, so a pin and the attempt that learned it commit together or not at all.
    /// </summary>
    /// <param name="contract">The operation's published contract, as the claim resolved it.</param>
    /// <param name="pluginId">Which plugin answered — the package the claim pinned, never a name from elsewhere.</param>
    public async Task RecordAsync(
        JasonDbContext db,
        WorkItem item,
        Attempt attempt,
        OperationContract contract,
        string pluginId,
        PluginInvocationResult result,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(attempt);
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        ArgumentNullException.ThrowIfNull(result);

        var returned = Returned(result.Outcome);
        JsonNode? rejected = null;

        switch (result.Outcome)
        {
            case InvocationOutcome.Succeeded succeeded when Problems(contract, succeeded) is { Count: > 0 } problems:

                // A2. A shape error is deterministic, so the next attempt runs the same code over the same answer
                // and can only spend budget while a person waits. The operation's repeat rule has no vote here.
                rejected = Capped(succeeded.Result);
                outcomes.Fail(
                    db,
                    item,
                    attempt,
                    AttemptErrors.ResultInvalid,
                    $"The plugin's answer is not the shape '{contract.Id}' publishes, so nothing can read it as a result.",
                    trace: null,
                    ErrorDetail.From(problems),
                    Actors.Dispatcher,
                    failureClass: FailureClass.Ambiguous,
                    retriable: false);
                break;

            case InvocationOutcome.Succeeded succeeded:
                await PinAsync(db, contract, pluginId, item, attempt, succeeded.ExternalIds, cancellationToken).ConfigureAwait(false);
                outcomes.Succeed(db, item, attempt, succeeded.Result, Actors.Dispatcher);
                break;

            case InvocationOutcome.Failed failed:

                // N13. A failed answer may still carry an identifier, and the answer-lost case is where it matters
                // most: the effect happened, the answer did not come back, and the identifier is the only trace.
                // An undeclared kind is refused rather than recorded, but it does not replace the failure the
                // plugin reported — hiding the reason an operation failed behind a shape error helps nobody.
                await PinAsync(db, contract, pluginId, item, attempt, failed.Error.ExternalIds, cancellationToken).ConfigureAwait(false);
                outcomes.Fail(
                    db,
                    item,
                    attempt,
                    failed.Error.Code,
                    failed.Error.Message,
                    failed.Error.Details?.ToJsonString(),
                    Refused(contract, failed.Error.ExternalIds),
                    Actors.Dispatcher,
                    failureClass: failed.Error.Class,
                    retriable: OutcomeContract.Retriable(failed.Error.Class, contract));
                break;

            case InvocationOutcome.ProtocolFailure protocol:
                var failureClass = OutcomeClassification.ClassOf(protocol.Code);
                outcomes.Fail(
                    db,
                    item,
                    attempt,
                    protocol.Code,
                    protocol.Message,
                    protocol.StderrTail,
                    details: null,
                    Actors.Dispatcher,
                    failureClass: failureClass,
                    retriable: OutcomeContract.Retriable(failureClass, contract));
                break;

            default:
                throw new InvalidOperationException($"An invocation ended as {result.Outcome.GetType().Name}, which nothing here knows how to record.");
        }

        // The half of the record only the answer could write. It is additive, so it meets whatever the handler
        // already merged for this same invocation without either of them having to know about the other.
        _ = await AttemptProvenance.CompleteAsync(
                db,
                attempt.PublicId,
                new InvocationRecord(ExternalIdsReturned: returned, RejectedResult: rejected),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Everything the operation refuses about this answer, result and identifiers together.</summary>
    private static IReadOnlyList<SchemaProblem> Problems(OperationContract contract, InvocationOutcome.Succeeded succeeded) =>
        [.. OutcomeContract.CheckResult(contract, succeeded.Result), .. OutcomeContract.CheckExternalIds(contract, succeeded.ExternalIds)];

    /// <summary>
    /// Which of a failed answer's identifiers were not written down, named beside the failure that was kept. The
    /// same check the success path refuses the whole answer on: one reading of "the contract does not declare it".
    /// The pointers are addressed from the outcome's root, and a failed answer keeps its identifiers on the error
    /// — so that is what an author is sent to, rather than a key that is null in the document they wrote.
    /// </summary>
    private static IReadOnlyList<ErrorDetail>? Refused(OperationContract contract, JsonObject? externalIds) =>
        OutcomeContract.CheckExternalIds(contract, externalIds, OutcomeContract.OnTheError) is { Count: > 0 } problems
            ? ErrorDetail.From(problems)
            : null;

    /// <summary>
    /// The identifiers the plugin answered with, whichever way it ended, as it returned them. Anything that is
    /// not a string identifies nothing, and what nothing later can read is not recorded as having been said.
    /// </summary>
    private static IReadOnlyDictionary<string, string>? Returned(InvocationOutcome outcome)
    {
        var externalIds = outcome switch
        {
            InvocationOutcome.Succeeded succeeded => succeeded.ExternalIds,
            InvocationOutcome.Failed failed => failed.Error.ExternalIds,
            _ => null,
        };

        if (externalIds is null || externalIds.Count == 0)
        {
            return null;
        }

        var said = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (kind, node) in externalIds)
        {
            if (node is JsonValue value && value.TryGetValue(out string? identifier) && identifier is not null)
            {
                said[kind] = identifier;
            }
        }

        return said.Count == 0 ? null : said;
    }

    /// <summary>
    /// The answer a shape error rejected, kept so its author can see what was actually sent. It is bounded by the
    /// same setting that bounds what the invoker reads back, because this is the same document arriving by
    /// another route, and an attempt row is not where an unbounded document belongs.
    /// </summary>
    private JsonNode? Capped(JsonNode? result)
    {
        if (result is null)
        {
            return null;
        }

        var limit = plugins.CurrentValue.Invoker.OutcomeBytes;
        var bytes = Encoding.UTF8.GetByteCount(result.ToJsonString());
        return bytes <= limit
            ? result.DeepClone()
            : new JsonObject
            {
                ["omitted"] = JsonValue.Create("the rejected answer was larger than Plugins:Invoker:OutcomeBytes"),
                ["bytes"] = JsonValue.Create(bytes),
                ["limit"] = JsonValue.Create(limit),
            };
    }

    /// <summary>
    /// Applies what the plugin said each of our entities is called at the provider. The pins are brought in
    /// first, and only this plugin's: the store does no I/O of its own, so an entity whose pins were never
    /// loaded would look like an entity nobody has pinned, and a first pin would be written over a standing one.
    /// </summary>
    private async Task PinAsync(
        JasonDbContext db,
        OperationContract contract,
        string pluginId,
        WorkItem item,
        Attempt attempt,
        JsonObject? externalIds,
        CancellationToken cancellationToken)
    {
        if (externalIds is null || externalIds.Count == 0)
        {
            // Returning nothing is an ordinary answer: an operation pins what it learned, and it may learn nothing.
            return;
        }

        var entry = db.Entry(item);
        await entry.Reference(w => w.Campaign).LoadAsync(cancellationToken).ConfigureAwait(false);
        await entry.Reference(w => w.Contact).LoadAsync(cancellationToken).ConfigureAwait(false);
        await PinsAsync(db, item.Campaign!, pluginId, cancellationToken).ConfigureAwait(false);
        if (item.Contact is { } contact)
        {
            await PinsAsync(db, contact, pluginId, cancellationToken).ConfigureAwait(false);
        }

        identifiers.Apply(db, contract, pluginId, item, item.Campaign!, item.Contact, attempt, externalIds);
    }

    private static Task PinsAsync(JasonDbContext db, Campaign campaign, string pluginId, CancellationToken cancellationToken) =>
        db.Entry(campaign).Collection(entity => entity.ExternalIds).Query()
            .Where(pin => pin.PluginId == pluginId)
            .LoadAsync(cancellationToken);

    private static Task PinsAsync(JasonDbContext db, Contact contact, string pluginId, CancellationToken cancellationToken) =>
        db.Entry(contact).Collection(person => person.ExternalIds).Query()
            .Where(pin => pin.PluginId == pluginId)
            .LoadAsync(cancellationToken);
}
