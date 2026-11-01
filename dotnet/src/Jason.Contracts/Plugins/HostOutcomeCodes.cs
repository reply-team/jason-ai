using System.Collections.Frozen;

namespace Jason.Contracts.Plugins;

/// <summary>
/// Every code the HOST itself may write into a failed outcome, spelled once. A plugin's own codes are its own
/// vocabulary (<c>rate_limited</c>, <c>not_found</c>, …) and travel untouched; these are the ones that mean the
/// host stopped the plugin rather than the provider answering badly.
/// </summary>
/// <remarks>
/// The list lives here rather than beside the host because the runtime reads it too, and cannot reference the host:
/// an operation's contract declares the codes a plugin may fail it with, and these are declared by none, since they
/// say what became of the plugin rather than what the provider answered. The runtime passes one only under a class the
/// host gives it (<see cref="Writes"/>).
/// </remarks>
public static class HostOutcomeCodes
{
    /// <summary>The plugin threw something that was not a declared failure.</summary>
    public const string PluginException = "plugin_exception";

    /// <summary>The entry function answered with something that is not <c>{ result, external_ids?, external_id_origins? }</c>.</summary>
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

    /// <summary>Every code above.</summary>
    public static IReadOnlySet<string> All { get; } = new[]
    {
        PluginException, BadReturn, ResultTooLarge, PluginSyntaxError, EntryFunctionMissing, PluginTimeout,
        PluginMemoryExceeded, PluginStatementLimit, PluginRecursionLimit, CapabilityNotGranted, ExecutableNotAllowed,
        ExecLimit, HttpHostNotAllowed, HttpSchemeNotAllowed, HttpMethodNotAllowed, HttpHeaderNotAllowed, HttpLimit,
        ModuleNotAllowed,
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>
    /// Whether <paramref name="code"/> under <paramref name="failureClass"/> is an ending the host writes. Every one of
    /// them is permanent, and a timeout is ambiguous as well once an external call was started, since the provider may
    /// have acted before the time ran out. A plugin can write any of these words through <c>host.fail</c> under any
    /// class; under another class it is the plugin's word, not the host's.
    /// </summary>
    public static bool Writes(FailureClass failureClass, string code) =>
        All.Contains(code)
        && (failureClass == FailureClass.Permanent || (failureClass == FailureClass.Ambiguous && string.Equals(code, PluginTimeout, StringComparison.Ordinal)));
}
