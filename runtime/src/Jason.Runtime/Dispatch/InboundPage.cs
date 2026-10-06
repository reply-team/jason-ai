using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Json;

namespace Jason.Runtime.Dispatch;

/// <summary>
/// What the inbound read takes from one page of <c>conversation.list</c>: whether the provider holds more, and for each
/// thread its identifier, its channel, when it last moved, the provider's two flags and the identifier of the person on
/// it. A closed projection: the subject, the preview, the name the provider shows, its campaign identifier and its
/// vendor block have no member here, so nothing this step does can carry them anywhere.
/// </summary>
public sealed record InboundPage(IReadOnlyList<InboundThread>? Items, bool? HasMore)
{
    /// <summary>
    /// The page a succeeded read ended with, or null when what it holds is not a page: no result at all, no list of
    /// threads, or no word on whether more follow. The result was held to the operation's schema when the read ended, so
    /// a page that does not read is a fault to be retried, not a page to guess at.
    /// </summary>
    /// <exception cref="JsonException">The result is JSON that does not bind to a page, such as a word where a flag belongs.</exception>
    public static InboundPage? Read(JsonNode? result)
    {
        if (result is not JsonObject)
        {
            return null;
        }

        var page = result.Deserialize<InboundPage>(JasonJson.Options);
        return page is { Items: not null, HasMore: not null } ? page : null;
    }
}

/// <summary>One thread of a page, as far as the inbound read takes it.</summary>
public sealed record InboundThread(
    string? ExternalId,
    string? Channel,
    DateTimeOffset? LastActivityAt,
    bool? Unread,
    bool? HasMeetingIntent,
    InboundPerson? Contact);

/// <summary>The person on a thread, as far as the inbound read takes them: the provider's identifier, never their name.</summary>
public sealed record InboundPerson(string? ExternalId);
