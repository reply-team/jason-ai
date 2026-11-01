namespace Jason.Runtime.Plugins.Invocation;

/// <summary>
/// Everything that can go wrong around an invocation rather than inside it, spelled once. A protocol failure is
/// never the plugin's answer: it means the runtime could not get one, and it is classified by how much may
/// already have happened.
/// </summary>
public static class ProtocolCodes
{
    /// <summary>No plugin of that id is in the active snapshot.</summary>
    public const string PluginNotLoaded = "plugin_not_loaded";

    /// <summary>The plugin is loaded but its environment is not usable — a declared executable is missing or unrunnable.</summary>
    public const string PluginUnavailable = "plugin_unavailable";

    /// <summary>The plugin does not implement the operation asked for.</summary>
    public const string PluginOperationUnsupported = "plugin_operation_unsupported";

    /// <summary>The plugin is of a kind nothing invokes.</summary>
    public const string PluginKindNotInvocable = "plugin_kind_not_invocable";

    /// <summary>The child could not be started at all.</summary>
    public const string PluginLaunchFailed = "plugin_launch_failed";

    /// <summary>The child refused the invocation before running any JavaScript.</summary>
    public const string PluginInvocationRejected = "plugin_invocation_rejected";

    /// <summary>The budget ran out and the process tree was killed.</summary>
    public const string PluginTimeout = "plugin_timeout";

    /// <summary>The caller cancelled and the process tree was killed.</summary>
    public const string PluginKilled = "plugin_killed";

    /// <summary>The child exited without writing an outcome.</summary>
    public const string PluginNoOutcome = "plugin_no_outcome";

    /// <summary>The child wrote something that is not a valid outcome for this invocation.</summary>
    public const string PluginMalformedOutcome = "plugin_malformed_outcome";

    /// <summary>The child wrote more than the invoker will read.</summary>
    public const string PluginOutputTooLarge = "plugin_output_too_large";

    /// <summary>The caller's input is larger than an envelope may carry, so no child was started.</summary>
    public const string PluginInputTooLarge = "plugin_input_too_large";

    /// <summary>The caller's binding is larger than an envelope may carry, so no child was started.</summary>
    public const string PluginBindingTooLarge = "plugin_binding_too_large";
}
