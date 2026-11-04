using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Jason.Runtime.Discovery;

namespace Jason.Runtime.Plugins.Door;

/// <summary>What a live handler capability opens: one plugin, one operation set, one journal line, until a moment.</summary>
public sealed record DoorGrant(string PluginId, IReadOnlySet<string> Operations, string LineId, DateTimeOffset ExpiresAt);

/// <summary>
/// A minted capability as its minter holds it: the token, which goes to the child on stdin and nowhere else, and the key
/// that revokes it. It prints as neither.
/// </summary>
public sealed class DoorTicket
{
    internal DoorTicket(string capability, string key)
    {
        Capability = capability;
        Key = key;
    }

    /// <summary>The bearer the plugin-host presents; it reaches the child's stdin and no other place.</summary>
    public string Capability { get; }

    internal string Key { get; }

    public override string ToString() => "DoorTicket(redacted)";
}

/// <summary>
/// The handler capabilities alive in this runtime: opaque random bearers, each minted for one invocation and recognised
/// only until it is revoked or its moment passes. They live in this process's memory alone, keyed by a hash of the
/// token, so a restart forgets every one of them and nothing on disk can bring one back.
/// </summary>
public sealed class HandlerCapabilities
{
    private readonly ConcurrentDictionary<string, DoorGrant> grants = new(StringComparer.Ordinal);

    /// <summary>How many capabilities are alive, for tests.</summary>
    public int Count => grants.Count;

    /// <summary>Mints a capability for one invocation of <paramref name="pluginId"/>'s handler.</summary>
    public DoorTicket Mint(string pluginId, IReadOnlySet<string> operations, string lineId, DateTimeOffset expiresAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentException.ThrowIfNullOrWhiteSpace(lineId);

        var grant = new DoorGrant(pluginId, operations.ToHashSet(StringComparer.Ordinal), lineId, expiresAt);
        while (true)
        {
            var capability = CapabilityToken.Generate();
            var key = KeyOf(capability);
            if (grants.TryAdd(key, grant))
            {
                return new DoorTicket(capability, key);
            }
        }
    }

    /// <summary>Revokes the capability; revoking one already dead does nothing.</summary>
    public void Revoke(DoorTicket ticket)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        grants.TryRemove(ticket.Key, out _);
    }

    /// <summary>
    /// Recognises <paramref name="bearer"/> as a live capability. The lookup is by a hash of the bearer and the stored
    /// key is compared in fixed time; a capability whose moment has come is removed and refused.
    /// </summary>
    public bool TryRecognise(string bearer, DateTimeOffset now, out DoorGrant grant)
    {
        grant = null!;
        if (string.IsNullOrEmpty(bearer))
        {
            return false;
        }

        var key = KeyOf(bearer);
        foreach (var entry in grants)
        {
            if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(entry.Key), Encoding.ASCII.GetBytes(key)))
            {
                continue;
            }

            if (now >= entry.Value.ExpiresAt)
            {
                grants.TryRemove(entry.Key, out _);
                return false;
            }

            grant = entry.Value;
            return true;
        }

        return false;
    }

    private static string KeyOf(string capability) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(capability)));
}
