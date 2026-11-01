using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Jason.Cli.Doctor;
using Jason.Cli.Plugins;
using Jason.Cli.Tests.Files;
using Jason.Cli.Tests.Update;
using Jason.Cli.Uninstall;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Contracts.Files;
using Jason.Contracts.Plugins;
using Jason.Contracts.Update;
using Jason.Runtime.Tests;

namespace Jason.Cli.Tests.Plugins;

/// <summary>
/// <c>jason plugin install</c>: the package this build's release names in its manifest, fetched beside the release's
/// checksums.txt, held to the line there and, unpacked, to the content digest the manifest gives, placed whole as
/// <c>plugins/&lt;id&gt;</c> or not at all, and then loaded by the runtime when one answers. Every environment names its
/// version, because one CI leg builds these tests with a release's.
/// </summary>
public sealed class PluginInstallCommandTests : IDisposable
{
    private const string Version = "0.21.0";

    private static readonly RuntimeDescriptor Live = new("v1", Version, "rt_LIVE", 77, "http://127.0.0.1:5000", "the-token", DateTimeOffset.UnixEpoch);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly TempPaths _paths = new();
    private readonly StringWriter _out = new();
    private readonly StringWriter _error = new();
    private readonly List<string> _posted = [];

    /// <summary>The bodies the runtime was sent, in order.</summary>
    private readonly List<string> _bodies = [];

    /// <summary>The account's name the CLI sees, when a test says.</summary>
    private string? _account;

    /// <summary>What the release's server answers a path with instead of the file, when a test says.</summary>
    private Func<string, HttpStatusCode?>? _failing;

    /// <summary>What the runtime answers the reload with, when a test says; otherwise the registry of an activated reload.</summary>
    private Func<HttpResponseMessage>? _reload;

    /// <summary>The envelope the runtime answers a reload it refuses with: a package of the candidate set it rejects.</summary>
    private const string Rejected = """{"error":{"code":"plugin_reload_rejected","message":"The plugin reload was rejected: 1 problem(s) in the candidate set; the previous snapshot stays active.","retryable":false,"details":[{"field":"example","code":"manifest_invalid","message":"The manifest is not valid."}]}}""";

    [Fact]
    public async Task Install_places_the_release_s_package_and_asks_the_runtime_to_reload()
    {
        var (release, package) = Release();
        _paths.WriteDescriptor(Live);

        var exit = await RunAsync(release, "plugin", "install", "example");

        Assert.True(exit == ExitCodes.Success, _out.ToString() + _error.ToString());
        var placed = _paths.Paths.PluginPackageDirectory("example");
        Assert.Equal(package.Digest, PackageDigest.Compute(placed).Digest);
        Assert.Contains($"/releases/download/v{Version}/manifest.json", release.Requested);
        Assert.Contains($"/releases/download/v{Version}/checksums.txt", release.Requested);
        Assert.Contains($"/releases/download/v{Version}/jason-plugin-example.zip", release.Requested);
        Assert.Equal([Operations.PluginReload], _posted);
        Assert.Equal(["example"], Directory.EnumerateFileSystemEntries(_paths.Paths.PluginsDirectory).Select(Path.GetFileName));

        using var answer = JsonDocument.Parse(_out.ToString());
        Assert.Equal("example", answer.RootElement.GetProperty("id").GetString());
        Assert.Equal("1.2.0", answer.RootElement.GetProperty("version").GetString());
        Assert.Equal(placed, answer.RootElement.GetProperty("directory").GetString());
        Assert.Equal(package.Digest, answer.RootElement.GetProperty("digest").GetString());
        Assert.Equal("new", answer.RootElement.GetProperty("placed").GetString());
        Assert.Equal("activated", answer.RootElement.GetProperty("load").GetString());
    }

    /// <summary>The reload an install asks for is a write like any other: without <c>--actor</c> it is made as the account's person.</summary>
    [Fact]
    public async Task The_reload_an_install_asks_for_is_made_as_the_account_s_person()
    {
        var (release, _) = Release();
        _paths.WriteDescriptor(Live);
        _account = "quillon";

        var exit = await RunAsync(release, "plugin", "install", "example");

        Assert.True(exit == ExitCodes.Success, _out.ToString() + _error.ToString());
        using var sent = JsonDocument.Parse(Assert.Single(_bodies));
        var actor = sent.RootElement.GetProperty("actor");
        Assert.Equal("human", actor.GetProperty("type").GetString());
        Assert.Equal("quillon", actor.GetProperty("id").GetString());
    }

    /// <summary>
    /// The package does not match its line in checksums.txt, or has none there: nothing is unpacked, and nothing is left
    /// in the plugins directory, not even the staging a placed package would have moved out of.
    /// </summary>
    [Theory]
    // Critical: the plugin package's checksum.
    [InlineData("another digest", "does not match", Traits = ["Category", "Critical", "CriticalFamily", nameof(Family.I1)])]
    [InlineData("no line", "lists no jason-plugin-example.zip")]
    public async Task A_package_that_does_not_match_checksums_txt_places_nothing(string wrong, string said)
    {
        var (release, _) = Release(checksums: wrong);
        _paths.WriteDescriptor(Live);

        var exit = await RunAsync(release, "plugin", "install", "example");

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Contains(said, _error.ToString(), StringComparison.Ordinal);
        Assert.Contains("Nothing was changed.", _error.ToString(), StringComparison.Ordinal);
        AssertNothingPlaced();
    }

