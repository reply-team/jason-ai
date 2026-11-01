using System.Reflection;

namespace Jason.Cli.Doctor;

/// <summary>
/// The plugins this repository ships: the directories under <c>plugins/</c> that hold a manifest, read from the tree
/// when this program is built and carried in its assembly's metadata. A diagnostic bundle carries these ids by name and
/// writes any other as an alias. Read rather than typed, so these sources name no plugin, and held to the tree by a test.
/// </summary>
public static class ShippedPlugins
{
    /// <summary>The key of the assembly's metadata that carries the directories, as the project file writes it.</summary>
    public const string MetadataKey = "ShippedPluginDirectories";

    /// <summary>The ids, in ordinal order.</summary>
    public static IReadOnlyList<string> Ids { get; } = Read();

    private static string[] Read()
    {
        var value = typeof(ShippedPlugins).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .SingleOrDefault(attribute => attribute.Key == MetadataKey)?.Value ?? string.Empty;

        // Each entry is a directory below plugins/ as the build wrote it, with its separator after it: the id is the
        // directory itself, and a manifest further down is no package of its own.
        return [.. value.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(directory => directory.TrimEnd('/', '\\'))
            .Where(directory => directory.Length > 0 && directory.IndexOfAny(['/', '\\']) < 0)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];
    }
}
