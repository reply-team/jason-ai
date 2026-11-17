namespace Jason.Cli.Update;

/// <summary>
/// The one lock of a carried update: <c>&lt;data&gt;/update/update.lock</c>, opened for this process alone, held by every
/// part of an update that acts — the hand-over, the finishing step, a rollback — and by <c>jason install</c>'s machine setup.
/// A second one beside it refuses with <c>update_in_progress</c> rather than read the same ledger or write the same Path
/// entry and skills: an agent's retry while the first still waits, the installer's own <c>jason install</c> while the update's
/// finish sets the machine up, or the Desktop's finishing step at a new version's first start while the verb still finishes.
/// </summary>
/// <remarks>
/// <para>
/// <b>Never let go between two parts.</b> A part that runs the next as a child it waits for — the carried process its copy
/// on macOS, either of them the new carried jason's finish — keeps the lock and hands the child a nonce
/// (<see cref="HandOver"/>): written beside the lock (<see cref="UpdatePaths.LockHolder"/>) and given to the child in
/// <see cref="Variable"/> alone. A child whose nonce is the one written there goes on under the lock it was handed
/// (<see cref="Joined"/>); any other process finds the lock held and refuses.
/// </para>
/// <para>
/// The operating system lets the lock go with the process, a kill included, so no lock outlives what held it; the file
/// itself stays and is taken again. A child still running when the process that handed it the lock is killed goes on under
/// a lock no one holds: that is the one moment a second update can begin beside it.
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

    private readonly FileStream? _held;
    private readonly UpdatePaths _update;
    private string? _nonce;

    private UpdateLock(UpdatePaths update, FileStream? held, string? nonce)
    {
        _update = update;
        _held = held;
        _nonce = nonce;
    }

    /// <summary>Whether this process goes on under a lock the process that started it holds, rather than holding one itself.</summary>
    public bool Joined => _held is null;

    /// <summary>
    /// Takes the lock, or joins the one the process that started this one holds where <paramref name="handed"/> is the nonce
    /// it wrote; refuses with <c>update_in_progress</c> where another process holds it. A lock file that cannot be made at all
    /// is <c>update_file_refused</c>.
    /// </summary>
    public static UpdateLock Take(UpdatePaths update, string what, string? handed = null)
    {
        ArgumentNullException.ThrowIfNull(update);
        try
        {
            Directory.CreateDirectory(update.Root);
            return new UpdateLock(update, new FileStream(update.Lock, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None), null);
        }
        catch (IOException held) when (File.Exists(update.Lock) && !Directory.Exists(update.Lock))
        {
            if (handed is { Length: > 0 } && string.Equals(Holder(update), handed, StringComparison.Ordinal))
            {
                return new UpdateLock(update, null, handed);
            }

            throw new UpdateException(
                UpdateCodes.InProgress,
                $"Another jason is updating this installation, or finishing or rolling back an update, or setting the machine up, and holds {update.Lock}, so {what} was not begun. "
                + "Let that one end, then run this again; `jason update status` says where the update stands.",
                held);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw new UpdateException(
                UpdateCodes.FileRefused,
                $"The update's lock at {update.Lock} could not be taken: {error.Message} Make sure {update.Root} is a directory this account can write to, then run this again.",
                error);
        }
    }

    /// <summary>
    /// The nonce a child this process runs and waits for is handed in <see cref="Variable"/>, so that it goes on under this
    /// lock: written beside the lock the first time it is asked for, and the one this process was handed where it joined.
    /// </summary>
    public string HandOver()
    {
        if (_nonce is { } nonce)
        {
            return nonce;
        }

        var fresh = Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(_update.LockHolder, fresh);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw new UpdateException(
                UpdateCodes.FileRefused,
                $"The update's lock could not be handed on at {_update.LockHolder}: {error.Message} Make sure {_update.Root} is a directory this account can write to, then run this again.",
                error);
        }

        _nonce = fresh;
        return fresh;
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

    /// <summary>Lets the lock go, and the nonce written for it: the next part, or a second update, may take it.</summary>
    public void Dispose()
    {
        if (_held is null)
        {
            return;
        }

        if (_nonce is not null)
        {
            try
            {
                File.Delete(_update.LockHolder);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // A nonce no child is handed any more names a lock no one holds: a child that reads it takes the lock itself.
            }
        }

        _held.Dispose();
    }

    private static string? Holder(UpdatePaths update)
    {
        try
        {
            return File.ReadAllText(update.LockHolder).Trim();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
