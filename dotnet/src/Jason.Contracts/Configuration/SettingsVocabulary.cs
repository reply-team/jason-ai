using System.Collections.Frozen;
using Jason.Contracts.Api;

namespace Jason.Contracts.Configuration;

/// <summary>
/// Every name the settings file is made of: each section, and each setting inside one, down through the nested
/// option classes. These are the keys a refused setting may print as they were typed (see <see cref="SettingKey"/>).
/// </summary>
/// <remarks>
/// The list lives here rather than beside the option classes because the command-line client needs the same names
/// and cannot reference the runtime. A copy is only as good as the test that holds it to the original, and one
/// does: the runtime's tests walk its option classes and compare, so a setting added there and not here fails a
/// test rather than turning into a placeholder in every refusal that names it. Configuration keys are matched
/// without regard to case, and so is this set.
/// </remarks>
public static class SettingsVocabulary
{
    public static IReadOnlySet<string> Names { get; } = new[]
    {
        // The sections.
        "Runtime", "Logging", "Dispatcher", "Manager", "Roles", "Plugins", "Routes", "Update", "Notifications", "Inbound", "Responder", "Launches", "Harness",
        "Subscriptions",

        // Runtime and Logging.
        "Port",
        "MinimumLevel", "RetainedFileCountLimit", "FileSizeLimitBytes",

        // Dispatcher, and the defaults of each kind of work it names.
        "Enabled", "TickSeconds", "MaxParallel", "DrainSeconds", "RetryDelaySeconds", "ExitGraceSeconds", "EventsCadenceSeconds", "SearchCadenceSeconds", "SearchStallSeconds", "AiRole", "ProviderOp",
        "TimeoutSeconds", "HeartbeatSeconds", "MaxAttempts",

        // How an AI role waits: MaxWaits here, and its ceiling MaxWaitSeconds, a name Notifications shares, below.
        "MaxWaits",

        // How work a provider block holds is tried again.
        "Blocks", "TrialSeconds",

        // Manager.
        "Triggers", "ReviewSeconds", "Priority", "MaxEntriesPerScan",

        // Roles.
        "DefaultExecutionProfile", "DefaultEntryCommand", "MaxStdoutBytes", "MaxSkillBytes",

        // Plugins: who is granted what, then the limits every invocation runs under.
        "Grants", "Config", "Limits", "Exec", "Http", "Files", "Invoker", "Env", "Api",
        "TimeoutMs", "MaxTimeoutMs", "MemoryMb", "MaxMemoryMb", "MaxStatements", "MaxRecursion",
        "OutputBytes", "MaxCalls", "LineBytes",
        "ResponseBytes", "RequestBytes",
        "OutcomeBytes", "StderrBytes", "KillGraceMs", "VersionCheckTimeoutMs", "LogLineBytes",

        // Routes, and the entry each one is.
        "Default", "Operations", "Plugin", "Binding",

        // Update.
        "CheckEnabled", "FeedUrl", "InitialDelayMinutes", "IntervalHours",

        // Notifications; a named plugin's entry is Enabled and Binding, both above.
        "Toast", "QuietSeconds", "MaxWaitSeconds", "MinIntervalMinutes",

        // Inbound; its switch is Enabled, above.
        "SyncSeconds", "LookbackSeconds",

        // Responder; Priority, TimeoutSeconds and MaxAttempts are above.
        "OnReply", "MaxAgeSeconds",

        // Launches: the daily limits, under a key that is a role; the roles Jason ships are joined below.
        "MaxPerDay",

        // Harness.
        "Root",

        // Subscriptions: the order of each kind's subscribers, under a key that is the kind, and the numbers the runner of
        // their chains works to; its MaxParallel is above. Subscriptions also names a grant's list of kinds, a section
        // above.
        "Order", "RunRecordsPerKind", "EnvelopeBytes", "TransientRetries", "RetryBackoffSeconds",
    }.Concat(ShippedRoles.Names).ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The whole path of each setting that is a list, with <c>*</c> for the one key of a map that stands in the path (a
    /// grant is kept under the plugin it is granted to): the only places a key made of digits may follow and still be
    /// shown, because only there is it an index. A path rather than a name, because a name is not where a thing stands
    /// — <c>Exec</c> and <c>Http</c> also name two objects of the plugin limits, and <c>Env</c> or <c>Triggers</c> can be
    /// a key somebody typed anywhere in a binding — so a digit key under any of those is a number somebody chose. The
    /// runtime's tests hold these paths to the option classes the same way they hold <see cref="Names"/>.
    /// </summary>
    public static IReadOnlyList<string> ListPaths { get; } =
    [
        "Manager:Triggers",
        "Roles:DefaultEntryCommand",

        // A grant's six lists.
        "Plugins:Grants:*:Exec",
        "Plugins:Grants:*:Http",
        "Plugins:Grants:*:Env",
        "Plugins:Grants:*:Api",
        "Plugins:Grants:*:Subscriptions",
        "Plugins:Grants:*:Files",

        // The order of a kind's subscribers.
        "Subscriptions:Order:*",
    ];
}
