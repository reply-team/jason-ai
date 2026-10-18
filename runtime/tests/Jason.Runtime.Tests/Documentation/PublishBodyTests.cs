using System.Text.RegularExpressions;
using Jason.Contracts.Update;
using static Jason.Runtime.Tests.Documentation.Workflows;

namespace Jason.Runtime.Tests.Documentation;

/// <summary>
/// The body of the pull request a release opens in the public repository, written from what the release staged there:
/// which contract documents changed, which pages explain them, how many files each area gained, changed and lost, and
/// every file. Run over git repositories these tests build — an imported tree committed, then a release's changes
/// staged — because the script reads nothing but the staged difference.
/// </summary>
public sealed class PublishBodyTests
{
    /// <summary>The public tree as it stood before the release: one commit.</summary>
    private static readonly Dictionary<string, string> Imported = new(StringComparer.Ordinal)
    {
        [".claude-plugin/marketplace.json"] = "{}\n",
        [".github/workflows/check.yml"] = "name: Check\n",
        ["CONTRIBUTING.md"] = "Changes are not taken here.\n",
        ["LICENSE"] = "MIT License\n",
        ["LICENSE-RUNTIME.md"] = "The runtime's licence.\n",
        ["README.md"] = "# Jason\n",
        ["THIRD-PARTY-NOTICES"] = "Third-party notices.\n",
        ["TRADEMARKS.md"] = "The marks.\n",
        ["VERSION"] = "0.5.0\n",
        ["docs/INSTALL.md"] = "# Installing Jason\n",
        ["docs/contracts/README.md"] = "The contracts.\n",
        ["docs/contracts/notifications/notification.send.json"] = "{\"family\":\"notification.send\"}\n",
        ["docs/contracts/operations/campaign.get.json"] = "{\"operation\":\"campaign.get\"}\n",
        ["docs/contracts/operations/campaign.get.md"] = "# campaign.get\n",
        ["install/install.sh"] = "#!/bin/sh\n",
        ["plugins/reply/main.js"] = "export default {};\n",
        ["skills/runtime/operating-jason/SKILL.md"] = "---\nname: operating-jason\n---\n",
    };

    [Fact]
    public void The_body_names_the_contract_documents_the_pages_the_areas_and_every_file()
    {
        using var tree = new Checkout();
        Change(tree.Root, "VERSION", "0.6.0\n");
        Change(tree.Root, "README.md", "# Jason, again\n");
        Change(tree.Root, "docs/contracts/operations/campaign.get.json", "{\"operation\":\"campaign.get\",\"version\":2}\n");
        Change(tree.Root, "docs/contracts/operations/campaign.update.json", "{\"operation\":\"campaign.update\"}\n");
        Change(tree.Root, "docs/contracts/operations/campaign.update.md", "# campaign.update\n");
        Remove(tree.Root, "docs/contracts/notifications/notification.send.json");
        Change(tree.Root, "plugins/reply/main.js", "export default { version: 2 };\n");
        Change(tree.Root, "skills/business/qualifying/SKILL.md", "---\nname: qualifying\n---\n");
        Change(tree.Root, "install/install.sh", "#!/bin/sh\necho 2\n");

        var (body, label) = Write(tree);

        Assert.Equal(
            """
            Jason 0.6.0: this pull request brings this repository to the version the runtime released as `v0.6.0` — its skills, plugins, operation contracts, install scripts and documentation. It was opened by the release process. Merging it tags `v0.6.0` here and publishes the draft release that holds the runtime's executables.

            The runtime was built and tested at its own tag `v0.6.0`, from source that is not published.

            ## Contract documents changed

            - `docs/contracts/notifications/notification.send.json` — removed
            - `docs/contracts/operations/campaign.get.json` — changed
            - `docs/contracts/operations/campaign.update.json` — added

            Pages that explain them, prose only: `docs/contracts/operations/campaign.update.md`.

            ## Files changed, by area

            | Area | Added | Changed | Removed |
            |---|---|---|---|
            | Skills (`skills/`, `.claude-plugin/`) | 1 | 0 | 0 |
            | Plugins (`plugins/`) | 0 | 1 | 0 |
            | Contracts (`docs/contracts/`) | 2 | 1 | 1 |
            | Documentation (`README.md`, `docs/`) | 0 | 1 | 0 |
            | Install path (`install/`) | 0 | 1 | 0 |
            | Licences (`LICENSE`, `LICENSE-RUNTIME.md`, `THIRD-PARTY-NOTICES`, `TRADEMARKS.md`) | 0 | 0 | 0 |
            | This repository's own files (`VERSION`, `CODEOWNERS`, `.gitattributes`, `.gitignore`, `CONTRIBUTING.md`, `SECURITY.md`, `.github/`) | 0 | 1 | 0 |

            <details><summary>Every file</summary>

            ```
            M{TAB}README.md
            M{TAB}VERSION
            D{TAB}docs/contracts/notifications/notification.send.json
            M{TAB}docs/contracts/operations/campaign.get.json
            A{TAB}docs/contracts/operations/campaign.update.json
            A{TAB}docs/contracts/operations/campaign.update.md
            M{TAB}install/install.sh
            M{TAB}plugins/reply/main.js
            A{TAB}skills/business/qualifying/SKILL.md
            ```

            </details>

            ## The draft release

            `v0.6.0` is a draft until this merges. It holds `jason-win-x64.zip`, `jason-linux-x64.tar.gz`, `jason-osx-arm64.tar.gz`, `manifest.json`, `checksums.txt`, `install.sh`, `install.ps1` and `jason-plugin-reply.zip`.

            """.Replace("{TAB}", "\t", StringComparison.Ordinal).ReplaceLineEndings("\n"),
            body);
        Assert.Equal("contracts", label);
    }

