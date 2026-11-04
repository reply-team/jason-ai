using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Operations;
using Jason.Contracts.Tests.OperationContracts;

namespace Jason.Contracts.Tests.Journal;

/// <summary>
/// A kind's document only grows. A reader written against a release's documents keeps reading every line a later release
/// writes, so a later document keeps every kind, every <c>since</c>, every body field, every entity and every
/// <c>field_since</c> entry the published one had, accepts every body the release published as valid, and requires
/// nothing the published one did not. The published documents are the last release's, read from its tag.
/// </summary>
public sealed class JournalKindHistoryTests
{
    private const string Journal = "docs/contracts/journal";

    private const string Fixtures = "docs/contracts/fixtures/journal";

    private const string ReleaseTags = "v[0-9]*.[0-9]*.[0-9]*";

    /// <summary>The documents this build embeds against the last published release's, where that release had any.</summary>
    [Fact]
    public void No_document_of_the_last_published_release_is_removed_moved_or_narrowed()
    {
        var tag = PublishedTag(ContractFiles.Root);
        if (tag is null)
        {
            var failure = NoReleaseTagFailure(UnderCi);
            Assert.True(failure is null, failure);
            Assert.Skip("No release tag is reachable from this checkout, so there is no published release to compare with.");
        }

        Assert.SkipWhen(PublishedFiles(ContractFiles.Root, tag!, Journal).Count == 0, $"{tag} publishes no journal documents, so there is nothing yet to keep.");

        var problems = Problems(ContractFiles.Root, tag!);
        Assert.True(problems.Count == 0, $"against {tag}:\n" + string.Join('\n', problems));
    }

    /// <summary>
    /// A clone without the tags skips outside CI, where a shallow clone is a person's choice; under CI the checkout is the
    /// workflow's, and a test that skipped there would never run at all, so it fails and names the checkout.
    /// </summary>
    [Fact]
    public void Under_ci_no_reachable_release_tag_fails_naming_the_checkout()
    {
        var failure = NoReleaseTagFailure(underCi: true);

        Assert.NotNull(failure);
        Assert.Contains("actions/checkout", failure, StringComparison.Ordinal);
        Assert.Contains("fetch-depth: 0", failure, StringComparison.Ordinal);
        Assert.Contains("release-stamp", failure, StringComparison.Ordinal);
    }

    [Fact]
    public void Outside_ci_no_reachable_release_tag_skips()
    {
        Assert.Null(NoReleaseTagFailure(underCi: false));
    }

    /// <summary>
    /// At a release's own tag the documents are compared with the release before it: the tag on the commit being built
    /// holds these documents themselves, not what an earlier reader was written against.
    /// </summary>
    [Fact]
    public void A_release_tag_on_the_commit_itself_is_passed_over_for_the_one_before()
    {
        using var repository = new Repository(Published());
        repository.Commit(Published(), tag: "v0.2.0");

        Assert.Equal("v0.1.0", PublishedTag(repository.Root));
    }

    [Fact]
    public void A_commit_whose_only_release_tag_is_its_own_has_no_published_release()
    {
        using var repository = new Repository(Published(), tag: null);
        repository.Commit(Published(), tag: "v0.1.0");

        Assert.Null(PublishedTag(repository.Root));
    }

    [Theory]
    [InlineData("grew")]
    [InlineData("became subscribable")]
    [InlineData("named on every line")]
    public void A_document_that_only_grew_is_kept(string change)
    {
        var published = Published();
        if (change == "became subscribable")
        {
            published["subscribable"] = false;
        }

        using var repository = new Repository(published);
        var current = Published();
        switch (change)
        {
            case "grew":
                ((JsonObject)current["body"]!["properties"]!["new"]!["properties"]!)["note"] = new JsonObject { ["type"] = "string" };
                ((JsonObject)current["field_since"]!)["/new/note"] = "0.31.0";
                break;
            case "named on every line":
                current["entities"]!["contact"]!["presence"] = "always";
                break;
        }

        repository.Commit(current);

        Assert.Empty(repository.Problems());
    }

