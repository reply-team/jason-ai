namespace Jason.PluginHost.Sdk;

/// <summary>
/// Every code the HOST itself may write into a failed outcome, spelled once. A plugin's own codes are its own
/// vocabulary (<c>rate_limited</c>, <c>not_found</c>, …) and travel untouched; these are the ones that mean the
/// host stopped the plugin rather than the provider answering badly.
/// </summary>
public static class OutcomeCodes
{
    /// <summary>The plugin threw something that was not a declared failure.</summary>
    public const string PluginException = "plugin_exception";

    /// <summary>The entry function answered with something that is not <c>{ result, external_ids? }</c>.</summary>
    public const string BadReturn = "bad_return";

    public const string ResultTooLarge = "result_too_large";

    public const string PluginSyntaxError = "plugin_syntax_error";

    public const string EntryFunctionMissing = "entry_function_missing";

    public const string PluginTimeout = "plugin_timeout";

    public const string PluginMemoryExceeded = "plugin_memory_exceeded";

    public const string PluginStatementLimit = "plugin_statement_limit";

    public const string PluginRecursionLimit = "plugin_recursion_limit";

    /// <summary>The plugin called a capability the user did not grant it.</summary>
    public const string CapabilityNotGranted = "capability_not_granted";

    public const string ExecutableNotAllowed = "executable_not_allowed";

    public const string ExecLimit = "exec_limit";

    public const string HttpHostNotAllowed = "http_host_not_allowed";

    public const string HttpSchemeNotAllowed = "http_scheme_not_allowed";

    public const string HttpMethodNotAllowed = "http_method_not_allowed";

    public const string HttpHeaderNotAllowed = "http_header_not_allowed";

    public const string HttpLimit = "http_limit";

    public const string ModuleNotAllowed = "module_not_allowed";
}