    /// <summary>
    /// The download matches its line, and what it unpacks to is not the tree the manifest names by its content digest:
    /// the checksum vouches for the file, the digest for the package, and both have to hold before anything is placed.
    /// </summary>
    [Fact]
    [Critical(Family.I1, "the plugin package's content digest")]
    public async Task A_package_whose_tree_is_not_the_manifest_s_digest_places_nothing()
    {
        var (release, _) = Release(contentDigest: "sha256:" + new string('0', 64));
        _paths.WriteDescriptor(Live);

        var exit = await RunAsync(release, "plugin", "install", "example");

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Contains("sha256:" + new string('0', 64), _error.ToString(), StringComparison.Ordinal);
        Assert.Contains("Nothing was changed.", _error.ToString(), StringComparison.Ordinal);
        AssertNothingPlaced();
    }

    /// <summary>
    /// An archive that is not a plugin package's is refused before anything is unpacked, and nothing lands anywhere: an entry
    /// that would land outside the package or that the digest would not count, one that names a drive or an alternate
    /// stream, carries a control character, or ends in a dot or a space that Windows would drop; a name twice, whatever its
    /// case; more files or more bytes than a package may hold; no plugin.yaml at its root. A directory entry is not a file,
    /// and wherever it points it writes nothing.
    /// </summary>
    [Theory]
    [InlineData("../outside.js")]
    [InlineData(".hidden/main.js")]
    [InlineData("/rooted.js")]
    [InlineData("a backslash")]
    [InlineData("C:x")]
    [InlineData("a:b")]
    [InlineData("main.js:hidden")]
    [InlineData("a NUL")]
    [InlineData("a trailing dot")]
    [InlineData("a trailing space")]
    [InlineData("a name twice in another case")]
    [InlineData("more files than a package holds")]
    [InlineData("more bytes than a package holds")]
    [InlineData("no plugin.yaml at the root")]
    [InlineData("a directory entry that points outside")]
    public async Task An_archive_that_is_not_a_plugin_package_places_nothing_anywhere(string shape)
    {
        var (entries, said) = Shaped(shape);
        var release = ReleaseOf(entries);
        _paths.WriteDescriptor(Live);

        var exit = await RunAsync(release, "plugin", "install", "example");

        if (said is null)
        {
            Assert.True(exit == ExitCodes.Success, _out.ToString() + _error.ToString());
            Assert.Equal(["example"], Directory.EnumerateFileSystemEntries(_paths.Paths.PluginsDirectory).Select(Path.GetFileName));
            Assert.False(Path.Exists(Path.Combine(_paths.Paths.Root, "d")));
        }
        else
        {
            Assert.Equal(ExitCodes.ApiError, exit);
            Assert.Contains(said, _error.ToString(), StringComparison.Ordinal);
            Assert.EndsWith("Nothing was changed.", _error.ToString().TrimEnd(), StringComparison.Ordinal);
            AssertNothingPlaced();
        }

        Assert.False(File.Exists(Path.Combine(_paths.Paths.Root, "outside.js")));
    }

    /// <summary>
    /// A file of the release that its server fails to send is refused in the words of every other refusal: what went wrong,
    /// then that nothing was changed.
    /// </summary>
    [Theory]
    [InlineData("jason-plugin-example.zip")]
    [InlineData("checksums.txt")]
    public async Task A_release_file_the_server_fails_to_send_says_nothing_was_changed(string file)
    {
        var (release, _) = Release();
        _failing = path => path.EndsWith("/" + file, StringComparison.Ordinal) ? HttpStatusCode.InternalServerError : null;

        var exit = await RunAsync(release, "plugin", "install", "example");

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Contains($"{file} answered 500.", _error.ToString(), StringComparison.Ordinal);
        Assert.EndsWith("Nothing was changed.", _error.ToString().TrimEnd(), StringComparison.Ordinal);
        AssertNothingPlaced();
    }

