using System.Collections;
using System.Globalization;
using System.Net;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Jason.Cli.Discovery;
using Jason.Cli.Http;
using Jason.Cli.Logs;
using Jason.Cli.Status;
using Jason.Contracts;
using Jason.Contracts.Api;
using Jason.Contracts.Configuration;
using Jason.Contracts.Discovery;
using Jason.Contracts.Execution;
using Jason.Contracts.Json;
using Jason.Contracts.Notifications;
using Jason.Contracts.Plugins;
using Jason.Contracts.Skills;

namespace Jason.Cli.Doctor;

/// <summary>One section of a bundle as the bundle's own listing names it.</summary>
/// <param name="Name">The section's key in the document.</param>
/// <param name="Items">
/// How many things the section holds: version facts, checks, plugins, the settings file and each variable, log events,
/// the journal's recent entries, work-item failures, journal entries. A section the runtime did not answer for, or one
/// too large to write, holds none.
/// </param>
/// <param name="Bytes">The size of the section's JSON in the file, as <see cref="DoctorBundle.Serialize"/> writes it.</param>
public sealed record BundleSection(string Name, int Items, long Bytes);

/// <summary>
/// A part of a bundle that carries less than the runtime answered, and what it leaves out: named in the bundle, so a
/// reader knows a field is absent on purpose rather than wondering whether the runtime omitted it.
/// </summary>
/// <param name="Field">Where the projected answer stands in the bundle, as <c>section.field</c>, or the section it is.</param>
/// <param name="Withheld">
/// The fields it leaves out, as paths inside the runtime's answer, <c>[]</c> standing for each item of a list, each with how
/// it is left out where that is not simply by its absence.
/// </param>
public sealed record BundleProjection(string Field, IReadOnlyList<string> Withheld);

/// <summary>How a field the bundle withholds is left out.</summary>
public enum WithheldAs
{
    /// <summary>The field is absent.</summary>
    Dropped,

    /// <summary>The field is absent unless its value is one of the runtime's own words, which travel as written.</summary>
    DroppedUnlessOwnWord,

    /// <summary>
    /// The field's text is replaced by <see cref="DoctorComposer.Withheld"/> where the runtime reported one, and is null
    /// where it reported none, so a reader tells a withheld value from no value.
    /// </summary>
    ReplacedByWord,

    /// <summary>
    /// The field travels as written when its value is one of its own closed set's words, and is replaced by
    /// <see cref="DoctorComposer.Withheld"/> otherwise; null stays null.
    /// </summary>
    ReplacedUnlessOwnWord,
}

/// <summary>One field of the runtime's answer that a bundle withholds, and how.</summary>
/// <param name="Path">The field's path inside <c>system.info</c>, <c>[]</c> standing for each item of a list.</param>
/// <param name="How">How it is left out.</param>
/// <param name="Words">The closed set whose words travel as written, for the rules that keep a field's own words.</param>
public sealed record WithheldField(string Path, WithheldAs How, IReadOnlyList<string>? Words = null)
{
    /// <summary>The field as the bundle names it: its path, and how it is left out where that is not simply by its absence.</summary>
    public string Described => How switch
    {
        WithheldAs.DroppedUnlessOwnWord => $"{Path}, unless it is one of the runtime's own words",
        WithheldAs.ReplacedByWord => $"{Path}, as \"{DoctorComposer.Withheld}\"",
        WithheldAs.ReplacedUnlessOwnWord => $"{Path}, as \"{DoctorComposer.Withheld}\" unless it is one of its own closed set's words",
        _ => Path,
    };

    /// <summary>Whether a value is one of this field's own words.</summary>
    public bool IsOwnWord(string? text) => text is not null && Words is { } words && words.Contains(text, StringComparer.Ordinal);
}

/// <summary>A composed bundle: the document to write, and what a person is told about it before and after it is written.</summary>
/// <param name="Document">The whole bundle, its sections in their order, every name rewritten and every exception capped.</param>
/// <param name="Contents">Each section after the bundle's own header, in order, with its items and its size.</param>
/// <param name="Warnings">What a person must read before sharing it: the journal's warning, when the journal was asked for.</param>
/// <param name="ValuesCarried">The settings whose literal values the bundle holds, each path once, as the bundle spells it.</param>
/// <param name="NeverCarried">What no bundle holds, the fixed list.</param>
/// <param name="Projected">The answers the bundle carries in part, and the fields of each it leaves out.</param>
public sealed record DoctorBundle(
    JsonObject Document,
    IReadOnlyList<BundleSection> Contents,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> ValuesCarried,
    IReadOnlyList<string> NeverCarried,
    IReadOnlyList<BundleProjection> Projected)
{
    /// <summary>The document as it is written to the file: compact JSON, plain ASCII, the sizes in its listing its own.</summary>
    public string Serialize() => AsciiJson.Serialize(Document);
}

/// <summary>
/// Composes the diagnostic bundle: one installation collected into one document a person can read before they send
/// it to somebody else. Nothing is written here; the verb writes what this returns.
/// </summary>
/// <remarks>
/// <para>
/// <b>Sections, in this order:</b> <c>bundle</c> (what the file is and what was done to it), <c>versions</c>,
/// <c>status</c> (the report <c>jason status</c> composes, with no provider CLI, so no program runs), <c>runtime</c>
/// (<c>system.info</c> as projected, each plugin by its codes, whether the data directories agree), <c>config</c> (the
/// settings file and the <c>JASON_*</c> variables by shape), <c>logs</c>, <c>journal</c> and <c>work_items</c> (counted,
/// never read for their values), and <c>journal_entries</c> only when the journal was asked for.
/// </para>
/// <para>
/// <b>Values are dropped here.</b> The journal and work-item pages arrive in this process with everything the runtime
/// keeps — contexts, results, error messages, decisions, reasons, who did what — and only the listed metadata is taken
/// from them: kinds, statuses, ids, codes, counts and times. The same holds for a plugin problem's message, for every
/// setting and variable off <see cref="SettingsAllowlist"/>, and for the fields of <c>system.info</c> in
/// <see cref="SystemInfoWithheld"/>, which the bundle names. The descriptor is read to reach the runtime and its token is
/// never copied anywhere.
/// </para>
/// <para>
/// <b>Bounded.</b> The log is read from its end and never past a byte budget; the journal and the work items are read a
/// page of a thousand at a time and never past ten pages, whatever the runtime sends; and once a read has timed out, no
/// read after it is attempted — a runtime that accepts a request and never answers costs one timeout, not one for every
/// read left. A read that fails partway says how far it got and leaves every other section as it would have been. Every
/// section is bounded in the bundle's own bytes, so the whole file is too: <see cref="MaxBundleBytes"/>.
/// </para>
/// <para>
/// <b>Last,</b> every section's content is rewritten (<see cref="IdentityRewriter"/>), its keys with its values, and
/// then each log event's exception text is capped (<see cref="ExceptionText"/>), the cap after the rewrite so that no cut
/// leaves half of a name, and then each list is kept to its budget. The frame — the sections' names and the header's own
/// words and marks — is this program's text and holds nothing of the installation, so it is set after the rewrite rather
/// than through it: a person called <c>status</c> does not rename the section, and a machine called <c>user</c> does not
/// turn the mark <c>&lt;user&gt;</c> into something else. The one header value that comes from the installation, the list
/// of settings paths whose values travel, is rewritten as its own strings.
/// </para>
/// <para>
/// <b>Loopback only.</b> The only requests are to the descriptor's base URL — the status checks' and this composer's —
/// and only where that address is this machine's own; with no descriptor there is none at all.
/// </para>
/// </remarks>
public static partial class DoctorComposer
{
    /// <summary>What the file is, written first in it.</summary>
    public const string Format = "jason-doctor/1";

    /// <summary>
    /// What the journal's entries add to a bundle that carries them: one text, which the warning, the never-carried list
    /// and the published pages all say word for word, so none of them can name what another does not. The flag carries
    /// every entry whole, so this names every value any kind of entry holds — the runtime's tests hold each kind the
    /// runtime writes to a phrase of it, or to the kinds that hold only metadata. The chronicle is append-only, so a
    /// value since replaced still travels, as the old value of the entry that replaced it — "as they changed" says so.
    /// </summary>
    public const string JournalCarries =
        "what the journal entries say: campaign names and fields as they changed, project names as they changed, the folders a project link recorded, campaign and work-item contexts as they changed, result formats, the addresses and numbers put on or taken off the do-not-contact register, the accounts a campaign's routes bind, the options decisions were answered with, the errors attempts ended with, provider identifiers, the effect, tool and operation a report names, the failure text and receipts notification channels reported, entries people and agents wrote themselves, the names actors gave, and the reasons they typed";

    /// <summary>The words a bundle that carries the journal's entries says before anything else, wherever it is shown.</summary>
    public const string JournalWarning = "This bundle includes " + JournalCarries + ". Read it before you share it.";

    /// <summary>What a section the runtime would have answered for says when it did not.</summary>
    public const string RuntimeDidNotAnswer = "runtime_did_not_answer";

    /// <summary>A read that came back as something other than the answer it asked for.</summary>
    public const string UnreadableAnswer = "unreadable_answer";

    /// <summary>A read the runtime accepted and did not answer in time. It is the last read a bundle makes.</summary>
    public const string TimedOut = "timed_out";

