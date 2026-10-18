namespace Jason.Runtime.Tests.Logging;

/// <summary>
/// Every property name a line of the runtime's log may carry, each one reviewed for what its value can be. The log is
/// what a diagnostic bundle carries off the machine, so a value a person typed — a context, a contact's field, a
/// setting, a reason, an answer a provider sent — has no property to travel in. Ids, counts, closed words, versions,
/// the loopback address and the route a request matched do, and so do the data directory's own paths, which the bundle
/// rewrites.
/// </summary>
/// <remarks>
/// One list, held twice: by the scan of every message template in the runtime's source, and by a run of the real
/// executable whose log is read back line by line, framework lines included. A name joins this list only with the
/// reason its value is never somebody's text; a sentence the runtime composes itself is allowed by name, beside the
/// code that composes it and what that code may put into it.
/// </remarks>
internal static class ReviewedLogProperties
{
    public static IReadOnlySet<string> Names { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        // Ids: public ids, process and connection ids, and the envelope's trace, span and event ids.
        "CampaignId", "WorkItemId", "AttemptId", "AttemptIds", "InstanceId", "InvocationId", "CorrelationId",
        "NotificationId", "SnapshotId", "PluginId", "Id", "RequestId", "ConnectionId", "@tr", "@sp", "@i",

        // Counts and numbers, and the envelope's renderings of a formatted number.
        "Count", "Plugins", "Candidates", "Problems", "Expired", "Lost", "Summoned", "Claimed", "Applied", "New",
        "Scan", "Exec", "Http", "Env", "Pid", "ExitCode", "DurationMs", "StatusCode", "Elapsed", "@r",

        // Where a settings file stopped parsing: a line and a byte within it (RuntimeHost).
        "LineNumber", "BytePositionInLine",

        // How many of the build's own patterns finished warming and how many did not (PatternWarmUp); how long that took
        // is a DurationMs, above.
        "Finished", "Unfinished",

        // The port the settings asked for and the one the API listens on, both numbers (RuntimeHost).
        "ConfiguredPort", "Port",

        // Codes and closed words, and the envelope's level. A code is the runtime's own constant or a plugin outcome's
        // code, which the outcome validator holds to a snake_case token of at most 64 characters before anything logs it
        // (Code: Claimer, PluginInvoker, NotificationSender, UpdateChecker). Verdict is "succeeded", "unknown", or
        // "failed" or "protocol_failure" followed by a closed failure class and such a code (PluginInvoker).
        "Code", "Codes", "Class", "Verdict", "Source", "Console", "Section", "Channel", "Activated", "Operation",
        "EnvName", "SourceContext", "EventId", "@l",

        // The kind of update feed, "default" or "configured", never its address (UpdateChecker).
        "Feed",

        // Why the endpoint descriptor could not be published or removed: one of the closed phrases of
        // DescriptorUnpublishableException and DescriptorPublisher.Why, whose only number is a rule's wait, never an
        // exception's message (RuntimeHost, RunningRuntime).
        "Why",

        // The type name of a failure to read the settings file, never its message (RuntimeHost).
        "ExceptionType",

        // Versions and digests.
        "Version", "Digest",

        // The loopback address the API listens on.
        "BaseUrl", "address",

        // The request line's path and method, both closed words though a client sends them: the route of the operation
        // the request matched or "unmatched", and "POST" or "other" — never either as sent (RequestLine). A line written
        // while an operation runs also carries the framework's own RequestPath, the path as sent; the request reached
        // the operation only by matching its literal route, so that is the route as the client spelled it.
        "RequestPath", "RequestMethod",

        // The envelope: timestamp, template, exception.
        "@t", "@mt", "@x",

        // Paths under the data directory and the process's own, carried as they are: the diagnostic bundle rewrites them.
        "DataDir", "Backup", "Directory", "Descriptor", "ContentRoot",

        // Summoner: the chronicle kind that summoned a manager, or "scheduled".
        "Intent",

        // ToastChannel: why there is no desktop, one of the channel's own presence words.
        "Detail",

        // LiveSettings: validator and binding sentences, which name a setting by its path and describe a refused text
        // by its length. ParseFailure: a read failure's type and where the reader stopped, and for a stored value the
        // table and column the model names it by. InboundSync: why a read's result is not a page, a closed word or the
        // ParseFailure sentence.
        "Failure",
    };

    /// <summary>
    /// Names that are not on the list because their values are free text a person typed, kept here so the reason is
    /// written down beside the decision. A plugin reload's reason goes to the journal and never to a log.
    /// </summary>
    public static IReadOnlySet<string> Refused { get; } = new HashSet<string>(StringComparer.Ordinal) { "Reason" };
}
