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

    /// <summary>
    /// Whether a failed attempt of <paramref name="contract"/> is worth another one. Three of the four classes
    /// mean the same thing whatever was being attempted — a transient failure may pass, and a permanent or a
    /// validation failure would fail the same way again — so only an ambiguous one reads the operation at all.
    /// </summary>
    /// <remarks>
    /// Ambiguous says the provider may already have acted, and what that costs is the operation's own business:
    /// a read may simply be repeated, a write whose contract obliges a recovery read first may be repeated
    /// because of that obligation, and one that declares <c>never</c> ends for a person rather than risking a
    /// second send. This is the only place the question is answered, so nothing can answer half of it.
    /// </remarks>
    public static bool Retriable(FailureClass failureClass, OperationContract contract)
    {
        ArgumentNullException.ThrowIfNull(contract);

        return failureClass switch
        {
            FailureClass.Transient => true,
            FailureClass.Ambiguous => contract.RepeatAfterAmbiguous is RepeatAfterAmbiguous.Safe or RepeatAfterAmbiguous.AfterRecoveryRead,
            _ => false,
        };
    }

    /// <summary>Validates a succeeded result against the operation's output schema.</summary>
    public static IReadOnlyList<SchemaProblem> CheckResult(OperationContract contract, JsonNode? result)
    {
        ArgumentNullException.ThrowIfNull(contract);

        return SchemaValidator.Validate(result, contract.OutputSchema);
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