    /// <summary>A read that was not made, because one before it timed out.</summary>
    public const string NotAttempted = "not_attempted";

    /// <summary>What a section says in place of itself when it is larger than its budget.</summary>
    public const string TooLarge = "too_large";

    /// <summary>The newest failures are the journal's: the items its newest entries say failed or expired.</summary>
    public const string FromJournal = "journal";

    /// <summary>
    /// The newest failures are the newest of what the work-item listing gave, which is oldest first and read no further
    /// than its page cap: the newest failures of a runtime with more than that many are not among them.
    /// </summary>
    public const string FromListOldestFirst = "list_oldest_first";

    /// <summary>The most events at <c>error</c> or <c>fatal</c> in <c>recent_errors</c>.</summary>
    public const int RecentErrors = 100;

    /// <summary>How far from the end of the log those errors are looked for: roughly the newest day of a busy installation.</summary>
    public const long ErrorSearchBytes = 64L * 1024 * 1024;

    /// <summary>The most of <c>recent_errors</c> in the bundle, so that a few enormous events cannot swell the file.</summary>
    public const long RecentErrorsBytes = 4L * 1024 * 1024;

    /// <summary>The most events in the slice of the log.</summary>
    public const int SliceEvents = 2_000;

    /// <summary>
    /// The most of the slice in the bundle, and the most log read for it: a few enormous events, or text outside ASCII,
    /// which the bundle writes six bytes to a character, end it before the count does.
    /// </summary>
    public const long SliceBytes = 4L * 1024 * 1024;

    /// <summary>The journal's window, in days back from the moment the bundle is composed.</summary>
    public const int JournalWindowDays = 7;

    /// <summary>One page of the journal or the work items: the most the API answers at once, and the most taken from one.</summary>
    public const int PageSize = 1_000;

    /// <summary>The most pages one listing is read for: what bounds the requests as well as the counts.</summary>
    public const int MaxPages = 10;

    /// <summary>The journal's newest entries listed by their metadata.</summary>
    public const int RecentEntries = 50;

    /// <summary>The newest failed or expired work items listed by their codes, each read by itself.</summary>
    public const int RecentFailures = 20;

    /// <summary>The most of the journal's entries in the bundle when the journal was asked for, so it still opens in an editor.</summary>
    public const long JournalEntriesBytes = 16L * 1024 * 1024;

    /// <summary>
    /// The most of each section besides the lists above. A section larger than its budget is written as
    /// <see cref="TooLarge"/> with its size instead of itself.
    /// </summary>
    public const long SectionBytes = 1024L * 1024;

    /// <summary>
    /// The most of the header. It is constants but for <c>values_carried</c>, which is cut from its end to fit, and
    /// <c>values_carried_truncated</c> says so.
    /// </summary>
    public const long HeaderBytes = 64L * 1024;

    /// <summary>
    /// The most a bundle can be: the lists' budgets, one <see cref="SectionBytes"/> for each of the nine sections, the
    /// header's budget, and a kilobyte for the names that hold them together.
    /// </summary>
    public const long MaxBundleBytes = RecentErrorsBytes + SliceBytes + JournalEntriesBytes + (9 * SectionBytes) + HeaderBytes + 1024;

    /// <summary>The most problem codes listed for one plugin; <c>problem_codes_truncated</c> says where there were more.</summary>
    public const int MaxProblemCodes = 20;

    /// <summary>
    /// The most of the plugin list inside the runtime section, in the bundle's own bytes: past it the list is cut, in the
    /// listing's order, and <c>plugins_truncated</c> says so — so the runtime's own report, in the rest of the section's
    /// <see cref="SectionBytes"/>, is never lost to a listing.
    /// </summary>
    public const long PluginsBytes = 512L * 1024;

    /// <summary>The longest plugin id, version or digest written as it is; a longer one is <see cref="TooLong"/>.</summary>
    public const int MaxPluginText = 128;

    /// <summary>What a plugin's id, version or digest longer than <see cref="MaxPluginText"/> characters is written as.</summary>
    public const string TooLong = "<longer than 128 characters>";

    /// <summary>What a withheld field's text is written as where the runtime reported one: see <see cref="WithheldAs.ReplacedByWord"/>.</summary>
    public const string Withheld = "withheld";

    /// <summary>
    /// The keys a section uses to say it is not whole, for whoever reads a bundle back — the verb's table among them — to
    /// look up by the composer's own spelling.
    /// </summary>
    public static class Keys
    {
        /// <summary>A section too large to write: <c>{omitted: too_large, bytes, budget}</c> in place of it.</summary>
        public const string Omitted = "omitted";

        /// <summary>The size a section too large to write had.</summary>
        public const string Bytes = "bytes";

        /// <summary>The budget a section too large to write was over.</summary>
        public const string Budget = "budget";

        /// <summary>A section the runtime would have answered for, and why it has nothing.</summary>
        public const string NotRead = "not_read";

        /// <summary>Whether the runtime answered <c>system.info</c>.</summary>
        public const string Answered = "answered";

        /// <summary>Why the runtime did not answer.</summary>
        public const string Reason = "reason";

        /// <summary>A read that stopped partway, and its code.</summary>
        public const string Incomplete = "incomplete";

        /// <summary>A listing cut short.</summary>
        public const string Truncated = "truncated";

        /// <summary>The ending of every other key that says a list was cut short: <c>slice_truncated</c> and the rest.</summary>
        public const string TruncatedSuffix = "_truncated";

        /// <summary>Where the newest failures came from: <see cref="FromJournal"/> or <see cref="FromListOldestFirst"/>.</summary>
        public const string RecentFailuresFrom = "recent_failures_from";
    }

    /// <summary>The names of the sections after the header, in their order.</summary>
    private const string Versions = "versions";
    private const string Status = "status";
    private const string Runtime = "runtime";
    private const string Routes = "routes";
    private const string Config = "config";
    private const string Logs = "logs";
    private const string Journal = "journal";
    private const string WorkItems = "work_items";
    private const string JournalEntries = "journal_entries";

    /// <summary>The property in which the runtime's log writes an event's exception.</summary>
    private const string ExceptionProperty = "@x";

    /// <summary>The most log files listed: a runtime keeps fourteen, and more is a directory something else filled.</summary>
    private const int MaxLogFiles = 1_000;

    /// <summary>
    /// What a variable's name starts with for the runtime to read it as a setting — in any case, as configuration matches
    /// it. The runtime's own copy cannot be referenced from here; the runtime's tests hold its prefix to this spelling of it.
    /// </summary>
    private const string VariablePrefix = "JASON_";

    /// <summary>
    /// The project's dialect with property names matched exactly. A context someone typed can hold two keys that differ
    /// only in case, and a node read with the dialect's case-insensitive names would refuse the whole page that holds it.
    /// </summary>
    private static readonly JsonSerializerOptions Dialect = new(JasonJson.Options) { PropertyNameCaseInsensitive = false };

    /// <summary>The <c>JASON_*</c> variables the code itself names, whose names are printed as they are; their values never.</summary>
    private static readonly HashSet<string> KnownVariables = new(StringComparer.OrdinalIgnoreCase)
    {
        JasonPaths.DataDirectoryVariable,
        ExecutionEnvironment.AttemptIdVariable,
        ExecutionEnvironment.WorkItemIdVariable,
    };

    /// <summary>
    /// What no bundle carries — the fixed list a bundle writes into itself and the verb prints. The harness root is the
    /// person's: it reaches a bundle as the runtime reports it in <c>system.info</c>, its path and its state, and nothing
    /// here reads under it. A folder under it travels only as the journal recorded it: a <c>project_linked</c> entry holds
    /// a project's folder and its campaigns', before and after, and those are journal values, carried with
    /// <c>--include-journal</c> alone.
    /// </summary>
    public static IReadOnlyList<string> NeverCarried { get; } =
    [
        "the capability token",
        "request and response bodies",
        "prompts",
        "work-item results",
        "the database",
        "work/",
        "plugin packages",
        "a route's account and its identity",
        "anything read from the harness root: no folder or file under it is listed or opened, so no memory file's name or word, and the folders a project link recorded travel only with --include-journal",
        "any setting or JASON_* value off the list",
        "journal values unless --include-journal: " + JournalCarries,
    ];

    /// <summary>
    /// The fields of <c>system.info</c> a bundle withholds, in this order. A channel's detail is a helper's last line of error
    /// output or pointers into somebody's binding; a presence's detail is the runtime's own word — one of
    /// <see cref="ChannelPresence.Details"/> — or text nobody controls, and only the word travels; a skill problem is an
    /// operating system's exception message, so only that there is one travels; and the release notes' address names the
    /// host of a configured feed, whose own setting never travels, so only whether a release had notes is kept, as
    /// <c>update.release_notes_offered</c>. A closed set travels and free text does not. This list is what the projection
    /// applies, so the two cannot disagree.
    /// </summary>
    public static IReadOnlyList<WithheldField> SystemInfoWithheld { get; } =
    [
        new("notifications.channels[].detail", WithheldAs.Dropped),
        new("notifications.channels[].presence_detail", WithheldAs.DroppedUnlessOwnWord, ChannelPresence.Details),
        new("skills.problem", WithheldAs.ReplacedByWord),
        new("skills.problem_code", WithheldAs.ReplacedUnlessOwnWord, SkillProblemCodes.All),
        new("skills.problem_exception", WithheldAs.ReplacedUnlessOwnWord, SkillProblemCodes.Exceptions),
        new("skills.roles[].problem", WithheldAs.ReplacedByWord),
        new("skills.roles[].problem_code", WithheldAs.ReplacedUnlessOwnWord, SkillProblemCodes.All),
        new("skills.roles[].problem_exception", WithheldAs.ReplacedUnlessOwnWord, SkillProblemCodes.Exceptions),
        new("update.release_notes_url", WithheldAs.Dropped),
    ];

