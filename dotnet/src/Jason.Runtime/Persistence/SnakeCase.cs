using System.Text.RegularExpressions;

namespace Jason.Runtime.Persistence;

/// <summary>PascalCase → snake_case for tables, columns, keys and indexes, and for enum values stored as text.</summary>
public static partial class SnakeCase
{
    [GeneratedRegex("([a-z0-9])([A-Z])")]
    private static partial Regex LowerUpperBoundary();

    public static string Convert(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return LowerUpperBoundary().Replace(name, "$1_$2").ToLowerInvariant();
    }
}
