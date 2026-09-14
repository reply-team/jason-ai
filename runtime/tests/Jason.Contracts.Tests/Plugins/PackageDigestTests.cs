using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Jason.Contracts.Plugins;

namespace Jason.Contracts.Tests.Plugins;

public class PackageDigestTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "jason-digest-tests", Guid.NewGuid().ToString("N"));

    public PackageDigestTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private string Write(string relativePath, string content)
    {
        var full = Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return full;
    }

    [Fact]
    public void Files_are_sorted_by_relative_path_with_forward_slashes()
    {
        Write("modules/helper.js", "h");
        Write("main.js", "m");
        Write("plugin.yaml", "y");

        Assert.Equal(["main.js", "modules/helper.js", "plugin.yaml"], PackageDigest.Files(_root));
    }

    [Fact]
    public void Hidden_files_and_directories_are_excluded()
    {
        Write("main.js", "m");
        Write(".DS_Store", "junk");
        Write(".git/config", "junk");
        Write("modules/.cache/y", "junk");

        Assert.Equal(["main.js"], PackageDigest.Files(_root));
    }

    [Fact]
    public void The_same_bytes_give_the_same_digest_whatever_the_creation_order()
    {
        Write("plugin.yaml", "y");
        Write("modules/helper.js", "h");
        Write("main.js", "m");
        var first = PackageDigest.Compute(_root);

        var other = Path.Combine(Path.GetTempPath(), "jason-digest-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(other, "modules"));
        File.WriteAllText(Path.Combine(other, "main.js"), "m");
        File.WriteAllText(Path.Combine(other, "modules", "helper.js"), "h");
        File.WriteAllText(Path.Combine(other, "plugin.yaml"), "y");
        try
        {
            Assert.Equal(first.Digest, PackageDigest.Compute(other).Digest);
            Assert.Equal(3, first.FileCount);
            Assert.Equal(3, first.TotalBytes);
        }
        finally
        {
            Directory.Delete(other, recursive: true);
        }
    }

    [Fact]
    public void Renaming_a_file_changes_the_digest()
    {
        Write("main.js", "m");
        var before = PackageDigest.Compute(_root).Digest;

        File.Move(Path.Combine(_root, "main.js"), Path.Combine(_root, "entry.js"));

        Assert.NotEqual(before, PackageDigest.Compute(_root).Digest);
    }

    [Fact]
    public void Changing_one_byte_changes_the_digest()
    {
        Write("main.js", "aaaa");
        var before = PackageDigest.Compute(_root).Digest;

        Write("main.js", "aaab");

        Assert.NotEqual(before, PackageDigest.Compute(_root).Digest);
    }

    [Fact]
    public void Adding_a_file_changes_the_digest()
    {
        Write("main.js", "m");
        var before = PackageDigest.Compute(_root).Digest;

        Write("README.md", string.Empty);

        Assert.NotEqual(before, PackageDigest.Compute(_root).Digest);
    }

    [Fact]
    public void The_digest_starts_with_sha256_and_is_sixty_four_hex_characters()
    {
        Write("main.js", "m");

        var digest = PackageDigest.Compute(_root).Digest;

        Assert.StartsWith("sha256:", digest, StringComparison.Ordinal);
        Assert.Matches("^sha256:[0-9a-f]{64}$", digest);
    }

    [Fact]
    public void Too_many_files_is_refused()
    {
        for (var index = 0; index <= PackageDigest.MaxFiles; index++)
        {
            File.WriteAllText(Path.Combine(_root, string.Create(CultureInfo.InvariantCulture, $"f{index}.js")), string.Empty);
        }

        Assert.Throws<PackageTooLargeException>(() => PackageDigest.Compute(_root));
    }

    [Fact]
    public void Too_many_bytes_is_refused()
    {
        using (var file = new FileStream(Path.Combine(_root, "big.bin"), FileMode.CreateNew))
        {
            file.SetLength(PackageDigest.MaxBytes + 1);
        }

        Assert.Throws<PackageTooLargeException>(() => PackageDigest.Compute(_root));
    }

    [Fact]
    public void A_known_package_has_a_known_digest()
    {
        Write("plugin.yaml", "a\n");
        Write("main.js", "b");

        var stream = new List<byte>();
        Append(stream, "main.js", "b"u8.ToArray());
        Append(stream, "plugin.yaml", "a\n"u8.ToArray());
        var expected = "sha256:" + Convert.ToHexStringLower(SHA256.HashData([.. stream]));

        Assert.Equal(expected, PackageDigest.Compute(_root).Digest);

        static void Append(List<byte> stream, string path, byte[] content)
        {
            stream.AddRange(Encoding.UTF8.GetBytes(path));
            stream.Add(0);
            stream.AddRange(Encoding.ASCII.GetBytes(content.Length.ToString(CultureInfo.InvariantCulture)));
            stream.Add(0);
            stream.AddRange(content);
        }
    }
}
