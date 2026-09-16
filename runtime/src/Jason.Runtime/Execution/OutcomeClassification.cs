using Jason.Contracts.Plugins;
using Jason.Runtime.Plugins.Invocation;

namespace Jason.Runtime.Execution;

/// <summary>
/// What a protocol failure was, in the same four classes a plugin answers in. Whether a class is worth another
/// attempt is not asked here: that question belongs to the operation being performed and is answered once, by
/// <see cref="Jason.Contracts.Operations.OutcomeContract.Retriable"/>, which reads the operation's own contract.
/// </summary>
public static class OutcomeClassification
{
    /// <summary>
    /// The class of a protocol failure: nothing ran (permanent), the start failed and may work next time
    /// (transient), or something ran and did not report cleanly (ambiguous). An unknown code is ambiguous,
    /// because the safe assumption about an unknown failure is that it may have done something.
    /// </summary>
    public static FailureClass ClassOf(string protocolCode) => protocolCode switch
    {
        ProtocolCodes.PluginLaunchFailed => FailureClass.Transient,
        ProtocolCodes.PluginNotLoaded
            or ProtocolCodes.PluginUnavailable
            or ProtocolCodes.PluginOperationUnsupported
            or ProtocolCodes.PluginKindNotInvocable
            or ProtocolCodes.PluginInvocationRejected
            or ProtocolCodes.PluginInputTooLarge
            or ProtocolCodes.PluginBindingTooLarge => FailureClass.Permanent,
        _ => FailureClass.Ambiguous,
    };
}
