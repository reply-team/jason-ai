using System.Globalization;

namespace Jason.Cli.Update;

/// <summary>
/// The one lock of a carried update: <c>&lt;data&gt;/update/update.lock</c>, opened for this process alone, held by every
/// part of an update that acts — the hand-over, the finishing step, a rollback — and by <c>jason install</c>'s machine setup.
/// A second one beside it refuses rather than read the same ledger or write the same Path entry and skills: an agent's retry
/// while the first still waits, the installer's own <c>jason install</c> while the update's finish sets the machine up, or
/// the Desktop's finishing step at a new version's first start while the verb still finishes. The refusal says what holds
/// it: an update, <c>update_in_progress</c>; <c>jason install</c>, <c>install_in_progress</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Held by the operating system.</b> The lock is the open handle, not the file: the file stays once its holder has gone,
/// a kill included, and is taken again by the next, so a file a dead process left refuses nobody. Beside it,
/// <see cref="UpdatePaths.LockHolder"/> names the holder — what it is, its process id, and the nonce it hands on — for a
/// refusal to say and for a child to join; it is written by each holder as it takes the lock, and read only while the lock
/// is held.
/// </para>
/// <para>
/// <b>Never let go between two parts.</b> A part that runs the next as a child it waits for — the carried process its copy
/// on macOS, either of them the new carried jason's finish — keeps the lock and hands the child the nonce
/// (<see cref="HandOver"/>) in <see cref="Variable"/> alone. A child whose nonce is the one the holder wrote goes on under the
/// lock it was handed (<see cref="Joined"/>); any other process finds the lock held and refuses.
/// </para>
/// <para>
/// Two edges, stated and not closed. A child still running when the process that handed it the lock is killed goes on under
/// a lock no one holds: that is the one moment a second update can begin beside it. And the lock is the data directory's: an
/// installer started from Jason Desktop's menu runs <c>jason install</c> on the default data directory, so a data directory
/// named only in a terminal has a lock that installer does not take.
/// </para>
/// </remarks>
public sealed class UpdateLock : IDisposable
{
    /// <summary>
    /// The variable a part of the update hands the child it runs the lock's nonce in. Not a secret: what it guards against is
    /// a second update nobody meant, and anything that can set a variable for that program can stop the first. Never on the
    /// command line, and taken out of this process's environment once read, so nothing the child starts carries it on.
    /// </summary>
    public const string Variable = "JASON_UPDATE_LOCK";

    /// <summary>What may hold the lock: a part of an update, or <c>jason install</c>'s machine setup.</summary>
    public enum Holder
    {
        Update,
        Install,
    }

    private readonly FileStream? _held;
    private readonly UpdatePaths _update;
    private readonly string _nonce;
    private bool _named;

    private UpdateLock(UpdatePaths update, FileStream? held, string nonce, bool named)
    {
        _update = update;
        _held = held;
        _nonce = nonce;
        _named = named;
    }

    /// <summary>Whether this process goes on under a lock the process that started it holds, rather than holding one itself.</summary>
    public bool Joined => _held is null;

    /// <summary>
    /// Takes the lock as <paramref name="holder"/>, or joins the one the process that started this one holds where
    /// <paramref name="handed"/> is the nonce it wrote. Where another process holds it, refuses by what holds it: an update
    /// with <c>update_in_progress</c>, <c>jason install</c> with <c>install_in_progress</c>. A lock file that cannot be made
    /// at all is <c>update_file_refused</c>.
    /// </summary>
    public static UpdateLock Take(UpdatePaths update, string what, string? handed = null, Holder holder = Holder.Update)
    {
        ArgumentNullException.ThrowIfNull(update);
        FileStream held;
        try
        {
            Directory.CreateDirectory(update.Root);
            held = new FileStream(update.Lock, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException busy) when (File.Exists(update.Lock) && !Directory.Exists(update.Lock))
        {
            var named = Named(update);
            if (handed is { Length: > 0 } && named is { } by && string.Equals(by.Nonce, handed, StringComparison.Ordinal))
            {
                return new UpdateLock(update, null, handed, named: true);
            }

            throw named is { Holder: Holder.Install } installing
                ? new UpdateException(
                    CliErrors.InstallInProgress,
                    string.Create(CultureInfo.InvariantCulture, $"`jason install` is setting this machine up, process {installing.Pid}, and holds {update.Lock}, so {what} was not begun. ")
                    + "It takes seconds: let it end, then run this again.",
                    busy,
                    retryable: true)
                : new UpdateException(
                    UpdateCodes.InProgress,
                    $"Another jason is updating this installation, or finishing or rolling back an update, and holds {update.Lock}, so {what} was not begun. "
                    + "Let that one end, then run this again; `jason update status` says where the update stands.",
                    busy);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw new UpdateException(
                UpdateCodes.FileRefused,
                $"The update's lock at {update.Lock} could not be taken: {error.Message} Make sure {update.Root} is a directory this account can write to, then run this again.",
                error);
        }

        // The holder named beside the lock, for a refusal to say and a child to join; one that cannot be written leaves the
        // lock held all the same, and a refusal then speaks of an update.
        var nonce = Guid.NewGuid().ToString("N");
        return new UpdateLock(update, held, nonce, Name(update, holder, nonce));
    }

    /// <summary>
    /// The nonce a child this process runs and waits for is handed in <see cref="Variable"/>, so that it goes on under this
    /// lock: the one written beside the lock, or the one this process was handed where it joined.
    /// </summary>
    public string HandOver()
    {
        if (!_named && !(_named = Name(_update, Holder.Update, _nonce)))
        {
            throw new UpdateException(
                UpdateCodes.FileRefused,
                $"The update's lock could not be handed on at {_update.LockHolder}. Make sure {_update.Root} is a directory this account can write to, then run this again.");
        }

        return _nonce;
    }

    /// <summary>
    /// The nonce the process that started this one handed it in <see cref="Variable"/>: from the variables a test hands in, else
    /// from this process's environment, which loses it here, so that nothing this process starts carries it on.
    /// </summary>
    public static string? Handed(IReadOnlyDictionary<string, string>? variables)
    {
        if (variables is not null)
        {
            return variables.GetValueOrDefault(Variable);
        }

        var handed = Environment.GetEnvironmentVariable(Variable);
        if (handed is not null)
        {
            Environment.SetEnvironmentVariable(Variable, null);
        }

        return handed;
    }

    /// <summary>Lets the lock go, and the holder named beside it: the next part, or a second update, may take it.</summary>
    public void Dispose()
    {
        if (_held is null)
        {
            return;
        }

        try
        {
            File.Delete(_update.LockHolder);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // A holder named for a lock no one holds is read by nobody: a process that finds the lock free takes it.
        }

        _held.Dispose();
    }

    /// <summary>The holder's line beside the lock: what it is, its process id and its nonce, a space between each.</summary>
    private static bool Name(UpdatePaths update, Holder holder, string nonce)
    {
        try
        {
            File.WriteAllText(update.LockHolder, string.Create(CultureInfo.InvariantCulture, $"{(holder is Holder.Install ? "install" : "update")} {Environment.ProcessId} {nonce}"));
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>The holder named beside the lock, where its line can be read; null otherwise.</summary>
    private static (Holder Holder, int Pid, string Nonce)? Named(UpdatePaths update)
    {
        try
        {
            var parts = File.ReadAllText(update.LockHolder).Trim().Split(' ');
            return parts.Length == 3 && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var pid) && parts[0] is "install" or "update"
                ? (parts[0] == "install" ? Holder.Install : Holder.Update, pid, parts[2])
                : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
