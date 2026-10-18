using System.Collections.Concurrent;
using Jason.Runtime.Discovery;

namespace Jason.Runtime.Ui;

/// <summary>
/// The one secret that travels through a URL: 256 random bits, good for one pairing within a minute, kept in memory
/// only. A runtime restart forgets every ticket, which is right: the link was for the runtime that printed it.
/// </summary>
public sealed class UiTickets(TimeProvider clock)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(60);

    private readonly ConcurrentDictionary<string, DateTimeOffset> _live = new(StringComparer.Ordinal);

    public (string Ticket, DateTimeOffset ExpiresAt) Mint()
    {
        Sweep();
        var ticket = CapabilityToken.Generate();
        var expires = clock.GetUtcNow() + Lifetime;
        _live[ticket] = expires;
        return (ticket, expires);
    }

    /// <summary>True once per ticket, and only before it expires. Removed either way.</summary>
    public bool TryRedeem(string ticket) =>
        _live.TryRemove(ticket, out var expires) && expires > clock.GetUtcNow();

    private void Sweep()
    {
        var now = clock.GetUtcNow();
        foreach (var (ticket, expires) in _live)
        {
            if (expires <= now)
            {
                _live.TryRemove(ticket, out _);
            }
        }
    }
}