    /// <summary>
    /// The draft release's sentence names every file the release publishes, in the order the workflow attaches them. It is
    /// one more list of those files, held to <see cref="ReleaseAssets.Published"/> as the workflow's own list and the two
    /// pages' tables are, so a file added to a release and forgotten here turns this red, and not only the body above.
    /// </summary>
    [Fact]
    public void The_draft_release_sentence_names_exactly_what_a_release_publishes()
    {
        using var tree = new Checkout();
        Change(tree.Root, "VERSION", "0.6.0\n");

        var (body, _) = Write(tree);

        var section = body.IndexOf("## The draft release\n", StringComparison.Ordinal);
        Assert.True(section >= 0, $"The body has no section on the draft release:\n{body}");
        var holds = body.IndexOf("It holds ", section, StringComparison.Ordinal);
        Assert.True(holds >= 0, $"The draft release's section does not say what it holds:\n{body[section..]}");
        var sentence = body[holds..body.IndexOf('\n', holds)];
        Assert.Equal(ReleaseAssets.Published, Regex.Matches(sentence, "`([^`]+)`").Select(match => match.Groups[1].Value));
    }

    [Fact]
    public void A_change_to_pages_alone_names_no_contract_document_and_takes_no_label()
    {
        using var tree = new Checkout();
        Change(tree.Root, "VERSION", "0.6.0\n");
        Change(tree.Root, "docs/contracts/operations/campaign.get.md", "# campaign.get, explained better\n");
        Change(tree.Root, "docs/contracts/README.md", "The contracts, explained better.\n");

        var (body, label) = Write(tree);

        Assert.Contains("## Contract documents changed\n\nNone.\n", body, StringComparison.Ordinal);
        Assert.Contains("Pages that explain them, prose only: `docs/contracts/README.md`, `docs/contracts/operations/campaign.get.md`.\n", body, StringComparison.Ordinal);
        Assert.Equal(string.Empty, label);
    }

    /// <summary>
    /// The fixtures beside the contracts are JSON too, and the tests run them, but they are examples of a contract and
    /// not one: a release that changes a fixture alone names no contract document and takes no label, and the fixture
    /// is still counted with the contracts.
    /// </summary>
    [Fact]
    public void A_change_to_a_fixture_alone_names_no_contract_document_and_takes_no_label()
    {
        using var tree = new Checkout();
        Change(tree.Root, "VERSION", "0.6.0\n");
        Change(tree.Root, "docs/contracts/fixtures/campaign.get/input-a-missing-id.json", "{\"campaign_id\":null}\n");

        var (body, label) = Write(tree);

        Assert.Contains("## Contract documents changed\n\nNone.\n", body, StringComparison.Ordinal);
        Assert.Contains("| Contracts (`docs/contracts/`) | 1 | 0 | 0 |", body, StringComparison.Ordinal);
        Assert.Equal(string.Empty, label);
    }

    [Fact]
    public void A_release_that_changes_nothing_under_the_contracts_says_so_twice()
    {
        using var tree = new Checkout();
        Change(tree.Root, "VERSION", "0.6.0\n");

        var (body, label) = Write(tree);

        Assert.Contains("## Contract documents changed\n\nNone.\n\nPages that explain them, prose only: none.\n", body, StringComparison.Ordinal);
        Assert.Equal(string.Empty, label);
    }

    [Fact]
    public void A_moved_file_is_a_removal_and_an_addition()
    {
        using var tree = new Checkout();
        Change(tree.Root, "VERSION", "0.6.0\n");
        Git(tree.Root, "mv", "docs/contracts/operations/campaign.get.json", "docs/contracts/operations/campaign.read.json");

        var (body, label) = Write(tree);

        Assert.Contains("- `docs/contracts/operations/campaign.get.json` — removed\n- `docs/contracts/operations/campaign.read.json` — added\n", body, StringComparison.Ordinal);
        Assert.Contains("| Contracts (`docs/contracts/`) | 1 | 0 | 1 |", body, StringComparison.Ordinal);
        Assert.Equal("contracts", label);
    }

