using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;

namespace Jason.Runtime.Tests;

/// <summary>
/// No test matches a sentence the system words. Windows and .NET say these in the machine's own language, so a fact that
/// looks for one passes only where that language is English, and a fact that looks for its absence proves nothing
/// anywhere else. What no language changes is asserted instead: a path, an exception's type, an HResult.
/// </summary>
/// <remarks>
/// <para>
/// One rule for every test assembly: this file is linked into each, and each reads itself, from the file alone. What is
/// read is every string a method body loads — plain, verbatim, raw, or an interpolated string's literal pieces — with how
/// many instructions load it, so a comment is never read and a second use of an allowed string is counted. A sentence is
/// compared without its spaces and in any case, as a test that strips the spaces a shell wrapped a line with would write
/// it. Each sentence is kept here as its words, so this guard's own strings do not carry it whole.
/// </para>
/// <para>
/// <see cref="CarriedOnPurpose"/> lists what an assembly carries anyway, exactly: a string loaded more often than it says
/// is named, and so is one it lists that is no longer loaded.
/// </para>
/// </remarks>
public sealed partial class SystemSentenceTests
{
    private static readonly string[][] Sentences =
    [
        ["Access", "to", "the", "path"],
        ["Cannot", "create", "a", "file"],
        ["being", "used", "by", "another", "process"],
        ["The", "process", "cannot", "access"],
    ];

    /// <summary>
    /// What a test assembly carries although it matches, by assembly, by string and by how many instructions load it,
    /// each with why.
    /// </summary>
    private static readonly Dictionary<string, Dictionary<string, (int Loads, string Why)>> CarriedOnPurpose = new(StringComparer.Ordinal);

    private static readonly Dictionary<short, OpCode> Codes = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(field => (OpCode)field.GetValue(null)!)
        .ToDictionary(code => code.Value);

    [Fact]
    [Critical(Family.F3, "no test leans on an OS-language sentence")]
    public void No_string_the_tests_load_matches_a_sentence_the_system_words()
    {
        var assembly = typeof(SystemSentenceTests).Assembly;
        var found = Unexplained(LoadedStrings(assembly.Location), CarriedOnPurpose.GetValueOrDefault(assembly.GetName().Name!) ?? []);

        Assert.True(
            found.Count == 0,
            $"{assembly.GetName().Name}: {found.Count} string(s) the tests load match a sentence the system words in the machine's own language, or are listed otherwise:\n"
            + string.Join('\n', found));
    }

    /// <summary>The rule itself: a sentence is found inside a longer string, without its spaces and in any case.</summary>
    [Theory]
    [InlineData("Accesstothepath")]
    [InlineData("' could not be removed: ACCESS TO THE PATH '")]
    [InlineData("the file is being\n  used by another process.")]
    public void A_sentence_is_found_without_its_spaces_and_in_any_case(string text)
    {
        Assert.NotEmpty(Matched(text));
    }

    /// <summary>And what no language changes passes: a path, an exception's type and HResult, a stand-in's own words.</summary>
    [Theory]
    [InlineData(@"unpacked\jason.exe")]
    [InlineData("The script stopped on System.IO.IOException 0x800700B7.")]
    [InlineData(@"'C:\x' is denied, the stand-in says.")]
    public void What_no_language_changes_passes(string text)
    {
        Assert.Empty(Matched(text));
    }

    /// <summary>
    /// The list is exact: a string loaded as often as it says passes, one more load is named, and a string it lists that
    /// is no longer loaded is named too.
    /// </summary>
    [Theory]
    [InlineData(2, 0)]
    [InlineData(3, 1)]
    [InlineData(0, 1)]
    public void What_an_assembly_carries_on_purpose_is_counted_exactly(int loads, int named)
    {
        // The sentence is put together here, so this assembly does not load it whole.
        var carried = string.Concat(Sentences[0]);
        var loaded = new Dictionary<string, int>(StringComparer.Ordinal) { ["The words of a test's own."] = 4 };
        if (loads > 0)
        {
            loaded[carried] = loads;
        }

        var listed = new Dictionary<string, (int Loads, string Why)>(StringComparer.Ordinal) { [carried] = (2, "a reason") };

        Assert.Equal(named, Unexplained(loaded, listed).Count);
    }

    /// <summary>
    /// Each loaded string that carries a sentence and is not listed with exactly its count, and each listed string no
    /// longer loaded.
    /// </summary>
    private static List<string> Unexplained(Dictionary<string, int> loaded, Dictionary<string, (int Loads, string Why)> listed)
    {
        var found = new List<string>();
        foreach (var (text, loads) in loaded)
        {
            var sentences = Matched(text);
            if (sentences.Count == 0)
            {
                continue;
            }

            if (!listed.TryGetValue(text, out var onPurpose))
            {
                found.Add($"\"{text}\", loaded {loads} time(s), carries \"{string.Join("\", \"", sentences)}\"");
            }
            else if (onPurpose.Loads != loads)
            {
                found.Add($"\"{text}\" is listed as loaded {onPurpose.Loads} time(s), and is loaded {loads} time(s)");
            }
        }

        found.AddRange(listed.Keys.Where(text => !loaded.ContainsKey(text)).Select(text => $"\"{text}\" is listed, and nothing loads it any more"));
        return found;
    }

    /// <summary>The sentences a string carries, compared without spaces and in any case.</summary>
    private static List<string> Matched(string text)
    {
        var squeezed = Spaces().Replace(text, string.Empty);
        return [.. Sentences.Where(words => squeezed.Contains(string.Concat(words), StringComparison.OrdinalIgnoreCase)).Select(words => string.Join(' ', words))];
    }

    /// <summary>Every string an assembly's method bodies load, with how many instructions load it, read without loading it again.</summary>
    private static Dictionary<string, int> LoadedStrings(string assemblyFile)
    {
        using var image = new PEReader(File.OpenRead(assemblyFile));
        var metadata = image.GetMetadataReader();
        var loaded = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var handle in metadata.MethodDefinitions)
        {
            var method = metadata.GetMethodDefinition(handle);
            if (method.RelativeVirtualAddress == 0)
            {
                continue;
            }

            foreach (var token in StringTokens(image.GetMethodBody(method.RelativeVirtualAddress).GetILBytes()!))
            {
                var text = metadata.GetUserString((UserStringHandle)MetadataTokens.Handle(token));
                loaded[text] = loaded.GetValueOrDefault(text) + 1;
            }
        }

        return loaded;
    }

    /// <summary>The token of every <c>ldstr</c> in a method body.</summary>
    private static IEnumerable<int> StringTokens(byte[] il)
    {
        var at = 0;
        while (at < il.Length)
        {
            var twoBytes = il[at] == 0xFE;
            var code = Codes[twoBytes ? unchecked((short)(0xFE00 | il[at + 1])) : il[at]];
            at += twoBytes ? 2 : 1;

            if (code.OperandType == OperandType.InlineString)
            {
                yield return BitConverter.ToInt32(il, at);
            }

            at += code.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + (4 * BitConverter.ToInt32(il, at)),
                _ => 4,
            };
        }
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
