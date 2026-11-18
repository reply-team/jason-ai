using System.Text.Json;
using Jason.Contracts.Json;

namespace Jason.Cli.Update;

/// <summary>The words a move's <c>state</c> takes, in its record, its document and <c>jason status</c>.</summary>
public static class MoveStates
{
    /// <summary>Begun, and not ended: running now, or stopped part-way by a kill. <c>doing</c> names the step begun.</summary>
    public const string Moving = "moving";

    /// <summary>Waiting on something outside it; <c>jason update apply</c> carries it on.</summary>
    public const string Pending = "pending";

    /// <summary>A step refused; <c>jason update apply</c> carries it on.</summary>
    public const string Failed = "failed";

    /// <summary>The command-line install is in Jason Desktop.</summary>
    public const string Done = "done";

    /// <summary>A system Jason Desktop takes no command-line install into: nothing changed, and nothing is recorded.</summary>
    public const string NotApplicable = "not_applicable";
}

/// <summary>The steps of a move, in order, as its record names the last one done.</summary>
public static class MoveSteps
{
    /// <summary>Both places looked in: the application that is there, its version, or none.</summary>
    public const string Looked = "looked";

    /// <summary>The application's installer (Windows) or zip (macOS) downloaded.</summary>
    public const string Fetched = "fetched";

    /// <summary>Its SHA-256 held to the release's checksums.txt, and its signature to the publisher's.</summary>
    public const string Verified = "verified";

    /// <summary>The application installed (Windows) or put in place by one rename (macOS), or one already there reused.</summary>
    public const string Placed = "placed";

    /// <summary>The application's own <c>jason install</c> run, the command-line install retired.</summary>
    public const string SetUp = "set_up";

    /// <summary>A runtime started from the application's jason where the setup stopped one.</summary>
    public const string Started = "started";

    /// <summary>Every step, in order.</summary>
    public static IReadOnlyList<string> All { get; } = [Looked, Fetched, Verified, Placed, SetUp, Started];
}

