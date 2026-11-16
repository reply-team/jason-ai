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

    public const string Csp = "default-src 'self'; connect-src 'self' http://127.0.0.1:*; frame-ancestors 'none'; base-uri 'none'";

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

    /// <summary>The policy of a file under <c>sealed/</c>, spelt out here so a change to the runtime's shows as this test going red.</summary>
    public const string SealedCsp =
        "default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; font-src data:; img-src data:; "
        + "connect-src 'none'; media-src 'none'; object-src 'none'; frame-src 'none'; child-src 'none'; worker-src 'none'; "
        + "manifest-src 'none'; form-action 'none'; base-uri 'none'; frame-ancestors 'self'; sandbox allow-scripts allow-forms";

    /// <summary>The headers the transport adds, beside those that say how the content may act.</summary>
    private static readonly string[] Transport = ["Date", "Server", "Content-Type", "Content-Length", "Last-Modified", "Accept-Ranges", "ETag", "Transfer-Encoding"];

    /// <summary>
    /// A page under <c>sealed/</c>, at any depth, is answered with the sealed set and nothing of the
    /// console's: opened by itself (a document request) or framed, the same headers, since the server cannot tell and
    /// does not ask. A file at the root or under <c>assets/</c> keeps the console's set and no feature policy.
    /// </summary>
    [Theory]
    [InlineData("document")]
    [InlineData("iframe")]
    public async Task A_file_under_sealed_is_served_unable_to_act_and_every_other_file_keeps_the_consoles_headers(string destination)
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct, prepare: paths =>
        {
            File.WriteAllText(paths.UserSettingsFile, RuntimeApiFixture.DispatcherOff);
            UiBuild.Write(paths.UiDefaultDirectory);
            UiBuild.WriteSealed(paths.UiDefaultDirectory);
        });

        foreach (var path in (string[])["/ui/sealed/page.html", "/ui/sealed/nested/part.html"])
        {
            var (status, body, headers) = await GetAsync(api, path, destination);
            Assert.Equal(HttpStatusCode.OK, status);
            Assert.Contains("sealed content", body, StringComparison.Ordinal);
            AssertSealed(headers, path);
        }

        foreach (var path in (string[])["/ui/", "/ui/favicon.svg", "/ui/assets/app-abc123.js"])
        {
            var (status, _, headers) = await GetAsync(api, path, destination);
            Assert.Equal(HttpStatusCode.OK, status);
            Assert.Equal(Csp, headers["Content-Security-Policy"]);
            Assert.False(headers.ContainsKey("Permissions-Policy"), path);
            Assert.False(headers.ContainsKey("Cross-Origin-Resource-Policy"), path);
            Assert.False(headers.ContainsKey("X-DNS-Prefetch-Control"), path);
        }

        // A name under sealed/ that holds no file is sealed too: the set is chosen before the file is looked for.
        var (missing, _, missingHeaders) = await GetAsync(api, "/ui/sealed/missing.html", destination);
        Assert.Equal(HttpStatusCode.NotFound, missing);
        AssertSealed(missingHeaders, "/ui/sealed/missing.html");
    }

    /// <summary>
    /// What a request can reach under <c>sealed/</c>: only a file named exactly as its directory holds it, under the sealed
    /// set. Another spelling the disk may take for the same name serves nothing: another case, a trailing dot or space,
    /// which Windows drops, a short name, a stream suffix, a name equal only after Unicode normalization or case folding,
    /// an encoded separator, a doubled one, a backslash. Dot segments are taken off by the server before the route reads
    /// the path, so they reach the exact file and answer it under the sealed set. No row is ever the console's set over
    /// sealed content. A short name exists only where the volume makes them, and a case or a normalization is the same
    /// name only on a disk that folds it: those rows hold on every system (a 404), and say most where the disk aliases;
    /// <see cref="A_segment_counts_only_when_its_directory_holds_that_very_name"/> holds them against a stand-in listing.
    /// </summary>
    [Theory]
    [InlineData("/ui/Sealed/page.html", "404")]
    [InlineData("/ui/SEALED/page.HTML", "404")]
    [InlineData("/ui/sealed/PAGE.HTML", "404")]
    [InlineData("/ui/Sealed/nested/part.html", "404")]
    [InlineData("/ui/sealed/Nested/part.html", "404")]
    [InlineData("/ui/sealed./page.html", "404")]
    [InlineData("/ui/sealed/page.html.", "404")]
    [InlineData("/ui/sealed%20/page.html", "404")]
    [InlineData("/ui/sealed/page.html%20", "404")]
    [InlineData("/ui/sealed/A-LONG~1.HTM", "404")]
    [InlineData("/ui/SEALED~1/page.html", "404")]
    [InlineData("/ui/sealed/page.html::$DATA", "404")]
    [InlineData("/ui/sealed::$INDEX_ALLOCATION/page.html", "404")]
    [InlineData("/ui/sealed/cafe%CC%81.html", "404")]
    [InlineData("/ui/%C5%BFealed/page.html", "404")]
    [InlineData("/ui/sealed%2Fpage.html", "404")]
    [InlineData("/ui/Sealed%2Fpage.html", "404")]
    [InlineData("/ui/sealed//page.html", "404")]
    [InlineData("/ui//sealed/page.html", "404")]
    [InlineData("/ui/sealed%5Cpage.html", "404")]
    [InlineData("/ui/Sealed%5Cpage.html", "404")]
    [InlineData("/ui/sealed/script.js", "404 sealed")]
    [InlineData("/ui/sealed/style.css", "404 sealed")]
    [InlineData("/ui/sealed", "404 console")]
    [InlineData("/ui/sealed/", "404 sealed")]
    [InlineData("/ui/sealed%252Fpage.html", "404")]
    [InlineData("/ui/sealed%252e%252e/index.html", "404")]
    [InlineData("/ui/con/page.html", "404")]
    [InlineData("/ui/sealed/nul", "404")]
    [InlineData("/ui/sealed/nul.html", "404")]
    [InlineData("/ui/sealed/%2e%2e/index.html", "console")]
    [InlineData("/ui/s%65aled/page.html", "sealed")]
    [InlineData("/ui/sealed/%2e/page.html", "sealed")]
    [InlineData("/ui/assets/%2e%2e/sealed/page.html", "sealed")]
    [InlineData("/ui/sealed/page.html", "sealed")]
    [InlineData("/ui/sealed/a-long-page-name.html", "sealed")]
    [InlineData("/ui/sealed/caf%C3%A9.html", "sealed")]
    public async Task Only_the_exact_name_reaches_a_sealed_file_and_only_under_the_sealed_set(string path, string answer)
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct, prepare: paths =>
        {
            File.WriteAllText(paths.UserSettingsFile, RuntimeApiFixture.DispatcherOff);
            UiBuild.Write(paths.UiDefaultDirectory);
            UiBuild.WriteSealed(paths.UiDefaultDirectory);
        });

        var (status, body, headers) = await GetAsync(api, path);
        switch (answer)
        {
            case "sealed":
                Assert.Equal(HttpStatusCode.OK, status);
                Assert.Contains("sealed content", body, StringComparison.Ordinal);
                AssertSealed(headers, path);
                return;
            case "console":
                Assert.Equal(HttpStatusCode.OK, status);
                Assert.Contains("<title>Jason · default</title>", body, StringComparison.Ordinal);
                Assert.Equal(Csp, headers["Content-Security-Policy"]);
                return;
        }

        Assert.True(status == HttpStatusCode.NotFound, $"{path} answered {(int)status} under {headers.GetValueOrDefault("Content-Security-Policy")}");
        Assert.DoesNotContain("sealed content", body, StringComparison.Ordinal);
        if (answer == "404 sealed")
        {
            AssertSealed(headers, path);
        }
        else if (answer == "404 console")
        {
            Assert.Equal(Csp, headers["Content-Security-Policy"]);
        }
    }

    /// <summary>
    /// A request that is no plain GET of the runtime's own pages, a HEAD or a request naming another origin, is answered
    /// outside the files' route: never with a sealed file's bytes.
    /// </summary>
    [Theory]
    [InlineData("HEAD", null)]
    [InlineData("GET", "http://example.invalid")]
    [InlineData("GET", "null")]
    public async Task A_head_or_a_foreign_origin_gets_no_bytes_of_a_sealed_file(string method, string? origin)
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct, prepare: paths =>
        {
            File.WriteAllText(paths.UserSettingsFile, RuntimeApiFixture.DispatcherOff);
            UiBuild.Write(paths.UiDefaultDirectory);
            UiBuild.WriteSealed(paths.UiDefaultDirectory);
        });

        var (status, body, headers) = await GetAsync(api, "/ui/sealed/page.html", method: new HttpMethod(method), origin: origin);
        Assert.NotEqual(HttpStatusCode.OK, status);
        Assert.DoesNotContain("sealed content", body, StringComparison.Ordinal);
        Assert.NotEqual("text/html; charset=utf-8", headers.GetValueOrDefault("Content-Type"));
    }

    /// <summary>
    /// The served build is the one directory the runtime chose, never a file of the other: with a custom build that has no
    /// <c>sealed/</c> active, the default's sealed page is a 404 under the sealed set.
    /// </summary>
    [Fact]
    public async Task A_sealed_page_of_the_build_not_served_is_not_served()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct, prepare: paths =>
        {
            File.WriteAllText(paths.UserSettingsFile, RuntimeApiFixture.DispatcherOff);
            UiBuild.Write(paths.UiDefaultDirectory);
            UiBuild.WriteSealed(paths.UiDefaultDirectory);
            UiBuild.Write(paths.UiCustomDirectory, marker: "custom");
        });

        var (index, indexBody, _) = await GetAsync(api, "/ui/");
        Assert.Equal(HttpStatusCode.OK, index);
        Assert.Contains("<title>Jason · custom</title>", indexBody, StringComparison.Ordinal);

        var (status, body, headers) = await GetAsync(api, "/ui/sealed/page.html");
        Assert.Equal(HttpStatusCode.NotFound, status);
        Assert.DoesNotContain("sealed content", body, StringComparison.Ordinal);
        AssertSealed(headers, "/ui/sealed/page.html");
    }

    /// <summary>
    /// A name the system rewrites is not served: a directory <c>sealed.</c> placed beside <c>sealed/</c> (on Windows
    /// only through the long-path prefix, which keeps the trailing dot) is listed by that name, while the path built from
    /// it is taken for <c>sealed/</c>; the file is served only where the path built is the root and the request, as
    /// written, so this is a 404 and never a sealed page under the console's set.
    /// </summary>
    [Fact]
    public async Task A_name_the_system_rewrites_is_not_served()
    {
        string? placed = null;
        await using var api = await RuntimeApiFixture.StartAsync(Ct, prepare: paths =>
        {
            File.WriteAllText(paths.UserSettingsFile, RuntimeApiFixture.DispatcherOff);
            UiBuild.Write(paths.UiDefaultDirectory);
            UiBuild.WriteSealed(paths.UiDefaultDirectory);
            placed = Path.Combine(Path.GetFullPath(paths.UiDefaultDirectory), "sealed.");
            Directory.CreateDirectory(OperatingSystem.IsWindows() ? @"\\?\" + placed : placed);
        });

        try
        {
            var (status, body, headers) = await GetAsync(api, "/ui/sealed./page.html");
            Assert.True(status == HttpStatusCode.NotFound, $"answered {(int)status} under {headers.GetValueOrDefault("Content-Security-Policy")}");
            Assert.DoesNotContain("sealed content", body, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(OperatingSystem.IsWindows() ? @"\\?\" + placed : placed!);
        }
    }

    /// <summary>
    /// The resolver's test of a name, against a stand-in listing, so the aliases a disk may make hold on every system: a
    /// directory lists <c>sealed</c>, and <c>sealed</c> lists a page, a long name and a name with an accent, composed.
    /// Each alias below is what some disk takes for one of them (a case on Windows and macOS, a short name on a Windows
    /// volume that makes them, a stream suffix on NTFS, the decomposed accent on macOS), and none is held.
    /// </summary>
    [Theory]
    [InlineData("sealed/page.html", true)]
    [InlineData("sealed/a-long-page-name.html", true)]
    [InlineData("sealed/caf\u00e9.html", true)]
    [InlineData("Sealed/page.html", false)]
    [InlineData("sealed/PAGE.HTML", false)]
    [InlineData("sealed/A-LONG~1.HTM", false)]
    [InlineData("SEALED~1/page.html", false)]
    [InlineData("sealed/page.html::$DATA", false)]
    [InlineData("sealed::$INDEX_ALLOCATION/page.html", false)]
    [InlineData("sealed/cafe\u0301.html", false)]
    [InlineData("\u017fealed/page.html", false)]
    [InlineData("sealed./page.html", false)]
    [InlineData("sealed /page.html", false)]
    public void A_segment_counts_only_when_its_directory_holds_that_very_name(string relative, bool held)
    {
        var root = Path.Combine(Path.GetTempPath(), "stand-in-ui");
        var listing = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            [root] = ["index.html", "manifest.json", "sealed"],
            [Path.Combine(root, "sealed")] = ["page.html", "a-long-page-name.html", "caf\u00e9.html"],
        };

        Assert.Equal(held, Jason.Runtime.Ui.UiModule.NamedAsHeld(root, relative.Split('/'), directory => listing.GetValueOrDefault(directory, [])));
    }

    /// <summary>A file of the console in another spelling is not served either: a file is served by the name its directory holds.</summary>
    [Fact]
    public async Task A_file_is_served_only_by_the_name_its_directory_holds()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct, prepare: paths =>
        {
            File.WriteAllText(paths.UserSettingsFile, RuntimeApiFixture.DispatcherOff);
            UiBuild.Write(paths.UiDefaultDirectory);
        });

        Assert.Equal(HttpStatusCode.OK, (await GetAsync(api, "/ui/favicon.svg")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await GetAsync(api, "/ui/FAVICON.SVG")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await GetAsync(api, "/ui/Assets/app-abc123.js")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await GetAsync(api, "/ui/favicon.svg.")).Status);
    }

    private static void AssertSealed(Dictionary<string, string> headers, string path)
    {
        Assert.Equal(SealedCsp, headers["Content-Security-Policy"]);
        Assert.DoesNotContain("allow-same-origin", headers["Content-Security-Policy"], StringComparison.Ordinal);
        Assert.Equal(string.Join(", ", Jason.Runtime.Ui.UiFiles.SealedDeniedFeatures.Select(feature => feature + "=()")), headers["Permissions-Policy"]);
        Assert.Contains("camera=()", headers["Permissions-Policy"], StringComparison.Ordinal);
        Assert.Equal("nosniff", headers["X-Content-Type-Options"]);
        Assert.Equal("off", headers["X-DNS-Prefetch-Control"]);
        Assert.Equal("no-referrer", headers["Referrer-Policy"]);
        Assert.Equal("same-origin", headers["Cross-Origin-Resource-Policy"]);
        Assert.Equal("no-store", headers["Cache-Control"]);
        string[] named = ["Content-Security-Policy", "Permissions-Policy", "X-Content-Type-Options", "X-DNS-Prefetch-Control", "Referrer-Policy", "Cross-Origin-Resource-Policy", "Cache-Control"];
        var other = headers.Keys.Except(named, StringComparer.OrdinalIgnoreCase).Except(Transport, StringComparer.OrdinalIgnoreCase).ToList();
        Assert.True(other.Count == 0, $"{path} carries headers the sealed set does not name: {string.Join(", ", other)}");
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

    internal static async Task<(HttpStatusCode Status, string Body, Dictionary<string, string> Headers)> GetAsync(RuntimeApiFixture api, string path, string? destination = null, HttpMethod? method = null, string? origin = null)
    {
        using var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var http = new HttpClient(handler) { BaseAddress = api.Runtime.BaseUrl };
        using var request = new HttpRequestMessage(method ?? HttpMethod.Get, path);
        if (destination is not null)
        {
            request.Headers.Add("Sec-Fetch-Dest", destination);
        }

        if (origin is not null)
        {
            request.Headers.Add("Origin", origin);
        }

        using var response = await http.SendAsync(request, Ct);
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, values) in response.Headers.Concat(response.Content.Headers))
        {
            headers[name] = string.Join(", ", values);
        }

        return (response.StatusCode, await response.Content.ReadAsStringAsync(Ct), headers);
    }
}