    [Theory]
    [InlineData("removed", "is no longer documented")]
    [InlineData("since", "`since` was 0.28.0 and is now 0.29.0")]
    [InlineData("field removed", "`/new/added` is no longer in the body")]
    [InlineData("item field removed", "`/new/contacts/*/id` is no longer in the body")]
    [InlineData("newly required", "`/new/reason` is required now and was not")]
    [InlineData("published body refused", "the body `a pause with its reason` was published as valid and is refused now at /new/reason")]
    [InlineData("field_since dropped", "`field_since` no longer says when `/new/added` was added")]
    [InlineData("entity dropped", "a line no longer names its `campaign`")]
    [InlineData("named on some lines", "`campaign` was named on every line and is now named on some")]
    [InlineData("each changed", "`contact` changed whether one place names one of them or many")]
    [InlineData("no longer subscribable", "was subscribable and is not now")]
    public void A_document_that_lost_or_narrowed_anything_is_refused(string change, string problem)
    {
        using var repository = new Repository(Published());
        var current = Published();
        var body = (JsonObject)current["body"]!["properties"]!["new"]!;
        switch (change)
        {
            case "removed":
                current = null;
                break;
            case "since":
                current["since"] = "0.29.0";
                break;
            case "field removed":
                ((JsonObject)body["properties"]!).Remove("added");
                break;
            case "item field removed":
                ((JsonObject)body["properties"]!["contacts"]!["items"]!["properties"]!).Remove("id");
                break;
            case "newly required":
                ((JsonArray)body["required"]!).Add("reason");
                break;
            case "published body refused":
                body["properties"]!["reason"] = new JsonObject { ["type"] = "integer" };
                break;
            case "field_since dropped":
                current["field_since"] = new JsonObject();
                break;
            case "entity dropped":
                ((JsonObject)current["entities"]!).Remove("campaign");
                break;
            case "named on some lines":
                current["entities"]!["campaign"]!["presence"] = "sometimes";
                break;
            case "each changed":
                ((JsonObject)current["entities"]!["contact"]!).Remove("each");
                break;
            case "no longer subscribable":
                current["subscribable"] = false;
                break;
        }

        repository.Commit(current);

        Assert.Contains(repository.Problems(), said => said.Contains(problem, StringComparison.Ordinal));
    }

    private static bool UnderCi => Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true";

    /// <summary>Why a missing release tag fails the test, or null where it only skips it.</summary>
    private static string? NoReleaseTagFailure(bool underCi) => underCi
        ? "No release tag is reachable from this checkout under CI, so nothing would compare the documents with the last "
          + "published release's: the actions/checkout of each job in .github/workflows/ci.yml that runs the suite, "
          + "build-test-publish and release-stamp, must keep fetch-depth: 0, which brings the tags."
        : null;

    /// <summary>
    /// The release a checkout's documents are compared with: the nearest release tag reachable from its commit, passing
    /// over a release tag on the commit itself.
    /// </summary>
    private static string? PublishedTag(string root)
    {
        var describe = new List<string> { "describe", "--tags", "--abbrev=0", "--match", ReleaseTags };
        var own = Git(root, "tag", "--points-at", "HEAD", "--list", ReleaseTags)?
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];
        foreach (var tag in own)
        {
            describe.Add("--exclude");
            describe.Add(tag);
        }

