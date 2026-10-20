using Jason.Contracts.Api;
using Jason.Runtime.Contacts;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;

namespace Jason.Runtime.Tests.Plugins.Reply;

/// <summary>
/// Profile links as somebody may have typed them at Reply, fed both to the runtime's own key for its suppression register
/// and, through a plugin host, to the plugin's: the plugin has to find the same profile the runtime keyed, or none. A key
/// the plugin computed differently would make the register's check at the claim one about somebody else.
/// </summary>
public static class ReplyProfileKeys
{
    /// <summary>Links the plugin keys exactly as the runtime does: escapes, letters outside ASCII and doubled trailing slashes.</summary>
    public static TheoryData<string> Keyed() =>
    [
        "https://www.linkedin.com/in/josé-garcía",
        "https://www.linkedin.com/in/jos%C3%A9-garc%C3%ADa",
        "https://www.linkedin.com/in/jos%c3%a9-garc%c3%ada",
        "https://www.linkedin.com/in/%4Aos%C3%A9",
        "https://www.linkedin.com/in/marta example",
        "https://www.linkedin.com/in/marta-\U0001F600",
        "https://www.linkedin.com/in/marta-example?trk=café",
        "https://example.com/in/José-Example",
        "https://example.com/in/jos%c3%a9-example",
        "https://www.linkedin.com/in/a%2Fb",
        "https://example.com/in/a%2fb",
        "https://www.linkedin.com/in/marta{x}|^",
        "https://www.linkedin.com/in/marta%e2%82",
        "https://www.linkedin.com/in/Marta-Example//",
        "https://www.linkedin.com/in/josé-garcía//",
    ];

    /// <summary>A profile as somebody may have typed it at Reply, and as a stop may have been recorded for it.</summary>
    public const string Plain = "https://www.linkedin.com/in/Marta-Example";

    /// <summary>The same profile with a doubled trailing slash, which the runtime keys with one slash left.</summary>
    public const string Doubled = "https://www.linkedin.com/in/Marta-Example//";

    /// <summary>
    /// The runtime's register with a stop recorded under <see cref="Plain"/>, asked about the value a contact's channel is
    /// stored under when it was written as <see cref="Doubled"/>. The register keeps the two apart, so the claim lets work
    /// about that person through; the plugin is then what must not reach the stopped profile through it. Answers the
    /// stored value, the one a work item about that person consumes.
    /// </summary>
    public static async Task<string> StoredBesideAStopUnderThePlainSpellingAsync(CancellationToken ct)
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var register = new SuppressionService(db, new JournalWriter(TimeProvider.System), TimeProvider.System);
        var stop = await register.AddAsync(new SuppressionAddRequest("linkedin", Plain, null, "said stop"), ct);

        var stored = RuntimeKey(Doubled);
        Assert.True(stored != stop.Value, $"the stop and the stored value are both '{stored}', and this case needs two.");
        Assert.False(await register.IsSuppressedAsync(db, "linkedin", stored, ct), $"the register refused '{stored}', which this case needs it to let through.");
        return stored;
    }

    /// <summary>
    /// Links the runtime reads more into than the plugin follows, each of which the runtime keys as one ordinary profile:
    /// a step back up the path, written plainly or escaped, and a percent sign that starts no escape, after which the
    /// runtime treats the escapes that follow in ways no one rule describes.
    /// </summary>
    public static TheoryData<string> Unfollowed() =>
    [
        "https://www.linkedin.com/in/x/../marta-alvarez",
        "https://www.linkedin.com/in/x/%2E%2E/marta-alvarez",
        "https://www.linkedin.com/in/50%off",
        "https://www.linkedin.com/in/a%4%5F",
    ];

    /// <summary>The runtime's own key for a profile link, as its register and its contacts store it.</summary>
    public static string RuntimeKey(string link)
    {
        var errors = new ValidationErrors();
        var keyed = ChannelRules.NormalizeValue(ChannelRules.LinkedIn, link, "channels[0].value", errors);
        Assert.False(errors.Any, $"the runtime refused '{link}' as a profile link.");
        return keyed!;
    }
}
