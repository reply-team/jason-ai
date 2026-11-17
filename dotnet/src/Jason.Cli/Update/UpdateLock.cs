namespace Jason.Cli.Update;

/// <summary>
/// The one lock of a carried update: <c>&lt;data&gt;/update/update.lock</c>, opened for this process alone, held by the part of
/// an update that acts — the hand-over up to <c>swapped</c>, the finishing step, a rollback — and let go before that part
/// runs the next as a child it waits for, which takes it in turn. A second one beside it refuses with
/// <c>update_in_progress</c> rather than read the same ledger: an agent's retry while the first still waits, or the
/// Desktop's finishing step at a new version's first start while the verb still finishes.
/// </summary>
/// <remarks>
/// The operating system lets the lock go with the process, a kill included, so no lock outlives what held it; the file
/// itself stays and is taken again. Between letting it go and the child taking it there is the moment a process takes to
/// start: a second update that takes the lock there is the one that goes on, and the child refuses.
/// </remarks>
public sealed class UpdateLock : IDisposable
{
    private readonly FileStream _held;

    private UpdateLock(FileStream held) => _held = held;

    /// <summary>
    /// Takes the lock, or refuses with <c>update_in_progress</c> where another process holds it. A lock file that cannot be
    /// made at all is <c>update_file_refused</c>.
    /// </summary>
    public static UpdateLock Take(UpdatePaths update, string what)
    {
        ArgumentNullException.ThrowIfNull(update);
        try
        {
            Directory.CreateDirectory(update.Root);
            return new UpdateLock(new FileStream(update.Lock, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
        }
        catch (IOException held) when (File.Exists(update.Lock) && !Directory.Exists(update.Lock))
        {
            throw new UpdateException(
                UpdateCodes.InProgress,
                $"Another jason is updating this installation, or finishing or rolling back an update, and holds {update.Lock}, so {what} was not begun. "
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

    /// <summary>Lets the lock go: the next part, or a second update, may take it.</summary>
    public void Dispose() => _held.Dispose();
}
