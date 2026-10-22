using System.Net;
using System.Text.Json;
using Jason.Cli.Tests.Update;
using Jason.Cli.Ui;
using Jason.Contracts.Ui;
using Jason.Contracts.Update;
using Jason.Runtime.Tests;

namespace Jason.Cli.Tests.Ui;

/// <summary>
/// <c>jason ui install</c>: this build's release's UI, fetched beside the release's checksums.txt, held to the line
/// there, to this build's version and to its API, and placed as the default build whole or not at all. Every
/// environment names its version, because one CI leg builds these tests with a release's.
/// </summary>
public sealed class UiInstallCommandTests : IDisposable
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly TempPaths _paths = new();
    private readonly StringWriter _out = new();
    private readonly StringWriter _error = new();

    [Fact]
    public async Task Install_places_this_release_s_build()
    {
        var release = new FakeRelease(SemanticVersion.Parse("0.13.0"));

        var exit = await RunAsync(release, "0.13.0+1a2b3c4", "ui", "install");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal("0.13.0", UiManifest.Read(_paths.Paths.UiDefaultDirectory)!.Version);
        Assert.Contains("/releases/download/v0.13.0/checksums.txt", release.Requested);
        Assert.Contains("/releases/download/v0.13.0/jason-ui.zip", release.Requested);
        using var answer = JsonDocument.Parse(_out.ToString());
        Assert.Equal("0.13.0", answer.RootElement.GetProperty("version").GetString());
        Assert.Equal("v1", answer.RootElement.GetProperty("api_version").GetString());
        Assert.Equal(_paths.Paths.UiDefaultDirectory, answer.RootElement.GetProperty("directory").GetString());
        Assert.False(Directory.EnumerateFiles(_paths.Paths.UiDirectory).Any(), "the download was left behind");
    }

    [Theory]
    // Critical: the UI's checksum.
    [InlineData("digest", "does not match", Traits = ["Category", "Critical", "CriticalFamily", nameof(Family.I1)])]
    [InlineData("no line", "lists no jason-ui.zip")]
    [InlineData("two lines", "more than once")]
    [InlineData("version", "0.12.1")]
    [InlineData("api", "v2")]
    [InlineData("too large", "larger than")]
    [InlineData("too large, said small", "larger than")]
    public async Task A_build_that_does_not_check_out_is_refused_and_the_old_one_stays(string wrong, string said)
    {
        UiAssetInstaller.Install(Written(UiZip.Build("0.12.0")), _paths.Paths, new UiExpected(SemanticVersion.Parse("0.12.0"), "v1"), TimeProvider.System);
        var release = new FakeRelease(SemanticVersion.Parse("0.13.0"));
        var good = UiZip.Build("0.13.0");
        var line = $"{Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(good))}  jason-ui.zip\n";
        _ = wrong switch
        {
            "digest" => release.WithUi(good, digest: new string('0', 64)),
            "no line" => release.ChecksumsSay($"{release.Digest}  {release.Asset}\n"),
            "two lines" => release.ChecksumsSay(line + line),
            "version" => release.WithUi(UiZip.Build("0.12.1")),
            "api" => release.WithUi(UiZip.Build("0.13.0", "v2")),
            "too large" => release.WithUi(new byte[(int)UiInstaller.MaxDownloadBytes + 1]),
            _ => release.WithUi(new byte[(int)UiInstaller.MaxDownloadBytes + 1]),
        };
        if (wrong == "too large, said small")
        {
            release.UiLengthSays = 1024;
        }

        var exit = await RunAsync(release, "0.13.0", "ui", "install");

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Contains(said, _error.ToString(), StringComparison.Ordinal);
        Assert.Equal("0.12.0", UiManifest.Read(_paths.Paths.UiDefaultDirectory)!.Version);
        Assert.Equal(["default"], Directory.EnumerateFileSystemEntries(_paths.Paths.UiDirectory).Select(Path.GetFileName));
    }

    [Fact]
    public async Task A_development_build_asks_nothing_of_the_default_feed()
    {
        var release = new FakeRelease(SemanticVersion.Parse("0.13.0"));

        var exit = await RunAsync(release, "0.13.0-dev+1a2b3c4", "ui", "install");

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Contains("not a release", _error.ToString(), StringComparison.Ordinal);
        Assert.Contains(_paths.Paths.UiCustomDirectory, _error.ToString(), StringComparison.Ordinal);
        Assert.Empty(release.Requested);
    }

    [Fact]
    public async Task A_development_build_takes_a_feed_it_is_given()
    {
        var release = new FakeRelease(SemanticVersion.Parse("0.13.0-dev")).ServedFromADirectory();

        var exit = await RunAsync(release, "0.13.0-dev+1a2b3c4", "ui", "install", "--feed", release.Feed.ToString());

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal("0.13.0-dev", UiManifest.Read(_paths.Paths.UiDefaultDirectory)!.Version);
    }

    /// <summary>
    /// A feed served from a plain directory, as CI and a mirror serve one, keeps its files beside its manifest. A
    /// caller that composed a /releases/download/ path for it would ask for one that was never going to exist.
    /// </summary>
    [Fact]
    public async Task A_directory_feed_is_read_where_it_stands()
    {
        var release = new FakeRelease(SemanticVersion.Parse("0.13.0")).ServedFromADirectory();

        var exit = await RunAsync(release, "0.13.0", "ui", "install", "--feed", release.Feed.ToString());

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(["/checksums.txt", "/jason-ui.zip"], release.Requested);
    }

    [Fact]
    public async Task A_version_no_release_published_says_so()
    {
        var release = new FakeRelease(SemanticVersion.Parse("0.13.0")) { FailWith = HttpStatusCode.NotFound };

        var exit = await RunAsync(release, "0.13.0", "ui", "install");

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Contains("No UI is published for 0.13.0", _error.ToString(), StringComparison.Ordinal);
        Assert.False(Directory.Exists(_paths.Paths.UiDefaultDirectory));
    }

    private Task<int> RunAsync(FakeRelease release, string version, params string[] args) =>
        CliApp.RunAsync(args, new CliEnvironment(_out, _error, _paths.Paths, HttpHandler: release, BuildVersion: version), Ct);

    private string Written(byte[] zip)
    {
        var file = Path.Combine(_paths.Paths.Root, $"jason-ui-{Guid.NewGuid():N}.zip");
        File.WriteAllBytes(file, zip);
        return file;
    }

    public void Dispose()
    {
        _paths.Dispose();
        _out.Dispose();
        _error.Dispose();
    }
}