    [Fact]
    public async Task An_id_the_release_does_not_name_is_refused_with_the_ids_it_does_name()
    {
        var (release, package) = Release();
        release.WithPlugin("another", SemanticVersion.Parse("0.1.0"), package.Zip, package.Digest);

        var exit = await RunAsync(release, "plugin", "install", "missing");

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Contains("'missing'", _error.ToString(), StringComparison.Ordinal);
        Assert.Contains("another, example", _error.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(release.Requested, path => path.EndsWith(".zip", StringComparison.Ordinal) || path.EndsWith(ReleaseAssets.Checksums, StringComparison.Ordinal));
        Assert.False(Directory.Exists(_paths.Paths.PluginPackageDirectory("missing")));
    }

    [Fact]
    public async Task An_id_that_is_not_a_plugin_s_id_is_a_usage_error()
    {
        var (release, _) = Release();

        var exit = await RunAsync(release, "plugin", "install", "../example");

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Empty(release.Requested);
    }

    /// <summary>
    /// A package already in the plugins directory under that id, and not the release's: one edited or built on this
    /// machine, or another version. It is not overwritten unless <c>--replace</c> says so, and then it is swapped whole.
    /// </summary>
    [Fact]
    public async Task A_different_package_in_place_is_kept_unless_replace_says_otherwise()
    {
        var (release, package) = Release();
        _paths.WriteDescriptor(Live);
        var target = _paths.Paths.PluginPackageDirectory("example");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "plugin.yaml"), "id: example\nversion: 0.0.1\n");
        File.WriteAllText(Path.Combine(target, "stale.js"), "// edited here\n");
        var before = PackageDigest.Compute(target).Digest;

        var refused = await RunAsync(release, "plugin", "install", "example");

        Assert.Equal(ExitCodes.ApiError, refused);
        Assert.Contains("--replace", _error.ToString(), StringComparison.Ordinal);
        Assert.Contains("Nothing was changed.", _error.ToString(), StringComparison.Ordinal);
        Assert.Equal(before, PackageDigest.Compute(target).Digest);
        Assert.Empty(_posted);

        var replaced = await RunAsync(release, "plugin", "install", "example", "--replace");