    /// <summary>
    /// The answers a bundle carries in part: the one place a list of withheld fields is declared. The runtime's own report,
    /// and the readiness report, whose facts are composed from that report as projected, so a withheld field is withheld
    /// wherever a fact would quote it.
    /// </summary>
    public static IReadOnlyList<BundleProjection> Projections { get; } =
    [
        new($"{Runtime}.system_info", [.. SystemInfoWithheld.Select(field => field.Described)]),
        new(Status, [.. SystemInfoWithheld.Select(field => field.Described)]),
    ];

    /// <summary>
    /// The journal kinds that say a work item failed or expired: the runtime writes them itself, with the item's id, and
    /// the contract's list of the runtime's own kinds holds both.
    /// </summary>
    public static IReadOnlyList<string> FailureKinds { get; } = ["workitem_failed", "workitem_expired"];

    [GeneratedRegex(@"^-?(0|[1-9][0-9]*)\z", RegexOptions.CultureInvariant)]
    private static partial Regex IntegerText();

    [GeneratedRegex(@"^[a-z][a-z0-9_]{0,63}\z", RegexOptions.CultureInvariant)]
    private static partial Regex Code();

    /// <summary>The configuration system's two separators in a variable's name, <c>__</c> and <c>:</c>, kept when split on.</summary>
    [GeneratedRegex("(__|:)", RegexOptions.CultureInvariant)]
    private static partial Regex VariableSeparator();

    /// <summary>The bundle, composed now.</summary>
    public static Task<DoctorBundle> ComposeAsync(CliEnvironment env, bool includeJournal, CancellationToken cancellationToken) =>
        ComposeAsync(env, includeJournal, TimeProvider.System, cancellationToken);

    /// <summary>The bundle, composed at the moment <paramref name="clock"/> says: its stamp and the journal's window are read from it.</summary>
    public static async Task<DoctorBundle> ComposeAsync(CliEnvironment env, bool includeJournal, TimeProvider clock, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(clock);

        var now = clock.GetUtcNow();

        // One latch for every request this bundle makes, the status checks' included: the first timeout closes it. The
        // status checks read system.info through a projection of it, so the facts they compose quote nothing it withholds.
        var latch = new TimeoutLatch(env.HttpHandler ?? new SocketsHttpHandler());
        var projected = new ProjectedSystemInfo(latch);
        try
        {
            // As the status verb composes it: its own reads, through the same descriptor, and no provider CLI, so no program.
            var status = Projected(await StatusChecks.ComposeAsync(env with { HttpHandler = projected }, providerCli: null, cancellationToken).ConfigureAwait(false));

            var descriptor = new DescriptorReader(env.Paths).Read();
            using var client = descriptor is not null && IsLoopback(descriptor.BaseUrl) ? new RuntimeClient(descriptor, latch) : null;
            var reader = client is null ? null : new Reader(client, latch);
            var runtime = await RuntimeAsync(reader, descriptor, env.Paths, cancellationToken).ConfigureAwait(false);
            var answered = runtime.Info is not null ? reader : null;

            var routes = answered is null ? null : await RoutesAsync(answered, InstalledPlugins.Ids(env.Paths), cancellationToken).ConfigureAwait(false);
            var journal = answered is null ? null : await JournalAsync(answered, now, includeJournal, cancellationToken).ConfigureAwait(false);
            var workItems = answered is null ? null : await WorkItemsAsync(answered, journal, cancellationToken).ConfigureAwait(false);
            var versions = VersionsSection(runtime.Info);
            var config = Configuration(env);
            var logs = LogsSection(env.Paths);

            // A section the runtime would have answered for says why it has nothing: the runtime did not answer, or a read
            // before it timed out and it was not attempted.
            var notRead = latch.Closed ? NotAttempted : RuntimeDidNotAnswer;
            var sections = new List<Section>
            {
                new(Versions, versions, versions.Count),
                new(Status, JsonSerializer.SerializeToNode(status, Dialect)!, status.Checks.Count),
                new(Runtime, runtime.Node, runtime.Plugins),
                routes ?? new Section(Routes, NotReadSection(notRead), 0),
                new(Config, config.Node, config.Items),
                new(Logs, logs.Node, logs.Items),
                journal?.Summary is { } summary ? new Section(Journal, summary, journal.Recent) : new Section(Journal, NotReadSection(notRead), 0),
                workItems ?? new Section(WorkItems, NotReadSection(notRead), 0),
            };

            if (includeJournal)
            {
                sections.Add(journal?.Entries is { } entries ? new Section(JournalEntries, entries, journal.EntryCount) : new Section(JournalEntries, NotReadSection(notRead), 0));
            }

            // Last, with every name the bundle has learned: this installation's, and the data directory of a runtime that
            // keeps its data elsewhere.
            var rewriter = new IdentityRewriter(Facts(env, runtime));
            var written = sections.Select(section => Finish(section, rewriter)).ToList();

            var contents = written
                .Select(entry => new BundleSection(entry.Name, entry.Items, entry.Bytes))
                .ToList();

            // The values a section too large to write would have carried are not in the bundle, so they are not listed.
            var configWritten = written.Single(entry => entry.Name == Config).Kept;
            var carried = (configWritten ? config.Carried : [])
                .Select(path => rewriter.Rewrite(JsonValue.Create(path)!).GetValue<string>())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            IReadOnlyList<string> warnings = includeJournal ? [JournalWarning] : [];

            var header = Header(now, includeJournal, contents, carried, warnings);
            var document = new JsonObject { ["bundle"] = header };
            foreach (var entry in written)
            {
                document[entry.Name] = entry.Content;
            }

            return new DoctorBundle(document, contents, warnings, carried, NeverCarried, Projections);
        }
        finally
        {
            // A handler somebody handed in is theirs; only the ones made here are let go, the latch with them.
            if (env.HttpHandler is null)
            {
                projected.Dispose();
            }
        }
    }

    /// <summary>
    /// The names the bundle is rewritten by: this installation's, read from the machine unless the environment names them;
    /// its own data directory; and, where the runtime answering keeps its data somewhere else, that directory too.
    /// </summary>
    private static IdentityFacts Facts(CliEnvironment env, RuntimeRead runtime)
    {
        var facts = env.Identity ?? IdentityFacts.ForThisMachine();
        if (facts.DataDirectory is null)
        {
            facts = facts with { DataDirectory = env.Paths.Root };
        }

        // Only a directory a runtime could have: a path from a root, in either kind of spelling. Anything else names
        // nothing, and a mark for it would land on every word that spells it.
        if (facts.RuntimeDataDirectory is null && runtime is { Agrees: false, Info.DataDir: { Length: > 0 } elsewhere } && FromARoot(elsewhere))
        {
            facts = facts with { RuntimeDataDirectory = elsewhere };
        }

        return facts;
    }

