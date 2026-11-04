using Jason.Runtime.Plugins.Door;

namespace Jason.Runtime.Tests.Plugins.Door;

/// <summary>
/// The capability a handler's door carries: minted for one invocation, recognised only while it lives, dead once it
/// is revoked or its time is up, and never written down anywhere but the one row in memory that recognises it.
/// </summary>
public class HandlerCapabilitiesTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 5, 9, 0, 0, TimeSpan.Zero);

    private static readonly IReadOnlySet<string> Operations = new HashSet<string>(StringComparer.Ordinal) { "campaign.get", "journal.append" };

    [Fact]
    public void A_minted_capability_is_recognised_with_what_it_was_minted_for()
    {
        var capabilities = new HandlerCapabilities();

        var ticket = capabilities.Mint("acme-forwarder", Operations, "jrn_01KTEST0000000000000000001", Now.AddMinutes(1));

        Assert.True(capabilities.TryRecognise(ticket.Capability, Now, out var grant));
        Assert.Equal("acme-forwarder", grant.PluginId);
        Assert.Equal("jrn_01KTEST0000000000000000001", grant.LineId);
        Assert.Equal(Operations.Order(StringComparer.Ordinal), grant.Operations.Order(StringComparer.Ordinal));
        Assert.Equal(Now.AddMinutes(1), grant.ExpiresAt);
    }

    [Fact]
    public void A_capability_is_256_random_bits_and_never_the_same_twice()
    {
        var capabilities = new HandlerCapabilities();

        var first = capabilities.Mint("acme-forwarder", Operations, "jrn_A", Now.AddMinutes(1));
        var second = capabilities.Mint("acme-forwarder", Operations, "jrn_A", Now.AddMinutes(1));

        Assert.Equal(43, first.Capability.Length);
        Assert.Matches("^[A-Za-z0-9_-]{43}$", first.Capability);
        Assert.NotEqual(first.Capability, second.Capability);
        Assert.DoesNotContain(first.Capability, first.ToString()!, StringComparison.Ordinal);
    }

    [Fact]
    [Critical(Family.R2, "a handler's capability dies with its invocation")]
    public void A_revoked_capability_is_dead()
    {
        var capabilities = new HandlerCapabilities();
        var ticket = capabilities.Mint("acme-forwarder", Operations, "jrn_A", Now.AddMinutes(1));

        capabilities.Revoke(ticket);
        capabilities.Revoke(ticket);

        Assert.False(capabilities.TryRecognise(ticket.Capability, Now, out _));
        Assert.Equal(0, capabilities.Count);
    }

    [Fact]
    public void A_capability_past_its_time_is_dead_and_forgotten_even_if_nobody_revoked_it()
    {
        var capabilities = new HandlerCapabilities();
        var ticket = capabilities.Mint("acme-forwarder", Operations, "jrn_A", Now.AddMinutes(1));

        Assert.True(capabilities.TryRecognise(ticket.Capability, Now.AddMinutes(1).AddTicks(-1), out _));
        Assert.False(capabilities.TryRecognise(ticket.Capability, Now.AddMinutes(1), out _));
        Assert.Equal(0, capabilities.Count);
    }

    [Fact]
    public void Only_the_capability_itself_is_recognised()
    {
        var capabilities = new HandlerCapabilities();
        var ticket = capabilities.Mint("acme-forwarder", Operations, "jrn_A", Now.AddMinutes(1));
        var near = (ticket.Capability[0] == 'A' ? "B" : "A") + ticket.Capability[1..];

        Assert.False(capabilities.TryRecognise(near, Now, out _));
        Assert.False(capabilities.TryRecognise(string.Empty, Now, out _));
        Assert.False(capabilities.TryRecognise(ticket.Capability + "x", Now, out _));
        Assert.True(capabilities.TryRecognise(ticket.Capability, Now, out _));
    }

    [Fact]
    public void Each_capability_lives_and_dies_on_its_own()
    {
        var capabilities = new HandlerCapabilities();
        var first = capabilities.Mint("acme-forwarder", Operations, "jrn_A", Now.AddMinutes(1));
        var second = capabilities.Mint("other-plugin", Operations, "jrn_B", Now.AddMinutes(1));

        capabilities.Revoke(first);

        Assert.False(capabilities.TryRecognise(first.Capability, Now, out _));
        Assert.True(capabilities.TryRecognise(second.Capability, Now, out var grant));
        Assert.Equal("other-plugin", grant.PluginId);
    }
}
