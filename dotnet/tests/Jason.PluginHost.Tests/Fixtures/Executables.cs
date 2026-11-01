namespace Jason.PluginHost.Tests.Fixtures;

/// <summary>
/// Where the programs a test grants a plugin actually live. The runtime resolves a declared name to a path at
/// reload; a test does the same by hand, because the child never looks anything up for itself.
/// </summary>
public static class Executables
{
    /// <summary>
    /// The .NET muxer, which every machine that can run these tests has. It is what the canonical fixture
    /// package declares, so its dll-plus-arguments shape is exercised exactly as a real plugin would.
    /// </summary>
    public static string Dotnet { get; } = Resolve("dotnet")
        ?? throw new InvalidOperationException("The tests need 'dotnet' on PATH or DOTNET_ROOT.");

    /// <summary>Looks a bare name up the way an operating system would: DOTNET_ROOT first, then PATH.</summary>
    public static string? Resolve(string name)
    {
        var extensions = OperatingSystem.IsWindows() ? new[] { ".exe", ".com" } : [string.Empty];
        var directories = new List<string>();

        var root = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrWhiteSpace(root))
        {
            directories.Add(root);
        }

        directories.AddRange((Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        foreach (var directory in directories)
        {
            foreach (var extension in extensions)
            {
                string candidate;
                try
                {
                    candidate = Path.Combine(directory, name + extension);
                }
                catch (ArgumentException)
                {
                    continue;
                }

                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }
}
