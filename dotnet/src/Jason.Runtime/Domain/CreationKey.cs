using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Json;
using Jason.Runtime.Json;

namespace Jason.Runtime.Domain;

/// <summary>
/// The key a caller gives <c>campaign.create</c> or <c>workitem.create</c>, and the one name of the request it came
/// with. The key is a property of the entity the request created, stored on its row, so a repeat of the same request
/// answers with that entity and a different request under the same key is refused; nothing here keeps anything.
/// </summary>
/// <remarks>
/// <para>
/// The key is the caller's text. It is taken as typed apart from the white space around it — which is how a reporter's
/// key is taken, so the two never behave differently — and refused, never quoted, when it cannot be one.
/// </para>
/// <para>
/// The digest is what "the same request" means: the request as the runtime understood it, with who asked, why, and the
/// key itself left out. How a request was spelled never makes two of it — the order of its fields, white space, an
/// escape, an absent field against a null one, a string the service would trim, an instant in another offset, a number
/// written another way — and anything it asks for always does.
/// </para>
/// </remarks>
public static class CreationKey
{
    public const string Field = "idempotency_key";

    public const int MaxLength = 128;

    /// <summary>The key, trimmed, or null when there is none; a key that cannot be one is a detail in <paramref name="errors"/>.</summary>
    public static string? Read(string? raw, ValidationErrors errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        if (raw is null)
        {
            return null;
        }

        var key = raw.Trim();
        if (key.Length == 0)
        {
            errors.Add(Field, "invalid", "idempotency_key must hold something other than white space.");
            return null;
        }

        if (key.Length > MaxLength)
        {
            errors.Add(Field, "too_long", string.Create(CultureInfo.InvariantCulture, $"idempotency_key must be at most {MaxLength} characters."));
            return null;
        }

        if (!IsStorable(key))
        {
            errors.Add(Field, "invalid", "idempotency_key must hold no control character and no half of a surrogate pair.");
            return null;
        }

        return key;
    }

    /// <summary>What a work-item request asks for, named.</summary>
    public static string Digest(WorkItemCreateRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Named(new JsonObject
        {
            ["campaign_id"] = Trimmed(request.CampaignId),
            ["kind"] = request.Kind is { } kind ? JsonSerializer.SerializeToNode(kind, JasonJson.Options) : null,
            ["role"] = Trimmed(request.Role),
            ["operation"] = Trimmed(request.Operation),
            ["contact_id"] = Trimmed(request.ContactId),
            ["execution_profile"] = Trimmed(request.ExecutionProfile),
            ["priority"] = request.Priority,
            ["not_before"] = Instant(request.NotBefore),
            ["due_at"] = Instant(request.DueAt),
            ["timeout_seconds"] = request.TimeoutSeconds,
            ["heartbeat_seconds"] = request.HeartbeatSeconds,
            ["max_attempts"] = request.MaxAttempts,
            ["context"] = Document(request.Context),
            ["result_format"] = Document(request.ResultFormat),

            // Named only when the request named them, so a key stored before items could carry them still matches the
            // request that made it.
            ["ask_person_note"] = Trimmed(request.AskPersonNote),
            ["wait_on"] = request.WaitOn is { } waitOn ? new JsonArray([.. waitOn.Select(id => JsonValue.Create(id?.Trim()))]) : null,
        });
    }

    /// <summary>What a campaign request asks for, named.</summary>
    public static string Digest(CampaignCreateRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Named(new JsonObject
        {
            ["name"] = Trimmed(request.Name),
            ["context"] = Document(request.Context),

            // Named only when the request named it, so a key stored before campaigns had projects still matches the
            // request that made it.
            ["project_id"] = Trimmed(request.ProjectId),
        });
    }

    /// <summary>The fields a request left null are not in it, so an absent field and a null one are the same request.</summary>
    private static string Named(JsonObject document)
    {
        foreach (var field in document.Where(member => member.Value is null).Select(member => member.Key).ToList())
        {
            document.Remove(field);
        }

        return CanonicalJson.Hash(document);
    }

    /// <summary>The service's own rule for the strings it stores: white space around them goes, and white space alone is none.</summary>
    private static string? Trimmed(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? Instant(DateTimeOffset? value) =>
        value?.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);

    /// <summary>
    /// A document the request carries, copied with every number written in one form. The parser keeps a number as it
    /// was written, so <c>1</c>, <c>1.0</c> and <c>1e0</c> would otherwise be three requests; the canonical form takes care
    /// of everything else a spelling can change.
    /// </summary>
    private static JsonNode? Document(JsonNode? node) => node switch
    {
        null => null,
        JsonObject obj => new JsonObject(obj.Select(member => KeyValuePair.Create(member.Key, Document(member.Value)))),
        JsonArray array => new JsonArray([.. array.Select(Document)]),
        JsonValue value when value.GetValueKind() == JsonValueKind.Number => JsonNode.Parse(Number(value.ToJsonString())),
        _ => node.DeepClone(),
    };

    /// <summary>
    /// A JSON number in one form: its significant digits with no leading or trailing zero, and an exponent when it needs
    /// one. Exact for any length of digits, because it never passes through a binary or decimal type: two numbers are one
    /// only when they are the same value. An exponent too long to be a 64-bit integer leaves the number as it was
    /// written — such a number names no value anybody repeats in two spellings, and reading its exponent whole would
    /// cost time that grows with the square of its length.
    /// </summary>
    private static string Number(string literal)
    {
        var negative = literal.StartsWith('-');
        var unsigned = negative ? literal[1..] : literal;
        var exponentAt = unsigned.IndexOfAny(['e', 'E']);
        var mantissa = exponentAt < 0 ? unsigned : unsigned[..exponentAt];
        long exponent = 0;
        if (exponentAt >= 0)
        {
            var written = unsigned[(exponentAt + 1)..];
            var sign = written.StartsWith('-') ? -1 : 1;
            var digitsOfExponent = written.TrimStart('+', '-').TrimStart('0');
            if (digitsOfExponent.Length > 18)
            {
                return literal;
            }

            exponent = sign * (digitsOfExponent.Length == 0 ? 0 : long.Parse(digitsOfExponent, NumberStyles.None, CultureInfo.InvariantCulture));
        }

        var point = mantissa.IndexOf('.', StringComparison.Ordinal);
        var digits = point < 0 ? mantissa : mantissa[..point] + mantissa[(point + 1)..];
        if (point >= 0)
        {
            exponent -= mantissa.Length - point - 1;
        }

        digits = digits.TrimStart('0');
        if (digits.Length == 0)
        {
            return "0";
        }

        var significant = digits.TrimEnd('0');
        exponent += digits.Length - significant.Length;

        var text = new StringBuilder();
        if (negative)
        {
            text.Append('-');
        }

        text.Append(significant);
        if (exponent != 0)
        {
            text.Append('e').Append(exponent.ToString(CultureInfo.InvariantCulture));
        }

        return text.ToString();
    }

    /// <summary>No control character, and every surrogate in a pair: anything else cannot be stored as UTF-8 and read back the same.</summary>
    private static bool IsStorable(string key)
    {
        for (var i = 0; i < key.Length; i++)
        {
            var c = key[i];
            if (char.IsControl(c))
            {
                return false;
            }

            if (char.IsHighSurrogate(c))
            {
                if (i + 1 >= key.Length || !char.IsLowSurrogate(key[i + 1]))
                {
                    return false;
                }

                i++;
            }
            else if (char.IsLowSurrogate(c))
            {
                return false;
            }
        }

        return true;
    }
}
