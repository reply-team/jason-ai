using Jason.Runtime.Discovery;

namespace Jason.Runtime.Tests.Discovery;

public class CapabilityTokenTests
{
    [Fact]
    public void Tokens_are_256_bit_base64url_and_unique()
    {
        var a = CapabilityToken.Generate();
        var b = CapabilityToken.Generate();
        Assert.Equal(43, a.Length);
        Assert.DoesNotContain('=', a);
        Assert.DoesNotContain('+', a);
        Assert.DoesNotContain('/', a);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Matching_is_exact()
    {
        var token = CapabilityToken.Generate();
        Assert.True(CapabilityToken.Matches(token, token));
        Assert.False(CapabilityToken.Matches(token[..^1] + "x", token));
        Assert.False(CapabilityToken.Matches(string.Empty, token));
        Assert.False(CapabilityToken.Matches(token + "0", token));
    }

    [Fact]
    public void Runtime_identity_uses_a_prefixed_ulid_and_the_current_process()
    {
        var info = RuntimeInfo.Create();
        Assert.StartsWith("rt_", info.InstanceId, StringComparison.Ordinal);
        Assert.Equal(Environment.ProcessId, info.Pid);
        Assert.StartsWith("0.1.0", info.RuntimeVersion, StringComparison.Ordinal);
        Assert.InRange(info.StartedAt, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(1));
    }
}