        describe.Add("HEAD");
        return Git(root, [.. describe])?.Trim();
    }

    /// <summary>The files a tag holds under one folder of the repository, at any depth.</summary>
    private static List<string> PublishedFiles(string root, string tag, string folder) =>
        Git(root, "ls-tree", "-r", "--name-only", tag, folder + "/")?
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(file => file.EndsWith(".json", StringComparison.Ordinal))
            .ToList() ?? [];

    /// <summary>Everything the documents in <paramref name="root"/>'s tree took from what <paramref name="tag"/> published.</summary>
    private static List<string> Problems(string root, string tag)
    {
        var problems = new List<string>();
        foreach (var file in PublishedFiles(root, tag, Journal))
        {
            var published = Assert.IsType<JsonObject>(JsonNode.Parse(Git(root, "show", $"{tag}:{file}")!));
            problems.AddRange(Narrowed(published, Current(root, (string)published["kind"]!)));
        }

        foreach (var file in PublishedFiles(root, tag, Fixtures))
        {
            var fixture = Assert.IsType<JsonObject>(JsonNode.Parse(Git(root, "show", $"{tag}:{file}")!));
            if ((string?)(fixture["expect"] as JsonValue) != "valid" || Current(root, (string)fixture["kind"]!) is not { } current)
            {
                continue;
            }

            var refused = SchemaValidator.Verdicts(SchemaValidator.Validate(fixture["body"], (JsonObject)current["body"]!));
            if (refused.Count > 0)
            {
                problems.Add(
                    $"{fixture["kind"]}: the body `{fixture["case"]}` was published as valid and is refused now at "
                    + string.Join(", ", refused.Select(problem => problem.Pointer).Distinct(StringComparer.Ordinal)) + ".");
            }
        }

        return problems;
    }

    /// <summary>A kind's document in the tree being built, or null where it has none.</summary>
    private static JsonObject? Current(string root, string kind)
    {
        var path = Path.Combine(root, Journal, kind + ".json");
        return File.Exists(path) ? Assert.IsType<JsonObject>(JsonNode.Parse(File.ReadAllText(path))) : null;
    }

    /// <summary>What <paramref name="current"/>, or its absence, took from <paramref name="published"/>.</summary>
    private static IEnumerable<string> Narrowed(JsonObject published, JsonObject? current)
    {
        var kind = (string)published["kind"]!;
        if (current is null)
        {
            yield return $"{kind}: is no longer documented.";
            yield break;
        }

        if ((string?)published["since"] != (string?)current["since"])
        {
            yield return $"{kind}: `since` was {published["since"]} and is now {current["since"]}.";
        }

        var (wasDescribed, wasRequired) = Places((JsonObject)published["body"]!);
        var (described, required) = Places((JsonObject)current["body"]!);
        foreach (var place in wasDescribed.Where(place => !described.Contains(place)).Order(StringComparer.Ordinal))
        {
            yield return $"{kind}: `{place}` is no longer in the body.";
        }

        foreach (var place in required.Where(place => !wasRequired.Contains(place)).Order(StringComparer.Ordinal))
        {
            yield return $"{kind}: `{place}` is required now and was not.";
        }

        if (IsTrue(published["subscribable"]) && !IsTrue(current["subscribable"]))
        {
            yield return $"{kind}: was subscribable and is not now.";
        }

        var since = current["field_since"] as JsonObject;
        foreach (var (place, _) in (published["field_since"] as JsonObject ?? []).OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            if (since?.ContainsKey(place) != true)
            {
                yield return $"{kind}: `field_since` no longer says when `{place}` was added.";
            }
        }

        var entities = current["entities"] as JsonObject;
        foreach (var (name, node) in (published["entities"] as JsonObject ?? []).OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            if (entities?[name] is not JsonObject entity)
            {
                yield return $"{kind}: a line no longer names its `{name}`.";
                continue;
            }

            if ((string?)node!["presence"] == "always" && (string?)entity["presence"] != "always")
            {
                yield return $"{kind}: `{name}` was named on every line and is now named on some.";
            }

            if (IsTrue(node!["each"]) != IsTrue(entity["each"]))
            {
                yield return $"{kind}: `{name}` changed whether one place names one of them or many.";
            }
        }
    }

    private static bool IsTrue(JsonNode? node) => node is JsonValue value && value.GetValueKind() == JsonValueKind.True;

    /// <summary>
    /// Every place a body describes a member, as a pointer with <c>*</c> for an array's index, and those it requires:
    /// through <c>properties</c>, <c>items</c>, every branch of <c>anyOf</c> and <c>allOf</c>, and <c>$ref</c> into the
    /// body's <c>$defs</c>.
    /// </summary>
    private static (HashSet<string> Described, HashSet<string> Required) Places(JsonObject body)
    {
        var described = new HashSet<string>(StringComparer.Ordinal);
        var required = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<(JsonObject Schema, string At, int Depth)>();
        pending.Push((body, string.Empty, 0));
        while (pending.Count > 0)
        {
            var (schema, at, depth) = pending.Pop();
            if (depth > 64)
            {
                continue;
            }

            if (schema["properties"] is JsonObject properties)
            {
                foreach (var (name, child) in properties)
                {
                    described.Add($"{at}/{name}");
                    if (child is JsonObject member)
                    {
                        pending.Push((member, $"{at}/{name}", depth + 1));
                    }
                }
            }

            if (schema["required"] is JsonArray names)
            {
                foreach (var name in names.OfType<JsonValue>().Select(name => (string)name!))
                {
                    required.Add($"{at}/{name}");
                }
            }

            if (schema["items"] is JsonObject items)
            {
                pending.Push((items, $"{at}/*", depth + 1));
            }

            foreach (var combinator in new[] { "anyOf", "allOf" })
            {
                foreach (var branch in (schema[combinator] as JsonArray)?.OfType<JsonObject>() ?? [])
                {
                    pending.Push((branch, at, depth + 1));
                }
            }

            if (schema["$ref"] is JsonValue reference && ((string?)reference)?.StartsWith("#/$defs/", StringComparison.Ordinal) == true
                && (body["$defs"] as JsonObject)?[((string)reference!)["#/$defs/".Length..]] is JsonObject defined)
            {
                pending.Push((defined, at, depth + 1));
            }
        }

        return (described, required);
    }

    /// <summary>A made-up document as a release published it.</summary>
    private static JsonObject Published() => (JsonObject)JsonNode.Parse("""
        {
          "kind": "campaign_paused",
          "version": 1,
          "since": "0.28.0",
          "summary": "A campaign stopped being active.",
          "subscribable": true,
          "entities": {
            "campaign": { "at": "/campaign_id", "presence": "always" },
            "contact": { "at": "/new/contacts/*/id", "presence": "sometimes", "each": true }
          },
          "field_since": { "/new/added": "0.29.0" },
          "body": {
            "type": "object",
            "additionalProperties": false,
            "required": ["key", "old", "new"],
            "properties": {
              "key": { "type": "null" },
              "old": { "type": "null" },
              "new": {
                "type": "object",
                "additionalProperties": false,
                "required": ["contacts"],
                "properties": {
                  "contacts": { "type": "array", "items": { "type": "object", "properties": { "id": { "type": "string" } } } },
                  "reason": { "type": "string" },
                  "added": { "type": "string" }
                }
              }
            }
          }
        }
        """)!;

    /// <summary>A body the made-up document's release published as valid.</summary>
    private static JsonObject Fixture() => (JsonObject)JsonNode.Parse("""
        {
          "kind": "campaign_paused",
          "version": 1,
          "case": "a pause with its reason",
          "body": { "key": null, "old": null, "new": { "contacts": [{ "id": "cnt_1" }], "reason": "a holiday", "added": "yes" } },
          "expect": "valid"
        }
        """)!;

    /// <summary>
    /// git in <paramref name="root"/>, its output, or null where it refused: no tag reachable, or no such tree. The
    /// machine's own settings are kept out — no system configuration, and the line endings, the signing and the identity
    /// set here — so a made-up repository holds exactly the bytes a row wrote; reading the checkout is the same either way.
    /// </summary>
    private static string? Git(string root, params string[] args)
    {
        var start = new ProcessStartInfo("git")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            WorkingDirectory = root,
        };
        start.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        foreach (var setting in (string[])["core.autocrlf=false", "commit.gpgsign=false", "tag.gpgsign=false", "init.defaultBranch=main", "user.name=Jason Tests", "user.email=tests@example.invalid", "maintenance.auto=false", "gc.auto=0", "core.longpaths=true"])
        {
            start.ArgumentList.Add("-c");
            start.ArgumentList.Add(setting);
        }

        foreach (var argument in args)
        {
            start.ArgumentList.Add(argument);
        }

        using var git = Process.Start(start)!;
        var stdout = git.StandardOutput.ReadToEndAsync();
        _ = git.StandardError.ReadToEndAsync();
        git.WaitForExit();
        return git.ExitCode == 0 ? stdout.Result : null;
    }

    /// <summary>
    /// A made-up repository whose first commit publishes one document and a body fixture of it, tagged as a release where
    /// a row says so, for the row to commit what a later tree holds on top of.
    /// </summary>
    private sealed class Repository : IDisposable
    {
        public Repository(JsonObject published, string? tag = "v0.1.0")
        {
            Directory.CreateDirectory(Root);
            Run("init", "--quiet");
            Write(Path.Combine(Fixtures, "campaign_paused", "body-a-pause.json"), Fixture());
            Commit(published, tag);
        }

        public string Root { get; } = Path.Combine(Path.GetTempPath(), "jason-history-tests", Guid.NewGuid().ToString("N"));

        /// <summary>Commits the document as <paramref name="document"/> says, or without it where it is null.</summary>
        public void Commit(JsonObject? document, string? tag = null)
        {
            var path = Path.Combine(Journal, "campaign_paused.json");
            if (document is null)
            {
                File.Delete(Path.Combine(Root, path));
            }
            else
            {
                Write(path, document);
            }

            Run("add", "--all");
            Run("commit", "--quiet", "--allow-empty", "-m", "documents");
            if (tag is not null)
            {
                Run("tag", tag);
            }
        }

        public List<string> Problems() => JournalKindHistoryTests.Problems(Root, Assert.IsType<string>(PublishedTag(Root)));

        public void Dispose()
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }

                Directory.Delete(Root, recursive: true);
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
            }
        }

        private void Write(string relative, JsonObject document)
        {
            var full = Path.Combine(Root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, document.ToJsonString() + "\n");
        }

        private void Run(params string[] args) =>
            Assert.True(Git(Root, args) is not null, $"git {string.Join(' ', args)} failed in a made-up repository.");
    }
}
