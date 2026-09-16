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
    public static IReadOnlyList<SchemaProblem> CheckExternalIds(OperationContract contract, JsonObject? externalIds)
    {
        ArgumentNullException.ThrowIfNull(contract);

        var problems = new List<SchemaProblem>();
        if (externalIds is null)
        {
            // Returning nothing is an ordinary answer: an operation pins what it learned, and it may learn nothing.
            return problems;
        }

        foreach (var (kind, value) in externalIds)
        {
            var pointer = "/external_ids/" + kind.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);
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
        }

        return problems;
    }
}
