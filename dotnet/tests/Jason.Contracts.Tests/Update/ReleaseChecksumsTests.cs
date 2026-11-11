using Jason.Contracts.Update;
using Jason.Runtime.Tests;

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
    // Critical: the checksum line: found, twice, not a digest.
    [InlineData(B + "  jason-linux-x64.tar.gz\n" + A + "  jason-ui.zip\n", A, null, Traits = ["Category", "Critical", "CriticalFamily", nameof(Family.I1)])]
    [InlineData(B + "  jason-linux-x64.tar.gz\r\n" + A + "  jason-ui.zip\r\n\r\n", A, null)]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA  jason-ui.zip\n", A, null)]
    [InlineData(A + " *jason-ui.zip\n", A, null)]
    [InlineData("\uFEFF" + A + "  jason-ui.zip\n", A, null)]
    [InlineData(A + "  jason-ui.zip  \n", A, null)]
    [InlineData(A + "  old-jason-ui.zip\n", null, "lists no jason-ui.zip")]
    // Critical: the checksum line: found, twice, not a digest.
    [InlineData(A + "  jason-ui.zip\n" + B + "  jason-ui.zip\n", null, "more than once", Traits = ["Category", "Critical", "CriticalFamily", nameof(Family.I1)])]
    // Critical: the checksum line: found, twice, not a digest.
    [InlineData("xyz  jason-ui.zip\n", null, "no SHA-256", Traits = ["Category", "Critical", "CriticalFamily", nameof(Family.I1)])]
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

    /// <summary>
    /// The checksums a release with the desktop's files writes, read by this reader and by the rule an install script
    /// greps with: each earlier file still has its one line, and a desktop file's name is never another's, the
    /// installer's beside its blockmap and a zip's beside its own.
    /// </summary>
    [Fact]
    public void A_release_with_the_desktop_s_files_still_gives_each_file_its_own_line()
    {
        var listed = ReleaseAssets.Published.Where(name => name != ReleaseAssets.Checksums && !ReleaseAssets.InstallScripts.Contains(name)).ToList();
        var digests = listed.Select((name, index) => Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(name + index)))).ToList();
        var text = string.Concat(listed.Select((name, index) => $"{digests[index]}  {name}\n"));

        Assert.Contains("jason-desktop-windows-x64-setup.exe", listed);
        foreach (var (name, index) in listed.Select((name, index) => (name, index)))
        {
            Assert.Equal(digests[index], ReleaseChecksums.DigestOf(text, name, out var problem));
            Assert.Null(problem);
            Assert.Single(text.Split('\n'), line => line.EndsWith("  " + name, StringComparison.Ordinal));
        }
    }
}