    [Fact]
    public void Two_runs_over_the_same_change_write_the_same_body()
    {
        using var tree = new Checkout();
        Change(tree.Root, "VERSION", "0.6.0\n");
        Change(tree.Root, "docs/contracts/operations/campaign.get.json", "{\"operation\":\"campaign.get\",\"version\":2}\n");
        Change(tree.Root, "skills/business/qualifying/SKILL.md", "---\nname: qualifying\n---\n");

        var first = Write(tree);
        var second = Write(tree);

        Assert.Equal(first, second);
    }

    /// <summary>
    /// A change to the terms the runtime is published under, to the notices of what it carries, or to the note on its
    /// marks is the change in a release's pull request its reviewer must not miss. The licence texts have a row of their
    /// own, which reads 0 0 0 in an ordinary release, so it stands out when it does not; the own-files row changes in
    /// every release, with <c>VERSION</c>, and would hide it.
    /// </summary>
    [Theory]
    [InlineData("LICENSE")]
    [InlineData("LICENSE-RUNTIME.md")]
    [InlineData("THIRD-PARTY-NOTICES")]
    [InlineData("TRADEMARKS.md")]
    public void A_change_to_a_licence_text_is_counted_under_the_licences(string licence)
    {
        using var tree = new Checkout();
        Change(tree.Root, "VERSION", "0.6.0\n");
        Change(tree.Root, licence, "Changed terms.\n");

        var (body, label) = Write(tree);

        Assert.Contains("| Licences (`LICENSE`, `LICENSE-RUNTIME.md`, `THIRD-PARTY-NOTICES`, `TRADEMARKS.md`) | 0 | 1 | 0 |\n", body, StringComparison.Ordinal);
        Assert.Contains("| This repository's own files (`VERSION`, `CODEOWNERS`, `.gitattributes`, `.gitignore`, `CONTRIBUTING.md`, `SECURITY.md`, `.github/`) | 0 | 1 | 0 |\n", body, StringComparison.Ordinal);
        Assert.Equal(string.Empty, label);
    }

    [Fact]
    public void A_file_in_no_area_is_refused()
    {
        using var tree = new Checkout();
        Change(tree.Root, "VERSION", "0.6.0\n");
        Change(tree.Root, "NOTES.md", "Notes.\n");

        var run = Run(tree, Path.Combine(tree.Root, "..", "body.md"), Path.Combine(tree.Root, "..", "output"));

        Assert.NotEqual(0, run.Exit);
        Assert.Contains("'NOTES.md'", run.Stderr, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every path a release can publish falls in one of the body's areas: each entry of the real list, <c>VERSION</c>,
    /// and each of the public repository's own files. Without this, a new entry would first be found by a release.
    /// </summary>
    [Fact]
    public void Every_path_a_release_publishes_belongs_to_an_area()
    {
        var root = RepositoryRoot();
        var published = OpenSet.Lines(root)
            .Append("VERSION")
            .Concat(Directory.EnumerateFiles(Path.Combine(root, Publication.PublicOwnDirectory), "*", SearchOption.AllDirectories)
                .Select(file => Path.GetRelativePath(Path.Combine(root, Publication.PublicOwnDirectory), file).Replace('\\', '/')))
            .ToList();

        var run = Script(new Dictionary<string, string>(), ScriptPath, "-AreasOf", string.Join(',', published));

        Assert.True(run.Exit == 0, run.Stderr);
    }

    private static string ScriptPath => Path.Combine(RepositoryRoot(), ".github", "scripts", "Write-PublishBody.ps1");

    /// <summary>The script over the checkout's staged change: the body it wrote and the label it gave the job.</summary>
    private static (string Body, string Label) Write(Checkout tree)
    {
        var body = Path.Combine(tree.Root, "..", "body.md");
        var output = Path.Combine(tree.Root, "..", "output");
        File.WriteAllText(output, string.Empty);

        var run = Run(tree, body, output);

        Assert.True(run.Exit == 0, run.Stderr);
        var label = Assert.Single(File.ReadAllLines(output), line => line.StartsWith("label=", StringComparison.Ordinal));
        return (File.ReadAllText(body), label["label=".Length..]);
    }

    private static (int Exit, string Stdout, string Stderr) Run(Checkout tree, string body, string output)
    {
        Git(tree.Root, "add", "-A");
        return Script(new Dictionary<string, string> { ["GITHUB_OUTPUT"] = output }, ScriptPath, "-Target", tree.Root, "-Version", "0.6.0", "-Body", body);
    }

    private static void Change(string root, string path, string text) => GitTrees.Write(root, path, text);

    private static void Remove(string root, string path) => File.Delete(Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar)));

    /// <summary>A checkout of the public repository holding <see cref="Imported"/> as its one commit, in a temporary tree.</summary>
    private sealed class Checkout : IDisposable
    {
        private readonly GitTrees trees = new();

        public Checkout() => Root = trees.NewRepository("public", [.. Imported.Select(file => (file.Key, file.Value))]);

        public string Root { get; }

        public void Dispose() => trees.Dispose();
    }
}
