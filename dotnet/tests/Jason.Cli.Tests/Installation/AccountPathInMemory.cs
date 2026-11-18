using System.Runtime.Versioning;
using Jason.Cli.Uninstall;
using Microsoft.Win32;

namespace Jason.Cli.Tests.Installation;

/// <summary>
/// An account's <c>Path</c> held in memory, standing for the registry's: a row given it reads, appends and takes entries
/// off as <c>jason install</c> does, and writes no key under this account.
/// </summary>
/// <remarks>
/// <see cref="Append"/> follows <see cref="UserPathValue.Append"/>: <see cref="PathEntry.WithDirectory"/>, the kind kept,
/// <c>REG_EXPAND_SZ</c> for a Path that does not exist yet, and nothing written where the folder is there already.
/// </remarks>
[SupportedOSPlatform("windows")]
internal sealed class AccountPathInMemory(StoredPath? stored = null) : IAccountPath
{
    /// <summary>The value as it stands now, or null where there is none.</summary>
    public StoredPath? Stored { get; private set; } = stored;

    public StoredPath? Read() => Stored;

    public void Write(string raw, RegistryValueKind kind) => Stored = raw.Length == 0 ? null : new StoredPath(raw, kind);

    public bool Append(string directory)
    {
        var raw = Stored?.Raw ?? string.Empty;
        var kind = Stored?.Kind ?? RegistryValueKind.ExpandString;
        var with = PathEntry.WithDirectory(raw, directory, PathEntry.ReadAs(kind != RegistryValueKind.String));
        if (Stored is not null && ReferenceEquals(with, raw))
        {
            return false;
        }

        Write(with, kind);
        return true;
    }
}
