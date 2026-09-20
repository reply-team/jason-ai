using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Jason.Contracts.Update;

/// <summary>
/// The steps an update takes, in the order it takes them. Each is written down <b>before</b> it is performed, so
/// the last step a ledger names is the last one that was begun — and the one the next invocation has to finish
/// or undo.
/// </summary>
public enum UpdateStep
{
    /// <summary>The new executable is downloaded, its digest checked and its archive unpacked.</summary>
    Staged,

    /// <summary>The runtime has been told to claim nothing new, and what it was holding has ended or timed out.</summary>
    Drained,

    /// <summary>The runtime has been asked to stop, and its process is gone.</summary>
    Stopped,

    /// <summary>The installed executable has been moved aside, and the install path is empty.</summary>
    Kept,

    /// <summary>The staged executable has been moved onto the install path.</summary>
    Swapped,

    /// <summary>A runtime has been started from the new executable.</summary>
    Started,

    /// <summary>That runtime has answered with the version it was supposed to be, and with its migrations applied.</summary>
    Healthy,

    /// <summary>
    /// The update is over. The ledger stays, as the record of what happened: which versions, whether the new
    /// binary's first start migrated, which backup it wrote, and where the chronicle stood when it was healthy.
    /// </summary>
    /// <remarks>
    /// It used to be deleted here, which left <c>jason update rollback</c> an hour later with nothing to read —
    /// not the backup to restore, not whether there was one, not the chronicle position that decides whether
    /// restoring is still safe. A completed record is not work in flight, and the next update overwrites it at
    /// <see cref="Staged"/>.
    /// </remarks>
    Complete,
}

