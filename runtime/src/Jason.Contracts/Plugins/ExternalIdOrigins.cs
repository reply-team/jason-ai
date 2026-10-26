using System.Text.Json;
using System.Text.Json.Nodes;

namespace Jason.Contracts.Plugins;

/// <summary>
/// How the provider came to hold an entity a plugin identified: it was there already and the call found it, or the call
/// created it. A plugin says so under <c>external_id_origins</c> on a succeeded answer, keyed as the <c>external_ids</c>
/// beside it; one that cannot tell says nothing, and nothing is ever guessed in its place.
/// </summary>
/// <remarks>
/// The rule lives here because the host and the runtime both hold an answer to it and neither can reference the other:
/// the host refuses an answer that breaks it as <c>bad_return</c>, and the runtime refuses an outcome that carries one as
/// malformed. The key is optional and additive, so the protocol stays at version 1.
/// </remarks>
public static class ExternalIdOrigins
{
    /// <summary>The provider held the entity before the call.</summary>
    public const string Found = "found";

    /// <summary>The call created the entity at the provider.</summary>
    public const string Created = "created";

    /// <summary>
    /// True when <paramref name="node"/> is absent, or an object every key of which is a key of
    /// <paramref name="externalIds"/> and every value exactly <see cref="Found"/> or <see cref="Created"/>; then
    /// <paramref name="origins"/> is that object, or null where nothing was said — an empty object included.
    /// </summary>
    /// <remarks>
    /// Kinds are compared ordinally, whatever comparer the objects were read with: the runtime reads an outcome with
    /// options that ignore the case of a property name, and a kind is the provider's word, spelled exactly.
    /// </remarks>
    public static bool TryRead(JsonNode? node, JsonObject? externalIds, out JsonObject? origins)
    {
        origins = null;
        if (node is null)
        {
            return true;
        }

        if (node is not JsonObject said)
        {
            return false;
        }

        foreach (var (kind, value) in said)
        {
            if (!Names(externalIds, kind) || !IsWord(value))
            {
                return false;
            }
        }

        origins = said.Count == 0 ? null : said;
        return true;
    }

    /// <summary>The word a plugin said for one kind, compared ordinally, or null where it said none.</summary>
    public static string? For(JsonObject? origins, string kind)
    {
        if (origins is null)
        {
            return null;
        }

        foreach (var (said, value) in origins)
        {
            if (string.Equals(said, kind, StringComparison.Ordinal) && IsWord(value))
            {
                return value!.GetValue<string>();
            }
        }

        return null;
    }

    private static bool Names(JsonObject? externalIds, string kind) =>
        externalIds is not null && externalIds.Any(pair => string.Equals(pair.Key, kind, StringComparison.Ordinal));

    private static bool IsWord(JsonNode? value) =>
        value is JsonValue text
        && text.GetValueKind() == JsonValueKind.String
        && text.GetValue<string>() is Found or Created;
}
