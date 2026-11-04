using Jason.Runtime.Journal;

namespace Jason.Runtime.Plugins.Subscriptions;

/// <summary>Where a journal kind stands for a subscription.</summary>
public enum KindStanding
{
    /// <summary>Not a kind the runtime writes to its journal.</summary>
    Unknown,

    /// <summary>A kind the runtime writes that no plugin may subscribe to.</summary>
    NotSubscribable,

    /// <summary>A kind a plugin may subscribe to.</summary>
    Subscribable,
}

/// <summary>
/// What a manifest's subscriptions and the order of a kind's subscribers are read against: the kinds the runtime writes
/// to its journal, each with whether a plugin may subscribe to it. <see cref="Catalog"/> is the one the runtime reads; a
/// test hands a lookup of its own where it needs a kind the catalog does not have.
/// </summary>
public sealed class SubscribableKinds
{
    private readonly Func<string, KindStanding> _standing;

    public SubscribableKinds(Func<string, KindStanding> standing)
    {
        ArgumentNullException.ThrowIfNull(standing);
        _standing = standing;
    }

    /// <summary>The kinds the runtime writes.</summary>
    public static SubscribableKinds Catalog { get; } = new(kind =>
        JournalKinds.Reserved.Contains(kind) ? KindStanding.Subscribable : KindStanding.Unknown);

    /// <summary>Where <paramref name="kind"/> stands.</summary>
    public KindStanding Of(string kind) => _standing(kind);
}
