using System.Security.Cryptography;
using System.Text;

namespace Jason.Runtime.Tests.Documentation;

/// <summary>
/// The licence and the trademark note at the root. The licence is the MIT licence and nothing else, so it covers the
/// whole repository and the executable built from it. The note is the text ReplyApp Inc. accepted, byte for byte, apart
/// from the edits it took here: it no longer says it is a draft that is not in force, and its first paragraph names the
/// one licence there is. The test puts those edits back and compares the result with the accepted text's SHA-256, so
/// any other change to the note fails here.
/// </summary>
public sealed class LicenceTextTests
{
    /// <summary>The MIT licence as the repository carries it, holder and year included.</summary>
    private const string Mit = """
        MIT License

        Copyright (c) 2026 ReplyApp Inc.

        Permission is hereby granted, free of charge, to any person obtaining a copy
        of this software and associated documentation files (the "Software"), to deal
        in the Software without restriction, including without limitation the rights
        to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
        copies of the Software, and to permit persons to whom the Software is
        furnished to do so, subject to the following conditions:

        The above copyright notice and this permission notice shall be included in all
        copies or substantial portions of the Software.

        THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
        IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
        FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
        AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
        LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
        OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
        SOFTWARE.

        """;

    /// <summary>The SHA-256 of the accepted trademark note, its draft line included.</summary>
    private const string AcceptedNote = "efe8fafad9aa5deac5725ab5d89e4cd204c67001519ef1e1c35860b3b42c821f";

    /// <summary>The note's first paragraph names the licence Jason is released under, which is the only one.</summary>
    private static readonly string[] NoteLicenceSentence =
    [
        "**ReplyApp Inc.** The MIT licence Jason is released under grants no right to use them, except as this",
        "note describes.",
    ];

    /// <summary>The same two lines as accepted, when the runtime had a licence of its own beside the MIT licence.</summary>
    private static readonly string[] AcceptedLicenceSentence =
    [
        "**ReplyApp Inc.** Neither the MIT licence of the open components nor the Jason Runtime Licence grants",
        "any right to use them, except as this note describes.",
    ];

    [Fact]
    public void The_licence_is_the_mit_licence_alone()
    {
        var bytes = File.ReadAllBytes(Path.Combine(RepositoryTree.Root(), "LICENSE"));

        Assert.Equal(Mit, Encoding.UTF8.GetString(bytes));
    }

    /// <summary>
    /// The runtime has no licence of its own any more: the file that held it is gone, and no other licence file sits
    /// beside the C# tree.
    /// </summary>
    [Fact]
    public void No_other_licence_file_is_at_the_root_or_beside_the_csharp_tree()
    {
        var root = RepositoryTree.Root();

        Assert.False(File.Exists(Path.Combine(root, "LICENSE-RUNTIME.md")), "LICENSE-RUNTIME.md is still at the root.");
        Assert.False(File.Exists(Path.Combine(root, "dotnet", "LICENSE")), "dotnet/LICENSE is there beside LICENSE.");
    }

    [Fact]
    public void The_trademark_note_is_the_accepted_text_without_its_draft_line_and_naming_one_licence()
    {
        var bytes = File.ReadAllBytes(Path.Combine(RepositoryTree.Root(), "TRADEMARKS.md"));
        var text = Encoding.UTF8.GetString(bytes);

        Assert.DoesNotContain("DRAFT", text, StringComparison.Ordinal);
        Assert.DoesNotContain('\r', text);
        Assert.Equal((byte)'\n', bytes[^1]);

        var lines = text.Split('\n').ToList();
        Assert.Equal(NoteLicenceSentence, lines[3..5]);
        lines[3] = AcceptedLicenceSentence[0];
        lines[4] = AcceptedLicenceSentence[1];
        lines.InsertRange(2, ["DRAFT FOR LEGAL REVIEW (8 October 2026). Not in force.", string.Empty]);
        Assert.Equal(AcceptedNote, Sha256(string.Join('\n', lines)));
    }

    private static string Sha256(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
