using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Plugins;

namespace Jason.Contracts.Operations;

/// <summary>
/// What the runtime checks in a plugin's answer once the protocol itself is satisfied: that a succeeded result is
/// the shape the operation promises, and that every identifier it hands back is a kind the operation declares.
/// </summary>
/// <remarks>
/// A well-formed outcome whose content is nonsense for the operation used to pass. It no longer does: a result the
/// contract does not recognise is a defect in the plugin, and pinning an identifier of an undeclared kind would put
/// a value into Jason's record that nothing later knows how to read.
/// </remarks>
public static class OutcomeContract
{
    /// <summary>The longest a provider's own identifier may be, matching what the protocol already accepts.</summary>
    public const int MaxExternalIdLength = 256;

    /// <summary>
    /// Where a failed answer keeps its identifiers: on the error, which is where <c>host.fail</c> puts them and
    /// the only place the runtime reads them. A pointer into a failed outcome is prefixed with this, so what an
    /// author is sent to is the key they actually wrote rather than one that is null in their document.
    /// </summary>
    public const string OnTheError = "/error";

    /// <summary>Where a failure outside its operation's declared codes is wrong when the code itself is not declared.</summary>
    private const string OnTheCode = OnTheError + "/code";

    /// <summary>Where it is wrong when the code is declared, under another class.</summary>
    private const string OnTheClass = OnTheError + "/class";

    /// <summary>
    /// Whether a failed attempt of <paramref name="contract"/> is worth another one. Four of the five classes
    /// mean the same thing whatever was being attempted — a transient failure may pass, a permanent or a
    /// validation failure would fail the same way again, and a blocked one waits on something outside the work
    /// item — so only an ambiguous one reads the operation at all.
    /// </summary>
    /// <remarks>
    /// Ambiguous says the provider may already have acted, and what that costs is the operation's own business:
    /// a read may simply be repeated, a write whose contract obliges a recovery read first may be repeated
    /// because of that obligation, and one that declares <c>never</c> ends for a person rather than risking a
    /// second send. This is the only place the question is answered, so nothing can answer half of it: this is what
    /// the contract allows, and the overload beside it adds the package's half, which the runtime asks.
    /// </remarks>
    public static bool Retriable(FailureClass failureClass, OperationContract contract)
    {
        ArgumentNullException.ThrowIfNull(contract);

        return failureClass switch
        {
            FailureClass.Transient => true,
            FailureClass.Ambiguous => contract.RepeatAfterAmbiguous is RepeatAfterAmbiguous.Safe or RepeatAfterAmbiguous.AfterRecoveryRead,

            // Final until the runtime parks work on a block: repeating at once would meet the same block, and the
            // block clearing is no part of an attempt's budget.
            FailureClass.Blocked => false,
            _ => false,
        };
    }

    /// <summary>
    /// The same question with the package's half of it: an ambiguous end of an operation whose recovery read settles a
    /// lost answer by the work item's key is worth another attempt only where the package that ran it says it makes every
    /// attempt under that key and settles the operation by it — <paramref name="packageSettlesByKey"/>, from its manifest.
    /// Without it a repeat could be a second send, so the end is final, as <c>never</c> would make it. Every other answer
    /// is the contract's alone.
    /// </summary>
    public static bool Retriable(FailureClass failureClass, OperationContract contract, bool packageSettlesByKey) =>
        Retriable(failureClass, contract)
        && (failureClass != FailureClass.Ambiguous || !SettlesByKey(contract) || packageSettlesByKey);

    /// <summary>Whether <paramref name="contract"/> settles a lost answer by the work item's key (<c>recovery_read.by_key</c>).</summary>
    public static bool SettlesByKey(OperationContract contract)
    {
        ArgumentNullException.ThrowIfNull(contract);
        return contract.RecoveryRead is { ByKey: true };
    }

