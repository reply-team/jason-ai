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
    [InlineData(HostOutcomeCodes.FilePathNotAllowed)]
    [InlineData(HostOutcomeCodes.FileLineTooLarge)]
    [InlineData(HostOutcomeCodes.FileLineMultiline)]
    [InlineData(HostOutcomeCodes.FileLimit)]
    public void Every_host_code_is_written_permanent(string code)
    {
        Assert.True(HostOutcomeCodes.Writes(FailureClass.Permanent, code), $"the host writes `{code}` permanent, and the list says it does not.");
    }

    /// <summary>
    /// A timeout and a failed append are the host endings that can be ambiguous: once a call that reaches outside was
    /// started, the provider may have acted before the time ran out or the disk refused the line.
    /// </summary>
    [Fact]
    public void Only_a_timeout_and_a_failed_append_are_also_written_ambiguous()
    {
        var ambiguous = HostOutcomeCodes.All.Where(code => HostOutcomeCodes.Writes(FailureClass.Ambiguous, code)).Order(StringComparer.Ordinal);

        Assert.Equal([HostOutcomeCodes.FileWriteFailed, HostOutcomeCodes.PluginTimeout], ambiguous);
    }

    /// <summary>
    /// An append the disk refused is the one host ending that is no rule: the same line may go through on a second try,
    /// and a plugin that does not catch it, with nothing started before it, ends transient, which every operation's
    /// check has to pass.
    /// </summary>
    [Fact]
    public void Only_a_failed_append_is_also_written_transient()
    {
        var transient = HostOutcomeCodes.All.Where(code => HostOutcomeCodes.Writes(FailureClass.Transient, code));

        Assert.Equal([HostOutcomeCodes.FileWriteFailed], transient);
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
