using System.CommandLine;
using System.Globalization;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Cli.Commands;
using Jason.Cli.Human;
using Jason.Cli.Logs;
using Jason.Contracts.Configuration;

namespace Jason.Cli.Doctor;

/// <summary>What was asked of <c>jason doctor</c>, as parsed.</summary>
/// <param name="DryRun">Compose the bundle and list it; write nothing.</param>
/// <param name="IncludeJournal">Also carry what the journal entries say, with the warning that goes with it.</param>
/// <param name="Output">The file to write instead of the default place, as typed; null for the default place.</param>
/// <param name="Human">Render for people instead of printing JSON.</param>
public sealed record DoctorOptions(bool DryRun, bool IncludeJournal, string? Output, bool Human);

/// <summary>What <c>jason doctor --dry-run</c> prints: where a plain run would write the bundle, and the bundle's own listing.</summary>
/// <param name="WouldWrite">The file a plain run would write now.</param>
/// <param name="Contents">Each section after the header, with its items and its size in the file.</param>
/// <param name="ValuesCarried">The settings whose literal values the bundle holds.</param>
/// <param name="NeverCarried">What no bundle holds.</param>
/// <param name="Projected">The answers the bundle carries in part, and what each leaves out.</param>
/// <param name="Warnings">What a person must read before sharing it.</param>
public sealed record DoctorListing(
    string WouldWrite,
    IReadOnlyList<BundleSection> Contents,
    IReadOnlyList<string> ValuesCarried,
    IReadOnlyList<string> NeverCarried,
    IReadOnlyList<BundleProjection> Projected,
    IReadOnlyList<string> Warnings);

/// <summary>What <c>jason doctor</c> prints after it wrote a bundle: where, how much and its digest, and the same listing.</summary>
/// <param name="Path">The file written.</param>
/// <param name="Bytes">How many bytes were written.</param>
/// <param name="Sha256">The SHA-256 of exactly those bytes, in lowercase hexadecimal, as a person compares it with the file.</param>
/// <param name="Contents">Each section after the header, with its items and its size in the file.</param>
/// <param name="ValuesCarried">The settings whose literal values the bundle holds.</param>
/// <param name="NeverCarried">What no bundle holds.</param>
/// <param name="Projected">The answers the bundle carries in part, and what each leaves out.</param>
/// <param name="Warnings">What a person must read before sharing it.</param>
public sealed record DoctorWritten(
    string Path,
    long Bytes,
    string Sha256,
    IReadOnlyList<BundleSection> Contents,
    IReadOnlyList<string> ValuesCarried,
    IReadOnlyList<string> NeverCarried,
    IReadOnlyList<BundleProjection> Projected,
    IReadOnlyList<string> Warnings);

/// <summary>
/// <c>jason doctor</c>: this installation collected into one diagnostic file a person can read before they share it. The
/// composition is <see cref="DoctorComposer"/>'s; this verb decides where the file goes, lists it before it is written,
/// writes it exactly as composed, and says what it wrote.
/// </summary>
/// <remarks>
/// <para>
/// One of the names in this CLI that stand for no API operation, and it cannot stand for one: half of what a bundle holds —
/// the files, the status checks — is not the runtime's to know, and a bundle is most wanted when the runtime does not
/// answer. So it never exits 3. A runtime that is not there, will not answer or refuses the descriptor's token is written
/// into the bundle as a fact, and the verb exits 0; the only refusals are its own, about the path it would write.
/// </para>
/// <para>
/// <b>Never over anything.</b> The file is created with <see cref="FileMode.CreateNew"/>, which is the one way to write
/// that cannot write over a file, even one another run made at the same name between a look and the write: a default name
/// that is taken moves on to the next number, and a file <c>--output</c> names that is already there is refused. Bundles
/// are not rolled; they are the person's, and stay until the person deletes them or the data directory goes.
/// </para>
/// <para>
/// <b>Listed before written.</b> There is no question asked between composing and writing — a question nobody can see
/// hangs a script — so <c>--dry-run</c> is how a person looks first: it composes the same bundle, writes nothing, not even
/// the directory, and lists what a plain run would write, item for item. It makes every check on the path that needs no
/// write, the same ones a plain run makes before composing; whether the place may be written only writing finds out, so a
/// plain run can still refuse a path the dry run listed, and the help says so.
/// </para>
/// </remarks>
public static class DoctorCommand
{
    /// <summary>The directory under the logs directory where a bundle is written when <c>--output</c> names no file.</summary>
    public const string DirectoryName = "doctor";

