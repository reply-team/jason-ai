using System.Text.RegularExpressions;
using Jason.Cli.Skills;
using Jason.Contracts.Update;

namespace Jason.Runtime.Tests.Documentation;

/// <summary>
/// The product's own address, <see cref="ProductAddress.Root"/>, in front of the public repository's releases. It
/// redirects every path under <c>/releases/</c> to the same path on GitHub, so what it answers is a short list of
/// shapes, and <see cref="Shapes"/> is the list the redirect is tested against:
/// <list type="bullet">
/// <item>(a) every address on it that the product writes has one of those shapes;</item>
/// <item>(b) nothing fetches a release from GitHub by name, outside the exceptions named here with their reasons;</item>
/// <item>(c) the detectors are run on lines before they are trusted with the tree, and must find the addresses the
/// tree really writes;</item>
/// <item>(d) what stays on GitHub stays there: the skills come from a <c>git</c> clone, which a redirect does not
/// serve.</item>
/// </list>
/// What is read: everything a release publishes (the open set and the public repository's own files), the release
/// notes template, the release workflow, the two private pages that describe releases, and the runtime's source.
/// </summary>
public sealed partial class ProductAddressTests : IDisposable
{
    /// <summary>
    /// A release's version as the texts write it: a version, the notes template's placeholder, or the release
    /// workflow's variable.
    /// </summary>
    private const string Version = @"(?:[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?|\{\{version\}\}|\$env:VERSION)";

    /// <summary>
    /// What the address answers, one row per shape. Each answers 302 with the same path under
    /// <c>https://github.com/reply-team/jason-ai</c>, the root with the repository itself. An asset is one of the
    /// files a release publishes, by its name in <see cref="ReleaseAssets.Published"/>.
    /// </summary>
    internal static readonly IReadOnlyList<(string Shape, Regex Pattern)> Shapes =
    [
        ("the root", Root()),
        ("the latest release's asset", LatestAsset()),
        ("a release's asset", PinnedAsset()),
        ("a release's page", ReleasePage()),
    ];

    /// <summary>
    /// The releases base alone, <see cref="ProductAddress.Releases"/>, is a prefix the code completes and never a
    /// request. It stands only where it is defined and where the two scripts complete it.
    /// </summary>
    private static readonly string[] BaseHolders =
        ["runtime/src/Jason.Contracts/Update/ProductAddress.cs", "install/install.sh", "install/install.ps1"];

    /// <summary>What is read besides what a release publishes: what writes a release, and what describes one.</summary>
    private static readonly string[] AlsoRead =
        [Links.NotesTemplate, ".github/workflows/release.yml", "docs/release-and-update.md", "docs/architecture.md"];

    /// <summary>
    /// The only places a release is still fetched from GitHub by name, how many times, and why. Each count is exact,
    /// so an exception that is no longer needed is named as well.
    /// </summary>
    internal static readonly IReadOnlyList<GitHubException> Exceptions =
    [
        new(
            "skills/runtime/operating-the-installation/SKILL.md",
            2,
            "a verified skill: its text changes only with a new reading by a host, and its two install lines still work, straight from GitHub"),
        new(
            "install/feeds/schema-1/manifest.json",
            1,
            "the feed fixture is the feed 0.1.0 published with its values taken out, compared with those bytes by a test; no install reads its release page"),
    ];

    private readonly string _root = Path.Combine(Path.GetTempPath(), "jason-tests", "product-address-" + Guid.NewGuid().ToString("N"));

    public ProductAddressTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    /// <summary>(a) Every address on the product's own address that the product writes is one the redirect answers.</summary>
    [Fact]
    public void Every_address_the_product_writes_is_one_the_redirect_answers()
    {
        var found = Unanswered(Publication.RepositoryRoot());

        Assert.True(
            found.Count == 0,
            $"Addresses the redirect does not answer ({found.Count}):\n" + string.Join('\n', found.Select(finding => "  " + finding)));
    }

    /// <summary>
    /// (b) A release is fetched from the product's own address. A GitHub release URL outside the named exceptions is
    /// a request that skips the address, and the store behind the address could no longer change without it.
    /// </summary>
    [Fact]
    public void No_release_is_fetched_from_github_by_name_outside_the_named_exceptions()
    {
        var found = FromGitHub(Publication.RepositoryRoot(), Exceptions);

        Assert.True(
            found.Count == 0,
            $"Releases fetched from GitHub by name ({found.Count}):\n" + string.Join('\n', found.Select(finding => "  " + finding)));
    }

