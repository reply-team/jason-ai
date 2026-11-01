using System.Reflection;
using Jason.Contracts.Plugins;

namespace Jason.Contracts.Tests.Plugins;

/// <summary>
/// The codes the plugin host writes into a failed outcome itself, and the classes it writes them under. They are
/// declared by no operation — they mean the host stopped the plugin, not that the provider answered — so the runtime
/// can only tell one from a plugin's own word by this list, and only under the classes the host gives them.
/// </summary>
public class HostOutcomeCodesTests
{
    [Fact]
    public void The_set_is_every_code_the_class_spells()
    {
        var spelled = typeof(HostOutcomeCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .Order(StringComparer.Ordinal);

        Assert.Equal(spelled, HostOutcomeCodes.All.Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData(HostOutcomeCodes.PluginException)]
    [InlineData(HostOutcomeCodes.CapabilityNotGranted)]
    [InlineData(HostOutcomeCodes.PluginTimeout)]
    public void Every_host_code_is_written_permanent(string code)
    {
        Assert.True(HostOutcomeCodes.Writes(FailureClass.Permanent, code), $"the host writes `{code}` permanent, and the list says it does not.");
    }

    /// <summary>
    /// A timeout is the one host ending that can be ambiguous: once an external call was started, the provider may have
    /// acted before the time ran out.
    /// </summary>
    [Fact]
    public void Only_a_timeout_is_also_written_ambiguous()
    {
        var ambiguous = HostOutcomeCodes.All.Where(code => HostOutcomeCodes.Writes(FailureClass.Ambiguous, code));

        Assert.Equal([HostOutcomeCodes.PluginTimeout], ambiguous);
    }

    /// <summary>
    /// A plugin may write a host code itself through <c>host.fail</c>, under any class. Under a class the host never
    /// gives that code it is the plugin's word, not the host's, and nothing exempts it.
    /// </summary>
    [Theory]
    [InlineData(FailureClass.Transient, HostOutcomeCodes.PluginException)]
    [InlineData(FailureClass.Validation, HostOutcomeCodes.HttpLimit)]
    [InlineData(FailureClass.Blocked, HostOutcomeCodes.PluginTimeout)]
    [InlineData(FailureClass.Ambiguous, HostOutcomeCodes.BadReturn)]
    [InlineData(FailureClass.Permanent, "provider_call_failed")]
    public void A_code_under_a_class_the_host_never_gives_it_is_not_the_host_s(FailureClass failureClass, string code)
    {
        Assert.False(HostOutcomeCodes.Writes(failureClass, code), $"`{code}` under `{failureClass}` is not an ending the host writes.");
    }
}