    /// <summary>How a bundle's default name starts; the second it was composed in, in UTC, follows.</summary>
    public const string NamePrefix = "jason-doctor-";

    /// <summary>How a bundle's default name ends.</summary>
    public const string Extension = ".json";

    /// <summary>
    /// The most numbers a taken default name moves through. Two runs in one second take two names; a thousand taken is a
    /// directory something else is filling, and the run says so rather than looking further.
    /// </summary>
    public const int MaxNumber = 1_000;

    private const long Mebibyte = 1024L * 1024;

    public static Command Build(CliEnvironment env)
    {
        ArgumentNullException.ThrowIfNull(env);

        const string Summary = "Collect this installation into one file a person can read before they share it.";
        var command = new Command("doctor", Summary)
        {
            Description = Summary + Environment.NewLine + Environment.NewLine + Contract(),
        };

        var dryRun = new Option<bool>("--dry-run")
        {
            Description = "Compose the bundle and list what a plain run would write, and where; write nothing.",
        };
        var includeJournal = new Option<bool>("--include-journal")
        {
            Description = "Also carry what the journal entries say. It prints a warning: read the file before you share it.",
        };
        var output = new Option<string?>("--output")
        {
            Description = "Write the bundle to this file instead of the logs directory. A file that is already there is refused.",
            HelpName = "file",
        };
        var human = VerbOptions.Human();

        command.Options.Add(dryRun);
        command.Options.Add(includeJournal);
        command.Options.Add(output);
        command.Options.Add(human);

        command.SetAction((parse, cancellationToken) => RunAsync(
            env,
            new DoctorOptions(parse.GetValue(dryRun), parse.GetValue(includeJournal), parse.GetValue(output), parse.GetValue(human)),
            cancellationToken));
        return command;
    }

    /// <summary>The verb, composed now.</summary>
    public static Task<int> RunAsync(CliEnvironment env, DoctorOptions options, CancellationToken cancellationToken) =>
        RunAsync(env, options, TimeProvider.System, cancellationToken);

    /// <summary>
    /// The verb, composed at the moment <paramref name="clock"/> says. Public, and taking that clock, for the reason
    /// <c>UninstallCommand.RunAsync</c> is public: two runs in one second are what a test has to make happen, and the
    /// repository has exactly one friend declaration, for a test that could not be written any other way. This one can.
    /// </summary>
    public static async Task<int> RunAsync(CliEnvironment env, DoctorOptions options, TimeProvider clock, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);

        // One moment, read once: the bundle's stamp and its file's name are the same second.
        var now = clock.GetUtcNow().ToUniversalTime();
        var stem = NamePrefix + now.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

        var named = Named(options.Output, out var unnamed);
        if (unnamed is not null)
        {
            return Refuse(env, options.Human, CliErrors.DoctorOutputUnwritable, unnamed);
        }

        var directory = named is null ? Path.Combine(env.Paths.LogsDirectory, DirectoryName) : Path.GetDirectoryName(named)!;
        var first = named ?? Candidate(directory, stem, 0);

        // Refused before anything is read, by the dry run as by the plain run: composing can wait out a runtime that does
        // not answer, and a person who named a file that is there, or a place that cannot be written for a reason known
        // without writing, learns it now rather than after that wait.
        if (named is not null && Taken(named))
        {
            return Refuse(
                env,
                options.Human,
                CliErrors.DoctorOutputExists,
                $"There is already something at {named}, and a bundle is never written over anything. Name a file that is not there yet, or leave out --output for a name of its own.");
        }

        if (CannotBeWritten(directory, first) is { } refusal)
        {
            return Refuse(env, options.Human, CliErrors.DoctorOutputUnwritable, refusal);
        }

        var bundle = await DoctorComposer.ComposeAsync(env, options.IncludeJournal, new Frozen(now), cancellationToken).ConfigureAwait(false);

        if (!options.DryRun)
        {
            return Write(env, bundle, named, directory, stem, options.Human);
        }

