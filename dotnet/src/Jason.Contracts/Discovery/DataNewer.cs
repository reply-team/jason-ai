namespace Jason.Contracts.Discovery;

/// <summary>
/// The refusal of a data directory whose database a newer Jason migrated: the database holds an applied migration this build
/// does not know. The runtime refuses such a start before its migrator, its lock, its log or anything else changes the data
/// directory: no file is added or removed, and the database and its log keep every byte. SQLite may rebuild
/// <c>jason.db-shm</c>. The refusal is said in this code and sentence; <c>jason runtime start</c> answers it as its own, and
/// the desktop branches on the code.
/// </summary>
/// <remarks>
/// It protects a build from 0.49.0 on against any later one. An earlier build has no such check and would run its own model
/// over the newer schema, since EF Core passes over history rows it has no migration for.
/// </remarks>
public static class DataNewer
{
    /// <summary>The refusal's code, in the start family the desktop branches on.</summary>
    public const string Code = "data_newer";

    /// <summary>
    /// The first version that refuses, its development builds included: a build of this version or a later one does not start
    /// on a database a newer Jason migrated, and an earlier one has no such check.
    /// </summary>
    public const string FirstRefusing = "0.49.0-dev";

    /// <summary>
    /// The sentence of a data directory at <paramref name="dataDirectory"/> a newer Jason migrated, said for a Jason of version
    /// <paramref name="current"/>, which is said without its build metadata: the part after a plus names a commit, not a
    /// version.
    /// </summary>
    public static string Sentence(string current, string dataDirectory)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(dataDirectory);
        var plus = current.IndexOf('+', StringComparison.Ordinal);
        var version = plus < 0 ? current : current[..plus];
        return $"This Jason is {version}, and the data in {dataDirectory} was changed by a newer Jason, so the runtime was not started and nothing was changed. "
            + "Update Jason with `jason update apply`, or Check for Updates in Jason Desktop.";
    }
}
