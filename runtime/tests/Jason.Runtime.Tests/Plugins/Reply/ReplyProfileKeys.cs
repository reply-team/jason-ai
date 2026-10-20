using Jason.Contracts.Channels;

namespace Jason.Runtime.Tests.Plugins.Reply;

/// <summary>
/// Profile links as somebody may have typed them at Reply, for the rows that drive the plugin end to end. The plugin keys
/// what Reply holds with <c>host.channelKey</c>, the runtime's own rule, so these rows prove the wiring rather than the
/// rule: the rule is held to its own table beside it, and the host to that table through the whole mode.
/// </summary>
public static class ReplyProfileKeys
{
    /// <summary>A profile as somebody may have typed it at Reply, and as a stop may have been recorded for it.</summary>
    public const string Plain = "https://www.linkedin.com/in/Marta-Example";

    /// <summary>
    /// A value a contact's channel may still hold from before the key: the start that re-keyed the stored links leaves a row
    /// as it stood where the same contact already holds the key in another. It is no key, so it is never the key of
    /// anything Reply holds.
    /// </summary>
    public const string LeftAsItStood = "https://www.linkedin.com/in/Marta-Example//";

    /// <summary>Spellings of one profile at Reply that the runtime keys alike: case and slashes, a host and a query, escapes, a path after the name.</summary>
    public static TheoryData<string> Alike() =>
    [
        "https://www.linkedin.com/in/Marta-Example//",
        "uk.linkedin.com/in/marta-example?trk=public_profile",
        "https://www.linkedin.com/in/J%C3%96RN-Example",
        "https://www.linkedin.com:443/in/marta-example/recent-activity/all/#about",
    ];

    /// <summary>Values at Reply the runtime keys as no profile at all — a link to another kind of page, another host — so they are nobody's.</summary>
    public static TheoryData<string> Apart() =>
    [
        "https://www.linkedin.com/pub/marta-example/1/2/3",
        "https://example.com/in/marta-example",
    ];

    /// <summary>The runtime's own key for a profile link, as its register and its contacts store it.</summary>
    public static string RuntimeKey(string link) =>
        ChannelRules.KeyOf(ChannelRules.LinkedIn, link) ?? throw new ArgumentException($"the runtime refuses '{link}' as a profile link.", nameof(link));
}
