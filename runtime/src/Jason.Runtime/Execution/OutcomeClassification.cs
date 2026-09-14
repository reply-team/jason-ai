using Jason.Contracts.Plugins;
using Jason.Runtime.Plugins.Invocation;

namespace Jason.Runtime.Execution;

/// <summary>
/// What a failure class means for the work item behind it. Only a transient failure may be repeated: a permanent
/// or a validation failure would fail the same way, and an ambiguous one may already have had its effect at the
/// provider — repeating it blindly could send the same message twice, which is the user's reputation, not a
/// retry budget. An ambiguous failure is therefore final until something can read back what really happened.
/// </summary>
public static class OutcomeClassification
{
    public static bool IsRetriable(FailureClass failureClass) => failureClass == FailureClass.Transient;

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
            or ProtocolCodes.PluginInvocationRejected => FailureClass.Permanent,
        _ => FailureClass.Ambiguous,
    };
}
