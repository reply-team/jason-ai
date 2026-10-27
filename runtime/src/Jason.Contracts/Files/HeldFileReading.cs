namespace Jason.Contracts.Files;

/// <summary>What a reading of a directory and its files makes of an access-denied that a move of the directory came back with.</summary>
public enum HeldFileReading
{
    /// <summary>
    /// Another program has a handle on the directory itself, or a file under it open: a sharing or a lock violation on opening
    /// it. The refusal is a hold, and the wait applies.
    /// </summary>
    Held,

    /// <summary>
    /// The directory cannot be opened for its deletion, which is what a rename of it needs: an access-denied on opening it. The
    /// refusal is a permission, and waiting does not change it.
    /// </summary>
    Refused,

    /// <summary>
    /// Nothing says either: the directory opened for its deletion and no file is held. What a hold hides from both, such as a
    /// file deleted while it is held, is not evidence of a permission, so the wait applies as it did before the reader.
    /// </summary>
    Unknown,
}