    /// <summary>An exception is a decision, so it says why, and the file it excuses is one the guard reads.</summary>
    [Fact]
    public void Every_exception_names_its_reason_and_a_file_that_is_read()
    {
        var read = Files(Publication.RepositoryRoot()).ToHashSet(StringComparer.Ordinal);

        Assert.All(Exceptions, exception =>
        {
            Assert.False(string.IsNullOrWhiteSpace(exception.Reason), $"{exception.File} is excepted without a reason.");
            Assert.Contains(exception.File, read);
        });
    }

    /// <summary>
    /// The files the guard reads besides the open set exist under these names: a renamed workflow or page would
    /// otherwise leave the guard reading nothing there and passing.
    /// </summary>
    [Fact]
    public void The_files_read_besides_the_open_set_are_there()
    {
        var root = Publication.RepositoryRoot();

        Assert.All(AlsoRead.Concat(BaseHolders), file => Assert.True(File.Exists(Path.Combine(root, file)), $"{file} is not there."));
    }

    /// <summary>
    /// (c) A detector that matched nothing would pass (a) on any tree. The product writes its address about twenty
    /// times, so fewer than half that means the detector changed, not the tree.
    /// </summary>
    [Fact]
    public void The_address_detector_finds_the_addresses_the_product_writes()
    {
        var root = Publication.RepositoryRoot();
        var seen = Files(root).Sum(file => Address().Matches(Text(root, file) ?? "").Count);

        Assert.True(seen >= 12, $"the detector found {seen} address(es) on {ProductAddress.Root}");
    }

    /// <summary>(c) The address detector and the shapes, on lines, before the tree.</summary>
    [Theory]
    [InlineData("curl -fsSL https://jasonharness.ai/releases/latest/download/install.sh | sh", "the latest release's asset")]
    [InlineData("irm https://jasonharness.ai/releases/latest/download/install.ps1 | iex", "the latest release's asset")]
    [InlineData("[the feed](https://jasonharness.ai/releases/latest/download/manifest.json).", "the latest release's asset")]
    [InlineData("curl -fsSL https://jasonharness.ai/releases/download/v0.7.0/install.sh | sh -s -- --version 0.7.0", "a release's asset")]
    [InlineData("https://jasonharness.ai/releases/download/v1.0.0-rc.1/jason-win-x64.zip", "a release's asset")]
    [InlineData("irm https://jasonharness.ai/releases/download/v{{version}}/install.ps1", "a release's asset")]
    [InlineData("-ReleaseNotesUrl \"https://jasonharness.ai/releases/tag/v$env:VERSION\"", "a release's page")]
    [InlineData("\"release_notes_url\": \"https://jasonharness.ai/releases/tag/v0.2.0\",", "a release's page")]
    [InlineData("\"url\": \"https://jasonharness.ai\"", "the root")]
    [InlineData("Documentation=https://jasonharness.ai", "the root")]
    [InlineData("https://jasonharness.ai/download/manifest.json", null)]
    [InlineData("https://jasonharness.ai/releases/latest/download/jason-win-arm64.zip", null)]
    [InlineData("https://jasonharness.ai/releases/latest/manifest.json", null)]
    [InlineData("https://jasonharness.ai/Releases/latest/download/manifest.json", null)]
    [InlineData("https://jasonharness.ai/releases/latest/download/manifest.json?from=readme", null)]
    [InlineData("https://jasonharness.ai/releases/download/0.7.0/install.sh", null)]
    [InlineData("http://jasonharness.ai/releases/latest/download/manifest.json", null)]
    [InlineData("https://www.jasonharness.ai/", null)]
    [InlineData("https://jasonharness.ai.example.com/releases/latest/download/manifest.json", null)]
    [InlineData("https://jasonharness.ai/releases", null)]
    public void The_address_detector_reads_the_shape_a_line_names(string line, string? shape)
    {
        var address = Assert.Single(Address().Matches(line)).Value.TrimEnd('.', ',', ';', ':', '!', '?');

        Assert.Equal(shape, ShapeOf(address, "README.md"));
    }

    /// <summary>(c) The GitHub release detector, on lines: a fetch of a release, and none of the links that stay.</summary>
    [Theory]
    [InlineData("curl -fsSL https://github.com/reply-team/jason-ai/releases/latest/download/install.sh | sh", true)]
    [InlineData("irm https://github.com/reply-team/jason-ai/releases/download/v0.1.0/install.ps1 | iex", true)]
    [InlineData("\"release_notes_url\": \"https://github.com/reply-team/jason-ai/releases/tag/v{{version}}\"", true)]
    [InlineData("https://GitHub.com/Reply-Team/Jason-AI/releases/latest/download/manifest.json", true)]
    [InlineData("https://github.com/reply-team/jason-ai/blob/v0.7.0/LICENSE-RUNTIME.md", false)]
    [InlineData("LICENCE_BASE=\"https://github.com/reply-team/jason-ai/blob\"", false)]
    [InlineData("git clone https://github.com/reply-team/jason-ai.git", false)]
    [InlineData("homepage: https://github.com/reply-team/jason-ai/tree/main/plugins/ntfy", false)]
    [InlineData("https://github.com/reply-team/reply-cli/releases/latest/download/reply.zip", false)]
    public void The_github_release_detector_reads_a_fetch_of_a_release(string line, bool found) =>
        Assert.Equal(found, GitHubRelease().IsMatch(line));