    /// <summary>
    /// The class a failure is recorded under: the plugin's own, except that a block outside its published contract is
    /// recorded <see cref="FailureClass.Permanent"/>, with what is wrong with it in <paramref name="blockProblems"/>.
    /// An answer that does not satisfy the contract is a refused answer, so a malformed block never stands for a real
    /// one, and nothing that later reads the class acts on data the contract refuses.
    /// </summary>
    public static FailureClass Recorded(OutcomeError error, out IReadOnlyList<SchemaProblem> blockProblems)
    {
        ArgumentNullException.ThrowIfNull(error);

        blockProblems = error.Class == FailureClass.Blocked ? ProviderBlockContract.Published.Check(error.Block) : [];
        return blockProblems.Count == 0 ? error.Class : FailureClass.Permanent;
    }

    /// <summary>
    /// Whether a failure is one <paramref name="contract"/> declares, under the class it declares it with; null when it
    /// is. The class is what everything after the attempt reads — whether it is repeated, whether a person is asked,
    /// whether an account is held back — so a code no planner was written for, or a declared code under a class the
    /// operation never gives it, is an answer outside the contract and is reported at the half of it that is wrong.
    /// </summary>
    /// <remarks>
    /// The host's own endings (<see cref="HostOutcomeCodes"/>) belong to no operation and pass beside every one of
    /// them, but only under the class the host itself writes: a plugin can say any code, and a host code under
    /// another class is the plugin's word, not the host's. The codes reserved beside every operation
    /// (<see cref="UnjudgedOutcomeCodes.Reserved"/>) pass beside every one the same way, each under the one class it is
    /// reserved with: what a package could not read in the provider's object
    /// (<see cref="UnjudgedOutcomeCodes.ProviderInputInvalid"/>) is no operation's argument, so no operation's list
    /// could declare it, and which of an operation's parts a provider has not shipped yet
    /// (<see cref="UnjudgedOutcomeCodes.CapabilityNotOffered"/>) is no list's to say in advance.
    /// </remarks>
    public static SchemaProblem? CheckFailure(OperationContract contract, OutcomeError error)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(error);

        var declared = contract.FailureCodes.FirstOrDefault(code => string.Equals(code.Code, error.Code, StringComparison.Ordinal));
        if (declared is not null)
        {
            return declared.Class == error.Class
                ? null
                : new SchemaProblem(
                    OnTheClass,
                    "enum",
                    $"`{contract.Id}` declares `{error.Code}` as `{Word(declared.Class)}`, and the plugin reported it `{Word(error.Class)}`.");
        }

