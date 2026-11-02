using System.Text.Json.Nodes;
using Jason.Contracts.Context;
using Jason.Contracts.Operations;
using Jason.Runtime.Domain;

namespace Jason.Runtime.Campaigns;

/// <summary>
/// The keys of a campaign's context the runtime holds to a published document (<see cref="ContextKeyCatalog"/>), read
/// where a context is written and nowhere else: whole at <c>campaign.create</c>, and at <c>campaign.update_context</c>
/// for each key the patch's <c>set</c> writes and its <c>unset</c> does not take away again. Every other key is the
/// caller's own and is never read.
/// </summary>
/// <remarks>
/// As a work item's <c>context.input</c> is read: a JSON null under a key is the key absent, a problem is reported at
/// <c>context.&lt;key&gt;</c> followed by its pointer with the code <c>invalid</c>, and a pattern that did not finish
/// is no verdict, so it is answered as unmeasured rather than refused.
/// </remarks>
internal static class ReservedContextKeys
{
    /// <summary>The keys of a context about to be written whole.</summary>
    public static void Validate(JsonObject? context, PatternTiming timing, ValidationErrors errors) =>
        Validate(context, null, timing, errors);

    /// <summary>
    /// The keys a patch writes. The patch writes the set before it honours the unset, so a key named in both ends up
    /// gone and is not read; a key the patch does not name is not read either, whatever is stored under it.
    /// </summary>
    public static void Validate(JsonObject? set, IReadOnlyList<string>? unset, PatternTiming timing, ValidationErrors errors)
    {
        ArgumentNullException.ThrowIfNull(timing);
        ArgumentNullException.ThrowIfNull(errors);
        if (set is null)
        {
            return;
        }

        foreach (var contract in ContextKeyCatalog.All)
        {
            if (!set.TryGetPropertyValue(contract.Key, out var value)
                || value is null
                || unset?.Contains(contract.Key, StringComparer.Ordinal) == true)
            {
                continue;
            }

            var field = "context." + contract.Key;
            var problems = contract.CheckValue(value, timing);
            foreach (var problem in SchemaValidator.Verdicts(problems))
            {
                errors.Add(field + problem.Pointer, "invalid", problem.Message);
            }

            foreach (var problem in problems.Where(problem => problem.Reason == SchemaValidator.PatternTimedOut))
            {
                errors.Unmeasured(field + problem.Pointer, problem.Message);
            }
        }
    }
}