    /// <summary>(a) on a tree: an address of no shape is named by file and line; the base stands only where it is completed.</summary>
    [Theory]
    [InlineData("See https://jasonharness.ai/download/manifest.json.", "README.md:1 https://jasonharness.ai/download/manifest.json")]
    [InlineData("A base:\nhttps://jasonharness.ai/releases", "README.md:2 https://jasonharness.ai/releases")]
    [InlineData("curl -fsSL https://jasonharness.ai/releases/latest/download/install.sh | sh", null)]
    public void An_address_of_no_shape_in_a_published_text_is_named(string text, string? expected)
    {
        Write(".github/publish/open-set.txt", "README.md\ninstall/\n");
        Write("README.md", text + "\n");
        Write("install/install.sh", "RELEASES_URL=\"https://jasonharness.ai/releases\"\n");

        string[] named = expected is null ? [] : [expected];
        Assert.Equal(named, Unanswered(_root).Select(finding => finding.ToString()));
    }

    /// <summary>
    /// (b) on a tree: a release fetched from GitHub outside the exceptions is named, an exception at its count is
    /// quiet, and an exception at another count is named with every line it excuses.
    /// </summary>
    [Fact]
    public void A_release_fetched_from_github_outside_the_exceptions_is_named()
    {
        const string Fetch = "curl -fsSL https://github.com/reply-team/jason-ai/releases/latest/download/install.sh | sh";
        Write(".github/publish/open-set.txt", "README.md\nskills/\n");
        Write("README.md", "# Install\n" + Fetch + "\n");
        Write("skills/a/SKILL.md", Fetch + "\n" + Fetch + "\n");
        Write("skills/b/SKILL.md", Fetch + "\n");

        var found = FromGitHub(_root, [new("skills/a/SKILL.md", 2, "a reason"), new("skills/b/SKILL.md", 2, "a reason")]);

        Assert.Equal(
            [
                "README.md:2 github.com/reply-team/jason-ai/releases/latest/download/",
                "skills/b/SKILL.md:0 excepted 2 time(s), found 1",
                "skills/b/SKILL.md:1 github.com/reply-team/jason-ai/releases/latest/download/",
            ],
            found.Select(finding => finding.ToString()));
    }

    /// <summary>
    /// (b) on a tree: the runtime's source is read too, so a release fetched from GitHub in any of its files is named.
    /// The default feed is held by its own rows; a fetch written anywhere else in the source has only this guard.
    /// </summary>
    [Fact]
    public void A_release_fetched_from_github_in_the_runtimes_source_is_named()
    {
        Write(".github/publish/open-set.txt", "README.md\n");
        Write("README.md", "# Install\n");
        Write("runtime/src/Some/Thing.cs", "// a comment\nvar feed = \"https://github.com/reply-team/jason-ai/releases/latest/download/manifest.json\";\n");

        Assert.Equal(
            ["runtime/src/Some/Thing.cs:2 github.com/reply-team/jason-ai/releases/latest/download/"],
            FromGitHub(_root, []).Select(finding => finding.ToString()));
    }

    /// <summary>And on this repository: the source the default feed is written in is among the files the guard reads.</summary>
    [Fact]
    public void The_guard_reads_the_runtimes_source()
    {
        var read = Files(Publication.RepositoryRoot());

        Assert.Contains("runtime/src/Jason.Contracts/Update/UpdateFeed.cs", read);
        Assert.Contains("runtime/src/Jason.Cli/Skills/SkillsSource.cs", read);
    }

    /// <summary>
    /// (d) The skills come from a <c>git</c> clone of the public repository, which a redirect does not serve, and the
    /// runtime's licence says they come from GitHub directly. Nothing else pinned this before the address existed.
    /// </summary>
    [Fact]
    public void The_skills_source_stays_a_clone_of_the_public_repository() =>
        Assert.Equal("https://github.com/reply-team/jason-ai.git", SkillsSource.DefaultSource);

    /// <summary>Which shape an address has, or <c>null</c> when the redirect answers no such request.</summary>
    private static string? ShapeOf(string address, string file)
    {
        foreach (var (shape, pattern) in Shapes)
        {
            var match = pattern.Match(address);
            if (match.Success
                && (!match.Groups["asset"].Success || ReleaseAssets.Published.Contains(match.Groups["asset"].Value, StringComparer.Ordinal)))
            {
                return shape;
            }
        }

        return address == ProductAddress.Releases && BaseHolders.Contains(file, StringComparer.Ordinal) ? "the releases base" : null;
    }

