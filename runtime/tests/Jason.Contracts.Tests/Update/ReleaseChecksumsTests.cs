using Jason.Contracts.Update;

namespace Jason.Contracts.Tests.Update;

/// <summary>
/// A release's checksums.txt, read the way sha256sum writes it and Write-Manifest.ps1 writes it: one
/// "&lt;sha256&gt;  &lt;name&gt;" per file. The one line for a name is its digest; anything else is said rather than guessed.
/// </summary>
public class ReleaseChecksumsTests
{
    private const string A = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string B = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    [Theory]
    [InlineData(B + "  jason-linux-x64.tar.gz\n" + A + "  jason-ui.zip\n", A, null)]
    [InlineData(B + "  jason-linux-x64.tar.gz\r\n" + A + "  jason-ui.zip\r\n\r\n", A, null)]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA  jason-ui.zip\n", A, null)]
    [InlineData(A + " *jason-ui.zip\n", A, null)]
    [InlineData("\uFEFF" + A + "  jason-ui.zip\n", A, null)]
    [InlineData(A + "  jason-ui.zip  \n", A, null)]
    [InlineData(A + "  old-jason-ui.zip\n", null, "lists no jason-ui.zip")]
    [InlineData(A + "  jason-ui.zip\n" + B + "  jason-ui.zip\n", null, "more than once")]
    [InlineData("xyz  jason-ui.zip\n", null, "no SHA-256")]
    [InlineData("", null, "lists no jason-ui.zip")]
    public void A_release_s_line_is_found_exactly(string text, string? digest, string? problem)
    {
        var found = ReleaseChecksums.DigestOf(text, "jason-ui.zip", out var said);

        Assert.Equal(digest, found);
        if (problem is null)
        {
            Assert.Null(said);
        }
        else
        {
            Assert.Contains(problem, said, StringComparison.Ordinal);
        }
    }
}