    /// <summary>A path that starts at a root: a separator, or a drive and a separator.</summary>
    private static bool FromARoot(string path) =>
        path[0] is '/' or '\\' || (path.Length > 2 && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] is '/' or '\\');

    /// <summary>
    /// The readiness report as a bundle carries it: a role-skills fact made of withheld problems — the status checks join
    /// a refused role's problem text, one after another, each with its code after it — is one closed value, how many, and
    /// the codes that are the set's words, <c>withheld (2: skill_too_large, skill_file_missing)</c>, never the word
    /// repeated. Everything else is as the status verb composed it.
    /// </summary>
    private static StatusReport Projected(StatusReport report) =>
        report with
        {
            Checks = [.. report.Checks.Select(check =>
                check.Name == RoleSkillsCheck && WithheldSummary(check.Fact) is { } summary
                    ? check with { Fact = summary }
                    : check)],
        };

    /// <summary>The status check whose subject is the role skills, and whose fact quotes their problems.</summary>
    private const string RoleSkillsCheck = "role_skills";

    /// <summary>
    /// A fact made only of withheld problems, each with its code in brackets where it has one, as one closed value: how
    /// many, and the codes that are words of the set. Null when the fact says anything more.
    /// </summary>
    private static string? WithheldSummary(string fact)
    {
        var words = fact.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var count = 0;
        var codes = new List<string>();
        for (var index = 0; index < words.Length; index++)
        {
            if (words[index] != Withheld)
            {
                return null;
            }

            count++;
            if (index + 1 < words.Length && words[index + 1] is ['[', .. var inner, ']'])
            {
                var code = new string(inner);
                if (SkillProblemCodes.All.Contains(code, StringComparer.Ordinal))
                {
                    codes.Add(code);
                }

                index++;
            }
        }

        return count == 0 ? null
            : codes.Count == 0 ? string.Create(CultureInfo.InvariantCulture, $"{Withheld} ({count})")
            : string.Create(CultureInfo.InvariantCulture, $"{Withheld} ({count}: {string.Join(", ", codes)})");
    }

    /// <summary>One section before the rewrite: its name, its content and how many things it holds.</summary>
    private sealed record Section(string Name, JsonNode Content, int Items);

    /// <summary>One section as it is written: its content, how many things it holds now, its size, and whether it was kept or is too large.</summary>
    private sealed record Written(string Name, JsonNode Content, int Items, long Bytes, bool Kept);

    private static JsonObject NotReadSection(string why) => new() { [Keys.NotRead] = why };

    /// <summary>
    /// A section, last: rewritten, then its exception text capped and its lists kept to their budgets in the bundle's own
    /// bytes, then measured — and written as <see cref="TooLarge"/> with its size where it is still larger than its budget.
    /// </summary>
    private static Written Finish(Section section, IdentityRewriter rewriter)
    {
        var content = rewriter.Rewrite(section.Content);
        var items = section.Items;
        if (section.Name == Logs)
        {
            items = FinishLogs(section.Content.AsObject(), content.AsObject());
        }
        else if (section.Name == JournalEntries && section.Content is JsonObject composed && composed.ContainsKey("entries"))
        {
            items = FinishEntries(composed, content.AsObject());
        }
        else if (section.Name == Runtime && section.Content is JsonObject answered && answered["plugins"] is JsonArray)
        {
            items = FinishPlugins(answered, content.AsObject());
        }

        var bytes = Bytes(content);
        var budget = Budget(section.Name);
        if (bytes <= budget)
        {
            return new Written(section.Name, content, items, bytes, Kept: true);
        }

        var omitted = new JsonObject { [Keys.Omitted] = TooLarge, [Keys.Bytes] = bytes, [Keys.Budget] = budget };
        return new Written(section.Name, omitted, 0, Bytes(omitted), Kept: false);
    }

    private static long Budget(string section) => section switch
    {
        Logs => RecentErrorsBytes + SliceBytes + SectionBytes,
        JournalEntries => JournalEntriesBytes + SectionBytes,
        _ => SectionBytes,
    };

    /// <summary>The size of a node in the file, as <see cref="AsciiJson"/> writes it.</summary>
    private static long Bytes(JsonNode node) => Encoding.UTF8.GetByteCount(AsciiJson.Serialize(node));

    /// <summary>
    /// Drops items from one end of a list until what is left fits the budget in the bundle's own bytes, and says whether
    /// it dropped any. A list read oldest first keeps its newest by dropping from the start; one read newest first, from
    /// the end.
    /// </summary>
    private static bool Fit(JsonArray items, long budget, bool dropOldestFirst)
    {
        var sizes = items.Select(item => item is null ? 5L : Bytes(item) + 1).ToList();
        var total = sizes.Sum();
        var dropped = false;
        while (total > budget && items.Count > 0)
        {
            var at = dropOldestFirst ? 0 : items.Count - 1;
            total -= sizes[at];
            sizes.RemoveAt(at);
            items.RemoveAt(at);
            dropped = true;
        }

        return dropped;
    }

    /// <summary>
    /// Where a member of a rewritten section is: where the composed section has it. The rewrite keeps a section's members
    /// and their order but may rename any key — a person called <c>x</c> turns <c>@x</c> into <c>@&lt;user&gt;</c> — so a
    /// member is found by its place, never by its key in the copy.
    /// </summary>
    private static int Place(JsonObject composed, string key) => composed.IndexOf(key);

    private static void SetAt(JsonObject composed, JsonObject rewritten, string key, JsonNode value)
    {
        if (Place(composed, key) is >= 0 and var at)
        {
            rewritten.SetAt(at, value);
        }
    }

    // ---- bundle --------------------------------------------------------------------------------------------------

    /// <summary>
    /// The header: what the file is, what was done to it, and its own listing — held to <see cref="HeaderBytes"/>, the
    /// paths of <paramref name="carried"/> cut from its end where they would pass it.
    /// </summary>
    private static JsonObject Header(DateTimeOffset now, bool includeJournal, List<BundleSection> contents, List<string> carried, IReadOnlyList<string> warnings)
    {
        var header = new JsonObject
        {
            ["format"] = Format,
            ["created_at"] = Timestamp(now),
            ["cli_version"] = JasonVersion.Current,
            ["include_journal"] = includeJournal,
        };

        foreach (var mark in (string[])["home_written_as", "data_written_as", "runtime_data_written_as", "user_written_as", "machine_written_as", "domain_written_as", "sid_written_as"])
        {
            header[mark] = IdentityRewriter.MarkWords[mark];
        }

        header["exception_text"] = new JsonObject
        {
            ["applies_to"] = "the @x of every log event",
            ["max_line_characters"] = ExceptionText.MaxLineCharacters,
            ["max_message_characters"] = ExceptionText.MaxMessageCharacters,
            ["max_frames"] = ExceptionText.MaxFrames,
        };
        header["projected"] = new JsonArray([.. Projections.Select(projection => (JsonNode)new JsonObject
        {
            ["field"] = projection.Field,
            ["withheld"] = Strings(projection.Withheld),
        })]);
        header["contents"] = new JsonArray([.. contents.Select(section => (JsonNode)new JsonObject
        {
            ["name"] = section.Name,
            ["items"] = section.Items,
            ["bytes"] = section.Bytes,
        })]);
        header["values_carried"] = Strings(carried);
        header["values_carried_truncated"] = false;
        header["never_carried"] = Strings(NeverCarried);
        header["warnings"] = Strings(warnings);

        // Held to its budget by measuring it: everything in it is this program's but the settings paths whose values travel,
        // which are cut from the end until it fits, the list handed in with them.
        var size = Bytes(header);
        if (size > HeaderBytes)
        {
            var listed = header["values_carried"]!.AsArray();
            while (listed.Count > 0 && size > HeaderBytes)
            {
                size -= Bytes(listed[^1]!) + (listed.Count > 1 ? 1 : 0);
                listed.RemoveAt(listed.Count - 1);
                carried.RemoveAt(carried.Count - 1);
            }

            // One byte shorter than the false it replaces.
            header["values_carried_truncated"] = true;
        }

        return header;
    }

    // ---- versions ------------------------------------------------------------------------------------------------

    private static JsonObject VersionsSection(SystemInfoResponse? info) =>
        JsonSerializer.SerializeToNode(
            new VersionsLine(
                JasonVersion.Current,
                info?.RuntimeVersion,
                info?.ApiVersion,
                RuntimeInformation.OSDescription,
                Word(RuntimeInformation.OSArchitecture.ToString()),
                Word(RuntimeInformation.ProcessArchitecture.ToString()),
                RuntimeInformation.FrameworkDescription),
            Dialect)!.AsObject();

    internal sealed record VersionsLine(string Cli, string? Runtime, string? Api, string Os, string OsArchitecture, string ProcessArchitecture, string Dotnet);

    // ---- runtime -------------------------------------------------------------------------------------------------

    /// <summary>One route as a bundle carries it: the operation, or <c>default</c>; the plugin; and whether it binds an account.</summary>
    private sealed record RouteShown(string Operation, string PluginId, bool BindsAccount);

    /// <summary>A campaign's own routes, by the campaign's public id.</summary>
    private sealed record CampaignRoutesShown(string CampaignId, IReadOnlyList<RouteShown> Routes);

    /// <summary>The routes in force, by the snapshots that froze them.</summary>
    private sealed record RoutesShown(string RoutingSnapshotId, string PluginSnapshotId, IReadOnlyList<RouteShown> Global, IReadOnlyList<CampaignRoutesShown> Campaigns);

    /// <summary>
    /// Which plugin performs which operation, here and per campaign: the first question after whether the runtime is
    /// running. Built by a projection that has no field for a binding or its identity, so nothing has to be taken out —
    /// an account is withheld like a credential, and the identity with it, since a digest of a short account id is a
    /// guess away from the account. An operation the catalogue does not know and a plugin this installation does not
    /// hold are written as the settings write a key nobody chose; ids are written only when they are well formed.
    /// </summary>
    private static async Task<Section> RoutesAsync(Reader reader, IReadOnlySet<string> installed, CancellationToken cancellationToken)
    {
        var (routes, failure) = await reader.AskAsync<RoutesDto>(Operations.RouteList, new JsonObject(), cancellationToken).ConfigureAwait(false);
        if (routes?.Global is null || routes.Campaigns is null)
        {
            return new Section(Routes, NotReadSection(failure ?? UnreadableAnswer), 0);
        }

        RouteShown Shown(string operation, RouteDto route) =>
            new(operation, SettingKey.Shown(route.PluginId ?? string.Empty, installed), route.Binding is not null);

        List<RouteShown> Rows(RouteDto? fallback, IReadOnlyDictionary<string, RouteDto>? operations)
        {
            var rows = new List<RouteShown>();
            if (fallback is not null)
            {
                rows.Add(Shown("default", fallback));
            }

            foreach (var (operation, route) in operations ?? new Dictionary<string, RouteDto>())
            {
                if (route is not null)
                {
                    rows.Add(Shown(OperationShown(operation), route));
                }
            }

            return rows;
        }

        var global = Rows(routes.Global.Default, routes.Global.Operations);
        var campaigns = routes.Campaigns
            .Where(campaign => campaign is not null)
            .Select(campaign => new CampaignRoutesShown(SettingKey.Shown(campaign.CampaignId ?? string.Empty, installed), Rows(campaign.Default, campaign.Operations)))
            .ToList();
        var shown = new RoutesShown(
            SettingKey.Shown(routes.RoutingSnapshotId ?? string.Empty, installed),
            SettingKey.Shown(routes.PluginSnapshotId ?? string.Empty, installed),
            global,
            campaigns);
        return new Section(Routes, JsonSerializer.SerializeToNode(shown, Dialect)!, global.Count + campaigns.Sum(campaign => campaign.Routes.Count));

        // An operation is shown when the catalogue knows it; no plugin id or public id is an operation.
        static string OperationShown(string operation) =>
            Jason.Contracts.Operations.OperationCatalog.Knows(operation) ? operation : SettingKey.Hidden;
    }

    /// <summary>The runtime section, the answer it was made from, how many plugins it lists, and whether the data directories agree.</summary>
    private sealed record RuntimeRead(JsonObject Node, SystemInfoResponse? Info, int Plugins, bool? Agrees = null);

    private static async Task<RuntimeRead> RuntimeAsync(Reader? reader, RuntimeDescriptor? descriptor, JasonPaths paths, CancellationToken cancellationToken)
    {
        if (descriptor is null)
        {
            return NotAnswered(CliErrors.NoDescriptor);
        }

        // A descriptor that names an address off this machine is not one this installation's runtime wrote, and the
        // token in it is not sent there.
        if (reader is null)
        {
            return NotAnswered(CliErrors.StaleDescriptor);
        }

        var (info, failure, malformed) = await reader.AskReadingAsync<SystemInfoResponse>(Operations.SystemInfo, new JsonObject(), cancellationToken).ConfigureAwait(false);
        if (info is null)
        {
            // An answer that cannot be read as one is said to be so; any other refusal is something else answering at that
            // address, since system.info cannot fail by design.
            return NotAnswered(
                malformed ? UnreadableAnswer
                : failure is CliErrors.RuntimeUnreachable or CliErrors.Unauthorized or TimedOut or NotAttempted ? failure
                : CliErrors.StaleDescriptor);
        }

        if (!IsWhole(info))
        {
            return NotAnswered(UnreadableAnswer);
        }

        if (!string.Equals(info.InstanceId, descriptor.InstanceId, StringComparison.Ordinal))
        {
            return NotAnswered(CliErrors.StaleDescriptor);
        }

        var agrees = SameDirectory(info.DataDir, paths.Root);
        var node = new JsonObject
        {
            [Keys.Answered] = true,
            ["system_info"] = Project(JsonSerializer.SerializeToNode(info, Dialect)!.AsObject()),
        };

        // Each plugin by what identifies it and by its problems' codes, only those that are words and only so many of them;
        // an id, a version or a digest longer than any a package has is not written. A problem's message is a sentence
        // about the machine — a path, an executable, a version string somebody's tool printed — and is left behind; and a
        // page of plugins is the most taken, whatever the runtime sends.
        var (registry, refused) = await reader.AskAsync<PluginRegistryDto>(Operations.PluginList, new JsonObject(), cancellationToken).ConfigureAwait(false);
        var listed = registry?.Plugins?.Where(plugin => plugin is not null).ToList();
        if (listed is null)
        {
            node["plugins"] = null;
        }
        else
        {
            var plugins = listed.Take(PageSize).Select(Plugin).ToList();
            node["plugins"] = JsonSerializer.SerializeToNode(plugins, Dialect);
            node["plugins_truncated"] = listed.Count > PageSize;
        }

        node["data_dir_agrees"] = agrees;
        if (listed is null)
        {
            node[Keys.Incomplete] = refused ?? UnreadableAnswer;
        }

        return new RuntimeRead(node, info, Math.Min(listed?.Count ?? 0, PageSize), agrees);
    }

    private static RuntimeRead NotAnswered(string reason) => new(new JsonObject { [Keys.Answered] = false, [Keys.Reason] = reason }, null, 0);

    /// <summary>
    /// Whether a <c>system.info</c> answer holds every section its readers go into: the database and its migrations, the
    /// dispatcher, the plugins and the routes, and, where it says anything of role skills or of notifications, their lists.
    /// A runtime always sends every one; something else answering at its address may not, and the contract's types do not
    /// make a reader refuse what is missing. Such an answer is unreadable rather than a runtime's: read as one, the status
    /// checks failed on it and no bundle was written.
    /// </summary>
    private static bool IsWhole(SystemInfoResponse info) =>
        info.Database?.AppliedMigrations is not null
        && info.Dispatcher is not null
        && info.Plugins is not null
        && info.Routes is not null
        && (info.Skills is null || info.Skills.Roles is not null)
        && (info.Notifications is null || info.Notifications.Channels is not null);

    private static PluginLine Plugin(PluginDto plugin)
    {
        var codes = (plugin.Problems ?? []).Select(problem => problem?.Code).OfType<string>().Where(code => Code().IsMatch(code)).ToList();
        return new PluginLine(
            Bounded(plugin.Id),
            Bounded(plugin.Version),
            Bounded(plugin.Digest),
            plugin.Status,
            [.. codes.Take(MaxProblemCodes)],
            codes.Count > MaxProblemCodes);
    }

    private static string? Bounded(string? text) => text is { Length: > MaxPluginText } ? TooLong : text;

    internal sealed record PluginLine(string? Id, string? Version, string? Digest, PluginStatus Status, IReadOnlyList<string> ProblemCodes, bool ProblemCodesTruncated);

    /// <summary>
    /// <c>system.info</c> as a bundle carries it: each field of <see cref="SystemInfoWithheld"/> left out as that list says,
    /// and whether the release notes were offered kept in place of their address. Applied to the answer the composer read
    /// and to the one the status checks read, so the facts they compose quote nothing it leaves out.
    /// </summary>
    private static JsonObject Project(JsonObject systemInfo)
    {
        if (systemInfo["update"] is JsonObject update)
        {
            update["release_notes_offered"] = update["release_notes_url"] is not null;
        }

        foreach (var field in SystemInfoWithheld)
        {
            Withhold(systemInfo, field.Path.Split('.'), field);
        }

        return systemInfo;
    }

    /// <summary>Leaves out the field a path names as it says, a key ending in <c>[]</c> standing for every item of the list it names.</summary>
    private static void Withhold(JsonNode? node, ReadOnlySpan<string> path, WithheldField how)
    {
        if (path.IsEmpty || node is not JsonObject members)
        {
            return;
        }

        var key = path[0];
        if (!key.EndsWith("[]", StringComparison.Ordinal))
        {
            if (path.Length > 1)
            {
                Withhold(members[key], path[1..], how);
            }
            else if (members.ContainsKey(key))
            {
                Leave(members, key, how);
            }

            return;
        }

        if (members[key[..^2]] is JsonArray items)
        {
            foreach (var item in items)
            {
                Withhold(item, path[1..], how);
            }
        }
    }

    private static void Leave(JsonObject members, string key, WithheldField field)
    {
        var text = members[key] is JsonValue value && value.TryGetValue(out string? held) ? held : null;
        switch (field.How)
        {
            case WithheldAs.DroppedUnlessOwnWord when field.IsOwnWord(text):
                break;
            case WithheldAs.ReplacedUnlessOwnWord when members[key] is null || field.IsOwnWord(text):
                break;
            case WithheldAs.ReplacedByWord or WithheldAs.ReplacedUnlessOwnWord:
                members[key] = members[key] is null ? null : Withheld;
                break;
            default:
                members.Remove(key);
                break;
        }
    }

    /// <summary>
    /// Whether the runtime answering keeps its data where this CLI looks for it. Two installations on one machine are the
    /// usual reason they differ, and the reason a bundle's logs and its runtime can be about two different things. Both are
    /// read as one directory is: in full, without a separator at the end, without regard to case where paths have none,
    /// and on Windows by the long form of an existing directory, so a short form of one is the one.
    /// </summary>
    private static bool SameDirectory(string? reported, string own)
    {
        if (string.IsNullOrWhiteSpace(reported))
        {
            return false;
        }

        try
        {
            return string.Equals(
                Spelled(reported),
                Spelled(own),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }
        catch (Exception unreadable) when (unreadable is ArgumentException or NotSupportedException or PathTooLongException or SecurityException)
        {
            return false;
        }
    }

    /// <summary>
    /// A directory as one directory is spelled: in full, without a separator at its end, and long rather than short — on
    /// Windows the full path of a path with a <c>~</c> in it is the operating system's long one, wherever the folder exists.
    /// </summary>
    private static string Spelled(string directory) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));

    private static bool IsLoopback(string? address) =>
        Uri.TryCreate(address, UriKind.Absolute, out var uri) && uri.IsLoopback;

    // ---- config --------------------------------------------------------------------------------------------------

    /// <summary>The config section, how many things it describes, and the paths whose values it carries.</summary>
    private sealed record ConfigRead(JsonObject Node, int Items, List<string> Carried);

    private static ConfigRead Configuration(CliEnvironment env)
    {
        var list = SettingsAllowlist.Default;
        var installed = InstalledPlugins.Ids(env.Paths);
        var carried = new List<string>();

        JsonObject settings;
        try
        {
            var file = env.Paths.UserSettingsFile;
            settings = ConfigShape.Describe(File.Exists(file) ? File.ReadAllText(file) : null, list, installed, out var fromFile);
            carried.AddRange(fromFile);
        }
        catch (Exception unreadable) when (unreadable is IOException or UnauthorizedAccessException or SecurityException)
        {
            settings = new JsonObject { ["exists"] = true, ["readable"] = false };
        }

        var variables = JasonVariables(env)
            .Select(variable => Variable(variable.Key, variable.Value, list, installed))
            .OrderBy(variable => variable.Name, StringComparer.Ordinal)
            .ToList();
        carried.AddRange(variables.Where(variable => variable.CarriedPath is not null).Select(variable => variable.CarriedPath!));

        var node = new JsonObject
        {
            ["settings_file"] = settings,
            ["environment"] = new JsonArray([.. variables.Select(variable => (JsonNode)variable.Node)]),
        };
        return new ConfigRead(node, 1 + variables.Count, carried);
    }

    /// <summary>One variable as the section describes it, by the name it is shown as, and the path whose value it carries if any.</summary>
    private sealed record VariableRead(string Name, JsonObject Node, string? CarriedPath);

    /// <summary>
    /// One <c>JASON_*</c> variable: its name, with every key of the setting it names judged by the rule a settings key is
    /// — a name the code knows as typed, anything else <c>&lt;key&gt;</c> — and the separators kept as typed, so the bundle
    /// never names a variable that is not set; its value's shape; and its value only where every key was shown and the
    /// setting is on the list with a value of its kind. A variable is text, so a value is judged as the JSON literal it
    /// spells: <c>4</c> is a number and <c>true</c> a switch, as the configuration binder reads them, and anything else is
    /// a string. One item of a list is recorded as the list's path and <c>[]</c>, as the allowlist publishes it.
    /// </summary>
    private static VariableRead Variable(string name, string value, SettingsAllowlist list, IReadOnlySet<string> installed)
    {
        string shown;
        string? carriedPath = null;
        if (KnownVariables.Contains(name))
        {
            shown = name;
        }
        else
        {
            // Keys at the even places, separators at the odd ones: the split keeps what it splits on.
            var parts = VariableSeparator().Split(name[VariablePrefix.Length..]);
            var keys = parts.Where((_, at) => at % 2 == 0).ToArray();
            var path = string.Join(':', keys);
            var shownKeys = SettingKey.ShownPath(path, installed).Split(':');
            shown = name[..VariablePrefix.Length] + string.Concat(parts.Select((part, at) => at % 2 == 0 ? shownKeys[at / 2] : part));
            if (shownKeys.SequenceEqual(keys, StringComparer.Ordinal) && list.Carries(path, Literal(value)))
            {
                carriedPath = keys.Length > 1 && keys[^1].Length > 0 && keys[^1].All(char.IsAsciiDigit)
                    ? string.Join(':', keys[..^1]) + "[]"
                    : path;
            }
        }

        var node = new JsonObject { ["name"] = shown, ["shape"] = ConfigShape.DescribeText(value) };
        if (carriedPath is not null)
        {
            node["value"] = Literal(value);
        }

        return new VariableRead(shown, node, carriedPath);
    }

    private static JsonNode Literal(string text) =>
        text is "true" or "false" || IntegerText().IsMatch(text) ? JsonNode.Parse(text)! : JsonValue.Create(text)!;

    private static IEnumerable<KeyValuePair<string, string>> JasonVariables(CliEnvironment env) =>
        (env.Variables ?? ProcessVariables()).Where(variable => variable.Key.StartsWith(VariablePrefix, StringComparison.OrdinalIgnoreCase));

    private static Dictionary<string, string> ProcessVariables()
    {
        var variables = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string name && entry.Value is string value)
            {
                variables[name] = value;
            }
        }

        return variables;
    }

    // ---- logs ----------------------------------------------------------------------------------------------------

    private sealed record LogsRead(JsonObject Node, int Items);

    /// <summary>
    /// The log directory, its newest files, the newest errors and a slice from its end, each read with a byte budget and
    /// kept, once rewritten, to its budget in the bundle. A part that cannot be read leaves the section saying so and every
    /// other part as it is.
    /// </summary>
    private static LogsRead LogsSection(JasonPaths paths)
    {
        var directory = paths.LogsDirectory;
        string? failure = null;
        var exists = false;
        IReadOnlyList<LogFile> files = [];
        try
        {
            exists = Directory.Exists(directory);
            if (exists)
            {
                files = LogFiles.In(directory);
            }
            else if (File.Exists(directory))
            {
                failure = CliErrors.LogsUnreadable; // a file where the directory belongs
            }
        }
        catch (Exception unreadable) when (Unreadable(unreadable))
        {
            failure = CliErrors.LogsUnreadable;
        }

        var errors = Tail(files, new LogFilter(LogLevelName.Error), RecentErrors, ErrorSearchBytes, ref failure);
        var slice = Tail(files, new LogFilter(), SliceEvents, SliceBytes, ref failure);
        var listed = files.Skip(Math.Max(0, files.Count - MaxLogFiles)).Select(file => new LogFileEntry(file.Name, file.Bytes, file.ModifiedAt)).ToList();

        var node = new JsonObject
        {
            ["directory"] = directory,
            ["exists"] = exists,
            ["files"] = JsonSerializer.SerializeToNode(listed, Dialect),
            ["files_truncated"] = files.Count > MaxLogFiles,
            ["recent_errors"] = Events(errors),
            ["recent_errors_truncated"] = errors is { } found && (found.LimitReached || found.BytesExhausted),
            ["slice"] = Events(slice),
            ["slice_truncated"] = slice is { } read && (read.LimitReached || read.BytesExhausted),

            // Both reads start at the end of the same log, so the longer one covers the shorter: its count is the count
            // of every line either read and could not take for an event.
            ["skipped_lines"] = Math.Max(errors?.SkippedLines ?? 0, slice?.SkippedLines ?? 0),
        };

        if (failure is not null)
        {
            node[Keys.Incomplete] = failure;
        }

        return new LogsRead(node, (errors?.Events.Count ?? 0) + (slice?.Events.Count ?? 0));
    }

    private static LogTail? Tail(IReadOnlyList<LogFile> files, LogFilter filter, int limit, long maxBytes, ref string? failure)
    {
        try
        {
            return LogReader.Tail(files, filter, limit, maxBytes);
        }
        catch (Exception unreadable) when (Unreadable(unreadable))
        {
            failure = CliErrors.LogsUnreadable;
            return null;
        }
    }

    private static JsonArray Events(LogTail? tail) => new([.. tail?.Events ?? []]);

    private static bool Unreadable(Exception exception) => exception is IOException or UnauthorizedAccessException or SecurityException;

    /// <summary>
    /// The rewritten logs section, finished: every event's exception text capped, then <c>recent_errors</c> and the slice
    /// each kept to its budget in the bundle, the newest events kept. Returns how many events are left.
    /// </summary>
    private static int FinishLogs(JsonObject composed, JsonObject rewritten)
    {
        var events = 0;
        foreach (var (list, flag, budget) in new[] { ("recent_errors", "recent_errors_truncated", RecentErrorsBytes), ("slice", "slice_truncated", SliceBytes) })
        {
            if (composed[list] is not JsonArray originals || Place(composed, list) is not (>= 0 and var at) || rewritten.GetAt(at).Value is not JsonArray copies)
            {
                continue;
            }

            CapExceptionText(originals, copies);
            if (Fit(copies, budget, dropOldestFirst: true))
            {
                SetAt(composed, rewritten, flag, true);
            }

            events += copies.Count;
        }

        return events;
    }

    /// <summary>
    /// Caps the exception text of every event in a rewritten list, each found where the composed event has it: the rewrite
    /// may have renamed its key.
    /// </summary>
    private static void CapExceptionText(JsonArray originals, JsonArray copies)
    {
        for (var item = 0; item < originals.Count && item < copies.Count; item++)
        {
            if (originals[item] is JsonObject evt
                && copies[item] is JsonObject copy
                && evt.IndexOf(ExceptionProperty) is >= 0 and var exception
                && copy.GetAt(exception).Value is JsonValue value
                && value.TryGetValue(out string? text))
            {
                copy.SetAt(exception, JsonValue.Create(ExceptionText.Cap(text)));
            }
        }
    }

    /// <summary>The rewritten journal entries kept to their budget in the bundle, the newest kept. Returns how many are left.</summary>
    private static int FinishEntries(JsonObject composed, JsonObject rewritten)
    {
        if (Place(composed, "entries") is not (>= 0 and var at) || rewritten.GetAt(at).Value is not JsonArray entries)
        {
            return 0;
        }

        if (Fit(entries, JournalEntriesBytes, dropOldestFirst: false))
        {
            SetAt(composed, rewritten, Keys.Truncated, true);
        }

        return entries.Count;
    }

    /// <summary>
    /// The rewritten plugin list kept to <see cref="PluginsBytes"/> in the bundle, its first plugins kept in the listing's
    /// order — newest first means nothing for plugins — so the runtime's own report beside it is never lost to it. Returns
    /// how many plugins are left.
    /// </summary>
    private static int FinishPlugins(JsonObject composed, JsonObject rewritten)
    {
        if (Place(composed, "plugins") is not (>= 0 and var at) || rewritten.GetAt(at).Value is not JsonArray plugins)
        {
            return 0;
        }

        if (Fit(plugins, PluginsBytes, dropOldestFirst: false))
        {
            SetAt(composed, rewritten, "plugins_truncated", true);
        }

        return plugins.Count;
    }

    // ---- journal -------------------------------------------------------------------------------------------------

    /// <summary>
    /// The journal's summary, how many recent entries it lists, its entries whole when they were asked for, how many pages
    /// were read, and the items its newest entries say failed or expired. The summary is null when it was not attempted.
    /// </summary>
    private sealed record JournalRead(
        JsonObject? Summary,
        int Recent,
        JsonObject? Entries,
        int EntryCount,
        int Pages,
        bool Truncated,
        JsonObject Window,
        IReadOnlyList<string> Failed,
        string? Failure = null,
        bool MoreFailed = false);

    private static async Task<JournalRead> JournalAsync(Reader reader, DateTimeOffset now, bool includeJournal, CancellationToken cancellationToken)
    {
        var since = now.AddDays(-JournalWindowDays);
        var window = new JsonObject { ["since"] = Timestamp(since), ["days"] = JournalWindowDays };
        var notAttempted = new JournalRead(null, 0, null, 0, 0, false, window, []);
        if (reader.Closed)
        {
            return notAttempted;
        }

        var byKind = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var recent = new List<EntryLine>();
        var failed = new List<string>();
        var moreFailed = false;
        var entries = new JsonArray();
        var entryBytes = 0L;
        var entriesCut = false;
        var counted = 0;

        var paged = await reader.PageAsync<JournalEntryDto>(
            Operations.JournalList,
            new JsonObject { ["since"] = Timestamp(since) },
            entry =>
            {
                counted++;
                var kind = entry.Kind ?? string.Empty;
                byKind[kind] = byKind.GetValueOrDefault(kind) + 1;

                // Newest first, so the first read are the newest; and of each only what says what happened when, never
                // what it said: no key, no old or new value, no reason, and of the actor only its type.
                if (recent.Count < RecentEntries)
                {
                    recent.Add(new EntryLine(entry.Ts, kind, entry.CampaignId, entry.WorkItemId, entry.AttemptId, entry.Actor?.Type));
                }

                // The newest items the journal says failed or expired, each once: the newest failures of all, since the
                // journal is newest first, whatever the work-item listing's order.
                if (FailureKinds.Contains(kind) && entry.WorkItemId is { Length: > 0 } item && !failed.Contains(item))
                {
                    if (failed.Count < RecentFailures)
                    {
                        failed.Add(item);
                    }
                    else
                    {
                        moreFailed = true;
                    }
                }

                if (includeJournal && !entriesCut)
                {
                    // The runtime names the person a line is about when it is read; the journal itself holds only ids, and a
                    // bundle carries no person's name.
                    var whole = JsonSerializer.SerializeToNode(entry with { ContactName = null }, Dialect)!;
                    var size = Bytes(whole);
                    if (entryBytes + size > JournalEntriesBytes)
                    {
                        entriesCut = true;
                    }
                    else
                    {
                        entries.Add(whole);
                        entryBytes += size;
                    }
                }
            },
            cancellationToken).ConfigureAwait(false);

        if (paged is { Pages: 0, Failure: NotAttempted })
        {
            return notAttempted;
        }

        var kinds = new JsonObject();
        foreach (var (kind, count) in byKind)
        {
            kinds[kind] = count;
        }

        var summary = new JsonObject
        {
            ["window"] = window.DeepClone(),
            ["counted"] = counted,
            [Keys.Truncated] = paged.Truncated,
            ["by_kind"] = kinds,
            ["recent"] = JsonSerializer.SerializeToNode(recent, Dialect),
        };
        if (paged.Failure is not null)
        {
            summary[Keys.Incomplete] = paged.Failure;
        }

        JsonObject? whole = null;
        if (includeJournal)
        {
            whole = new JsonObject { ["entries"] = entries, [Keys.Truncated] = paged.Truncated || entriesCut };
            if (paged.Failure is not null)
            {
                whole[Keys.Incomplete] = paged.Failure;
            }
        }

        return new JournalRead(summary, recent.Count, whole, entries.Count, paged.Pages, paged.Truncated, window, failed, paged.Failure, moreFailed);
    }

    internal sealed record EntryLine(DateTimeOffset Ts, string Kind, string? CampaignId, string? WorkItemId, string? AttemptId, ActorType? ActorType);

    // ---- work items ----------------------------------------------------------------------------------------------

    /// <summary>
    /// Every work item counted by kind and status, and the newest failed or expired ones by their codes. The newest are the
    /// ones the journal's newest entries name, each read by itself; where the journal could not be read at all, they are
    /// the newest of what the listing of failures gave, and the section says the listing is oldest first.
    /// </summary>
    private static async Task<Section> WorkItemsAsync(Reader reader, JournalRead? journal, CancellationToken cancellationToken)
    {
        if (reader.Closed)
        {
            return new Section(WorkItems, NotReadSection(NotAttempted), 0);
        }

        var tally = new Dictionary<(WorkItemKind Kind, WorkItemStatus Status), int>();
        var counted = 0;
        var counting = await reader.PageAsync<WorkItemSummaryDto>(
            Operations.WorkItemList,
            new JsonObject(),
            item =>
            {
                counted++;
                tally[(item.Kind, item.Status)] = tally.GetValueOrDefault((item.Kind, item.Status)) + 1;
            },
            cancellationToken).ConfigureAwait(false);

        if (counting is { Pages: 0, Failure: NotAttempted })
        {
            return new Section(WorkItems, NotReadSection(NotAttempted), 0);
        }

        // Cut short whenever failures may exist that were not read: past the items read, or past where the source was cut
        // or stopped — whatever the count listed.
        var fromJournal = journal is { Pages: > 0 };
        var (failures, failure, truncated) = fromJournal
            ? await FailuresFromJournalAsync(reader, journal!, cancellationToken).ConfigureAwait(false)
            : await FailuresFromListingAsync(reader, cancellationToken).ConfigureAwait(false);

        var byKindAndStatus = new JsonObject();
        foreach (var kind in Enum.GetValues<WorkItemKind>())
        {
            var statuses = new JsonObject();
            foreach (var status in Enum.GetValues<WorkItemStatus>())
            {
                if (tally.GetValueOrDefault((kind, status)) is > 0 and var count)
                {
                    statuses[Word(status.ToString())] = count;
                }
            }

            if (statuses.Count > 0)
            {
                byKindAndStatus[Word(kind.ToString())] = statuses;
            }
        }

        var node = new JsonObject
        {
            ["counted"] = counted,
            [Keys.Truncated] = counting.Truncated,
            ["by_kind_and_status"] = byKindAndStatus,
            ["recent_failures"] = JsonSerializer.SerializeToNode(failures, Dialect),
            [Keys.RecentFailuresFrom] = fromJournal ? FromJournal : FromListOldestFirst,
        };
        if (fromJournal)
        {
            node["recent_failures_window"] = journal!.Window.DeepClone();
        }

        node["recent_failures_truncated"] = truncated;
        if ((counting.Failure ?? failure) is { } incomplete)
        {
            node[Keys.Incomplete] = incomplete;
        }

        return new Section(WorkItems, node, failures.Count);
    }

    /// <summary>
    /// The newest failures in the journal's window that are still failed or expired: the items its newest entries say
    /// failed or expired, each read with <c>workitem.get</c> — the first <see cref="RecentFailures"/> distinct ones and no
    /// more — in the journal's order, newest first, and kept only where the item is still failed or expired when read: one
    /// retried since and succeeded, or reopened, is not a failure. One that cannot be read is left out, and the first
    /// reason is kept; so is the journal's own, where it stopped partway. A failure older than the window is not a recent
    /// one; the count above still counts it.
    /// </summary>
    private static async Task<(List<FailureLine> Failures, string? Failure, bool StoppedEarly)> FailuresFromJournalAsync(Reader reader, JournalRead journal, CancellationToken cancellationToken)
    {
        var failures = new List<FailureLine>();
        string? first = null;
        foreach (var id in journal.Failed)
        {
            var (item, failure) = await reader.AskAsync<WorkItemSummaryDto>(Operations.WorkItemGet, new JsonObject { ["work_item_id"] = id }, cancellationToken).ConfigureAwait(false);
            if (item is null)
            {
                first ??= failure ?? UnreadableAnswer;
                continue;
            }

            if (item.Status is WorkItemStatus.Failed or WorkItemStatus.Expired)
            {
                failures.Add(Failure(item));
            }
        }

        // Cut short wherever failures may exist that were not read: the journal named more, stopped partway or failed, or
        // an item it named could not be read, or was not asked about once a read before it had timed out.
        return (failures, first ?? journal.Failure, first is not null || journal.MoreFailed || journal.Truncated || journal.Failure is not null);
    }

    /// <summary>
    /// The newest of what the listing of failed and expired items gave. The listing is oldest first and read no further
    /// than its page cap, so past that many failures the newest are not among them, which <c>list_oldest_first</c> says.
    /// </summary>
    private static async Task<(List<FailureLine> Failures, string? Failure, bool StoppedEarly)> FailuresFromListingAsync(Reader reader, CancellationToken cancellationToken)
    {
        var found = new List<(DateTimeOffset At, int Order, FailureLine Line)>();
        var paged = await reader.PageAsync<WorkItemSummaryDto>(
            Operations.WorkItemList,
            new JsonObject { ["status"] = new JsonArray(Word(nameof(WorkItemStatus.Failed)), Word(nameof(WorkItemStatus.Expired))) },
            item => found.Add((item.FinishedAt ?? item.UpdatedAt, found.Count, Failure(item))),
            cancellationToken).ConfigureAwait(false);

        var newest = found
            .OrderByDescending(failure => failure.At)
            .ThenByDescending(failure => failure.Order)
            .Take(RecentFailures)
            .Select(failure => failure.Line)
            .ToList();
        return (newest, paged.Failure, paged.Truncated || paged.Failure is not null);
    }

    /// <summary>
    /// Of a failure only what says which item failed how: never its context or result, and of its error the code and the
    /// class, never the message, which is a plugin's or a role's own sentence and may carry an identifier.
    /// </summary>
    private static FailureLine Failure(WorkItemSummaryDto item) => new(
        item.Id,
        item.CampaignId,
        item.Kind,
        item.Operation,
        item.Role,
        item.Status,
        item.AttemptCount,
        item.LastError is { } error ? new FailureCode(error.Code, error.Class) : null,
        item.FinishedAt);

    internal sealed record FailureLine(
        string Id,
        string CampaignId,
        WorkItemKind Kind,
        string? Operation,
        string? Role,
        WorkItemStatus Status,
        int AttemptCount,
        FailureCode? LastError,
        DateTimeOffset? FinishedAt);

    internal sealed record FailureCode(string Code, FailureClass? Class);

    // ---- reading -------------------------------------------------------------------------------------------------

    /// <summary>How a paged read ended: how many pages were read, whether more were left at the cap, and why it stopped early if it did.</summary>
    private sealed record Paged(int Pages, bool Truncated, string? Failure);

    /// <summary>
    /// Every request a bundle makes passes here, the status checks' included. A request to an address off this machine is
    /// refused before it is sent; and once a request has been given up on — the client's timeout cancels it — every later
    /// one is refused at once, so a runtime that accepts requests and never answers them costs one timeout.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The body is read here too, before the response leaves: a client otherwise reads it after the handlers have returned,
    /// under the same timeout, and a runtime that sends its headers and then stalls would time out where no handler sees
    /// it. A refused connection is not a timeout and does not close the latch: it fails at once, and the reads after it may
    /// still find the runtime. A cancellation the caller asked for closes it too, which costs nothing, since the bundle is
    /// then being abandoned. <c>jason status</c> has no latch: only a bundle shares one among its reads.
    /// </para>
    /// </remarks>
    private sealed class TimeoutLatch(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        private int _timedOut;

        public bool Closed => Volatile.Read(ref _timedOut) == 1;

        /// <summary>Closes the latch for a timeout seen outside it.</summary>
        public void Close() => Interlocked.Exchange(ref _timedOut, 1);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri is not { IsLoopback: true })
            {
                throw new HttpRequestException("The runtime's address is not on this machine, so nothing was sent to it.");
            }

            if (Closed)
            {
                throw new HttpRequestException("An earlier request to the runtime timed out, so this one was not made.");
            }

            HttpResponseMessage? response = null;
            try
            {
                response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
                await response.Content.LoadIntoBufferAsync(cancellationToken).ConfigureAwait(false);
                return response;
            }
            catch (OperationCanceledException)
            {
                response?.Dispose();
                Close();
                throw;
            }
        }
    }

    /// <summary>
    /// What the status checks read through, inside a bundle: <c>system.info</c> as the bundle carries it, so a fact they
    /// compose from it quotes nothing <see cref="SystemInfoWithheld"/> leaves out. An answer that cannot be read as one, or
    /// that leaves out a section the checks go into (<see cref="IsWhole"/>), reaches them as no answer at all, which they
    /// say as nothing having answered.
    /// </summary>
    private sealed class ProjectedSystemInfo(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode || request.RequestUri?.AbsolutePath.EndsWith(Operations.Route(Operations.SystemInfo), StringComparison.Ordinal) != true)
            {
                return response;
            }

            // Read as the status checks read it, into the contract's own type, so only the fields the contract names can
            // travel on — a key spelled in another case binds to its field and is withheld with it — and then projected
            // as the runtime section is. An answer that cannot be read as one is passed on as no answer at all.
            var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            SystemInfoResponse? answer;
            try
            {
                answer = JsonSerializer.Deserialize<SystemInfoResponse>(text, JasonJson.Options);
            }
            catch (Exception unreadable) when (unreadable is JsonException or ArgumentException or InvalidOperationException or NotSupportedException)
            {
                answer = null;
            }

            response.Content.Dispose();
            if (answer is null || !IsWhole(answer))
            {
                response.StatusCode = HttpStatusCode.BadGateway;
                response.Content = new StringContent(string.Empty);
                return response;
            }

            response.Content = new StringContent(Project(JsonSerializer.SerializeToNode(answer, Dialect)!.AsObject()).ToJsonString(), Encoding.UTF8, "application/json");
            return response;
        }
    }

    /// <summary>The composer's own reads: one client, behind the latch every request of the bundle passes.</summary>
    private sealed class Reader(RuntimeClient client, TimeoutLatch latch)
    {
        public bool Closed => latch.Closed;

        /// <summary>
        /// One request, and either its answer or a code for why there is none: not attempted, because one before it timed
        /// out; timed out; the runtime did not answer; it refused the token; it refused the request with a code of its own;
        /// or it answered with something that is not the answer asked for. Never an exception: a bundle is most wanted
        /// when the runtime is in trouble.
        /// </summary>
        public async Task<(T? Value, string? Failure)> AskAsync<T>(string operation, JsonObject body, CancellationToken cancellationToken)
            where T : class
        {
            var (value, failure, _) = await AskReadingAsync<T>(operation, body, cancellationToken).ConfigureAwait(false);
            return (value, failure);
        }

        /// <summary>
        /// The same, and whether the runtime answered with success and the answer could not be read as the one asked for —
        /// which tells a malformed answer from a runtime that refused or never answered.
        /// </summary>
        public async Task<(T? Value, string? Failure, bool Malformed)> AskReadingAsync<T>(string operation, JsonObject body, CancellationToken cancellationToken)
            where T : class
        {
            if (latch.Closed)
            {
                return (null, NotAttempted, false);
            }

            RuntimeResponse response;
            try
            {
                response = await client.PostAsync(operation, RequestBody.Serialize(body), cancellationToken).ConfigureAwait(false);
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Wherever the client gave up — the latch closes for what it sees, and this for anything it did not.
                latch.Close();
                return (null, TimedOut, false);
            }
            catch (HttpRequestException)
            {
                return (null, CliErrors.RuntimeUnreachable, false);
            }

            if (response.StatusCode == 401)
            {
                return (null, CliErrors.Unauthorized, false);
            }

            if (!response.IsSuccess)
            {
                return (null, Refusal(response.Body), false);
            }

            try
            {
                return JsonSerializer.Deserialize<T>(response.Body, Dialect) is { } value ? (value, null, false) : (null, UnreadableAnswer, true);
            }
            catch (Exception unreadable) when (unreadable is JsonException or ArgumentException or InvalidOperationException or NotSupportedException)
            {
                return (null, UnreadableAnswer, true);
            }
        }

        /// <summary>
        /// Reads a listing a page at a time, handing each item over as it arrives and keeping none of them: at most
        /// <see cref="PageSize"/> from a page and <see cref="MaxPages"/> pages, whatever the runtime sends. A page that
        /// cannot be read ends it there, and the items already handed over stand.
        /// </summary>
        public async Task<Paged> PageAsync<T>(string operation, JsonObject filter, Action<T> each, CancellationToken cancellationToken)
            where T : class
        {
            string? cursor = null;
            var cut = false;
            for (var pages = 0; pages < MaxPages; pages++)
            {
                var body = filter.DeepClone().AsObject();
                body["limit"] = PageSize;
                if (cursor is not null)
                {
                    body["cursor"] = cursor;
                }

                var (page, failure) = await AskAsync<Page<T>>(operation, body, cancellationToken).ConfigureAwait(false);
                if (page?.Items is null)
                {
                    return new Paged(pages, false, failure ?? UnreadableAnswer);
                }

                cut |= page.Items.Count > PageSize;
                foreach (var item in page.Items.Take(PageSize))
                {
                    if (item is not null)
                    {
                        each(item);
                    }
                }

                cursor = page.NextCursor;
                if (cursor is null)
                {
                    return new Paged(pages + 1, cut, null);
                }
            }

            return new Paged(MaxPages, true, null);
        }
    }

    /// <summary>The code of an error the runtime answered with — a word of its own vocabulary, never its message.</summary>
    private static string Refusal(string body)
    {
        try
        {
            var code = JsonSerializer.Deserialize<ErrorResponse>(body, Dialect)?.Error?.Code;
            return code is not null && Code().IsMatch(code) ? code : UnreadableAnswer;
        }
        catch (Exception unreadable) when (unreadable is JsonException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            return UnreadableAnswer;
        }
    }

    private static JsonNode Timestamp(DateTimeOffset moment) => JsonSerializer.SerializeToNode(moment, Dialect)!;

    private static string Word(string name) => JsonNamingPolicy.SnakeCaseLower.ConvertName(name);

    private static JsonArray Strings(IEnumerable<string> values) => new([.. values.Select(value => (JsonNode)JsonValue.Create(value)!)]);
}
