using Jason.Contracts.Discovery;

namespace Jason.Runtime.Discovery;

/// <summary>Creates the user-scoped data directory idempotently. Every install path converges here.</summary>
public static class DataDirectoryLayout
{
    public static void Ensure(JasonPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        Directory.CreateDirectory(paths.Root);
        foreach (var directory in paths.Layout)
        {
            Directory.CreateDirectory(directory);
        }

        FilePermissions.RestrictDirectory(paths.RunDirectory);
    }
}
