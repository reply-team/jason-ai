namespace Jason.Runtime.Discovery;

/// <summary>
/// The data directory could not be prepared, so this runtime has not started.
/// </summary>
/// <remarks>
/// <para>
/// Its own type because of <em>when</em> it happens: preparing the data directory is the first thing a runtime
/// does, before its logging exists. An exception escaping there ended the process with nothing written
/// anywhere, while the CLI that started it reported a failure and sent the operator to a log directory that
/// had just been created empty. The first run of a new installation is the worst possible moment to be told
/// to go and read something that is not there.
/// </para>
/// <para>
/// The message names the directory and says what would make it usable, because that is the only thing the
/// person can act on. <c>JASON_DATA_DIR</c> may name any directory at all, and the commonest cause is one
/// whose rights reach this account through an inheritance that stops short of full control.
/// </para>
/// </remarks>
public sealed class DataDirectoryUnusableException(string path, Exception cause)
    : Exception(
        $"The data directory '{path}' could not be prepared, so the runtime did not start: {cause?.Message} "
        + "This account needs full control of that directory. Point JASON_DATA_DIR at one it has, or grant it "
        + "there. Some of its directories may have been created before this; no file was written, and no log "
        + "file exists to read, because this happens before logging starts.",
        cause);