/// <summary>
/// What an update is doing, written down before each step it takes. One file, at
/// <c>&lt;data&gt;/update/ledger.json</c>, replaced whole every time.
/// </summary>
/// <remarks>
/// <para>
/// It is called a ledger and never a journal. The journal is the runtime's chronicle: a table, a verb, append-only
/// and enforced three times over. This is a file that one process writes and another reads, and it is overwritten
/// by the next update. Two things called the same word, one of which can be replaced, is a documentation defect
/// waiting to happen.
/// </para>
/// <para>
/// Read as strictly as a manifest, and for the same reason: an applier acts on what this says — it renames the
/// files these paths name and decides from these versions whether to go on or go back — so half a ledger would
/// send it down a path the installation is not on. Everything it carries is validated here, once.
/// </para>
/// </remarks>
public sealed record UpdateLedger(
    SemanticVersion FromVersion,
    SemanticVersion ToVersion,
    UpdateStep Step,
    DateTimeOffset StartedAt,
    string InstallPath,
    string StagedPath,
    string PreviousPath,
    DateTimeOffset? StoppedAt = null,
    string? BackupFile = null,
    IReadOnlyList<string>? NewlyApplied = null,
    string? ChronicleId = null)
{
    /// <summary>The file's name under the update directory, so nothing composes it from a string twice.</summary>
    public const string FileName = "ledger.json";

    /// <summary>Longer than any ledger this build writes, and short enough that a file that is not one is refused fast.</summary>
    private const int MaxBytes = 64 * 1024;

    /// <summary>The two names a write goes through, which are also how a reader knows one is happening.</summary>
    private const string Writing = ".writing";

    private const string Replaced = ".replaced";

    /// <summary>
    /// How patient a reader is while a write is in flight. A replacement is two renames; this is longer than
    /// any of them and short enough that a person who asked a question is still waiting for the answer.
    /// </summary>
    private const int Attempts = 25;

    private static readonly TimeSpan Wait = TimeSpan.FromMilliseconds(20);

    /// <summary>What the new runtime's first start applied, empty where it applied nothing.</summary>
    public IReadOnlyList<string> NewlyApplied { get; init; } = NewlyApplied ?? [];

    /// <summary>The same update, one step further on. Nothing else about it changes.</summary>
    public UpdateLedger At(UpdateStep step) => this with { Step = step };

    /// <summary>The ledger as it goes on disk: snake_case keys, versions as text, moments in UTC with a zone.</summary>
    public string ToJson()
    {
        var document = new JsonObject
        {
            ["from_version"] = FromVersion.ToString(),
            ["to_version"] = ToVersion.ToString(),
            ["step"] = Name(Step),
            ["started_at"] = Moment(StartedAt),
            ["install_path"] = InstallPath,
            ["staged_path"] = StagedPath,
            ["previous_path"] = PreviousPath,
            ["stopped_at"] = StoppedAt is null ? null : Moment(StoppedAt.Value),
            ["backup_file"] = BackupFile,
            ["newly_applied"] = new JsonArray([.. NewlyApplied.Select(m => (JsonNode)JsonValue.Create(m)!)]),
            ["chronicle_id"] = ChronicleId,
        };

        return document.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    /// <exception cref="UpdateLedgerException">The document is not a ledger this build can act on.</exception>
    public static UpdateLedger Read(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        if (json.Length > MaxBytes)
        {
            throw new UpdateLedgerException($"the ledger is longer than the {MaxBytes} characters one is read to.");
        }

        JsonNode? node;
        try
        {
            node = JsonNode.Parse(json);
        }
        catch (JsonException error)
        {
            throw new UpdateLedgerException("the ledger is not JSON.", error);
        }

        if (node is not JsonObject document)
        {
            throw new UpdateLedgerException("the ledger is not a JSON object.");
        }

        return new UpdateLedger(
            Version(document, "from_version"),
            Version(document, "to_version"),
            Step: ReadStep(document),
            StartedAt: Moment(document, "started_at", required: true)!.Value,
            InstallPath: Text(document, "install_path"),
            StagedPath: Text(document, "staged_path"),
            PreviousPath: Text(document, "previous_path"),
            StoppedAt: Moment(document, "stopped_at", required: false),
            BackupFile: OptionalText(document, "backup_file"),
            NewlyApplied: Migrations(document),
            ChronicleId: OptionalText(document, "chronicle_id"));
    }

    /// <summary>The ledger at this path, or null where there is none: nothing in flight is not a failure.</summary>
    /// <remarks>
    /// Opened so that nothing else is blocked by the reading. An update replaces this file by renaming over it,
    /// and on Windows a reader holding it with ordinary sharing makes that rename fail — so
    /// <c>jason update status</c>, run at the wrong instant, would stop an update in its tracks. Readers give
    /// way to the writer here rather than the other way round.
    /// </remarks>
    /// <exception cref="UpdateLedgerException">There is a file and it is not a ledger.</exception>
    public static UpdateLedger? ReadFile(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                if (File.Exists(path))
                {
                    using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    using var reader = new StreamReader(file);
                    return Read(reader.ReadToEnd());
                }

                // No file. Either there is no update, or one is replacing this very file as it is read: the
                // writer's own temporary names are how those two are told apart, and telling them apart is the
                // whole point — `jason update status` answering "nothing in flight" in the middle of an update
                // would be a lie told at the worst possible moment.
                //
                // Asked here, after the name was found missing, and not before it: a write that begins in
                // between would be invisible to an answer computed first, and the lie would be told anyway.
                if (!Replacing(path) || attempt >= Attempts)
                {
                    return null;
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException && attempt < Attempts)
            {
                // The same instant, seen from the other side: the name existed when it was asked about and was
                // gone, or momentarily unopenable, when it was opened.
            }

            Thread.Sleep(Wait);
        }
    }

    /// <summary>Whether a write is part-way through, by the two names only a write in flight leaves behind.</summary>
    private static bool Replacing(string path) => File.Exists(path + Writing) || File.Exists(path + Replaced);

    /// <summary>
    /// Writes the ledger whole, or not at all: to a temporary file beside it and then a rename over it.
    /// </summary>
    /// <remarks>
    /// The step a ledger names is taken <em>after</em> the file is on disk, so a torn write is the one state
    /// nothing could recover from — an applier would read half a document, refuse it, and stop in a place the
    /// installation is not in. A rename on one volume is atomic; a write in place is not.
    /// </remarks>
    public void Write(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporary = path + Writing;
        File.WriteAllText(temporary, ToJson());

        if (!File.Exists(path))
        {
            File.Move(temporary, path);
            return;
        }

        // Replace, not move-with-overwrite. Measured on Windows: a move over a file somebody has open is denied
        // even when that reader allowed sharing every way it can, while Replace succeeds — it is the call the
        // operating system provides for exactly this, swapping the names rather than deleting one of them. The
        // reader's share mode is the other half and both are needed: Replace against a reader that did not allow
        // sharing fails as well. The backup it insists on is deleted the moment it has served its purpose.
        var replaced = path + Replaced;
        File.Replace(temporary, path, replaced, ignoreMetadataErrors: true);
        File.Delete(replaced);
    }

    private static string Name(UpdateStep step) => step switch
    {
        UpdateStep.Staged => "staged",
        UpdateStep.Drained => "drained",
        UpdateStep.Stopped => "stopped",
        UpdateStep.Kept => "kept",
        UpdateStep.Swapped => "swapped",
        UpdateStep.Started => "started",
        UpdateStep.Healthy => "healthy",
        UpdateStep.Complete => "complete",
        _ => throw new ArgumentOutOfRangeException(nameof(step), step, "There is no such update step."),
    };

    private static UpdateStep ReadStep(JsonObject document)
    {
        var text = Text(document, "step");
        foreach (var step in Enum.GetValues<UpdateStep>())
        {
            if (string.Equals(Name(step), text, StringComparison.Ordinal))
            {
                return step;
            }
        }

        throw new UpdateLedgerException($"the ledger says step '{text}', and this build knows no such step.");
    }

    private static SemanticVersion Version(JsonObject document, string name) =>
        SemanticVersion.TryParse(Text(document, name), out var version)
            ? version
            : throw new UpdateLedgerException($"the ledger's '{name}' is not a version.");

    private static string Text(JsonObject document, string name) =>
        OptionalText(document, name) ?? throw new UpdateLedgerException($"the ledger carries no '{name}'.");

    private static string? OptionalText(JsonObject document, string name)
    {
        if (document[name] is not JsonValue value || !value.TryGetValue<string>(out var text) || string.IsNullOrEmpty(text))
        {
            return null;
        }

        return text;
    }

    /// <summary>Always with a zone, for the reason a manifest's moment is: a file is read on the machine that wrote it, and on others.</summary>
    private static DateTimeOffset? Moment(JsonObject document, string name, bool required)
    {
        var text = OptionalText(document, name);
        if (text is null)
        {
            return required ? throw new UpdateLedgerException($"the ledger carries no '{name}'.") : null;
        }

        return HasZone(text)
            && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var moment)
                ? moment.ToUniversalTime()
                : throw new UpdateLedgerException($"the ledger's '{name}' is not a moment with a time zone.");
    }

    private static bool HasZone(string text) =>
        text.EndsWith('Z') || text.Length > 6 && (text[^6] is '+' or '-') && text[^3] == ':';

    /// <summary>
    /// UTC, with the zone spelled <c>Z</c> rather than as an offset — the shape a manifest's own moment takes,
    /// and the one that survives a JSON encoder: the default one escapes a <c>+</c> to <c>+</c>, which
    /// turns a file a person may have to read by hand into one they have to decode.
    /// </summary>
    private static string Moment(DateTimeOffset moment) =>
        moment.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    private static IReadOnlyList<string> Migrations(JsonObject document)
    {
        if (document["newly_applied"] is not JsonArray array)
        {
            return [];
        }

        var migrations = new List<string>(array.Count);
        foreach (var element in array)
        {
            if (element is not JsonValue value || !value.TryGetValue<string>(out var name) || string.IsNullOrEmpty(name))
            {
                throw new UpdateLedgerException("the ledger's 'newly_applied' names something that is not a migration.");
            }

            migrations.Add(name);
        }

        return migrations;
    }
}

/// <summary>
/// The ledger could not be read. One code, because there is one thing a caller can do about any of them: say
/// what is on disk is not a ledger, and stop rather than act on a guess.
/// </summary>
public sealed class UpdateLedgerException : Exception
{
    public const string Invalid = "update_ledger_invalid";

    public UpdateLedgerException()
        : this("The update ledger could not be read.")
    {
    }

    public UpdateLedgerException(string message)
        : base(message) => Code = Invalid;

    public UpdateLedgerException(string message, Exception? innerException)
        : base(message, innerException) => Code = Invalid;

    /// <summary>Lowercase snake_case, as every error code in this product is.</summary>
    public string Code { get; } = Invalid;
}