        return HostOutcomeCodes.Writes(error.Class, error.Code) || UnjudgedOutcomeCodes.Reserves(error.Class, error.Code)
            ? null
            : new SchemaProblem(
                OnTheCode,
                "enum",
                $"`{contract.Id}` declares no failure code `{error.Code}`; a plugin reports a failure under a code its operation declares.");
    }

    private static string Word(FailureClass failureClass) => failureClass.ToString().ToLowerInvariant();

    /// <summary>Validates a succeeded result against the operation's output schema.</summary>
    public static IReadOnlyList<SchemaProblem> CheckResult(OperationContract contract, JsonNode? result) =>
        CheckResult(contract, result, PatternTiming.Default);

    /// <summary>The same check, with the schema's patterns matched under <paramref name="timing"/>.</summary>
    public static IReadOnlyList<SchemaProblem> CheckResult(OperationContract contract, JsonNode? result, PatternTiming timing)
    {
        ArgumentNullException.ThrowIfNull(contract);

        return SchemaValidator.Validate(result, contract.OutputSchema, timing);
    }

    /// <summary>Where a result carries the reference of the act a work item made the provider take.</summary>
    public const string ReferencePointer = "/items/0/reference";

    /// <summary>
    /// The reference of the act a work item made the provider take is said twice in a succeeded answer: beside the result,
    /// under the kind the operation declares for its <see cref="PinnedEntity.WorkItem"/>, and in the result at
    /// <see cref="ReferencePointer"/>. The two are one value, or both are absent and the result's reference is null: a
    /// reference said once, or two that differ, would leave a person reading one and a later call working by the other.
    /// </summary>
    public static IReadOnlyList<SchemaProblem> CheckReference(OperationContract contract, JsonNode? result, JsonObject? externalIds)
    {
        ArgumentNullException.ThrowIfNull(contract);

        var problems = new List<SchemaProblem>();
        foreach (var (kind, declared) in contract.ExternalIds)
        {
            if (declared.Entity != PinnedEntity.WorkItem)
            {
                continue;
            }

            var returned = externalIds?[kind] is JsonValue said && said.TryGetValue(out string? text) ? text : null;
            var carried = result is JsonObject answer
                && answer["items"] is JsonArray { Count: > 0 } items
                && items[0] is JsonObject item
                && item["reference"] is JsonValue reference
                && reference.TryGetValue(out string? written)
                ? written
                : null;
            if (!string.Equals(returned, carried, StringComparison.Ordinal))
            {
                var pointer = "/external_ids/" + kind;
                problems.Add(new SchemaProblem(
                    pointer,
                    "const",
                    $"The identifier under `{kind}` is the result's `items[0].reference`, said in both places or in neither, and this answer says them apart."));
            }
        }

        return problems;
    }

    /// <summary>Every returned external-id key must be a kind the contract declares, carrying a string value.</summary>
    /// <param name="pointerPrefix">
    /// Where in the outcome these identifiers were read from: empty beside a result, <see cref="OnTheError"/> on
    /// a failure. A pointer is an address into the document its author wrote, so it has to carry the half of the
    /// path the caller knows and the check does not.
    /// </param>
    public static IReadOnlyList<SchemaProblem> CheckExternalIds(
        OperationContract contract,
        JsonObject? externalIds,
        string pointerPrefix = "")
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(pointerPrefix);

        var problems = new List<SchemaProblem>();
        if (externalIds is null)
        {
            // Returning nothing is an ordinary answer: an operation pins what it learned, and it may learn nothing.
            return problems;
        }

        foreach (var (kind, value) in externalIds)
        {
            var pointer = pointerPrefix + "/external_ids/" + kind.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);
            if (!contract.ExternalIds.ContainsKey(kind))
            {
                problems.Add(new SchemaProblem(
                    pointer,
                    "additional_properties",
                    $"`{contract.Id}` declares no identifier of kind `{kind}`, and an undeclared pin is one nothing later knows how to read."));
                continue;
            }

            if (value is not JsonValue text || value.GetValueKind() != JsonValueKind.String || !text.TryGetValue(out string? identifier) || identifier is null)
            {
                problems.Add(new SchemaProblem(pointer, "type", $"The identifier under `{kind}` is a string, exactly as the provider wrote it."));
                continue;
            }

            if (identifier.Length == 0)
            {
                problems.Add(new SchemaProblem(pointer, "min_length", $"The identifier under `{kind}` is empty, which identifies nothing."));
            }
            else if (identifier.Length > MaxExternalIdLength)
            {
                problems.Add(new SchemaProblem(
                    pointer,
                    "max_length",
                    string.Create(CultureInfo.InvariantCulture, $"An identifier is at most {MaxExternalIdLength} characters.")));
            }
            else if (ControlCharacterIn(identifier) is { } control)
            {
                problems.Add(new SchemaProblem(
                    pointer,
                    "pattern",
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"The identifier under `{kind}` carries U+{(int)control:X4}; an identifier is printed to a person as it stands, so it carries no control character.")));
            }
        }

        return problems;
    }

    /// <summary>
    /// The first control character <paramref name="identifier"/> carries, or null where it carries none.
    /// </summary>
    /// <remarks>
    /// A provider's identifier is length-checked and then rendered verbatim into a person's terminal, beside the
    /// rest of a table. A newline in it forges a row, an escape sequence rewrites whatever is already on the
    /// screen, and a NUL ends the value somewhere no reader expects. Nothing downstream is in a position to
    /// decide this — a renderer that escaped the value would still be showing a person something the provider
    /// composed — so it is refused where it would be written down, and one place answers the question for both
    /// the contract's check and the store's.
    /// </remarks>
    public static char? ControlCharacterIn(string identifier)
    {
        ArgumentNullException.ThrowIfNull(identifier);

        foreach (var character in identifier)
        {
            if (char.IsControl(character))
            {
                return character;
            }
        }

        return null;
    }
}
