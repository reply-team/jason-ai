using Jason.Contracts.Discovery;
using Jason.Contracts.Update;

namespace Jason.Cli.Update;

/// <summary>
/// Where an update keeps its things: under the data directory, never beside the executable. Composed from
/// <see cref="JasonPaths"/> and from the platform, and never from anything a feed said.
/// </summary>
/// <remarks>
/// The install directory belongs to whoever installed Jason — it may be read-only, it may be on another volume,
/// and writing into it is how an update leaves rubbish behind on a machine that later removes the program by
/// deleting one file. So the downloads, the ledger, the copy of the applier and the executable that was replaced
/// all live here, where everything else Jason writes already lives.
/// </remarks>
public sealed class UpdatePaths(JasonPaths paths)
{
    /// <summary>Everything an update writes, under one directory.</summary>
    public string Root { get; } = Path.Combine(paths?.Root ?? throw new ArgumentNullException(nameof(paths)), "update");

    /// <summary>The record of what an update is doing, and of the last one that finished.</summary>
    public string Ledger => Path.Combine(Root, UpdateLedger.FileName);

    /// <summary>Where the executable that was replaced is kept, so a rollback has something to put back.</summary>
    public string Previous => Path.Combine(Root, "previous");

    /// <summary>The copy of this build that performs the swap, because the file being swapped is the one running.</summary>
    public string Applier => Path.Combine(Root, "applier");

    /// <summary>
    /// Where a rollback puts the executable it replaced, when that executable is the program running the
    /// rollback and so cannot be deleted yet.
    /// </summary>
    public string Replaced => Path.Combine(Root, "replaced");

    /// <summary>Everything downloaded for updates, whichever version.</summary>
    public string Staging => Path.Combine(Root, "staged");

    /// <summary>
    /// Where one version's download is unpacked. The directory is named from the version this build parsed, not
    /// from any text a feed supplied: a version is three numbers and an identifier, so the name cannot walk
    /// anywhere.
    /// </summary>
    public string StagedFor(SemanticVersion version) => Path.Combine(Staging, version.ToString());

    /// <summary>The executable inside a staged directory, named from this platform rather than from the archive.</summary>
    public string StagedExecutable(SemanticVersion version) => Path.Combine(StagedFor(version), ReleaseAssets.ExecutableName);

    /// <summary>The kept executable, under the name it will be put back as.</summary>
    public string PreviousExecutable => Path.Combine(Previous, ReleaseAssets.ExecutableName);
}
