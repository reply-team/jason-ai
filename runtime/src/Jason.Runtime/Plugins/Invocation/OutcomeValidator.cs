using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Jason.Contracts.Json;
using Jason.Contracts.Plugins;

namespace Jason.Runtime.Plugins.Invocation;

/// <summary>
/// Reads the child's stdout back as an outcome, and believes nothing until every rule of the protocol holds. The
/// child is a separate program that ran code the user installed: that it exited zero says the protocol completed,
/// not that what it wrote is an answer. Whatever fails here is a protocol failure, never the plugin's verdict.
/// </summary>
/// <remarks>
/// The shape and the sizes are all that can be checked in this version — canonical result schemas do not exist
/// yet, so a well-formed outcome whose result is nonsense for the operation still passes.
/// </remarks>
public static partial class OutcomeValidator
{
    /// <summary>
    /// True when <paramref name="stdout"/> is exactly one outcome for <paramref name="expectedInvocationId"/>.
    /// Otherwise <paramref name="problem"/> names the first rule that was broken, in the protocol's words.
    /// </summary>
    public static bool TryValidate(string stdout, string expectedInvocationId, out PluginOutcome? outcome, out string problem)
    {
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedInvocationId);

        outcome = null;
        if (!IsExactlyOneObject(stdout, out problem))
        {
            return false;
        }

        PluginOutcome? read;
        try
        {
            read = JsonSerializer.Deserialize<PluginOutcome>(stdout, JasonJson.Options);
        }
        catch (JsonException exception)
        {
            problem = $"stdout is not an outcome document: {exception.Message}";
            return false;
        }

        if (read is null)
        {
            problem = "stdout carried the JSON literal null instead of an outcome.";
            return false;
        }

        if (!Check(read, expectedInvocationId, out problem))
        {
            return false;
        }

        outcome = read;
        return true;
    }

    private static bool Check(PluginOutcome read, string expectedInvocationId, out string problem)
    {
        if (read.ProtocolVersion != PluginProtocol.CurrentVersion)
        {
            problem = string.Create(CultureInfo.InvariantCulture, $"protocol_version must be {PluginProtocol.CurrentVersion}; the outcome says {read.ProtocolVersion}.");
            return false;
        }

        if (!string.Equals(read.InvocationId, expectedInvocationId, StringComparison.Ordinal))
        {
            problem = $"invocation_id must be '{expectedInvocationId}'; the outcome says '{read.InvocationId}'.";
            return false;
        }

        if (!Enum.IsDefined(read.Status))
        {
            problem = "status must be 'succeeded' or 'failed'.";
            return false;
        }

        if (read.Diagnostics is null)
        {
            problem = "diagnostics must be present.";
            return false;
        }

        if (read.Status == OutcomeStatus.Succeeded && read.Error is not null)
        {
            problem = "a succeeded outcome carries no error.";
            return false;
        }

        if (read.Status == OutcomeStatus.Failed && read.Error is null)
        {
            problem = "a failed outcome must carry an error.";
            return false;
        }

        if (!Fits(read.Result, PluginProtocol.MaxResultBytes, "result", out problem))
        {
            return false;
        }

        if (!IdsFit(read.ExternalIds, "external_ids", out problem))
        {
            return false;
        }

        return read.Error is null || Error(read.Error, out problem);
    }

    private static bool Error(OutcomeError error, out string problem)
    {
        if (!Enum.IsDefined(error.Class))
        {
            problem = "error.class must be one of transient, permanent, validation, ambiguous.";
            return false;
        }

        if (error.Code is null || !CodePattern().IsMatch(error.Code))
        {
            problem = $"error.code must be lowercase snake_case of at most {PluginProtocol.MaxErrorCodeLength} characters; the outcome says '{error.Code}'.";
            return false;
        }

        if (error.Message is null || error.Message.Length > PluginProtocol.MaxErrorMessageLength)
        {
            problem = string.Create(CultureInfo.InvariantCulture, $"error.message is at most {PluginProtocol.MaxErrorMessageLength} characters; the outcome wrote {error.Message?.Length ?? 0}.");
            return false;
        }

        return Fits(error.Details, PluginProtocol.MaxDetailsBytes, "error.details", out problem)
            && IdsFit(error.ExternalIds, "error.external_ids", out problem);
    }

    /// <summary>
    /// One JSON document and no more. A child that writes twice has broken the channel the outcome travels on,
    /// and taking the first document would be reading past the point where the protocol stopped holding.
    /// </summary>
    private static bool IsExactlyOneObject(string stdout, out string problem)
    {
        problem = string.Empty;
        var bytes = Encoding.UTF8.GetBytes(stdout);

        // Reading past the first document is allowed here so that a second one is reported as what it is,
        // rather than as a syntax error at the point where the first one correctly ended.
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { AllowMultipleValues = true });
        try
        {
            if (!reader.Read())
            {
                problem = "stdout carried no JSON document.";
                return false;
            }

            if (reader.TokenType != JsonTokenType.StartObject)
            {
                problem = "stdout is not a JSON object.";
                return false;
            }

            reader.Skip();
            if (reader.Read())
            {
                problem = "stdout carries more than one JSON document; an invocation answers exactly once.";
                return false;
            }
        }
        catch (JsonException exception)
        {
            problem = $"stdout is not valid JSON: {exception.Message}";
            return false;
        }

        return true;
    }

    private static bool Fits(JsonNode? node, int maxBytes, string name, out string problem)
    {
        problem = string.Empty;
        if (node is null)
        {
            return true;
        }

        var bytes = Encoding.UTF8.GetByteCount(node.ToJsonString());
        if (bytes <= maxBytes)
        {
            return true;
        }

        problem = string.Create(CultureInfo.InvariantCulture, $"{name} is at most {maxBytes} bytes; the outcome wrote {bytes}.");
        return false;
    }

    private static bool IdsFit(JsonObject? ids, string name, out string problem)
    {
        problem = string.Empty;
        if (ids is null)
        {
            return true;
        }

        if (ids.Count > PluginProtocol.MaxExternalIds)
        {
            problem = string.Create(CultureInfo.InvariantCulture, $"{name} holds at most {PluginProtocol.MaxExternalIds} entries; the outcome wrote {ids.Count}.");
            return false;
        }

        foreach (var (key, value) in ids)
        {
            var text = value is JsonValue single && single.TryGetValue<string>(out var read) ? read : null;
            if (text is null)
            {
                problem = $"{name}.{key} must be a string.";
                return false;
            }

            if (text.Length > PluginProtocol.MaxExternalIdLength)
            {
                problem = string.Create(CultureInfo.InvariantCulture, $"{name}.{key} is at most {PluginProtocol.MaxExternalIdLength} characters; the outcome wrote {text.Length}.");
                return false;
            }
        }

        return true;
    }

    [GeneratedRegex(@"^[a-z][a-z0-9_]{0,63}$")]
    private static partial Regex CodePattern();
}
