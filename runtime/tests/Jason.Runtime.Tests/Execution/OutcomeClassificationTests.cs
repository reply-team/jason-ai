using Jason.Contracts.Plugins;
using Jason.Runtime.Execution;
using Jason.Runtime.Plugins.Invocation;

namespace Jason.Runtime.Tests.Execution;

/// <summary>
/// How far a protocol failure got, which is the only question this table answers. Whether that class is worth
/// another attempt is the operation's own contract's to say, and <c>RetriabilityTests</c> is where it is asked.
/// </summary>
public class OutcomeClassificationTests
{
    [Theory]
    [InlineData(ProtocolCodes.PluginLaunchFailed, FailureClass.Transient)]
    [InlineData(ProtocolCodes.PluginNotLoaded, FailureClass.Permanent)]
    [InlineData(ProtocolCodes.PluginUnavailable, FailureClass.Permanent)]
    [InlineData(ProtocolCodes.PluginOperationUnsupported, FailureClass.Permanent)]
    [InlineData(ProtocolCodes.PluginKindNotInvocable, FailureClass.Permanent)]
    [InlineData(ProtocolCodes.PluginInvocationRejected, FailureClass.Permanent)]
    [InlineData(ProtocolCodes.PluginInputTooLarge, FailureClass.Permanent)]
    [InlineData(ProtocolCodes.PluginBindingTooLarge, FailureClass.Permanent)]
    [InlineData(ProtocolCodes.PluginTimeout, FailureClass.Ambiguous)]
    [InlineData(ProtocolCodes.PluginKilled, FailureClass.Ambiguous)]
    [InlineData(ProtocolCodes.PluginNoOutcome, FailureClass.Ambiguous)]
    [InlineData(ProtocolCodes.PluginMalformedOutcome, FailureClass.Ambiguous)]
    [InlineData(ProtocolCodes.PluginOutputTooLarge, FailureClass.Ambiguous)]
    public void A_protocol_failure_says_whether_anything_could_have_happened(string code, FailureClass expected) =>
        Assert.Equal(expected, OutcomeClassification.ClassOf(code));

    [Fact]
    public void A_code_the_table_does_not_know_is_treated_as_ambiguous()
    {
        Assert.Equal(FailureClass.Ambiguous, OutcomeClassification.ClassOf("anything_else"));
        Assert.Equal(FailureClass.Ambiguous, OutcomeClassification.ClassOf(string.Empty));
    }
}
