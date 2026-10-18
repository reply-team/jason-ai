using System.IO.Compression;
using System.Text;
using Jason.Contracts.Discovery;
using Jason.Contracts.Plugins;
using Jason.Contracts.Ui;

namespace Jason.Contracts.Tests.Ui;

/// <summary>
/// The asset is a zip with the build at its root. The unpacker refuses anything that is not that before it touches
/// the installed UI, places a good one atomically, and answers with the facts status reports.
/// </summary>
public sealed class UiAssetInstallerTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("jason-ui-asset").FullName;

    private JasonPaths Paths => new(_root);

    [Fact]
    public void A_good_asset_is_placed_as_the_default_ui_with_its_manifest_and_digest()
    {
        var zip = Zip(("index.html", "<title>x</title>"), ("manifest.json", "{\"version\":\"0.10.0\",\"api_version\":\"v1\"}"), ("favicon.svg", "<svg/>"), ("assets/app-1.js", "1"), ("THIRD-PARTY-NOTICES", "preact: MIT"));

        var installed = UiAssetInstaller.Install(zip, Paths);

        Assert.Equal(Paths.UiDefaultDirectory, installed.Directory);
        Assert.Equal(new UiManifest("0.10.0", "v1"), installed.Manifest);
        Assert.Equal(5, installed.FileCount);
        Assert.Equal(PackageDigest.Compute(Paths.UiDefaultDirectory).Digest, installed.Digest);
        Assert.Equal("1", File.ReadAllText(Path.Combine(Paths.UiDefaultDirectory, "assets", "app-1.js")));
        Assert.DoesNotContain(Directory.GetDirectories(Paths.UiDirectory), d => Path.GetFileName(d).StartsWith('.'));
    }

    [Fact]
    public void A_second_install_replaces_the_first_whole()
    {
        UiAssetInstaller.Install(Zip(("index.html", "one"), ("manifest.json", "{\"version\":\"1\",\"api_version\":\"v1\"}"), ("assets/old.js", "x")), Paths);
        UiAssetInstaller.Install(Zip(("index.html", "two"), ("manifest.json", "{\"version\":\"2\",\"api_version\":\"v1\"}"), ("assets/new.js", "y")), Paths);

        Assert.Equal("two", File.ReadAllText(Path.Combine(Paths.UiDefaultDirectory, "index.html")));
        Assert.False(File.Exists(Path.Combine(Paths.UiDefaultDirectory, "assets", "old.js")));
    }

    [Fact]
    public void The_custom_directory_is_never_touched()
    {
        Directory.CreateDirectory(Paths.UiCustomDirectory);
        File.WriteAllText(Path.Combine(Paths.UiCustomDirectory, "index.html"), "mine");
        UiAssetInstaller.Install(Zip(("index.html", "x"), ("manifest.json", "{\"version\":\"1\",\"api_version\":\"v1\"}")), Paths);
        Assert.Equal("mine", File.ReadAllText(Path.Combine(Paths.UiCustomDirectory, "index.html")));
    }

    [Theory]
    [InlineData("../escape.html")]
    [InlineData("/abs.html")]
    [InlineData("jason-ui/index.html")]
    [InlineData(".hidden")]
    public void An_entry_that_is_not_a_file_of_a_build_refuses_the_whole_asset_before_anything_changes(string entry)
    {
        UiAssetInstaller.Install(Zip(("index.html", "keep"), ("manifest.json", "{\"version\":\"1\",\"api_version\":\"v1\"}")), Paths);
        var bad = Zip(("index.html", "x"), ("manifest.json", "{\"version\":\"2\",\"api_version\":\"v1\"}"), (entry, "x"));

        var refused = Assert.Throws<UiAssetRefusedException>(() => UiAssetInstaller.Install(bad, Paths));

        Assert.Contains(entry.TrimStart('/'), refused.Message, StringComparison.Ordinal);
        Assert.Equal("keep", File.ReadAllText(Path.Combine(Paths.UiDefaultDirectory, "index.html")));
    }

    [Fact]
    public void An_asset_without_an_index_or_without_a_manifest_is_refused_by_name()
    {
        Assert.Contains("index.html", Assert.Throws<UiAssetRefusedException>(() => UiAssetInstaller.Install(Zip(("manifest.json", "{\"version\":\"1\",\"api_version\":\"v1\"}")), Paths)).Message, StringComparison.Ordinal);
        Assert.Contains("manifest.json", Assert.Throws<UiAssetRefusedException>(() => UiAssetInstaller.Install(Zip(("index.html", "x")), Paths)).Message, StringComparison.Ordinal);
        Assert.Contains("manifest.json", Assert.Throws<UiAssetRefusedException>(() => UiAssetInstaller.Install(Zip(("index.html", "x"), ("manifest.json", "nope")), Paths)).Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Paths.UiDefaultDirectory));
    }

    /// <summary>An asset over the byte bound is refused by what it declares, before anything is extracted.</summary>
    [Fact]
    public void An_asset_over_the_byte_bound_is_refused_before_anything_is_extracted()
    {
        var refused = Assert.Throws<UiAssetRefusedException>(() => UiAssetInstaller.Install(WithBigEntry("honest.zip", PackageDigest.MaxBytes + 1), Paths));

        Assert.Contains("holds at most", refused.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Paths.UiDirectory));
    }

    /// <summary>
    /// The sizes an archive declares are what the bound adds up, and they are also what an entry yields: here the
    /// fields say a kilobyte where the deflate stream holds a byte over the bound, and a kilobyte is what comes out.
    /// If a runtime ever yielded the stream whole, this goes red and the bound moves to the bytes extracted.
    /// </summary>
    [Fact]
    public void An_entry_yields_no_more_than_it_declares_so_the_declared_sum_is_the_bound()
    {
        var real = PackageDigest.MaxBytes + 1;
        var file = WithBigEntry("lying.zip", real);
        var bytes = File.ReadAllBytes(file);
        var honest = BitConverter.GetBytes((uint)real);
        var claimed = BitConverter.GetBytes(1000u);
        var patched = 0;
        for (var at = 0; at <= bytes.Length - 4; at++)
        {
            if (bytes.AsSpan(at, 4).SequenceEqual(honest))
            {
                claimed.CopyTo(bytes, at);
                patched++;
            }
        }

        Assert.True(patched >= 2, $"the size fields were not found to patch ({patched})");
        File.WriteAllBytes(file, bytes);

        var installed = UiAssetInstaller.Install(file, Paths);

        Assert.Equal(1000, new FileInfo(Path.Combine(installed.Directory, "assets", "big.bin")).Length);
    }

    /// <summary>A build whose <c>assets/big.bin</c> is <paramref name="size"/> zero bytes: a few kilobytes of archive, whatever the size.</summary>
    private string WithBigEntry(string name, long size)
    {
        var file = Path.Combine(_root, name);
        using var archive = ZipFile.Open(file, ZipArchiveMode.Create);
        foreach (var (entry, text) in new[] { ("index.html", "x"), ("manifest.json", "{\"version\":\"1\",\"api_version\":\"v1\"}") })
        {
            using var small = archive.CreateEntry(entry).Open();
            small.Write(Encoding.UTF8.GetBytes(text));
        }

        using var big = archive.CreateEntry("assets/big.bin", CompressionLevel.Optimal).Open();
        var zeros = new byte[81920];
        for (var left = size; left > 0; left -= zeros.Length)
        {
            big.Write(zeros, 0, (int)Math.Min(left, zeros.Length));
        }

        return file;
    }

    [Fact]
    public void A_file_that_is_not_a_zip_is_refused()
    {
        var file = Path.Combine(_root, "not.zip");
        File.WriteAllText(file, "hello");
        Assert.Throws<UiAssetRefusedException>(() => UiAssetInstaller.Install(file, Paths));
    }

    private string Zip(params (string Name, string Text)[] entries)
    {
        var file = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".zip");
        using (var archive = ZipFile.Open(file, ZipArchiveMode.Create))
        {
            foreach (var (name, text) in entries)
            {
                var entry = archive.CreateEntry(name);
                using var stream = entry.Open();
                stream.Write(Encoding.UTF8.GetBytes(text));
            }
        }

        return file;
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
