using System.Buffers.Binary;
using System.Globalization;
using System.Reflection.PortableExecutable;
using System.Text;

namespace Jason.App.Tests.Hosting;

/// <summary>
/// The application manifests a PE image carries, read the way the resource table lays them out: type 24, then
/// a name, then a language, then the bytes. Managed, so the compiled program is read on every platform CI runs.
/// </summary>
internal static class Win32Resources
{
    private const uint ManifestType = 24;

    public static IReadOnlyList<string> Manifests(string path)
    {
        using var pe = new PEReader(File.OpenRead(path));
        var table = pe.PEHeaders.PEHeader!.ResourceTableDirectory;
        if (table.Size == 0)
        {
            return [];
        }

        byte[] section = [.. pe.GetSectionData(table.RelativeVirtualAddress).GetContent()];
        var found = new List<string>();
        foreach (var (id, types) in Entries(section, 0))
        {
            if (id != ManifestType || !types.Directory)
            {
                continue;
            }

            foreach (var (_, names) in Entries(section, types.Offset))
            {
                foreach (var (_, language) in Entries(section, names.Offset))
                {
                    var rva = BinaryPrimitives.ReadInt32LittleEndian(section.AsSpan(language.Offset));
                    var size = BinaryPrimitives.ReadInt32LittleEndian(section.AsSpan(language.Offset + 4));
                    byte[] bytes = [.. pe.GetSectionData(rva).GetContent(0, size)];
                    found.Add(Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF'));
                }
            }
        }

        return found;
    }

    /// <summary>
    /// What the image's resource table holds, for a failure to say. "No manifest in the image" and "the reader
    /// failed" are different findings, and only the first may ever lead anywhere but a fix to the reader.
    /// </summary>
    public static string Describe(string path)
    {
        using var pe = new PEReader(File.OpenRead(path));
        var table = pe.PEHeaders.PEHeader!.ResourceTableDirectory;
        if (table.Size == 0)
        {
            return "its resource table is empty (0 bytes)";
        }

        byte[] section = [.. pe.GetSectionData(table.RelativeVirtualAddress).GetContent()];
        var types = Entries(section, 0)
            .Select(entry => entry.Id == uint.MaxValue ? "named" : entry.Id.ToString(CultureInfo.InvariantCulture));
        return $"its resource table is {table.Size} bytes and holds types [{string.Join(", ", types)}]";
    }

    /// <summary>One directory's entries: its id (a named entry reads as <see cref="uint.MaxValue"/>) and where it points.</summary>
    private static List<(uint Id, (bool Directory, int Offset) Target)> Entries(byte[] section, int at)
    {
        var count = BinaryPrimitives.ReadUInt16LittleEndian(section.AsSpan(at + 12))
            + BinaryPrimitives.ReadUInt16LittleEndian(section.AsSpan(at + 14));
        var entries = new List<(uint, (bool, int))>(count);
        for (var index = 0; index < count; index++)
        {
            var entry = at + 16 + (index * 8);
            var name = BinaryPrimitives.ReadUInt32LittleEndian(section.AsSpan(entry));
            var target = BinaryPrimitives.ReadUInt32LittleEndian(section.AsSpan(entry + 4));
            entries.Add(((name & 0x8000_0000) != 0 ? uint.MaxValue : name, ((target & 0x8000_0000) != 0, (int)(target & 0x7FFF_FFFF))));
        }

        return entries;
    }
}
