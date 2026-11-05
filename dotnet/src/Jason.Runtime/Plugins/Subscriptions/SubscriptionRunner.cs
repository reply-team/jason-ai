using Jason.Runtime.Configuration;

namespace Jason.Runtime.Plugins.Subscriptions;

/// <summary>
/// What runs the subscribed kinds' chains, off the scan: never a work item and never one of the dispatcher's handler
/// slots. It holds one chain in flight per kind and at most <c>Subscriptions:MaxParallel</c> children at once.
/// </summary>
public sealed class SubscriptionRunner(LiveSettings<SubscriptionsOptions> settings)
{
    private readonly object _gate = new();
    private readonly List<string> _flight = [];
    private int _children;

    /// <summary>Whether a chain offered now would have a child to run in: the scan offers nothing more once this is false.</summary>
    public bool HasFreeChild
    {
        get
        {
            lock (_gate)
            {
                return _children < settings.Current.MaxParallel;
            }
        }
    }

    /// <summary>The kinds with a chain in flight, in the order they were taken.</summary>
    public IReadOnlyList<string> InFlight
    {
        get
        {
            lock (_gate)
            {
                return [.. _flight];
            }
        }
    }

    /// <summary>Takes a kind's next turn; false, and nothing taken, while the kind has a chain in flight or every child is taken.</summary>
    public bool TryStart(string kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        lock (_gate)
        {
            if (_flight.Contains(kind, StringComparer.Ordinal) || _children >= settings.Current.MaxParallel)
            {
                return false;
            }

            _flight.Add(kind);
            _children++;
            return true;
        }
    }
}
