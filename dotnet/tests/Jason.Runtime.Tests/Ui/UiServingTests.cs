using System.Net;
using Jason.Contracts.Api;
using Jason.Contracts.Ui;

namespace Jason.Runtime.Tests.Ui;

/// <summary>
/// The runtime serves the UI's files and nothing else: from custom when it is a build, else from default, with the
/// headers that make the page safe to run, and a 404 that says what is missing when nothing is installed.
/// </summary>
public class UiServingTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public const string Csp = "default-src 'self'; frame-ancestors 'none'; base-uri 'none'";

    [Fact]
    public async Task With_nothing_installed_the_page_is_404_ui_not_installed_and_system_info_says_so()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        var (status, body, _) = await GetAsync(api, "/ui/");
        Assert.Equal(HttpStatusCode.NotFound, status);
        Assert.Contains("\"ui_not_installed\"", body, StringComparison.Ordinal);
        Assert.Contains(api.Paths.UiDefaultDirectory.Replace("\\", "\\\\", StringComparison.Ordinal), body, StringComparison.Ordinal);

        var info = await api.PostOkAsync<SystemInfoResponse>(Operations.SystemInfo, null, Ct);
        Assert.Null(info.Ui);
    }

    [Fact]
    public async Task The_default_build_is_served_with_the_headers_and_reported()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct, prepare: paths =>
        {
            File.WriteAllText(paths.UserSettingsFile, RuntimeApiFixture.DispatcherOff);
            UiBuild.Write(paths.UiDefaultDirectory, version: "0.10.0");
        });

        var (status, body, headers) = await GetAsync(api, "/ui/");
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains("<title>Jason · default</title>", body, StringComparison.Ordinal);
        Assert.Equal("text/html; charset=utf-8", headers["Content-Type"]);
        Assert.Equal(Csp, headers["Content-Security-Policy"]);
        Assert.Equal("nosniff", headers["X-Content-Type-Options"]);
        Assert.Equal("no-referrer", headers["Referrer-Policy"]);
        Assert.Equal("no-store", headers["Cache-Control"]);

        var (assetStatus, _, assetHeaders) = await GetAsync(api, "/ui/assets/app-abc123.js");
        Assert.Equal(HttpStatusCode.OK, assetStatus);
        Assert.Equal("text/javascript; charset=utf-8", assetHeaders["Content-Type"]);
        Assert.Equal("public, max-age=31536000, immutable", assetHeaders["Cache-Control"]);
        Assert.Equal(Csp, assetHeaders["Content-Security-Policy"]);

        var (missingStatus, _, missingHeaders) = await GetAsync(api, "/ui/assets/missing-abc123.js");
        Assert.Equal(HttpStatusCode.NotFound, missingStatus);
        Assert.Equal("no-store", missingHeaders["Cache-Control"]);

        var info = await api.PostOkAsync<SystemInfoResponse>(Operations.SystemInfo, null, Ct);
        Assert.NotNull(info.Ui);
        Assert.Equal("default", info.Ui.Source);
        Assert.Equal(api.Paths.UiDefaultDirectory, info.Ui.Directory);
        Assert.Equal("0.10.0", info.Ui.Version);
        Assert.Equal("v1", info.Ui.ApiVersion);
        Assert.True(info.Ui.ApiVersionMatches);
        Assert.StartsWith("sha256:", info.Ui.Digest, StringComparison.Ordinal);
        Assert.Null(info.Ui.Problem);
    }

    [Fact]
    public async Task A_custom_build_beats_the_default_and_is_told_apart_by_source_and_digest()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct, prepare: paths =>
        {
            File.WriteAllText(paths.UserSettingsFile, RuntimeApiFixture.DispatcherOff);
            UiBuild.Write(paths.UiDefaultDirectory, marker: "default");
            UiBuild.Write(paths.UiCustomDirectory, version: "mine", marker: "custom");
        });

        var (_, body, _) = await GetAsync(api, "/ui/");
        Assert.Contains("<title>Jason · custom</title>", body, StringComparison.Ordinal);

        var info = await api.PostOkAsync<SystemInfoResponse>(Operations.SystemInfo, null, Ct);
        Assert.Equal("custom", info.Ui!.Source);
        Assert.Equal("mine", info.Ui.Version);
    }

    /// <summary>
    /// A custom directory that holds something other than a build — an index without a manifest, a manifest that is not
    /// one, or files without an index — is skipped, and what is wrong with it is said where the person looks: on the
    /// served default in system.info, or in the 404 and the ui.open refusal when there is no default to serve.
    /// </summary>
    [Theory]
    [InlineData(true, "no-manifest")]
    [InlineData(true, "bad-manifest")]
    [InlineData(true, "no-index")]
    [InlineData(false, "no-manifest")]
    [InlineData(false, "bad-manifest")]
    [InlineData(false, "no-index")]
    public async Task A_custom_directory_that_is_not_a_build_is_skipped_and_named_as_the_problem(bool withDefault, string shape)
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct, prepare: paths =>
        {
            File.WriteAllText(paths.UserSettingsFile, RuntimeApiFixture.DispatcherOff);
            if (withDefault)
            {
                UiBuild.Write(paths.UiDefaultDirectory);
            }

            Directory.CreateDirectory(paths.UiCustomDirectory);
            if (shape == "no-index")
            {
                File.WriteAllText(Path.Combine(paths.UiCustomDirectory, "app.js"), "export {};");
                return;
            }

            File.WriteAllText(Path.Combine(paths.UiCustomDirectory, UiAssetLayout.IndexFile), "<html></html>");
            if (shape == "bad-manifest")
            {
                File.WriteAllText(Path.Combine(paths.UiCustomDirectory, UiAssetLayout.ManifestFile), "{not json");
            }
        });
        var missing = shape == "no-index" ? UiAssetLayout.IndexFile : UiAssetLayout.ManifestFile;

        var (status, body, _) = await GetAsync(api, "/ui/");
        var info = await api.PostOkAsync<SystemInfoResponse>(Operations.SystemInfo, null, Ct);
        if (withDefault)
        {
            Assert.Equal(HttpStatusCode.OK, status);
            Assert.Contains("<title>Jason · default</title>", body, StringComparison.Ordinal);
            Assert.Equal("default", info.Ui!.Source);
            Assert.Contains(missing, info.Ui.Problem, StringComparison.Ordinal);
            Assert.Contains(api.Paths.UiCustomDirectory, info.Ui.Problem, StringComparison.Ordinal);
            return;
        }

        Assert.Equal(HttpStatusCode.NotFound, status);
        Assert.Contains("\"ui_not_installed\"", body, StringComparison.Ordinal);
        Assert.Contains(missing, body, StringComparison.Ordinal);
        Assert.Contains(api.Paths.UiCustomDirectory.Replace("\\", "\\\\", StringComparison.Ordinal), body, StringComparison.Ordinal);
        Assert.Null(info.Ui);

        var (refusal, why) = await api.PostAsync(Operations.UiOpen, null, Ct);
        Assert.Equal(HttpStatusCode.NotFound, refusal);
        Assert.Contains("\"ui_not_installed\"", why, StringComparison.Ordinal);
        Assert.Contains(missing, why, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_api_version_that_is_not_this_runtimes_is_served_but_flagged()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct, prepare: paths =>
        {
            File.WriteAllText(paths.UserSettingsFile, RuntimeApiFixture.DispatcherOff);
            UiBuild.Write(paths.UiDefaultDirectory, apiVersion: "v0");
        });

        var (status, _, _) = await GetAsync(api, "/ui/");
        Assert.Equal(HttpStatusCode.OK, status);
        var info = await api.PostOkAsync<SystemInfoResponse>(Operations.SystemInfo, null, Ct);
        Assert.False(info.Ui!.ApiVersionMatches);
    }

    [Theory]
    // Critical: the UI server cannot climb.
    [InlineData("/ui/%2e%2e/config/settings.json", Traits = ["Category", "Critical", "CriticalFamily", nameof(Family.X2)])]
    [InlineData("/ui/..%2fconfig%2fsettings.json")]
    [InlineData("/ui/assets/")]
    [InlineData("/ui/assets")]
    [InlineData("/ui/.hidden")]
    [InlineData("/ui/.secret.json")]
    [InlineData("/ui/manifest.json.bak")]
    [InlineData("/ui/nothing.html")]
    public async Task Nothing_outside_the_build_and_nothing_but_its_files_is_served(string path)
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct, prepare: paths =>
        {
            File.WriteAllText(paths.UserSettingsFile, RuntimeApiFixture.DispatcherOff);
            UiBuild.Write(paths.UiDefaultDirectory);
            File.WriteAllText(Path.Combine(paths.UiDefaultDirectory, ".hidden"), "x");
            // A hidden file with a served extension: only the dot rule stands between it and the wire.
            File.WriteAllText(Path.Combine(paths.UiDefaultDirectory, ".secret.json"), "{}");
            File.WriteAllText(Path.Combine(paths.UiDefaultDirectory, "manifest.json.bak"), "x");
        });

        // A dotted escape is normalised away before routing and lands outside /ui, where the bearer check answers;
        // every other row is this module's own 404. Neither is ever a file.
        var (status, body, _) = await GetAsync(api, path);
        Assert.True(status is HttpStatusCode.NotFound or HttpStatusCode.Unauthorized, $"{path} answered {(int)status}");
        Assert.True(body.Contains("\"not_found\"", StringComparison.Ordinal) || body.Contains("\"unauthorized\"", StringComparison.Ordinal), body);
        Assert.DoesNotContain("Dispatcher", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_bare_prefix_redirects_to_the_page_and_the_files_need_no_bearer()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct, prepare: paths =>
        {
            File.WriteAllText(paths.UserSettingsFile, RuntimeApiFixture.DispatcherOff);
            UiBuild.Write(paths.UiDefaultDirectory);
        });

        var (status, _, headers) = await GetAsync(api, "/ui");
        Assert.Equal(HttpStatusCode.MovedPermanently, status);
        Assert.Equal("/ui/", headers["Location"]);
    }

    internal static async Task<(HttpStatusCode Status, string Body, Dictionary<string, string> Headers)> GetAsync(RuntimeApiFixture api, string path)
    {
        using var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var http = new HttpClient(handler) { BaseAddress = api.Runtime.BaseUrl };
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        using var response = await http.SendAsync(request, Ct);
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, values) in response.Headers.Concat(response.Content.Headers))
        {
            headers[name] = string.Join(", ", values);
        }

        return (response.StatusCode, await response.Content.ReadAsStringAsync(Ct), headers);
    }
}
