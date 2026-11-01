using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace Jason.Runtime.Tests;

/// <summary>
/// The strings an assembly carries, read from its file without loading it. One reader for every guard that holds a rule
/// over the text a program carries, so that none of them reads C# of its own.
/// </summary>
internal static class UserStrings
{
    /// <summary>
    /// The literal pieces of the strings the code in <paramref name="assemblyFile"/> carries in its user-string heap:
    /// plain, verbatim, raw, interpolated, or constants joined at compile time, and no comment. A string is there once
    /// however many instructions load it.
    /// </summary>
    public static List<string> Of(string assemblyFile)
    {
        using var image = new PEReader(File.OpenRead(assemblyFile));
        var metadata = image.GetMetadataReader();
        var strings = new List<string>();
        if (metadata.GetHeapSize(HeapIndex.UserString) <= 1)
        {
            return strings;
        }

        for (var handle = MetadataTokens.UserStringHandle(1); !handle.IsNil; handle = metadata.GetNextHandle(handle))
        {
            strings.Add(metadata.GetUserString(handle));
        }

        return strings;
    }
}
