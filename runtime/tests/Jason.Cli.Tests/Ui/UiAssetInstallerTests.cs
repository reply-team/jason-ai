using System.IO.Compression;
using System.Text;
using Jason.Cli.Ui;
using Jason.Contracts.Discovery;
using Jason.Contracts.Plugins;
using Jason.Contracts.Ui;
using Jason.Contracts.Update;
using Jason.Runtime.Tests;

namespace Jason.Cli.Tests.Ui;

/// <summary>
/// The asset is a zip with the build at its root. The unpacker refuses anything that is not that before it touches
/// the installed UI, places a good one atomically, and answers with the facts status reports. It is held to the
/// version it was fetched for and to the API the runtime speaks, so a release's zip can't land beside another release.
/// </summary>
public sealed class UiAssetInstallerTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("jason-ui-asset").FullName;

    private static readonly UiExpected Expected = new(new SemanticVersion(0, 10, 0, null), "v1");

    private JasonPaths Paths => new(_root);

    [Fact]
    public void A_good_asset_is_placed_as_the_default_ui_with_its_manifest_and_digest()
    {
        var zip = Zip(("index.html", "<title>x</title>"), ("manifest.json", "{\"version\":\"0.10.0\",\"api_version\":\"v1\"}"), ("favicon.svg", "<svg/>"), ("assets/app-1.js", "1"), ("THIRD-PARTY-NOTICES", "preact: MIT"));

        var installed = Install(zip);

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
        Install(Zip(("index.html", "one"), ("manifest.json", "{\"version\":\"0.10.0\",\"api_version\":\"v1\"}"), ("assets/old.js", "x")));
        Install(Zip(("index.html", "two"), ("manifest.json", "{\"version\":\"0.10.0\",\"api_version\":\"v1\"}"), ("assets/new.js", "y")));

        Assert.Equal("two", File.ReadAllText(Path.Combine(Paths.UiDefaultDirectory, "index.html")));
        Assert.False(File.Exists(Path.Combine(Paths.UiDefaultDirectory, "assets", "old.js")));
    }

    [Fact]
    [Critical(Family.B6, "ui/custom belongs to the person")]
    public void The_custom_directory_is_never_touched()
    {
        Directory.CreateDirectory(Paths.UiCustomDirectory);
        File.WriteAllText(Path.Combine(Paths.UiCustomDirectory, "index.html"), "mine");
        Install(Zip(("index.html", "x"), ("manifest.json", "{\"version\":\"0.10.0\",\"api_version\":\"v1\"}")));
        Assert.Equal("mine", File.ReadAllText(Path.Combine(Paths.UiCustomDirectory, "index.html")));
    }

    [Theory]
    // Critical: zip-slip refused whole.
    [InlineData("../escape.html", Traits = ["Category", "Critical", "CriticalFamily", nameof(Family.X2)])]
    [InlineData("/abs.html")]
    [InlineData("jason-ui/index.html")]
    [InlineData(".hidden")]
    public void An_entry_that_is_not_a_file_of_a_build_refuses_the_whole_asset_before_anything_changes(string entry)
    {
        Install(Zip(("index.html", "keep"), ("manifest.json", "{\"version\":\"0.10.0\",\"api_version\":\"v1\"}")));
        var bad = Zip(("index.html", "x"), ("manifest.json", "{\"version\":\"0.10.0\",\"api_version\":\"v1\"}"), (entry, "x"));

        var refused = Assert.Throws<UiAssetRefusedException>(() => Install(bad));

        Assert.Contains(entry.TrimStart('/'), refused.Message, StringComparison.Ordinal);
        Assert.Equal("keep", File.ReadAllText(Path.Combine(Paths.UiDefaultDirectory, "index.html")));
    }

    [Fact]
    public void An_asset_without_an_index_or_without_a_manifest_is_refused_by_name()
    {
        Assert.Contains("index.html", Assert.Throws<UiAssetRefusedException>(() => Install(Zip(("manifest.json", "{\"version\":\"0.10.0\",\"api_version\":\"v1\"}")))).Message, StringComparison.Ordinal);
        Assert.Contains("manifest.json", Assert.Throws<UiAssetRefusedException>(() => Install(Zip(("index.html", "x")))).Message, StringComparison.Ordinal);
        Assert.Contains("manifest.json", Assert.Throws<UiAssetRefusedException>(() => Install(Zip(("index.html", "x"), ("manifest.json", "nope")))).Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Paths.UiDefaultDirectory));
    }

    /// <summary>An asset over the byte bound is refused by what it declares, before anything is extracted.</summary>
    [Fact]
    [Critical(Family.I1, "the UI asset bound")]
    public void An_asset_over_the_byte_bound_is_refused_before_anything_is_extracted()
    {
        var refused = Assert.Throws<UiAssetRefusedException>(() => Install(WithBigEntry("honest.zip", PackageDigest.MaxBytes + 1)));

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

        var installed = Install(file);

        Assert.Equal(1000, new FileInfo(Path.Combine(installed.Directory, "assets", "big.bin")).Length);
    }

    /// <summary>A build whose <c>assets/big.bin</c> is <paramref name="size"/> zero bytes: a few kilobytes of archive, whatever the size.</summary>
    private string WithBigEntry(string name, long size)
    {
        var file = Path.Combine(_root, name);
        using var archive = ZipFile.Open(file, ZipArchiveMode.Create);
        foreach (var (entry, text) in new[] { ("index.html", "x"), ("manifest.json", "{\"version\":\"0.10.0\",\"api_version\":\"v1\"}") })
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
        Assert.Throws<UiAssetRefusedException>(() => Install(file));
    }

    [Theory]
    [InlineData("0.14.0", "v1", "0.14.0")]
    [InlineData("0.13.0", "v2", "v2")]
    public void A_build_for_another_version_or_api_is_refused_and_the_old_one_stays(string version, string api, string named)
    {
        var expected = new UiExpected(new SemanticVersion(0, 13, 0, null), "v1");
        Install(Written(UiZip.Build("0.13.0")), expected);

        var refused = Assert.Throws<UiAssetRefusedException>(() => Install(Written(UiZip.Build(version, api)), expected));

        Assert.Contains(named, refused.Message, StringComparison.Ordinal);
        Assert.Equal("0.13.0", UiManifest.Read(Paths.UiDefaultDirectory)!.Version);
        Assert.DoesNotContain(Directory.GetDirectories(Paths.UiDirectory), d => Path.GetFileName(d) != "default");
    }

    /// <summary>A release build reports its version with the commit after a plus, which says nothing about which release it is.</summary>
    [Fact]
    public void A_build_is_held_to_its_version_without_metadata()
    {
        var installed = Install(Written(UiZip.Build("0.13.0+1a2b3c4")), new UiExpected(new SemanticVersion(0, 13, 0, null), "v1"));

        Assert.Equal("0.13.0+1a2b3c4", installed.Manifest.Version);
    }

    /// <summary>
    /// Windows refuses a directory rename while anything holds a file inside the tree — the runtime serving it, a scanner
    /// reading what was just unpacked — and says access is denied. The move is tried again, as the skills' swap tries it.
    /// </summary>
    [Fact]
    public void A_move_refused_while_a_file_in_it_is_held_is_tried_again()
    {
        var tries = 0;

        UiAssetInstaller.Move(
            () =>
            {
                if (++tries < 3)
                {
                    throw new IOException("held by another program", unchecked((int)0x80070005));
                }
            },
            "default",
            TimeProvider.System,
            _ => { });

        Assert.Equal(3, tries);
    }

    [Fact]
    public void A_hold_that_outlasts_the_wait_is_a_refusal_that_says_so()
    {
        var clock = new Stepping();

        var refused = Assert.Throws<UiAssetRefusedException>(() => UiAssetInstaller.Move(
            () => throw new IOException("held by another program", unchecked((int)0x80070005)),
            "default",
            clock,
            clock.Advance));

        Assert.Contains("something is holding a file in it open", refused.Message, StringComparison.Ordinal);
    }

    /// <summary>A clock that moves only when a pause is taken, so a wait of seconds takes none.</summary>
    private sealed class Stepping : TimeProvider
    {
        private long _now;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => _now;

        public void Advance(TimeSpan by) => _now += by.Ticks;
    }

    private UiAssetInstalled Install(string zip, UiExpected? expected = null) =>
        UiAssetInstaller.Install(zip, Paths, expected ?? Expected, TimeProvider.System);

    private string Written(byte[] zip)
    {
        var file = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".zip");
        File.WriteAllBytes(file, zip);
        return file;
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