        Assert.True(replaced == ExitCodes.Success, _out.ToString() + _error.ToString());
        Assert.Equal(package.Digest, PackageDigest.Compute(target).Digest);
        Assert.False(File.Exists(Path.Combine(target, "stale.js")));
        Assert.Equal(["example"], Directory.EnumerateFileSystemEntries(_paths.Paths.PluginsDirectory).Select(Path.GetFileName));
        using var answer = JsonDocument.Parse(_out.ToString());
        Assert.Equal("replaced", answer.RootElement.GetProperty("placed").GetString());
    }

    /// <summary>The release's own package already in place is left as it is, nothing is downloaded, and the runtime is still asked to load it.</summary>
    [Fact]
    public async Task The_release_s_own_package_in_place_is_left_as_it_is()
    {
        var (release, package) = Release();
        _paths.WriteDescriptor(Live);
        var target = _paths.Paths.PluginPackageDirectory("example");
        ZipFile.ExtractToDirectory(new MemoryStream(package.Zip), target);

        var exit = await RunAsync(release, "plugin", "install", "example");

        Assert.True(exit == ExitCodes.Success, _out.ToString() + _error.ToString());
        Assert.DoesNotContain(release.Requested, path => path.EndsWith(".zip", StringComparison.Ordinal));
        Assert.Equal([Operations.PluginReload], _posted);
        using var answer = JsonDocument.Parse(_out.ToString());
        Assert.Equal("already_in_place", answer.RootElement.GetProperty("placed").GetString());
    }

    /// <summary>
    /// No runtime answers: the package is placed all the same, the verb says the runtime loads it at its next start, and
    /// it starts nothing — whether a runtime runs is the person's to decide, and the files are this verb's.
    /// </summary>
    [Fact]
    public async Task Without_a_runtime_the_package_is_placed_and_loads_at_the_next_start()
    {
        var (release, package) = Release();

        var exit = await RunAsync(release, "plugin", "install", "example");

        Assert.True(exit == ExitCodes.Success, _out.ToString() + _error.ToString());
        Assert.Equal(package.Digest, PackageDigest.Compute(_paths.Paths.PluginPackageDirectory("example")).Digest);
        Assert.Empty(_posted);
        Assert.Contains("next start", _error.ToString(), StringComparison.Ordinal);
        using var answer = JsonDocument.Parse(_out.ToString());
        Assert.Equal("at_next_start", answer.RootElement.GetProperty("load").GetString());
    }

    /// <summary>
    /// The runtime answers and refuses the reload, keeping the plugins it had: the package stays placed, the verb exits 1,
    /// and its body says <c>rejected</c> and carries the runtime's refusal under <c>error</c>, so an agent reads why from
    /// stdout; stderr says it too.
    /// </summary>
    [Fact]
    public async Task A_reload_the_runtime_refuses_leaves_the_package_placed_and_carries_the_refusal()
    {
        var (release, package) = Release();
        _paths.WriteDescriptor(Live);
        _reload = () => new HttpResponseMessage(HttpStatusCode.Conflict) { Content = new StringContent(Rejected, Encoding.UTF8, "application/json") };

        var exit = await RunAsync(release, "plugin", "install", "example");

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Equal(package.Digest, PackageDigest.Compute(_paths.Paths.PluginPackageDirectory("example")).Digest);
        Assert.Contains("plugin_reload_rejected", _error.ToString(), StringComparison.Ordinal);
        using var answer = JsonDocument.Parse(_out.ToString());
        Assert.Equal("rejected", answer.RootElement.GetProperty("load").GetString());
        var error = answer.RootElement.GetProperty("error");
        Assert.Equal("plugin_reload_rejected", error.GetProperty("code").GetString());
        Assert.StartsWith("The plugin reload was rejected", error.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.False(error.GetProperty("retryable").GetBoolean());
    }

    /// <summary>
    /// A runtime answers at the descriptor's address and refuses the descriptor's token: the package is placed, it loads at
    /// the runtime's next start, and the verb exits 0 saying the token was refused, not that no runtime answered.
    /// </summary>
    [Fact]
    public async Task A_runtime_that_refuses_the_token_leaves_the_package_to_load_at_its_next_start()
    {
        var (release, package) = Release();
        _paths.WriteDescriptor(Live);
        _reload = () => new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new ByteArrayContent([]) };

        var exit = await RunAsync(release, "plugin", "install", "example");

        Assert.True(exit == ExitCodes.Success, _out.ToString() + _error.ToString());
        Assert.Equal(package.Digest, PackageDigest.Compute(_paths.Paths.PluginPackageDirectory("example")).Digest);
        Assert.Contains("refused the descriptor's capability token", _error.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("No runtime answered", _error.ToString(), StringComparison.Ordinal);
        using var answer = JsonDocument.Parse(_out.ToString());
        Assert.Equal("at_next_start", answer.RootElement.GetProperty("load").GetString());
        Assert.False(answer.RootElement.TryGetProperty("error", out _));
    }

    /// <summary>The help is the verb's contract: it says what a refused reload leaves and how the verb ends. Read with the line breaks made spaces.</summary>
    [Fact]
    public async Task The_help_says_a_refused_reload_leaves_the_package_placed_and_exits_1()
    {
        var output = new StringWriter();

        await CliApp.RunAsync(["plugin", "install", "--help"], new CliEnvironment(output, new StringWriter(), _paths.Paths), Ct);

        Assert.Contains(
            "A runtime that answers and refuses the reload leaves the package placed, and the verb exits 1 with load: rejected in its body and the runtime's refusal under error.",
            System.Text.RegularExpressions.Regex.Replace(output.ToString(), @"\s+", " "),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_development_build_asks_nothing_of_the_default_feed()
    {
        var (release, _) = Release();

        var exit = await RunAsync(release, Version + "-dev+1a2b3c4", ["plugin", "install", "example"]);

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Contains("not a release", _error.ToString(), StringComparison.Ordinal);
        Assert.Empty(release.Requested);
    }

    /// <summary>A release whose manifest names another version is not this build's: its packages are refused, whatever they are.</summary>
    [Fact]
    public async Task A_release_of_another_version_is_refused()
    {
        var (release, _) = Release();
        release.ServedFromADirectory();

        var exit = await RunAsync(release, "0.20.0", ["plugin", "install", "example", "--feed", release.Feed.ToString()]);

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Contains("0.20.0", _error.ToString(), StringComparison.Ordinal);
        Assert.Contains(Version, _error.ToString(), StringComparison.Ordinal);
        AssertNothingPlaced();
    }

    /// <summary>
    /// The installer as a caller that knows what it is replacing uses it: the package expected in place, by its digest, is
    /// moved to the directory the caller keeps it in, and the release's goes in as <c>plugins/&lt;id&gt;</c>.
    /// </summary>
    [Fact]
    public async Task The_package_expected_in_place_is_swapped_for_the_release_s_and_kept_where_asked()
    {
        var (release, package) = Release();
        var before = InstallAnother();
        var keep = KeepDirectory();

        var placed = await PlaceAsync(release, PluginInPlace.Digest(before), keep);

        var target = _paths.Paths.PluginPackageDirectory("example");
        Assert.Equal(PluginPlacement.Replaced, placed.Placement);
        Assert.Equal(keep, placed.Kept);
        Assert.Equal(package.Digest, PackageDigest.Compute(target).Digest);
        Assert.Equal(before, PackageDigest.Compute(keep).Digest);
        Assert.Equal(["example"], Directory.EnumerateFileSystemEntries(_paths.Paths.PluginsDirectory).Select(Path.GetFileName));
    }

    /// <summary>
    /// The compare and the swap are one act: what is in place is not what the caller expected — another package, one where
    /// none was expected, none where one was — so nothing is fetched, nothing moves and nothing is kept.
    /// </summary>
    [Theory]
    [InlineData("another digest expected")]
    [InlineData("nothing expected")]
    [InlineData("a package expected where there is none")]
    public async Task A_package_in_place_that_is_not_the_one_expected_is_left_and_nothing_is_fetched(string mismatch)
    {
        var (release, _) = Release();
        var target = _paths.Paths.PluginPackageDirectory("example");
        var before = mismatch == "a package expected where there is none" ? null : InstallAnother();
        var expected = mismatch switch
        {
            "another digest expected" => PluginInPlace.Digest("sha256:" + new string('0', 64)),
            "nothing expected" => PluginInPlace.Nothing,
            _ => PluginInPlace.Digest("sha256:" + new string('0', 64)),
        };
        var keep = KeepDirectory();

        var refused = await Assert.ThrowsAsync<PluginInstallException>(() => PlaceAsync(release, expected, keep));

        Assert.True(refused.InPlaceDiffers);
        Assert.Contains("Nothing was changed.", refused.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(release.Requested, path => path.EndsWith(".zip", StringComparison.Ordinal));
        Assert.False(Path.Exists(keep));
        if (before is null)
        {
            Assert.False(Path.Exists(target));
        }
        else
        {
            Assert.Equal(before, PackageDigest.Compute(target).Digest);
            Assert.Equal(["example"], Directory.EnumerateFileSystemEntries(_paths.Paths.PluginsDirectory).Select(Path.GetFileName));
        }
    }

    /// <summary>
    /// A second call after the first went through — a step run again after an interruption — finds the release's package in
    /// place, fetches no package, moves nothing, leaves the copy the first call kept as it was, and says so.
    /// </summary>
    [Fact]
    public async Task A_second_call_finds_the_release_s_package_in_place_and_does_nothing()
    {
        var (release, package) = Release();
        var before = InstallAnother();
        var keep = KeepDirectory();
        await PlaceAsync(release, PluginInPlace.Digest(before), keep);
        release.Requested.Clear();

        var again = await PlaceAsync(release, PluginInPlace.Digest(before), keep);

        Assert.Equal(PluginPlacement.AlreadyInPlace, again.Placement);
        Assert.Null(again.Kept);
        Assert.Equal(package.Digest, again.Digest);
        Assert.DoesNotContain(release.Requested, path => path.EndsWith(".zip", StringComparison.Ordinal));
        Assert.Equal(package.Digest, PackageDigest.Compute(_paths.Paths.PluginPackageDirectory("example")).Digest);
        Assert.Equal(before, PackageDigest.Compute(keep).Digest);
    }

    /// <summary>A directory to keep the old package in that already exists would be written over, so it is refused before anything moves.</summary>
    [Fact]
    public async Task A_keep_directory_that_already_exists_is_refused_before_anything_moves()
    {
        var (release, _) = Release();
        var before = InstallAnother();
        var keep = KeepDirectory();
        Directory.CreateDirectory(keep);

        var refused = await Assert.ThrowsAsync<PluginInstallException>(() => PlaceAsync(release, PluginInPlace.Digest(before), keep));

        Assert.False(refused.InPlaceDiffers);
        Assert.Contains(keep, refused.Message, StringComparison.Ordinal);
        Assert.Equal(before, PackageDigest.Compute(_paths.Paths.PluginPackageDirectory("example")).Digest);
        Assert.Empty(Directory.EnumerateFileSystemEntries(keep));
        Assert.DoesNotContain(release.Requested, path => path.EndsWith(".zip", StringComparison.Ordinal));
    }

    /// <summary>
    /// A file in the package in place that another program holds open for the whole wait, as a scanner or an editor holds
    /// one, stops <c>--replace</c>: Windows refuses to rename a directory with a held file in it, the move is tried for the
    /// installer's wait and then given up, and the command exits 1 saying the file is held and that nothing was changed,
    /// with the package in place as it was. The wait is the installer's own, so this row takes it.
    /// </summary>
    [Fact]
    public async Task A_file_held_in_the_package_in_place_stops_replace_and_changes_nothing()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Elsewhere an open file does not stop a rename, so there is nothing to wait out.");

        var (release, _) = Release();
        _paths.WriteDescriptor(Live);
        var before = InstallAnother();
        var target = _paths.Paths.PluginPackageDirectory("example");

        int exit;
        using (new FileStream(Path.Combine(target, "main.js"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            exit = await RunAsync(release, "plugin", "install", "example", "--replace");
        }

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Contains("or a file in it is held open by another program", _error.ToString(), StringComparison.Ordinal);
        Assert.EndsWith("Nothing was changed.", _error.ToString().TrimEnd(), StringComparison.Ordinal);
        Assert.Equal(string.Empty, _out.ToString());
        Assert.Empty(_posted);
        Assert.Equal(before, PackageDigest.Compute(target).Digest);
        Assert.Equal(["example"], Directory.EnumerateFileSystemEntries(_paths.Paths.PluginsDirectory).Select(Path.GetFileName));
    }

    /// <summary>
    /// The same hold through the installer, under a short wait of the test's own, by a reader that lets others read, as a
    /// scanner does: the package in place is read and is the one expected, but Windows still refuses to rename its
    /// directory. The rename is tried for the whole of the wait before it is given up, so a holder that lets go inside it
    /// is waited out, and the package stays in place, with nothing kept.
    /// </summary>
    [Fact]
    public async Task A_file_held_for_the_whole_wait_is_tried_for_it_and_the_package_in_place_stays()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Elsewhere an open file does not stop a rename, so there is nothing to wait out.");

        var (release, _) = Release();
        var before = InstallAnother();
        var target = _paths.Paths.PluginPackageDirectory("example");
        var keep = KeepDirectory();
        var rule = PluginInstaller.PackageMoves with { Wait = TimeSpan.FromMilliseconds(300), Interval = TimeSpan.FromMilliseconds(50) };
        var started = TimeProvider.System.GetTimestamp();

        PluginInstallException refused;
        using (new FileStream(Path.Combine(target, "main.js"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            refused = await Assert.ThrowsAsync<PluginInstallException>(() => PlaceAsync(release, PluginInPlace.Digest(before), keep, rule));
        }

        Assert.True(TimeProvider.System.GetElapsedTime(started) >= rule.Wait, "the move gave up before the wait was over.");
        Assert.False(refused.InPlaceDiffers);
        Assert.Contains("or a file in it is held open by another program", refused.Message, StringComparison.Ordinal);
        Assert.EndsWith("Nothing was changed.", refused.Message, StringComparison.Ordinal);
        Assert.Equal(before, PackageDigest.Compute(target).Digest);
        Assert.False(Path.Exists(keep));
        Assert.Equal(["example"], Directory.EnumerateFileSystemEntries(_paths.Paths.PluginsDirectory).Select(Path.GetFileName));
    }

    /// <summary>
    /// A package in place that this account is refused, as one placed by an elevated process is, is not a package held open:
    /// Windows answers the rename with the same access-denied, and the directory itself shows that it is a permission. The move
    /// is given up at once, without a pause of the installer's wait, saying that waiting would not help and that nothing
    /// was changed, with the package in place as it was.
    /// </summary>
    [Fact]
    public async Task A_package_in_place_this_account_is_refused_stops_replace_at_once_and_changes_nothing()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "A deny on the directory is Windows' own way to refuse its rename.");

        var (release, _) = Release();
        var before = InstallAnother();
        var target = _paths.Paths.PluginPackageDirectory("example");
        var keep = KeepDirectory();
        var pauses = new List<TimeSpan>();
        var clock = new PausedClock();

        PluginInstallException refused;
        var undo = OperatingSystem.IsWindows() ? RefusedPackage.Deny(target) : throw new PlatformNotSupportedException();
        try
        {
            refused = await Assert.ThrowsAsync<PluginInstallException>(
                () => PlaceAsync(release, PluginInPlace.Digest(before), keep, PluginInstaller.PackageMoves, (delay, _) =>
                    {
                        pauses.Add(delay);
                        clock.Pass(delay);
                    },
                    new IdentityFacts(_paths.Paths.Root, "someone", "a-machine", null, null),
                    clock));
        }
        finally
        {
            undo();
        }

        Assert.Empty(pauses);
        Assert.Equal($"'~{target[_paths.Paths.Root.Length..]}' could not be moved: {AccessRefused.Clause}. Nothing was changed.", refused.Message);
        Assert.DoesNotContain("held open", refused.Message, StringComparison.Ordinal);
        Assert.Equal(before, PackageDigest.Compute(target).Digest);
        Assert.False(Path.Exists(keep));
        Assert.Equal(["example"], Directory.EnumerateFileSystemEntries(_paths.Paths.PluginsDirectory).Select(Path.GetFileName));
    }

    /// <summary>
    /// The release's package cannot be moved in once the package in place has been moved aside: the one in place is moved
    /// back, so <c>--replace</c> exits 1 having changed nothing, and nothing is left beside the packages.
    /// </summary>
    [Fact]
    public async Task A_package_that_cannot_be_moved_in_puts_the_one_in_place_back_and_changes_nothing()
    {
        var (release, _) = Release();
        var before = InstallAnother();
        var target = _paths.Paths.PluginPackageDirectory("example");

        int exit;
        PluginInstaller.BeforeMove.Value = (from, _) => RefuseWhen(Path.GetFileName(from).StartsWith(".staging-", StringComparison.Ordinal));
        try
        {
            exit = await RunAsync(release, "plugin", "install", "example", "--replace");
        }
        finally
        {
            PluginInstaller.BeforeMove.Value = null;
        }

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.EndsWith("Nothing was changed.", _error.ToString().TrimEnd(), StringComparison.Ordinal);
        Assert.Equal(before, PackageDigest.Compute(target).Digest);
        Assert.Equal(["example"], Directory.EnumerateFileSystemEntries(_paths.Paths.PluginsDirectory).Select(Path.GetFileName));
    }

    /// <summary>
    /// Neither the release's package nor the one moved aside can be moved into <c>plugins/&lt;id&gt;</c>: <c>--replace</c>
    /// exits 1 saying the release's was not placed and naming the directory the package that was there is now in, so a
    /// person can move it back; it does not say that nothing was changed, because something was.
    /// </summary>
    [Fact]
    public async Task A_package_that_cannot_be_moved_back_is_named_where_it_now_is()
    {
        var (release, _) = Release();
        var before = InstallAnother();
        var target = _paths.Paths.PluginPackageDirectory("example");

        int exit;
        PluginInstaller.BeforeMove.Value = (_, to) => RefuseWhen(to == target && !Path.Exists(target));
        try
        {
            exit = await RunAsync(release, "plugin", "install", "example", "--replace");
        }
        finally
        {
            PluginInstaller.BeforeMove.Value = null;
        }

        var aside = Assert.Single(Directory.EnumerateDirectories(_paths.Paths.PluginsDirectory, ".old-example-*"));
        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Contains(aside, _error.ToString(), StringComparison.Ordinal);
        Assert.Contains("was not placed", _error.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("Nothing was changed.", _error.ToString(), StringComparison.Ordinal);
        Assert.False(Path.Exists(target));
        Assert.Equal(before, PackageDigest.Compute(aside).Digest);
    }

    /// <summary>The same, for a caller that keeps the package it replaces: the words name its keep directory, which holds it.</summary>
    [Fact]
    public async Task A_package_that_cannot_be_moved_back_is_named_in_the_keep_directory()
    {
        var (release, _) = Release();
        var before = InstallAnother();
        var target = _paths.Paths.PluginPackageDirectory("example");
        var keep = KeepDirectory();

        PluginInstallException refused;
        PluginInstaller.BeforeMove.Value = (_, to) => RefuseWhen(to == target && !Path.Exists(target));
        try
        {
            refused = await Assert.ThrowsAsync<PluginInstallException>(() => PlaceAsync(release, PluginInPlace.Digest(before), keep));
        }
        finally
        {
            PluginInstaller.BeforeMove.Value = null;
        }

        Assert.Contains(keep, refused.Message, StringComparison.Ordinal);
        Assert.Contains("was not placed", refused.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Nothing was changed.", refused.Message, StringComparison.Ordinal);
        Assert.False(refused.InPlaceDiffers);
        Assert.Equal(before, PackageDigest.Compute(keep).Digest);
    }

    /// <summary>A rename refused the way a disk refuses one, which no wait changes.</summary>
    private static void RefuseWhen(bool refused)
    {
        if (refused)
        {
            throw new IOException("The test refuses this rename.");
        }
    }

    /// <summary>A package of <c>example</c> that is not the release's, as an earlier version would be; returns its digest.</summary>
    private string InstallAnother()
    {
        var target = _paths.Paths.PluginPackageDirectory("example");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "plugin.yaml"), "id: example\nversion: 1.1.0\n");
        File.WriteAllText(Path.Combine(target, "main.js"), "export default { earlier: true };\n");
        return PackageDigest.Compute(target).Digest;
    }

    /// <summary>Where a caller keeps the package it replaces: in the data directory, beside the plugins directory, not yet made.</summary>
    private string KeepDirectory() =>
        Path.Combine(Path.GetDirectoryName(_paths.Paths.PluginsDirectory)!, "kept", "example");

    /// <summary>The installer called directly, as a caller with the release's manifest in hand calls it.</summary>
    private async Task<PluginPlaced> PlaceAsync(
        FakeRelease release,
        PluginInPlace expected,
        string? keep,
        HeldFileRule? rule = null,
        Action<TimeSpan, CancellationToken>? pause = null,
        IdentityFacts? identity = null,
        TimeProvider? clock = null)
    {
        var env = new CliEnvironment(_out, _error, _paths.Paths, HttpHandler: release, BuildVersion: Version, Identity: identity);
        var pinned = UpdateFeed.PinnedFor(release.Feed, SemanticVersion.Parse(Version));
        UpdatePluginPackage package;
        using (var client = new HttpClient(release, disposeHandler: false))
        {
            package = (await new UpdateFeed(client).ReadAsync(pinned, Ct)).Plugins["example"];
        }

        return rule is null
            ? await PluginInstaller.PlaceAsync(env, pinned, "example", package, expected, keep, Ct)
            : await PluginInstaller.PlaceAsync(env, pinned, "example", package, expected, keep, rule, clock ?? TimeProvider.System, pause ?? HeldFileRetry.Sleep, Ct);
    }

    private void AssertNothingPlaced() =>
        Assert.False(
            Directory.Exists(_paths.Paths.PluginsDirectory) && Directory.EnumerateFileSystemEntries(_paths.Paths.PluginsDirectory).Any(),
            $"The plugins directory holds {string.Join(", ", Directory.Exists(_paths.Paths.PluginsDirectory) ? Directory.EnumerateFileSystemEntries(_paths.Paths.PluginsDirectory).Select(Path.GetFileName) : [])}.");

    /// <summary>A package a test made, as its archive and the content digest of the tree it unpacks to.</summary>
    private sealed record Package(byte[] Zip, string Digest);

    /// <summary>
    /// A release of <see cref="Version"/> publishing the package <c>example</c> 1.2.0, with what <paramref name="checksums"/>
    /// says of it, and the content digest the manifest gives when <paramref name="contentDigest"/> names one.
    /// </summary>
    private static (FakeRelease Release, Package Package) Release(string checksums = "its own", string? contentDigest = null)
    {
        (string Name, string Text)[] files = [("plugin.yaml", "id: example\nversion: 1.2.0\n"), ("main.js", "export default {};\n"), ("modules/send.js", "// send\n")];
        var digest = PackageDigest.Compute(files.Select(file => new PackageFile(file.Name, Encoding.UTF8.GetByteCount(file.Text), () => new MemoryStream(Encoding.UTF8.GetBytes(file.Text))))).Digest;

        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, text) in files)
            {
                using var stream = zip.CreateEntry(name).Open();
                stream.Write(Encoding.UTF8.GetBytes(text));
            }
        }

        var bytes = memory.ToArray();
        var release = new FakeRelease(SemanticVersion.Parse(Version));
        var checksum = checksums == "another digest" ? Convert.ToHexStringLower(SHA256.HashData("another package"u8)) : null;
        release.WithPlugin("example", SemanticVersion.Parse("1.2.0"), bytes, contentDigest ?? digest, checksum);
        if (checksums == "no line")
        {
            release.ChecksumsSay($"{release.Digest}  {release.Asset}\n");
        }

        return (release, new Package(bytes, digest));
    }

    /// <summary>The files of a well-formed package of <c>example</c> 1.2.0, the tree the manifest names by its digest.</summary>
    private static readonly FakeRelease.Entry[] PackageFiles = [new("plugin.yaml", "id: example\nversion: 1.2.0\n"), new("main.js", "export default {};\n")];

    /// <summary>What the installer says of an archive past a cap, before it unpacks anything: both caps at once.</summary>
    private static readonly string Caps = string.Create(CultureInfo.InvariantCulture, $"holds at most {PackageDigest.MaxFiles} files and {PackageDigest.MaxBytes} bytes");

    /// <summary>An archive of the shape a test names, and what the refusal of it says, or null when it is a package.</summary>
    private static (FakeRelease.Entry[] Entries, string? Said) Shaped(string shape) => shape switch
    {
        "a backslash" => ([.. PackageFiles, new("modules\\other.js", "// other\n")], @"'modules\other.js'"),
        "a NUL" => ([.. PackageFiles, new("a\0b.js", "// nul\n")], @"'a\u0000b.js'"),
        "a trailing dot" => ([.. PackageFiles, new("notes.", "// dot\n")], "'notes.'"),
        "a trailing space" => ([.. PackageFiles, new("notes ", "// space\n")], "'notes '"),
        "a name twice in another case" => ([.. PackageFiles, new("MAIN.JS", "// twice\n")], "'MAIN.JS' appears twice"),
        "more files than a package holds" => (
            [.. PackageFiles, .. Enumerable.Range(0, PackageDigest.MaxFiles).Select(i => new FakeRelease.Entry($"files/{i}.js", "//\n"))],
            Caps),
        "more bytes than a package holds" => ([.. PackageFiles, new("large.bin", new byte[PackageDigest.MaxBytes + 1])], Caps),
        "no plugin.yaml at the root" => ([PackageFiles[1], new("modules/plugin.yaml", "id: example\n")], "holds no plugin.yaml at its root"),
        "a directory entry that points outside" => ([.. PackageFiles, new("../d", "", FakeRelease.EntryKind.Directory)], null),
        _ => ([.. PackageFiles, new(shape, "// not of the package\n")], $"'{shape}'"),
    };

    /// <summary>
    /// A release of <see cref="Version"/> publishing as <c>example</c> 1.2.0 an archive of exactly <paramref name="entries"/>,
    /// which its manifest names by the digest of <see cref="PackageFiles"/>.
    /// </summary>
    private static FakeRelease ReleaseOf(IEnumerable<FakeRelease.Entry> entries)
    {
        var digest = PackageDigest.Compute(PackageFiles.Select(file => new PackageFile(file.Name, file.Content.Length, () => new MemoryStream(file.Content)))).Digest;
        var release = new FakeRelease(SemanticVersion.Parse(Version));
        release.WithPlugin("example", SemanticVersion.Parse("1.2.0"), FakeRelease.ArchiveOf("jason-plugin-example.zip", entries), digest);
        return release;
    }

    private Task<int> RunAsync(FakeRelease release, params string[] args) => RunAsync(release, Version, args);

    private Task<int> RunAsync(FakeRelease release, string version, string[] args)
    {
        _out.GetStringBuilder().Clear();
        _error.GetStringBuilder().Clear();
        var handler = new FakeHandler(request =>
        {
            if (request.RequestUri!.Port != 5000)
            {
                return _failing?.Invoke(request.RequestUri.AbsolutePath) is { } failure
                    ? new HttpResponseMessage(failure) { Content = new ByteArrayContent([]) }
                    : release.Answer(request);
            }

            _posted.Add(request.RequestUri.AbsolutePath["/v1/".Length..]);
            _bodies.Add(request.Content!.ReadAsStringAsync(CancellationToken.None).GetAwaiter().GetResult());
            const string Registry = """{"snapshot":{"id":"snp_A","loaded_at":"2026-10-26T00:00:00Z","source":"reload","plugin_count":1},"routing_snapshot_id":"rsn_A","plugins":[],"last_reload":null,"activated":true}""";
            return _reload?.Invoke() ?? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Registry, Encoding.UTF8, "application/json") };
        });

        return CliApp.RunAsync(args, new CliEnvironment(_out, _error, _paths.Paths, HttpHandler: handler, BuildVersion: version, AccountName: _account), Ct);
    }

    public void Dispose()
    {
        _paths.Dispose();
        _out.Dispose();
        _error.Dispose();
    }
}