/// <summary>
/// The record of the move of a command-line install into Jason Desktop, <c>&lt;data&gt;/update/move.json</c>: written before
/// each step begins, as the update's ledger is, so that what a kill leaves says the step it was in.
/// </summary>
/// <param name="Version">The version the update installed, which the move installs Jason Desktop at.</param>
/// <param name="From">The command-line install's executable being moved.</param>
/// <param name="State">One of <see cref="MoveStates"/>.</param>
/// <param name="Step">The last step done (<see cref="MoveSteps"/>), or null where none was.</param>
/// <param name="Doing">The step begun, while the state is <c>moving</c>; null otherwise.</param>
/// <param name="Application">The application installed or reused; null before the look.</param>
/// <param name="Reason">Why the move is pending or failed, the sentence <c>jason status</c> says; null otherwise.</param>
/// <param name="Feed">The feed the update read, which a move carried on reads again.</param>
/// <param name="UpdatedAt">When this was written.</param>
/// <param name="Marker">
/// What the move gives the setup's environment in <see cref="MoveMarker.Variable"/>: kept from one run of the move to the next
/// while the record stands not done, so a setup an earlier run left running is still known as the move's.
/// </param>
/// <param name="Waits">
/// Where the move is pending on something no command typed here changes — an Intel Mac, an application older than 0.49 —
/// what would move it; <c>jason status</c> then says so rather than name a repair that cannot help. Null otherwise.
/// </param>
public sealed record MoveRecord(
    string Version,
    string From,
    string State,
    string? Step,
    string? Doing,
    string? Application,
    string? Reason,
    string? Feed,
    DateTimeOffset UpdatedAt,
    string? Marker = null,
    string? Waits = null)
{
    /// <summary>
    /// Whether the command-line install at <paramref name="from"/> is retired into the application: gone (renamed aside or
    /// removed, on Windows) or a wrapper over the application's jason (on macOS). One rule, which <c>jason status</c> and a
    /// rollback both read.
    /// </summary>
    public static bool Retired(string from) => !File.Exists(from) || Installation.CommandFiles.IsWrapperFile(from);

    /// <summary>
    /// Whether this record says a move that is done and still stands: in state done, the application it names still there,
    /// and the command-line install it moved from retired (<see cref="Retired"/>). A done record whose application is gone,
    /// or whose install is a command-line install again, as after going back from 0.49, is a move to make again. One rule,
    /// which the move, <c>jason status</c> and a rollback all read.
    /// </summary>
    public bool DoneAndStanding(bool windows) =>
        State == MoveStates.Done
        && Application is { Length: > 0 } application
        && CommandLineMove.ApplicationStands(application, windows)
        && Retired(From);

    /// <summary>Whether a record is read by Windows' look at an application: the setup's system, else the one this runs on.</summary>
    internal static bool OnWindows(CliEnvironment env) =>
        env.Setup is { } setup ? setup.Platform is Installation.SetupSystem.Windows : OperatingSystem.IsWindows();

    /// <summary>
    /// The record at <paramref name="file"/>; null where there is none. One that cannot be read throws
    /// <see cref="MoveRecordUnreadable"/>, which <c>jason status</c> says as it is and a move writes over.
    /// </summary>
    public static MoveRecord? Read(string file)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(file);
        string text;
        try
        {
            text = File.ReadAllText(file);
        }
        catch (Exception missing) when (missing is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
        catch (Exception unread) when (unread is IOException or UnauthorizedAccessException)
        {
            throw new MoveRecordUnreadable($"The record of the move at {file} could not be read: {unread.Message}", unread);
        }

        MoveRecord? record;
        try
        {
            record = JsonSerializer.Deserialize<MoveRecord>(text, JasonJson.Options);
        }
        catch (JsonException invalid)
        {
            throw new MoveRecordUnreadable($"The record of the move at {file} is not one: {invalid.Message}", invalid);
        }

        return record is { Version.Length: > 0, From.Length: > 0, State.Length: > 0 }
            ? record
            : throw new MoveRecordUnreadable($"The record of the move at {file} is not one: it names no version, executable or state.", null);
    }

    /// <summary>Written beside itself first and renamed into place, so that a kill never leaves half a record.</summary>
    public void Write(string file)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(file);
        var directory = Path.GetDirectoryName(file)!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".move.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(this, JasonJson.Options));
            File.Move(temporary, file, overwrite: true);
        }
        catch
        {
            try
            {
                File.Delete(temporary);
            }
            catch (Exception left) when (left is IOException or UnauthorizedAccessException)
            {
                // A temporary file of a dot name, which nothing reads.
            }

            throw;
        }
    }
}

/// <summary>
/// The marker a move gives the environment of the setup it runs. <c>jason install</c> run under it — the installer's own last
/// step — does nothing while the record it names stands not done: the move sets the machine up itself once the installer has
/// returned, and an installer the move left running, past its bound or after a kill, then retires nothing and stops no
/// runtime. A marker that names no record standing not done, one left in somebody's shell say, is ignored.
/// </summary>
public static class MoveMarker
{
    /// <summary>The variable, in the setup's environment alone.</summary>
    public const string Variable = "JASON_MOVE";

    /// <summary>
    /// The move record the marker in <paramref name="variables"/> (the process's own where null) names, where that record stands
    /// not done; null otherwise, an unreadable record included.
    /// </summary>
    public static MoveRecord? Standing(IReadOnlyDictionary<string, string>? variables, UpdatePaths update)
    {
        ArgumentNullException.ThrowIfNull(update);
        var marker = variables is null ? Environment.GetEnvironmentVariable(Variable) : variables.GetValueOrDefault(Variable);
        if (string.IsNullOrWhiteSpace(marker))
        {
            return null;
        }

        try
        {
            return MoveRecord.Read(update.Move) is { } record && record.State != MoveStates.Done && string.Equals(record.Marker, marker, StringComparison.Ordinal)
                ? record
                : null;
        }
        catch (MoveRecordUnreadable)
        {
            return null;
        }
    }
}

/// <summary>A record of the move that is there and cannot be read as one.</summary>
public sealed class MoveRecordUnreadable(string message, Exception? inner) : Exception(message, inner);
