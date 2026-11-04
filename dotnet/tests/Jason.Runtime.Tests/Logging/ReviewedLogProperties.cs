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

        // An events source's public id: the account a read hangs on, never its binding or its cursor (EventReader).
        "SourceId",

        // Counts and numbers, and the envelope's renderings of a formatted number.
        "Count", "Plugins", "Candidates", "Problems", "Expired", "Lost", "Summoned", "Claimed", "Applied", "New",
        "Scan", "Exec", "Http", "Env", "Pid", "ExitCode", "DurationMs", "StatusCode", "Elapsed", "@r",

        // How many journal kinds a plugin is admitted to (PluginLoader).
        "Subscriptions",

        // How many pages a read of an account took, how many events it stored and how many the store already held (EventReader).
        "Pages", "Stored", "Duplicates",

        // How many stored LinkedIn links the start that re-keyed them found to be keys, left because the rule refuses them,
        // left beside the one holding their key, and how many keys more than one contact holds: counts (RuntimeHost; Stored
        // above is the number of links there).
        "Keys", "Refused", "LeftBeside", "SharedKeys",

        // Where a settings file stopped parsing: a line and a byte within it (RuntimeHost).
        "LineNumber", "BytePositionInLine",

        // How many of the build's own patterns finished warming and how many did not (PatternWarmUp); how long that took
        // is a DurationMs, above.
        "Finished", "Unfinished",

        // The import-file sweep at start: how many recorded folders it checked and skipped, and the public id of one it skipped
        // (ImportFilesSweep; Level above is a closed word, root, project or campaign).
        "Folders", "Skipped", "EntityId",

        // The port the settings asked for and the one the API listens on, both numbers (RuntimeHost).
        "ConfiguredPort", "Port",

        // Codes and closed words, and the envelope's level. A code is the runtime's own constant or a plugin outcome's
        // code, which the outcome validator holds to a snake_case token of at most 64 characters before anything logs it
        // (Code: Claimer, PluginInvoker, NotificationSender, UpdateChecker, and Responder, whose codes are its own skip
        // codes and the code of a domain error that refused a line). Verdict is "succeeded", "unknown", or
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

        // A migration that failed as the start began, by the id the history names it by: one of the build's own migrations
        // (RuntimeHost).
        "Migration",

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
        "DataDir", "Backup", "Directory", "Descriptor", "ContentRoot", "Ledger",

        // The step an update stopped at, one of the ledger's own words, or "unknown" where the ledger cannot be read: the
        // word is composed from the step the ledger was parsed to, never the file's text or a launch's mark (RuntimeHost).
        "Step",

        // Summoner: the chronicle kind that summoned a manager, or "scheduled".
        "Intent",

        // JournalLines: the kind of a line written off its document, which is only ever checked, and so logged, when it is
        // one the runtime reserves and publishes a document for: one of the build's own constants, never a kind a person
        // or an agent appended under a name of its own.
        "Kind",

        // Claimer: the level of a memory file the host could not write as a read rule, "client", "project" or
        // "campaign" — never the file's path, which is built from names a person gave.
        "Level",

        // ToastChannel: why there is no desktop, one of the channel's own presence words.
        "Detail",

        // ToastChannel: the toast host's jason: link state, "foreign" or "failed" and never another word, and the HRESULT
        // of a refused write, 0x and eight hex digits, or "none": both checked by the channel before the line is written.
        "Scheme", "Hr",

        // LiveSettings: validator and binding sentences, which name a setting by its path and describe a refused text
        // by its length. ParseFailure: a read failure's type and where the reader stopped, and for a stored value the
        // table and column the model names it by. InboundSync: why a read's result is not a page, a closed word or the
        // ParseFailure sentence. Responder: the ParseFailure sentence.
        "Failure",
    };

    /// <summary>
    /// Names that are not on the list because their values are free text a person typed, kept here so the reason is
    /// written down beside the decision. A plugin reload's reason goes to the journal and never to a log.
    /// </summary>
    public static IReadOnlySet<string> Refused { get; } = new HashSet<string>(StringComparer.Ordinal) { "Reason" };
}
