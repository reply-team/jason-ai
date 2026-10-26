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
/// the validator used. An answer a schema pattern could not finish measuring is not refused: it is a lost answer,
/// ambiguous, and the operation's own repeat rule decides, because the plugin has already run.
/// </para>
/// <para>
/// Whether a failure is worth another attempt is never decided here: the class comes from the plugin or from how
/// far the protocol got, and <see cref="OutcomeContract.Retriable"/> reads the operation's own contract.
/// </para>
/// </remarks>
public sealed class ProviderOutcomeRecorder(
    AttemptOutcomes outcomes,
    ExternalIdStore identifiers,
    LiveSettings<PluginsOptions> plugins,
    PatternTiming? timing = null)
{
    /// <summary>Where a failed outcome carries its block, as a pointer from the outcome's root.</summary>
    private const string OnTheBlock = OutcomeContract.OnTheError + "/block";

    /// <summary>How an answer is matched against the output schema's patterns: the runtime's one default, which a test replaces.</summary>
    private readonly PatternTiming _timing = timing ?? PatternTiming.Default;

    /// <summary>
    /// Ends <paramref name="attempt"/> by what came back from the plugin. Nothing is saved: the outcome joins the
    /// caller's change set, so a pin and the attempt that learned it commit together or not at all.
    /// </summary>
    /// <param name="contract">The operation's published contract, as the claim resolved it.</param>
    /// <param name="pluginId">Which plugin answered — the package the claim pinned, never a name from elsewhere.</param>
    /// <param name="input">
    /// The input the claim composed, where a provider block holds the operation: the sending account it names is what
    /// a sender's block is matched by.
    /// </param>
    public async Task RecordAsync(
        JasonDbContext db,
        WorkItem item,
        Attempt attempt,
        OperationContract contract,
        string pluginId,
        PluginInvocationResult result,
        CancellationToken cancellationToken,
        JsonObject? input = null)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(attempt);
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        ArgumentNullException.ThrowIfNull(result);

        var returned = Returned(result.Outcome);
        JsonNode? rejected = null;

        // What this answer says about the provider's blocks, where one holds the operation at all: nothing is known
        // unless a branch below learns otherwise.
        var held = OperationCatalog.HeldByProviderBlock.Contains(contract.Id);
        var answer = BlockAnswer.Undecided;

        // Measured once: an answer is held to its contract a single time, and every branch below reads that verdict.
        var problems = result.Outcome is InvocationOutcome.Succeeded answered ? Problems(contract, answered, _timing) : [];

        switch (result.Outcome)
        {
            case InvocationOutcome.Succeeded succeeded when SchemaValidator.Unmeasured(problems):

                // The plugin has run and its effect may have landed; only the measuring of its answer failed. That
                // is a lost answer, so it is ambiguous and the operation's own repeat rule decides — calling it
                // transient would run a completed write again blind, which is what that rule exists to prevent. The
                // answer was never measured, so it is neither a result nor a refused one: it is kept on the attempt's
                // trace for a person, and nothing in it is written down, exactly as for an answer that never came.
                outcomes.Fail(
                    db,
                    item,
                    attempt,
                    AttemptErrors.ValidationTimedOut,
                    $"A pattern in the schema '{contract.Id}' publishes did not finish matching the plugin's answer in time, twice, so the answer was never measured; it is on this attempt's trace.",
                    Capped(succeeded.Result)?.ToJsonString(),
                    ErrorDetail.From(problems),
                    Actors.Dispatcher,
                    failureClass: FailureClass.Ambiguous,
                    retriable: OutcomeContract.Retriable(FailureClass.Ambiguous, contract));
                break;

            case InvocationOutcome.Succeeded succeeded when problems.Count > 0:

                // A2. A shape error is deterministic, so the next attempt runs the same code over the same answer
                // and can only spend budget while a person waits. The operation's repeat rule has no vote here. An
                // answer wrong somewhere is wrong whatever an unfinished pattern would have said of the rest, so
                // only what is known to be wrong is named.
                rejected = Capped(succeeded.Result);
                outcomes.Fail(
                    db,
                    item,
                    attempt,
                    AttemptErrors.ResultInvalid,
                    $"The plugin's answer is not the shape '{contract.Id}' publishes, so nothing can read it as a result.",
                    trace: null,
                    ErrorDetail.From(SchemaValidator.Verdicts(problems)),
                    Actors.Dispatcher,
                    failureClass: FailureClass.Ambiguous,
                    retriable: false);
                break;

            case InvocationOutcome.Succeeded succeeded:
                await PinAsync(db, contract, pluginId, item, attempt, succeeded.ExternalIds, succeeded.ExternalIdOrigins, cancellationToken)
                    .ConfigureAwait(false);
                outcomes.Succeed(db, item, attempt, succeeded.Result, Actors.Dispatcher);
                answer = BlockAnswer.GotPast;
                break;

            case InvocationOutcome.Failed failed when OutcomeContract.CheckFailure(contract, failed.Error) is { } outside:

                // A failure is read by its class, and a code the operation does not declare — or a declared code
                // under a class it never gives it — is a class no planner was written for. It is a defect in the
                // plugin, as a shape error is: the next attempt would run the same code over the same answer, so it
                // ends here and is never repeated, whatever the class it came with would have said. Nothing in it is
                // written down — no pin and no block, since an answer outside the contract stores nothing — but what
                // the plugin said is kept on the trace, where its author reads what was actually sent.
                outcomes.Fail(
                    db,
                    item,
                    attempt,
                    AttemptErrors.ResultInvalid,
                    $"The plugin reported a failure outside what '{contract.Id}' declares, so nothing can read its class; what it said is on this attempt's trace.",
                    Said(failed.Error),
                    ErrorDetail.From([outside]),
                    Actors.Dispatcher,
                    failureClass: FailureClass.Ambiguous,
                    retriable: false);
                break;

            case InvocationOutcome.Failed failed:

                // N13. A failed answer may still carry an identifier, and the answer-lost case is where it matters
                // most: the effect happened, the answer did not come back, and the identifier is the only trace.
                // An undeclared kind is refused rather than recorded, but it does not replace the failure the
                // plugin reported — hiding the reason an operation failed behind a shape error helps nobody.
                await PinAsync(db, contract, pluginId, item, attempt, failed.Error.ExternalIds, origins: null, cancellationToken).ConfigureAwait(false);

                // A block outside its contract is refused the same way an undeclared identifier is: the failure the
                // plugin reported stands, recorded permanent, with what is wrong with the block named beside it, and
                // the block itself is not kept, so nothing that later reads a block reads one the contract refuses.
                var recorded = OutcomeContract.Recorded(failed.Error, out var blockProblems);
                if (held && recorded == FailureClass.Blocked)
                {
                    // The provider refused for a reason outside the work and did nothing: the work has not been tried,
                    // so the attempt ends blocked, uncounted, and the item waits on the block the answer names.
                    var blocked = new AttemptErrorDto(
                        failed.Error.Code,
                        failed.Error.Message,
                        OutcomeContract.Retriable(recorded, contract),
                        failed.Error.Details?.ToJsonString(),
                        Refused(contract, failed.Error.ExternalIds, blockProblems),
                        recorded,
                        failed.Error.Block?.DeepClone().AsObject());
                    await outcomes.AnsweredAsync(db, item, attempt, pluginId, input, BlockAnswer.Blocked, failed.Error.Block, blocked, cancellationToken)
                        .ConfigureAwait(false);
                    held = false;
                    break;
                }

                // A permanent or validation failure is the provider's judgement of the work itself, so the call got
                // past whatever block was open; a class the contract changed on the way is not the plugin's word, and a
                // host's own ending says what became of the plugin, not what the provider answered.
                if (recorded == failed.Error.Class && recorded is FailureClass.Permanent or FailureClass.Validation
                    && !HostOutcomeCodes.All.Contains(failed.Error.Code))
                {
                    answer = BlockAnswer.GotPast;
                }

                outcomes.Fail(
                    db,
                    item,
                    attempt,
                    failed.Error.Code,
                    failed.Error.Message,
                    failed.Error.Details?.ToJsonString(),
                    Refused(contract, failed.Error.ExternalIds, blockProblems),
                    Actors.Dispatcher,
                    failureClass: recorded,
                    retriable: OutcomeContract.Retriable(recorded, contract),
                    block: recorded == FailureClass.Blocked ? failed.Error.Block : null);
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

        if (held)
        {
            await outcomes.AnsweredAsync(db, item, attempt, pluginId, input, answer, said: null, error: null, cancellationToken).ConfigureAwait(false);
        }

        // The half of the record only the answer could write. It is additive, so it meets whatever the handler
        // already merged for this same invocation without either of them having to know about the other.
        _ = await AttemptProvenance.CompleteAsync(
                db,
                attempt.PublicId,
                new InvocationRecord(ExternalIdsReturned: returned, RejectedResult: rejected, ExternalIdOrigins: Origins(result.Outcome)),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Everything the operation refuses about this answer, result and identifiers together.</summary>
    private static IReadOnlyList<SchemaProblem> Problems(OperationContract contract, InvocationOutcome.Succeeded succeeded, PatternTiming timing) =>
        [.. OutcomeContract.CheckResult(contract, succeeded.Result, timing), .. OutcomeContract.CheckExternalIds(contract, succeeded.ExternalIds)];

    /// <summary>
    /// Which of a failed answer's identifiers were not written down, and what is wrong with a block that was not
    /// kept, named beside the failure that was kept. The identifiers meet the same check the success path refuses the
    /// whole answer on: one reading of "the contract does not declare it". The pointers are addressed from the
    /// outcome's root, and a failed answer keeps its identifiers and its block on the error — so that is what an
    /// author is sent to, rather than a key that is null in the document they wrote.
    /// </summary>
    private static IReadOnlyList<ErrorDetail>? Refused(
        OperationContract contract,
        JsonObject? externalIds,
        IReadOnlyList<SchemaProblem> blockProblems)
    {
        IReadOnlyList<SchemaProblem> problems =
        [
            .. OutcomeContract.CheckExternalIds(contract, externalIds, OutcomeContract.OnTheError),
            .. blockProblems.Select(problem => problem with { Pointer = OnTheBlock + problem.Pointer }),
        ];

        return problems.Count > 0 ? ErrorDetail.From(problems) : null;
    }

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
    /// What the plugin said about whether the provider found or created what it identified, as it said it. Only a
    /// succeeded answer carries it; a plugin that said nothing leaves nothing here, and nothing is guessed in its place.
    /// </summary>
    private static IReadOnlyDictionary<string, string>? Origins(InvocationOutcome outcome)
    {
        if (outcome is not InvocationOutcome.Succeeded { ExternalIdOrigins: { Count: > 0 } origins })
        {
            return null;
        }

        var said = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (kind, node) in origins)
        {
            if (node is JsonValue value && value.TryGetValue(out string? origin) && origin is not null)
            {
                said[kind] = origin;
            }
        }

        return said.Count == 0 ? null : said;
    }

    /// <summary>
    /// What a plugin said in a failure the contract refuses, in the words of its own outcome. It arrived inside an
    /// outcome the invoker already bounded, so it needs no bound of its own; its identifiers are not repeated here,
    /// because the attempt's record already says what was returned.
    /// </summary>
    private static string Said(OutcomeError error) => new JsonObject
    {
        ["code"] = error.Code,
        ["class"] = error.Class.ToString().ToLowerInvariant(),
        ["message"] = error.Message,
        ["details"] = error.Details?.DeepClone(),
    }.ToJsonString();

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

        var limit = plugins.Current.Invoker.OutcomeBytes;
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
        JsonObject? origins,
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

        identifiers.Apply(db, contract, pluginId, item, item.Campaign!, item.Contact, attempt, externalIds, origins);
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
