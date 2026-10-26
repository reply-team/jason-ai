using System.Buffers.Text;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Jason.Contracts.Api;
using Jason.Contracts.Ui;
using Jason.Runtime.Api;
using Jason.Runtime.Campaigns;
using Jason.Runtime.Configuration;
using Jason.Runtime.Domain;
using Jason.Runtime.Hosting;
using Jason.Runtime.Ui;

namespace Jason.Runtime.Tests.Documentation;

/// <summary>
/// The page for a program of your own that calls the runtime, held to the runtime it describes: every operation it names
/// is one the runtime has and every one the runtime has is listed, every path it names is in the tree, what it says a UI
/// build is matches what the runtime serves, and what it quotes of the descriptor and of a refusal is what a running
/// runtime writes.
/// </summary>
public partial class RuntimeApiDocTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly string[] FileExtensions = ["json", "md", "html", "yaml", "js", "txt", "zip"];

    [Fact]
    public void Every_operation_the_page_names_is_one_the_runtime_has()
    {
        var unknown = Named(Page()).Except(Published(), StringComparer.Ordinal).ToList();

        Assert.True(unknown.Count == 0, $"docs/runtime-api.md names operations the runtime does not have: {string.Join(", ", unknown)}");
    }

    [Fact]
    public void The_page_lists_every_operation_the_runtime_has()
    {
        var listed = Named(Section(Page(), "## What `/v1/` holds")).ToHashSet(StringComparer.Ordinal);
        var missing = Published().Where(operation => !listed.Contains(operation)).ToList();

        Assert.True(missing.Count == 0, $"docs/runtime-api.md does not list: {string.Join(", ", missing)}");
    }

    [Fact]
    public void Every_path_the_page_names_is_in_the_tree()
    {
        var root = Publication.RepositoryRoot();
        var docs = Path.Combine(root, "docs");
        var page = Page();

        var linked = Link().Matches(page).Select(match => match.Groups["target"].Value)
            .Where(target => !target.Contains("://", StringComparison.Ordinal) && !target.StartsWith('#'))
            .Select(target => Path.GetFullPath(Path.Combine(docs, target.Split('#')[0])));
        var named = TreePath().Matches(page).Select(match => Path.GetFullPath(Path.Combine(root, match.Groups["path"].Value)));

        var absent = linked.Concat(named).Where(path => !File.Exists(path) && !Directory.Exists(path)).Select(path => Path.GetRelativePath(root, path)).ToList();

        Assert.True(absent.Count == 0, $"docs/runtime-api.md names paths that are not in the tree: {string.Join(", ", absent)}");
    }

    [Fact]
    public void The_page_prints_the_numbers_and_names_from_the_code()
    {
        var page = Flat(Page());

        Assert.Contains($"port `{RuntimeOptions.DefaultPort.ToString(CultureInfo.InvariantCulture)}` by default", page, StringComparison.Ordinal);
        Assert.Contains($"`limit` is 1 to {Paging.MaxLimit.ToString(CultureInfo.InvariantCulture)} and {Paging.DefaultLimit.ToString(CultureInfo.InvariantCulture)} when absent", page, StringComparison.Ordinal);
        Assert.Contains($"at most {ErrorResults.MaxDetails.ToString(CultureInfo.InvariantCulture)}.", page, StringComparison.Ordinal);
        Assert.Contains($"at most {CampaignService.MaxReasonLength.ToString(CultureInfo.InvariantCulture)} characters", page, StringComparison.Ordinal);
        Assert.Contains($"`Content-Security-Policy: {UiFiles.ContentSecurityPolicy}`", page, StringComparison.Ordinal);
        Assert.Contains($"`POST {UiPaths.Door}`", page, StringComparison.Ordinal);

        var lifetime = $"{UiTickets.Lifetime.TotalSeconds.ToString(CultureInfo.InvariantCulture)} seconds";
        var said = Regex.Matches(page, @"good for (?<span>\S+ \S+?)[ ,.]").Select(match => match.Groups["span"].Value).ToList();
        Assert.NotEmpty(said);
        Assert.All(said, span => Assert.Equal(lifetime, span));
    }

    /// <summary>
    /// What a build is: a manifest of exactly the members <see cref="UiManifest"/> reads, and files of exactly the kinds
    /// <see cref="UiFiles"/> serves, no kind left out and none added.
    /// </summary>
    [Fact]
    public void The_page_names_the_manifest_members_and_every_kind_of_file_a_build_is_served()
    {
        var section = Section(Page(), "## Where a program's own pages can live");

        var members = typeof(UiManifest).GetProperties().Select(property => $"`{JsonNamingPolicy.SnakeCaseLower.ConvertName(property.Name)}`");
        Assert.Contains($"`manifest.json` that holds exactly {string.Join(" and ", members)}", Flat(section), StringComparison.Ordinal);

        var served = ((IEnumerable<KeyValuePair<string, string>>)typeof(UiFiles).GetField("ContentTypes", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!)
            .Select(pair => pair.Key).Order(StringComparer.Ordinal).ToList();
        var named = Regex.Matches(section, @"`(?<kind>\.[a-z0-9]+)`").Select(match => match.Groups["kind"].Value).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(served, named);
    }

    /// <summary>
    /// A real runtime read back against what the page prints: the descriptor's path, every field of it in its table and
    /// its example, the token's size, the two UI directories, and three refusals — a token that is not the runtime's and
    /// no token at all (status, header and body), and a foreign <c>Origin</c> before any token is read (status and code).
    /// That the token is made fresh at every start is not held here.
    /// </summary>
    [Fact]
    public async Task The_descriptor_the_token_size_the_ui_directories_and_three_refusals_are_what_a_runtime_has()
    {
        using var dir = new TempDataDir();
        await using var runtime = await RuntimeHost.StartAsync(dir.Paths, TestRuntimeOptions.Quiet, Ct);
        var page = Page();
        var flat = Flat(page);

        var relative = Path.GetRelativePath(dir.Paths.Root, dir.Paths.DescriptorFile).Replace('\\', '/');
        Assert.Contains($"`~/.jason/{relative}`", page, StringComparison.Ordinal);

        var written = JsonNode.Parse(await File.ReadAllTextAsync(dir.Paths.DescriptorFile, Ct))!.AsObject();
        var fields = written.Select(field => field.Key).ToList();
        var tabled = Regex.Matches(Section(page, "## How a program finds the runtime"), @"(?m)^\| `(?<field>[a-z_]+)` \|").Select(match => match.Groups["field"].Value).ToList();
        Assert.Equal(fields.Order(StringComparer.Ordinal), tabled.Order(StringComparer.Ordinal));

        var example = JsonNode.Parse(Fenced(Section(page, "## How a program finds the runtime"), "json"))!.AsObject();
        Assert.Equal(fields, example.Select(field => field.Key).ToList());

        var token = (string)written["token"]!;
        var bits = Base64Url.DecodeFromChars(token).Length * 8;
        Assert.Contains($"The token is {bits.ToString(CultureInfo.InvariantCulture)} random bits, made fresh at every start.", flat, StringComparison.Ordinal);

        var directories = new[] { dir.Paths.UiCustomDirectory, dir.Paths.UiDefaultDirectory }
            .Select(directory => Path.GetRelativePath(dir.Paths.Root, directory).Replace('\\', '/') + "/").Order(StringComparer.Ordinal);
        var named = Regex.Matches(page, @"`(?<directory>ui/[^`/]+/)`").Select(match => match.Groups["directory"].Value).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);
        Assert.Equal(directories, named);

        var printed = JsonNode.Parse(Fenced(Section(page, "## How a program authorises"), "json"));
        Assert.Contains("answered `401`, with `WWW-Authenticate: Bearer`", flat, StringComparison.Ordinal);
        using var http = new HttpClient { BaseAddress = runtime.BaseUrl };

        using var wrong = new HttpRequestMessage(HttpMethod.Post, Operations.Route(Operations.SystemInfo)) { Content = new StringContent("{}") };
        wrong.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "not" + token);
        using var wrongAnswer = await http.SendAsync(wrong, Ct);
        await AssertUnauthorized(wrongAnswer, printed);

        using var none = await http.PostAsync(Operations.Route(Operations.SystemInfo), new StringContent("{}"), Ct);
        await AssertUnauthorized(none, printed);

        using var foreign = new HttpRequestMessage(HttpMethod.Post, Operations.Route(Operations.SystemInfo)) { Content = new StringContent("{}") };
        foreign.Headers.Add("Origin", "http://example.com");
        using var foreignAnswer = await http.SendAsync(foreign, Ct);
        var code = (string?)JsonNode.Parse(await foreignAnswer.Content.ReadAsStringAsync(Ct))?["error"]?["code"];
        Assert.Contains($"answered `{(int)foreignAnswer.StatusCode}` with the code `{code}` before its token is read", flat, StringComparison.Ordinal);
    }

    private static async Task AssertUnauthorized(HttpResponseMessage answer, JsonNode? printed)
    {
        Assert.Equal(HttpStatusCode.Unauthorized, answer.StatusCode);
        Assert.Equal("Bearer", answer.Headers.WwwAuthenticate.ToString());
        var body = JsonNode.Parse(await answer.Content.ReadAsStringAsync(Ct));
        Assert.True(JsonNode.DeepEquals(body, printed), $"The runtime answers {body?.ToJsonString()}; the page prints {printed?.ToJsonString()}.");
    }

    [Fact]
    public void The_readme_says_the_runtime_api_is_open_and_links_the_page()
    {
        var readme = Flat(File.ReadAllText(Path.Combine(Publication.RepositoryRoot(), "README.md")));

        Assert.Contains(
            "The Runtime API is documented and open as well, so a plugin of your own — a program that reads and writes the runtime — can be built over it: [docs/runtime-api.md](docs/runtime-api.md).",
            readme,
            StringComparison.Ordinal);
    }

    /// <summary>The operations, with no command or contract document standing in for them.</summary>
    private static List<string> Published() =>
        [.. typeof(Operations).GetFields().Where(field => field.IsLiteral).Select(field => (string)field.GetRawConstantValue()!)];

    /// <summary>Every inline code span shaped like an operation, a file name aside.</summary>
    private static IEnumerable<string> Named(string text) =>
        OperationShaped().Matches(text).Select(match => match.Groups["name"].Value)
            .Where(name => !FileExtensions.Contains(name[(name.LastIndexOf('.') + 1)..], StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal);

    private static string Page() => File.ReadAllText(Path.Combine(Publication.RepositoryRoot(), "docs", "runtime-api.md")).Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string Flat(string text) => Regex.Replace(text, @"\s+", " ");

    /// <summary>From a heading to the next heading of the same level.</summary>
    private static string Section(string page, string heading)
    {
        var start = page.IndexOf(heading + "\n", StringComparison.Ordinal);
        Assert.True(start >= 0, $"docs/runtime-api.md has no heading '{heading}'.");
        var next = page.IndexOf("\n## ", start + heading.Length, StringComparison.Ordinal);
        return next < 0 ? page[start..] : page[start..next];
    }

    /// <summary>The first fenced block of a language in the text.</summary>
    private static string Fenced(string text, string language)
    {
        var match = Regex.Match(text, $@"(?ms)^\s*```{language}\n(?<body>.*?)\n\s*```");
        Assert.True(match.Success, $"No fenced {language} block.");
        return match.Groups["body"].Value;
    }

    [GeneratedRegex(@"`(?<name>[a-z]+\.[a-z_]+)`")]
    private static partial Regex OperationShaped();

    [GeneratedRegex(@"\]\((?<target>[^)\s]+)\)")]
    private static partial Regex Link();

    [GeneratedRegex(@"`(?<path>(?:docs|skills|plugins|examples|install)/[^`\s]*)`")]
    private static partial Regex TreePath();
}
