using Jason.Contracts.Plugins;
using Jason.Runtime.Execution;
using Jason.Runtime.Plugins.Invocation;

namespace Jason.Runtime.Tests.Execution;

public class OutcomeClassificationTests
{
    [Theory]
    [InlineData(FailureClass.Transient, true)]
    [InlineData(FailureClass.Permanent, false)]
    [InlineData(FailureClass.Validation, false)]
    [InlineData(FailureClass.Ambiguous, false)]
    public void Only_a_transient_failure_may_be_repeated(FailureClass failureClass, bool retriable) =>
        Assert.Equal(retriable, OutcomeClassification.IsRetriable(failureClass));

    [Theory]
    [InlineData(ProtocolCodes.PluginLaunchFailed, FailureClass.Transient)]
    [InlineData(ProtocolCodes.PluginNotLoaded, FailureClass.Permanent)]
    [InlineData(ProtocolCodes.PluginUnavailable, FailureClass.Permanent)]
    [InlineData(ProtocolCodes.PluginOperationUnsupported, FailureClass.Permanent)]
    [InlineData(ProtocolCodes.PluginKindNotInvocable, FailureClass.Permanent)]
    [InlineData(ProtocolCodes.PluginInvocationRejected, FailureClass.Permanent)]
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

    [Fact]
    public void Nothing_that_did_not_run_is_ever_repeated_blindly()
    {
        Assert.False(OutcomeClassification.IsRetriable(OutcomeClassification.ClassOf(ProtocolCodes.PluginTimeout)));
        Assert.True(OutcomeClassification.IsRetriable(OutcomeClassification.ClassOf(ProtocolCodes.PluginLaunchFailed)));
    }
}