        return (named ?? NextFree(directory, stem)) is { } path
            ? Listed(env, bundle, path, options.Human)
            : AllTaken(env, options.Human, directory, stem);
    }

    // ---- where --------------------------------------------------------------------------------------------------------

    /// <summary>
    /// The file <c>--output</c> names, as a full path, or null when it names none. A blank value is a usage error. A value
    /// that names no file — one ending in a separator, which names a directory, a person typing <c>--output reports/</c>
    /// for "put it in reports" — or that is no path at all is refused here, in <paramref name="refusal"/>, before anything
    /// is read or made.
    /// </summary>
    private static string? Named(string? output, out string? refusal)
    {
        refusal = null;
        if (output is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(output))
        {
            throw new UsageException($"--output must name a file; '{output}' does not.");
        }

        string full;
        try
        {
            full = Path.GetFullPath(output);
        }
        catch (Exception unusable) when (Unwritable(unusable))
        {
            refusal = $"'{output}' cannot be written: {unusable.Message} Nothing was written.";
            return null;
        }

        // A device's name — NUL, CON — is a name Windows answers for itself, in any directory, and is no file.
        if (OperatingSystem.IsWindows() && full.StartsWith(@"\\.\", StringComparison.Ordinal))
        {
            refusal = $"'{output}' names a device, not a file, and nothing was written: --output takes the file to write, for example {Path.Join(Environment.CurrentDirectory, "jason-doctor.json")}.";
            return null;
        }

        if (Path.EndsInDirectorySeparator(output) || Path.GetFileName(full).Length == 0)
        {
            refusal = $"'{output}' names a directory, not a file, and nothing was written: --output takes the file to write, for example {Path.Join(full, "jason-doctor.json")}.";
            return null;
        }

        return full;
    }

    /// <summary>Whether anything at all is at a path: a file, or a directory, which is not written over either.</summary>
    private static bool Taken(string path) => File.Exists(path) || Directory.Exists(path);

    /// <summary>
    /// Why a file cannot be written, where that is known without writing anything: its volume or root is not there, a file
    /// stands where a directory would have to be, or a name that would have to be made — the file's, or a directory's on
    /// the way to it — is one this system refuses. Null where none of these holds, which is no promise that the place may
    /// be written: whether it may, only writing finds out, so a plain run can still refuse what a dry run listed.
    /// </summary>
    private static string? CannotBeWritten(string directory, string file)
    {
        var root = Path.GetPathRoot(file);
        if (!string.IsNullOrEmpty(root) && !Directory.Exists(root))
        {
            return $"{file} cannot be written: {root} is not there. Nothing was written.";
        }

        // From the file's own directory up to the first one that is there, every name below it would have to be made.
        var toMake = new List<string> { Path.GetFileName(file) };
        for (var at = directory; !string.IsNullOrEmpty(at); at = Path.GetDirectoryName(at))
        {
            if (Directory.Exists(at))
            {
                break;
            }

            if (File.Exists(at))
            {
                return $"{file} cannot be written: {at} is a file, where a directory would have to be. Nothing was written.";
            }

            toMake.Add(Path.GetFileName(at));
        }

        return toMake.FirstOrDefault(name => name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) is { } refused
            ? $"{file} cannot be written: '{refused}' is not a name this system allows. Nothing was written."
            : null;
    }

    /// <summary>The nearest of a directory and the directories above it that is there now, or null when none is.</summary>
    private static string? NearestThere(string directory)
    {
        for (var at = directory; !string.IsNullOrEmpty(at); at = Path.GetDirectoryName(at))
        {
            if (Directory.Exists(at))
            {
                return at;
            }
        }

        return null;
    }

    /// <summary>
    /// Takes away the directories a run made for a file it then did not write, from the deepest up to the nearest one that
    /// was there before, each only while it is empty: a directory another run has written into since is left as it is,
    /// and so is everything above it. A run beside this one, into the same new directory, can still lose a directory it
    /// had not yet written into; it makes the directory again before it refuses (<see cref="Write"/>).
    /// </summary>
    private static void Unmake(string directory, string? there)
    {
        for (var at = directory; !string.IsNullOrEmpty(at) && !string.Equals(at, there, StringComparison.Ordinal); at = Path.GetDirectoryName(at))
        {
            try
            {
                // Not recursive: a directory that is not empty refuses, which is where this stops.
                Directory.Delete(at);
            }
            catch (Exception kept) when (Unwritable(kept))
            {
                return;
            }
        }
    }

    /// <summary>
    /// The default name, or the first numbered one after it that nothing is at: what a plain run would take now. Null when
    /// every one is taken, which a plain run refuses.
    /// </summary>
    private static string? NextFree(string directory, string stem) =>
        Enumerable.Range(0, MaxNumber + 1).Select(number => Candidate(directory, stem, number)).FirstOrDefault(path => !Taken(path));

    private static string Candidate(string directory, string stem, int number) =>
        Path.Combine(directory, number == 0 ? stem + Extension : string.Create(CultureInfo.InvariantCulture, $"{stem}-{number}{Extension}"));

    // ---- write --------------------------------------------------------------------------------------------------------

    private enum Created
    {
        Written,
        Taken,
        Refused,

        /// <summary>The directory the file was to go in was not there when it was created: another run had taken it away.</summary>
        DirectoryGone,
    }

    /// <summary>
    /// How many times the directory is made again when it has gone between being made and the file's being created in it,
    /// before that counts as a refusal. A run beside this one that refuses takes away the empty directories it made, and
    /// can take this run's with them in that gap; nothing removes a directory twice for long.
    /// </summary>
    private const int Remakes = 2;

    /// <summary>
    /// Called just before each file is created, with its path, once its directory has been made. A test's only way into
    /// the moment between the two, where another run can take the directory away or write into it: what is held there
    /// cannot otherwise be shown except by racing two runs, and a race that a test may lose is no proof.
    /// </summary>
    internal static AsyncLocal<Action<string>?> BeforeCreate { get; } = new();

    private static int Write(CliEnvironment env, DoctorBundle bundle, string? named, string directory, string stem, bool human)
    {
        // Exactly the composer's text: every size in its listing is measured in this form, and the digest printed is of
        // these bytes.
        var bytes = Encoding.UTF8.GetBytes(bundle.Serialize());

        // What this run makes starts below the nearest directory already there, and a refusal takes it away again, so a
        // run that says nothing was written leaves the tree as it found it.
        var there = NearestThere(directory);
        int Refused(string code, string message)
        {
            Unmake(directory, there);
            return Refuse(env, human, code, message);
        }

        // The file, created in a directory made for it, and the directory made again a couple of times where another run
        // took it away in between.
        Created Create(string path, out string? reason)
        {
            for (var made = 0; ; made++)
            {
                try
                {
                    Directory.CreateDirectory(directory);
                }
                catch (DirectoryNotFoundException) when (made < Remakes)
                {
                    // A directory above it was taken away while this one was being made.
                    continue;
                }
                catch (Exception failure) when (Unwritable(failure))
                {
                    reason = failure.Message;
                    return Created.Refused;
                }

                BeforeCreate.Value?.Invoke(path);
                var created = TryCreate(path, bytes, out reason);
                if (created != Created.DirectoryGone)
                {
                    return created;
                }

                if (made == Remakes)
                {
                    return Created.Refused;
                }
            }
        }

        if (named is not null)
        {
            return Create(named, out var reason) switch
            {
                Created.Written => Written(env, bundle, named, bytes, human),
                Created.Taken => Refused(CliErrors.DoctorOutputExists, $"Something was put at {named} while the bundle was composed, and a bundle is never written over anything. Nothing was written."),
                _ => Refused(CliErrors.DoctorOutputUnwritable, $"{named} cannot be written: {reason} Nothing was written."),
            };
        }

        for (var number = 0; number <= MaxNumber; number++)
        {
            var candidate = Candidate(directory, stem, number);
            switch (Create(candidate, out var reason))
            {
                case Created.Written:
                    return Written(env, bundle, candidate, bytes, human);
                case Created.Taken:
                    continue;
                default:
                    return Refused(CliErrors.DoctorOutputUnwritable, $"{candidate} cannot be written: {reason} Nothing was written.");
            }
        }

        // Every name is taken, so the directory holds every one of them: there is nothing this run made to take away.
        return AllTaken(env, human, directory, stem);
    }

    private static int AllTaken(CliEnvironment env, bool human, string directory, string stem) =>
        Refuse(
            env,
            human,
            CliErrors.DoctorOutputUnwritable,
            $"Every name from {Candidate(directory, stem, 0)} to {Candidate(directory, stem, MaxNumber)} is taken, so nothing was written. Name a file with --output.");

    /// <summary>
    /// Creates the file and writes the bytes, or says why not: it was there, which is never written over, or it cannot be
    /// written. A file this made and could not finish is taken away again, so a refusal leaves no half of a bundle behind.
    /// That last branch has no test: nothing a test can arrange makes a write fail once the file has been made, short of a
    /// seam this verb would carry for nothing else.
    /// </summary>
    private static Created TryCreate(string path, byte[] bytes, out string? reason)
    {
        reason = null;
        FileStream file;
        try
        {
            file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        }
        catch (Exception failure) when (Unwritable(failure))
        {
            if (AlreadyThere(failure))
            {
                return Created.Taken;
            }

            reason = failure.Message;
            return failure is DirectoryNotFoundException ? Created.DirectoryGone : Created.Refused;
        }

        try
        {
            using (file)
            {
                file.Write(bytes);
            }

            return Created.Written;
        }
        catch (Exception failure) when (Unwritable(failure))
        {
            reason = failure.Message;
            try
            {
                File.Delete(path);
            }
            catch (Exception leftover) when (Unwritable(leftover))
            {
                reason += $" The part written could not be taken away again: {path}.";
            }

            return Created.Refused;
        }
    }

    private static bool Unwritable(Exception failure) =>
        failure is IOException or UnauthorizedAccessException or SecurityException or ArgumentException or NotSupportedException;

    /// <summary>
    /// Whether a create failed because a file is already there, read from the failure itself — the system's own code for
    /// it, which .NET carries as the exception's HResult: <c>ERROR_FILE_EXISTS</c> or <c>ERROR_ALREADY_EXISTS</c> on
    /// Windows, <c>EEXIST</c> elsewhere. Never by looking at the path again afterwards: what is there by then may be a
    /// directory this run made itself.
    /// </summary>
    private static bool AlreadyThere(Exception failure) =>
        failure is IOException { HResult: var code }
        && (OperatingSystem.IsWindows() ? code is WindowsFileExists or WindowsAlreadyExists : code == UnixFileExists);

    private const int WindowsFileExists = unchecked((int)0x80070050);

    private const int WindowsAlreadyExists = unchecked((int)0x800700B7);

    /// <summary><c>EEXIST</c>, which is 17 on Linux and on macOS alike.</summary>
    private const int UnixFileExists = 17;

    // ---- what is printed ----------------------------------------------------------------------------------------------

    private static int Listed(CliEnvironment env, DoctorBundle bundle, string path, bool human)
    {
        if (human)
        {
            Render(env.Out, bundle, $"Would write {path}");
        }
        else
        {
            env.Out.WriteLine(AsciiJson.Serialize(new DoctorListing(path, bundle.Contents, bundle.ValuesCarried, bundle.NeverCarried, bundle.Projected, bundle.Warnings)));
        }

        Warn(env, bundle);
        return ExitCodes.Success;
    }

    private static int Written(CliEnvironment env, DoctorBundle bundle, string path, byte[] bytes, bool human)
    {
        var sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes));
        if (human)
        {
            Render(env.Out, bundle, string.Create(CultureInfo.InvariantCulture, $"Written to {path} ({bytes.LongLength} bytes, sha256 {sha256})."));
        }
        else
        {
            env.Out.WriteLine(AsciiJson.Serialize(new DoctorWritten(path, bytes.LongLength, sha256, bundle.Contents, bundle.ValuesCarried, bundle.NeverCarried, bundle.Projected, bundle.Warnings)));
        }

        Warn(env, bundle);
        return ExitCodes.Success;
    }

    /// <summary>
    /// The bundle's warnings on stderr, each once, after the result: the journal's, when its entries were asked for. It is
    /// in the printed result and in the file as well, and here it is where a person at a terminal sees it last.
    /// </summary>
    private static void Warn(CliEnvironment env, DoctorBundle bundle)
    {
        foreach (var warning in bundle.Warnings)
        {
            env.Error.WriteLine(warning);
        }
    }

    private static int Refuse(CliEnvironment env, bool human, string code, string message)
    {
        if (human)
        {
            env.Error.WriteLine(message);
        }
        else
        {
            env.Out.WriteLine(CliErrors.Serialize(code, message, retryable: false));
        }

        return ExitCodes.ApiError;
    }

    // ---- for people ---------------------------------------------------------------------------------------------------

    /// <summary>The listing as a table, each section with what the bundle says about it, the lists beneath, and where.</summary>
    private static void Render(TextWriter output, DoctorBundle bundle, string where)
    {
        var table = new HumanTable("SECTION", "ITEMS", "BYTES", "NOTE");
        foreach (var section in bundle.Contents)
        {
            table.Row(
                section.Name,
                section.Items.ToString(CultureInfo.InvariantCulture),
                section.Bytes.ToString(CultureInfo.InvariantCulture),
                Note(bundle.Document[section.Name]));
        }

        output.WriteLine(table.Render());
        output.WriteLine();
        output.WriteLine("Values carried:  " + (bundle.ValuesCarried.Count == 0 ? "none" : string.Join(", ", bundle.ValuesCarried)));
        output.WriteLine("Never carried:   " + string.Join("; ", bundle.NeverCarried));
        foreach (var projection in bundle.Projected)
        {
            output.WriteLine($"Projected:       {projection.Field} without {string.Join("; ", projection.Withheld)}");
        }

        output.WriteLine("Rewritten:       " + string.Join(", ", Rewritten(bundle.Document)));
        foreach (var warning in bundle.Warnings)
        {
            output.WriteLine();
            output.WriteLine("Warning: " + warning);
        }

        output.WriteLine();
        output.WriteLine(where);
    }

    /// <summary>
    /// What a section says about itself when it is not whole, in the bundle's own words: too large to write, not read and
    /// why, not answered and why, stopped partway and why, the lists cut short, and where the newest failures came from.
    /// </summary>
    private static string Note(JsonNode? section)
    {
        if (section is not JsonObject members)
        {
            return string.Empty;
        }

        if (Word(members[DoctorComposer.Keys.Omitted]) is { } omitted)
        {
            return $"{omitted}: {members[DoctorComposer.Keys.Bytes]?.ToJsonString()} bytes, its budget {members[DoctorComposer.Keys.Budget]?.ToJsonString()}";
        }

        var notes = new List<string>();
        if (Word(members[DoctorComposer.Keys.NotRead]) is { } notRead)
        {
            notes.Add($"not read: {notRead}");
        }

        if (members[DoctorComposer.Keys.Answered] is JsonValue answered && answered.GetValueKind() == JsonValueKind.False)
        {
            notes.Add($"not answered: {Word(members[DoctorComposer.Keys.Reason])}");
        }

        if (Word(members[DoctorComposer.Keys.Incomplete]) is { } incomplete)
        {
            notes.Add($"incomplete: {incomplete}");
        }

        var cut = members
            .Where(member => (member.Key == DoctorComposer.Keys.Truncated || member.Key.EndsWith(DoctorComposer.Keys.TruncatedSuffix, StringComparison.Ordinal))
                && member.Value is JsonValue flag && flag.GetValueKind() == JsonValueKind.True)
            .Select(member => member.Key)
            .ToList();
        if (cut.Count > 0)
        {
            notes.Add("cut short: " + string.Join(", ", cut));
        }

        if (Word(members[DoctorComposer.Keys.RecentFailuresFrom]) is { } from)
        {
            notes.Add($"recent failures from: {from}");
        }

        return string.Join("; ", notes);
    }

    /// <summary>Each kind of name and what the bundle writes it as, read from its header: <c>home as ~</c> and so on.</summary>
    private static IEnumerable<string> Rewritten(JsonObject document)
    {
        const string Suffix = "_written_as";
        return document["bundle"] is JsonObject header
            ? header.Where(member => member.Key.EndsWith(Suffix, StringComparison.Ordinal)).Select(member => $"{member.Key[..^Suffix.Length]} as {Word(member.Value)}")
            : [];
    }

    private static string? Word(JsonNode? node) =>
        node is JsonValue value && value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : null;

    // ---- the contract -------------------------------------------------------------------------------------------------

    /// <summary>
    /// The contract, printed in help. A person deciding whether to share a file, or an agent telling one how to make it,
    /// has this page and nothing else, so it says what is in the file and what never is, what was rewritten, where it is
    /// written and for how long, and what each exit code means. Every list and bound in it is read from the code that
    /// applies it, never copied: the allowlist, the never-list, the marks, the projection and the composer's bounds.
    /// </summary>
    private static string Contract()
    {
        // The marks are words of this program's own, read without making a rewriter: printing help reads nothing about the
        // account it runs as.
        var marks = IdentityRewriter.MarkWords;

        return string.Create(
            CultureInfo.InvariantCulture,
            $"""
            Run --dry-run first: it composes the whole bundle, writes nothing, and lists what a plain run would write —
            the sections and their sizes, the settings whose values it carries, what it never carries and what it leaves
            out. Nothing is asked in between and nothing is sent anywhere: the file is written on this machine, and it is
            yours to read and to share. It never reaches the network. The only requests it makes are to this
            installation's own runtime, through its descriptor and at an address on this machine; with no descriptor
            it makes none.

            Sections, in this order:
              bundle           what the file is ({DoctorComposer.Format}), when it was made, what was rewritten in it, and
                               its own listing: contents, values_carried, never_carried, projected and warnings.
              versions         the CLI, the runtime and its API, the operating system and .NET.
              status           the report 'jason status' composes, without --provider-cli, so no program is run.
              runtime          what the runtime says about itself — system.info, projected as below — each plugin by
                               its id, version, digest, status and problem codes, at most {DoctorComposer.MaxProblemCodes} codes a plugin
                               (problem_codes_truncated where there were more) and an id, version or digest longer than
                               {DoctorComposer.MaxPluginText} characters written as {DoctorComposer.TooLong}, the list at most {Size(DoctorComposer.PluginsBytes)} in the
                               bundle, its first plugins kept and plugins_truncated past that; and data_dir_agrees, whether its data
                               directory is this one. When it did not answer, the reason: {CliErrors.NoDescriptor}, {CliErrors.RuntimeUnreachable},
                               {CliErrors.Unauthorized}, {CliErrors.StaleDescriptor}, {DoctorComposer.TimedOut}, {DoctorComposer.NotAttempted}, or
                               {DoctorComposer.UnreadableAnswer} where it answered with something that is not system.info.
              config           the settings file and every JASON_* variable by shape — keys, types, lengths — with a
                               value only where the list below carries it.
              logs             the log files by name, size and time; the newest error and fatal events, at most {DoctorComposer.RecentErrors},
                               looked for in the last {Size(DoctorComposer.ErrorSearchBytes)} of the log; and the newest events of any
                               level, at most {DoctorComposer.SliceEvents} events, read from no more than the last {Size(DoctorComposer.SliceBytes)}.
              journal          the last {DoctorComposer.JournalWindowDays} days of the journal counted by kind, and its newest {DoctorComposer.RecentEntries} entries
                               by time, kind, ids and the actor's type — never what an entry says.
              work_items       every work item counted by kind and status, and the newest {DoctorComposer.RecentFailures} failed or expired —
                               the newest failures in the journal's window that are still failed or expired, each read
                               by itself — each by its error's code and class, never its message. recent_failures_from
                               says where they were found: {DoctorComposer.FromJournal}, or {DoctorComposer.FromListOldestFirst} when not one page of the
                               journal could be read, which is oldest first and misses the newest of a long list.
              journal_entries  only with --include-journal: the journal's entries whole, newest first.
            The journal and the work items are read {DoctorComposer.PageSize} to a page and never past {DoctorComposer.MaxPages} pages.

            Bounds, in the bundle's own bytes: recent_errors at most {Size(DoctorComposer.RecentErrorsBytes)} and the slice at most {Size(DoctorComposer.SliceBytes)}, each
            keeping the newest events; journal_entries at most {Size(DoctorComposer.JournalEntriesBytes)}, keeping the newest entries; the rest
            of every section at most {Size(DoctorComposer.SectionBytes)}; and the header at most {Size(DoctorComposer.HeaderBytes)}, its values_carried cut from the end to
            fit, which values_carried_truncated says. A section larger than its budget is written as omitted:
            {DoctorComposer.TooLarge}, with its bytes and its budget, and listed with 0 items.
            A bundle is at most {DoctorComposer.MaxBundleBytes} bytes.

            What a section says when it is not whole. One the runtime would have answered for says not_read:
            {DoctorComposer.RuntimeDidNotAnswer}, or {DoctorComposer.NotAttempted} when a read before it timed out — after one timeout nothing more
            is asked. A read that stopped partway leaves incomplete and its code: {CliErrors.RuntimeUnreachable}, {DoctorComposer.TimedOut},
            {CliErrors.Unauthorized}, {DoctorComposer.UnreadableAnswer}, {CliErrors.LogsUnreadable}, or the runtime's own code. A list cut short
            says so: files_truncated, recent_errors_truncated, slice_truncated, plugins_truncated,
            problem_codes_truncated, values_carried_truncated, truncated and recent_failures_truncated — the last
            wherever failures may exist that were not read: past the items read, or where their source was cut or
            stopped partway, whatever the count listed.

            Values carried — only these settings, from the settings file or a JASON_* variable, and only a value of the
            kind shown; every other setting and variable is described by its shape and never by its value:
            {Indented(SettingsAllowlist.Default.Published)}

            Never carried:
            {Indented(DoctorComposer.NeverCarried)}

            Rewritten in every key and every value before anything is written: the home directory as {marks["home_written_as"]}, a data
            directory outside it as {marks["data_written_as"]}, the data directory of a runtime that keeps its data somewhere else
            (data_dir_agrees false, which stays in the bundle beside it) as {marks["runtime_data_written_as"]} — each directory only as a
            whole path, never in the middle of a word and never a bare root such as D:\, the longest first, and on
            Windows its short form too — then the account's name as {marks["user_written_as"]}, the machine's as {marks["machine_written_as"]}, the domain as
            {marks["domain_written_as"]} and a Windows account identifier as {marks["sid_written_as"]} — which the header lists as home_written_as,
            data_written_as, runtime_data_written_as, user_written_as, machine_written_as, domain_written_as and
            sid_written_as. A settings key this program does not know is written as {SettingKey.Hidden}. Then the exception
            text of every log event (@x) is capped: {ExceptionText.MaxLineCharacters} characters a line, {ExceptionText.MaxMessageCharacters} for its message, {ExceptionText.MaxFrames} frames.
            And the runtime's own report is carried in part — a closed set of the runtime's own words travels, free text
            does not — each answer below without the fields after it, which the header names under projected. The
            status section's facts are composed from that report as projected, so they quote none of it either. A
            skill problem is written as "{DoctorComposer.Withheld}" where the runtime reported one and is null where it reported
            none, and the status section's role-skills fact made of them reads "{DoctorComposer.Withheld} (N)" with their count; a
            presence detail that is one of the runtime's own words travels as written:
            {Indented(DoctorComposer.Projections.Select(projection => $"{projection.Field}: {string.Join("; ", projection.Withheld)}"))}

            --include-journal adds journal_entries, and with it this warning — on stderr, in the printed result and in
            the bundle:
              {DoctorComposer.JournalWarning}

            Where: the logs directory of the data directory, in {DirectoryName} — ~/.jason/logs/{DirectoryName}, or logs/{DirectoryName} under
            JASON_DATA_DIR when that is set — as {NamePrefix}YYYYMMDDTHHMMSSZ{Extension}, the time in UTC. The directory is
            created. A name already taken, by two runs in one second, takes -1, -2 and so on, up to -{MaxNumber}; past that it
            refuses with {CliErrors.DoctorOutputUnwritable}, and nothing is ever overwritten. --output <file> writes there instead, and
            refuses a path that is already there or one that names a directory rather than a file. Bundles in the data directory
            are not rolled: they stay until 'jason uninstall --purge-data' or until a person deletes them — the files are the person's.

            Output: --dry-run prints would_write, contents, values_carried, never_carried, projected and warnings, and
            writes nothing, not even the directory. It makes every check that needs no write — a name that is a file's
            and one this system allows, a volume that is there, nothing already at the path, no file where a directory
            has to be — but a dry run cannot know whether the place may be written, so a plain run may still refuse with
            {CliErrors.DoctorOutputUnwritable}. A plain run writes the file and prints path, bytes, sha256 — of the file's exact
            bytes — and the same five lists. The file and the printed JSON are compact and plain ASCII: every character
            above ~ is a \uXXXX escape, which a parser reads back as the same text. --human prints the listing as a table
            instead, then "Would write …" or "Written to …". --human and the warning on stderr are written as text as it
            is, so a console that is not UTF-8 may show some characters differently; the JSON is the exact form.

            Exit codes: 0 when the bundle was listed or written, whether or not the runtime answered. 1 when it refused
            here, and nothing was written, not even a directory: {CliErrors.DoctorOutputExists}, a path that is already there,
            or {CliErrors.DoctorOutputUnwritable}, a path that cannot be written — one that names no file, a name this system
            refuses, a volume that is not there, a file where a directory has to be, every numbered name taken, or a
            place the system will not let it write. 2 for a usage error. Never 3: a bundle is most wanted when the
            runtime does not answer.
            """);
    }

    /// <summary>Each line on a line of its own, indented as the contract's lists are.</summary>
    private static string Indented(IEnumerable<string> lines) => string.Join("\n", lines.Select(line => "  " + line));

    /// <summary>A size as the contract states it: in mebibytes or kibibytes when it is a whole number of them, in bytes otherwise.</summary>
    private static string Size(long bytes) =>
        bytes % Mebibyte == 0 ? string.Create(CultureInfo.InvariantCulture, $"{bytes / Mebibyte} MiB")
        : bytes % 1024 == 0 ? string.Create(CultureInfo.InvariantCulture, $"{bytes / 1024} KiB")
        : string.Create(CultureInfo.InvariantCulture, $"{bytes} bytes");

    /// <summary>One moment, whatever is asked of it: the clock the composer is handed, so its stamp is the file name's second.</summary>
    private sealed class Frozen(DateTimeOffset moment) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => moment;
    }
}