    private static List<Finding> Unanswered(string root)
    {
        var found = new List<Finding>();
        foreach (var file in Files(root))
        {
            if (Text(root, file) is not { } text)
            {
                continue;
            }

            foreach (Match match in Address().Matches(text))
            {
                var address = match.Value.TrimEnd('.', ',', ';', ':', '!', '?');
                if (ShapeOf(address, file) is null)
                {
                    found.Add(new Finding(file, LineOf(text, match.Index), address));
                }
            }
        }

        return found;
    }

    private static List<Finding> FromGitHub(string root, IReadOnlyList<GitHubException> exceptions)
    {
        var found = new List<Finding>();
        var counted = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var file in Files(root))
        {
            if (Text(root, file) is not { } text)
            {
                continue;
            }

            var matches = GitHubRelease().Matches(text).Select(match => new Finding(file, LineOf(text, match.Index), match.Value)).ToList();
            counted[file] = matches.Count;
            var exception = exceptions.FirstOrDefault(candidate => candidate.File == file);
            if (exception is not null && exception.Count != matches.Count)
            {
                found.Add(new Finding(file, 0, $"excepted {exception.Count} time(s), found {matches.Count}"));
            }

            if (exception is null || exception.Count != matches.Count)
            {
                found.AddRange(matches);
            }
        }

        found.AddRange(exceptions
            .Where(exception => !counted.ContainsKey(exception.File))
            .Select(exception => new Finding(exception.File, 0, $"excepted {exception.Count} time(s), and not read")));
        return found;
    }

    /// <summary>
    /// Every file read, as a repository-relative path with forward slashes: the open set's entries, a directory's
    /// files at any depth; the public repository's own files; <see cref="AlsoRead"/>; and the runtime's source.
    /// </summary>
    private static SortedSet<string> Files(string root)
    {
        var files = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var entry in OpenSet.Lines(root))
        {
            Add(root, entry, "*", files);
        }

        Add(root, Publication.PublicOwnDirectory + "/", "*", files);
        foreach (var file in AlsoRead)
        {
            Add(root, file, "*", files);
        }

        Add(root, "runtime/src/", "*.cs", files);
        return files;
    }

    private static void Add(string root, string entry, string pattern, SortedSet<string> files)
    {
        var full = Path.Combine(root, entry.TrimEnd('/').Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(full))
        {
            files.Add(entry);
            return;
        }

        if (!Directory.Exists(full))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(full, pattern, SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');
            if (!relative.Split('/').Any(segment => segment is "bin" or "obj"))
            {
                files.Add(relative);
            }
        }
    }

    /// <summary>A file's text, or <c>null</c> for a file that is not text.</summary>
    private static string? Text(string root, string file)
    {
        var text = File.ReadAllText(Path.Combine(root, file.Replace('/', Path.DirectorySeparatorChar))).ReplaceLineEndings("\n");
        return text.Contains('\0', StringComparison.Ordinal) ? null : text;
    }

    private static int LineOf(string text, int index) => text.AsSpan(0, index).Count('\n') + 1;

    private void Write(string relative, string text)
    {
        var path = Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    /// <summary>Anything on the product's address or a host under it, up to where a URL ends in text.</summary>
    [GeneratedRegex(@"https?://(?:[\w-]+\.)*jasonharness\.ai(?![\w-])[^\s""'`<>()\[\]|*\\]*", RegexOptions.IgnoreCase)]
    private static partial Regex Address();

    /// <summary>A fetch of a release from the public repository on GitHub: an asset, latest or pinned, or a release's page.</summary>
    [GeneratedRegex(@"github\.com/reply-team/jason-ai/releases/(?:latest/download|download|tag)/", RegexOptions.IgnoreCase)]
    private static partial Regex GitHubRelease();

    [GeneratedRegex(@"^https://jasonharness\.ai/?$")]
    private static partial Regex Root();

    [GeneratedRegex(@"^https://jasonharness\.ai/releases/latest/download/(?<asset>[^/?#]+)$")]
    private static partial Regex LatestAsset();

    [GeneratedRegex(@"^https://jasonharness\.ai/releases/download/v" + Version + @"/(?<asset>[^/?#]+)$")]
    private static partial Regex PinnedAsset();

    [GeneratedRegex(@"^https://jasonharness\.ai/releases/tag/v" + Version + "$")]
    private static partial Regex ReleasePage();

    private sealed record Finding(string File, int Line, string What)
    {
        public override string ToString() => $"{File}:{Line} {What}";
    }
}

/// <summary>A file that still fetches a release from GitHub by name, how many times, and why.</summary>
internal sealed record GitHubException(string File, int Count, string Reason);
