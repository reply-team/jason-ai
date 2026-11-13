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
/// <c>field_since</c> entry the published one had, keeps every word of a closed list, accepts every body the release published as valid, and requires
/// nothing the published one did not of a body the release described: a member only a branch added to an <c>anyOf</c> the
/// release already had requires, or one inside an object the release never described, no published body could have
/// needed. The release's branches are known by their place in the list alone, so a branch put before them, or a change to
/// any of them, makes every branch of that list the release's and a member it newly requires is refused. The published
/// documents are the last release's, read from its tag.
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
    [InlineData("a new branch requires an object the release never described")]
    [InlineData("an optional object the release never described requires its members")]
    [InlineData("a word added to a closed list")]
    [InlineData("a closed list opened")]
    public void A_document_that_only_grew_is_kept(string change)
    {
        var branched = change == "a new branch requires an object the release never described";
        var listed = change is "a word added to a closed list" or "a closed list opened";
        var published = listed ? Listed() : branched ? Branched() : Published();
        if (change == "became subscribable")
        {
            published["subscribable"] = false;
        }

        using var repository = new Repository(published);
        var current = listed ? Listed() : branched ? Branched() : Published();
        var body = (JsonObject)current["body"]!["properties"]!["new"]!;
        switch (change)
        {
            case "a word added to a closed list":
                body["properties"]!["reason"] = JsonNode.Parse("""{ "enum": ["a holiday", "a strike", "a storm"] }""");
                break;
            case "a closed list opened":
                body["properties"]!["reason"] = new JsonObject { ["type"] = "string" };
                break;
            case "grew":
                ((JsonObject)body["properties"]!)["note"] = new JsonObject { ["type"] = "string" };
                ((JsonObject)current["field_since"]!)["/new/note"] = "0.31.0";
                break;
            case "a new branch requires an object the release never described":
                // The shape of a hold's `held_by`: a member only a new branch requires, whose own members are required.
                ((JsonObject)body["properties"]!)["held"] = JsonNode.Parse("""
                    { "type": "object", "additionalProperties": false, "required": ["code", "limit"],
                      "properties": { "code": { "type": "string" }, "limit": { "type": "integer" } } }
                    """);
                ((JsonArray)body["anyOf"]!).Add(JsonNode.Parse("""{ "required": ["held"] }"""));
                ((JsonObject)current["field_since"]!)["/new/held"] = "0.31.0";
                break;
            case "an optional object the release never described requires its members":
                ((JsonObject)body["properties"]!)["note"] = JsonNode.Parse("""
                    { "type": "object", "required": ["text"], "properties": { "text": { "type": "string" } } }
                    """);
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
    [InlineData("newly required at the root", "`/note` is required now and was not")]
    [InlineData("newly required in a branch the release had", "`/new/note` is required now and was not")]
    [InlineData("newly required in a branch of the release's put after a new one", "`/new/note` is required now and was not")]
    [InlineData("newly required by a new branch where the release described it", "`/new/reason` is required now and was not")]
    [InlineData("newly required by a new branch of an allOf", "`/new/held` is required now and was not")]
    [InlineData("newly required by a branch added to an allOf the release had", "`/new/held` is required now and was not")]
    [InlineData("newly required by the one branch of a first anyOf", "`/held` is required now and was not")]
    [InlineData("newly required in a branch of the release's moved and widened", "`/new/note` is required now and was not")]
    [InlineData("newly required in a branch of the release's moved and described", "`/new/note` is required now and was not")]
    [InlineData("published body refused", "the body `a pause with its reason` was published as valid and is refused now at /new/reason")]
    [InlineData("field_since dropped", "`field_since` no longer says when `/new/added` was added")]
    [InlineData("entity dropped", "a line no longer names its `campaign`")]
    [InlineData("named on some lines", "`campaign` was named on every line and is now named on some")]
    [InlineData("each changed", "`contact` changed whether one place names one of them or many")]
    [InlineData("no longer subscribable", "was subscribable and is not now")]
    [InlineData("a word removed from a closed list", "`/new/reason` no longer accepts `a strike`")]
    [InlineData("a word removed from a list one branch holds", "`/new/kind` no longer accepts `b`")]
    [InlineData("a const changed", "`/new/pinned` no longer accepts `yes`")]
    public void A_document_that_lost_or_narrowed_anything_is_refused(string change, string problem)
    {
        var listed = change.Contains("list", StringComparison.Ordinal) || change == "a const changed";
        var branched = !listed && change.Contains("branch", StringComparison.Ordinal);
        var published = listed ? Listed() : branched ? Branched() : Published();
        var current = listed ? Listed() : branched ? Branched() : Published();
        var body = (JsonObject)current["body"]!["properties"]!["new"]!;
        if (change == "newly required by a branch added to an allOf the release had")
        {
            var all = """[{ "properties": { "reason": { "type": "string" } } }]""";
            published["body"]!["properties"]!["new"]!["allOf"] = JsonNode.Parse(all);
            body["allOf"] = JsonNode.Parse(all);
        }

        using var repository = new Repository(published);
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
            case "newly required at the root":
                ((JsonObject)current["body"]!["properties"]!)["note"] = new JsonObject { ["type"] = "string" };
                ((JsonArray)current["body"]!["required"]!).Add("note");
                break;
            case "newly required in a branch the release had":
                // Grown alone, so the list still says what the release's said: the branch is the release's by its place in it.
                body["anyOf"]![0] = JsonNode.Parse("""
                    { "properties": { "reason": { "enum": ["a holiday"] }, "note": { "type": "string" } }, "required": ["added", "note"] }
                    """);
                break;
            case "newly required in a branch of the release's put after a new one":
                // The release's second branch, grown, now third: a new branch in the release's first place makes the list the release's.
                body["anyOf"] = JsonNode.Parse("""
                    [
                      { "properties": { "reason": { "type": "string" } } },
                      { "properties": { "reason": { "enum": ["a holiday"] } }, "required": ["added"] },
                      { "properties": { "added": { "type": "string" }, "note": { "type": "string" } }, "required": ["note"] }
                    ]
                    """);
                break;
            case "newly required by a new branch where the release described it":
                // New by `held`, which the release never described; `reason` it did, as optional.
                ((JsonArray)body["anyOf"]!).Add(JsonNode.Parse("""{ "properties": { "held": { "type": "string" } }, "required": ["held", "reason"] }"""));
                break;
            case "newly required by a new branch of an allOf":
                // Every body must match every branch of an `allOf`, so its one branch is a requirement of every body.
                body["allOf"] = JsonNode.Parse("""[{ "properties": { "held": { "type": "string" } }, "required": ["held"] }]""");
                break;
            case "newly required by a branch added to an allOf the release had":
                ((JsonArray)body["allOf"]!).Add(JsonNode.Parse("""{ "properties": { "held": { "type": "string" } }, "required": ["held"] }"""));
                break;
            case "newly required by the one branch of a first anyOf":
                // The root had no `anyOf`, so a body matches its one branch or nothing.
                current["body"]!["anyOf"] = JsonNode.Parse("""[{ "properties": { "held": { "type": "string" } }, "required": ["held"] }]""");
                break;
            case "newly required in a branch of the release's moved and widened":
                // The release's second branch, widened and grown, now third, with another branch in its place.
                body["anyOf"] = JsonNode.Parse("""
                    [
                      { "properties": { "reason": { "enum": ["a holiday"] } }, "required": ["added"] },
                      { "properties": { "reason": { "enum": ["a strike"] } }, "required": ["added"] },
                      { "properties": { "added": { "type": ["string", "null"] }, "note": { "type": "string" } }, "required": ["note"] }
                    ]
                    """);
                break;
            case "newly required in a branch of the release's moved and described":
                // The same move, the release's second branch changed by a description alone besides its growth.
                body["anyOf"] = JsonNode.Parse("""
                    [
                      { "properties": { "reason": { "enum": ["a holiday"] } }, "required": ["added"] },
                      { "properties": { "reason": { "enum": ["a strike"] } }, "required": ["added"] },
                      { "description": "Any other pause.", "properties": { "added": { "type": "string" }, "note": { "type": "string" } }, "required": ["note"] }
                    ]
                    """);
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
            case "a word removed from a closed list":
                body["properties"]!["reason"] = JsonNode.Parse("""{ "enum": ["a holiday"] }""");
                break;
            case "a word removed from a list one branch holds":
                body["anyOf"]![0]!["properties"]!["kind"] = JsonNode.Parse("""{ "enum": ["a"] }""");
                break;
            case "a const changed":
                body["properties"]!["pinned"] = JsonNode.Parse("""{ "const": "no" }""");
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
          + "tests (each part of a platform's tests) and release-stamp, must keep fetch-depth: 0, which brings the tags."
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

        var was = Places((JsonObject)published["body"]!);
        var now = Places((JsonObject)current["body"]!, was);
        foreach (var place in was.Described.Where(place => !now.Described.Contains(place)).Order(StringComparer.Ordinal))
        {
            yield return $"{kind}: `{place}` is no longer in the body.";
        }

        // A place that left its closed list altogether accepts more than it did, and one no longer described is said above.
        foreach (var (place, words) in was.Words.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            if (now.Words.TryGetValue(place, out var kept))
            {
                foreach (var word in words.Where(word => !kept.Contains(word)).Order(StringComparer.Ordinal))
                {
                    yield return $"{kind}: `{place}` no longer accepts `{word}`.";
                }
            }
        }

        // Newly required narrows the kind only where a body the release described could have been held to it: the place
        // itself was described (and optional), or an object the release described, or a branch of the release's, requires it.
        foreach (var (place, by) in now.Required.Where(entry => !was.Required.ContainsKey(entry.Key)).OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            if (was.Described.Contains(place) || by == Requirement.Release)
            {
                yield return $"{kind}: `{place}` is required now and was not.";
            }
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
    /// The words the closed list at <paramref name="at"/> accepts, <c>enum</c> and <c>const</c> alike, added to the place's. A place's
    /// words are a union over its branches, so a word one <c>anyOf</c> branch drops while another keeps it is not seen as removed, nor
    /// a closed list an <c>allOf</c> branch narrows.
    /// </summary>
    private static void AddWords(Shape into, string at, JsonObject schema)
    {
        var listed = (schema["enum"] as JsonArray)?.ToList() ?? [];
        if (schema.TryGetPropertyValue("const", out var single))
        {
            listed.Add(single);
        }

        foreach (var word in listed)
        {
            if (!into.Words.TryGetValue(at, out var words))
            {
                into.Words[at] = words = new(StringComparer.Ordinal);
            }

            words.Add(word is JsonValue value && value.GetValueKind() == JsonValueKind.String ? (string)value! : word?.ToJsonString() ?? "null");
        }
    }

    /// <summary>Where a place's requirement comes from, as the last published release reads it.</summary>
    private enum Requirement
    {
        /// <summary>The root, an object the release described, an <c>allOf</c> branch, or any branch of a list the release had: a body it described could be held to it.</summary>
        Release,

        /// <summary>Only a branch added to an <c>anyOf</c> the release had, or an object the release never described: no body it published could have needed it.</summary>
        New,
    }

    /// <summary>
    /// What a body describes and requires. <see cref="Required"/> keeps, for each place, where its requirement comes from,
    /// <see cref="Requirement.Release"/> wherever one of its requirements is; <see cref="Branches"/> holds every branch by
    /// its location in the document, such as <c>#/properties/new/anyOf/0</c>, for a later document to know the release's by.
    /// <see cref="Words"/> holds, for each place with a closed list, every word any branch accepts there.
    /// </summary>
    private sealed record Shape(
        HashSet<string> Described,
        Dictionary<string, Requirement> Required,
        Dictionary<string, JsonObject> Branches,
        Dictionary<string, HashSet<string>> Words)
    {
        public static Shape Empty() => new(new(StringComparer.Ordinal), new(StringComparer.Ordinal), new(StringComparer.Ordinal), new(StringComparer.Ordinal));
    }

    /// <summary>
    /// Every place a body describes a member, as a pointer with <c>*</c> for an array's index, and those it requires:
    /// through <c>properties</c>, <c>items</c>, every branch of <c>anyOf</c> and <c>allOf</c>, and <c>$ref</c> into the
    /// body's <c>$defs</c>. Read against <paramref name="release"/>, the shape of the last published release's
    /// document, a requirement is <see cref="Requirement.New"/> where an object whose own place the release never
    /// described makes it, or where it lies inside a new branch: a branch of an <c>anyOf</c> the release had with at
    /// least one branch, at an index the release's list did not reach, while every branch at an index the release had
    /// still says what the release's said there once every place the release never described is set aside. Any other
    /// branch is the release's: every branch of an <c>allOf</c>, of a first <c>anyOf</c>, and of a list one of whose
    /// release branches was moved or changed.
    /// </summary>
    private static Shape Places(JsonObject body, Shape? release = null)
    {
        var shape = Shape.Empty();
        Walk(shape, body, string.Empty, "#", Requirement.Release, 0);
        return shape;

        void Walk(Shape into, JsonObject schema, string at, string location, Requirement by, int depth)
        {
            if (depth > 64)
            {
                return;
            }

            if (schema["properties"] is JsonObject properties)
            {
                foreach (var (name, child) in properties)
                {
                    into.Described.Add($"{at}/{name}");
                    if (child is JsonObject member)
                    {
                        Walk(into, member, $"{at}/{name}", $"{location}/properties/{name}", by, depth + 1);
                    }
                }
            }

            AddWords(into, at, schema);

            if (schema["required"] is JsonArray names)
            {
                var owner = release is null || ObjectDescribed(release.Described, at) ? by : Requirement.New;
                foreach (var name in names.OfType<JsonValue>().Select(name => (string)name!))
                {
                    var place = $"{at}/{name}";
                    into.Required[place] = into.Required.GetValueOrDefault(place, owner) == Requirement.Release ? Requirement.Release : owner;
                }
            }

            if (schema["items"] is JsonObject items)
            {
                Walk(into, items, $"{at}/*", $"{location}/items", by, depth + 1);
            }

            foreach (var combinator in new[] { "anyOf", "allOf" })
            {
                var branches = (schema[combinator] as JsonArray)?.OfType<JsonObject>().ToList() ?? [];
                var had = combinator == "anyOf" && by == Requirement.Release && release is not null
                    ? Kept(branches, at, $"{location}/{combinator}")
                    : 0;
                for (var index = 0; index < branches.Count; index++)
                {
                    var here = $"{location}/{combinator}/{index}";
                    into.Branches[here] = branches[index];
                    var within = had > 0 && index >= had ? Requirement.New : by;
                    Walk(into, branches[index], at, here, within, depth + 1);
                }
            }

            if (schema["$ref"] is JsonValue reference && ((string?)reference)?.StartsWith("#/$defs/", StringComparison.Ordinal) == true
                && (body["$defs"] as JsonObject)?[((string)reference!)["#/$defs/".Length..]] is JsonObject defined)
            {
                Walk(into, defined, at, (string)reference!, by, depth + 1);
            }
        }

        // How many branches the release had in the `anyOf` at `list`, where each of them still stands at its index and says
        // what it said there once every place the release never described is set aside; otherwise none, which reads every
        // branch of the list as the release's.
        int Kept(List<JsonObject> branches, string at, string list)
        {
            var had = 0;
            for (; release!.Branches.TryGetValue($"{list}/{had}", out var was); had++)
            {
                if (had >= branches.Count
                    || !JsonNode.DeepEquals(Known(branches[had], at, release.Described), Known(was, at, release.Described)))
                {
                    return 0;
                }
            }

            return had;
        }
    }

    /// <summary>Whether the object at <paramref name="at"/> is one the release described: the root, or a described place, through its items.</summary>
    private static bool ObjectDescribed(HashSet<string> described, string at)
    {
        while (at.EndsWith("/*", StringComparison.Ordinal))
        {
            at = at[..^2];
        }

        return at.Length == 0 || described.Contains(at);
    }

    /// <summary>
    /// A copy of <paramref name="schema"/>, standing at <paramref name="at"/>, with every place outside
    /// <paramref name="described"/> set aside, its member and its requirement both, and a set of members or a list of
    /// requirements that leaves empty dropped: a branch as much of it as the release could have known.
    /// </summary>
    private static JsonObject Known(JsonObject schema, string at, HashSet<string> described)
    {
        var copy = (JsonObject)schema.DeepClone();
        if (copy["properties"] is JsonObject properties)
        {
            foreach (var name in properties.Select(entry => entry.Key).ToList())
            {
                if (!described.Contains($"{at}/{name}"))
                {
                    properties.Remove(name);
                }
                else if (properties[name] is JsonObject member)
                {
                    properties[name] = Known(member, $"{at}/{name}", described);
                }
            }

            if (properties.Count == 0)
            {
                copy.Remove("properties");
            }
        }

        if (copy["required"] is JsonArray names)
        {
            var kept = names.OfType<JsonValue>().Select(name => (string)name!).Where(name => described.Contains($"{at}/{name}")).ToList();
            if (kept.Count == 0)
            {
                copy.Remove("required");
            }
            else
            {
                copy["required"] = new JsonArray([.. kept.Select(name => (JsonNode)JsonValue.Create(name))]);
            }
        }

        if (copy["items"] is JsonObject items)
        {
            copy["items"] = Known(items, $"{at}/*", described);
        }

        foreach (var combinator in new[] { "anyOf", "allOf" })
        {
            if (copy[combinator] is JsonArray branches)
            {
                copy[combinator] = new JsonArray([.. branches.Select(branch => branch is JsonObject inner ? Known(inner, at, described) : branch?.DeepClone())]);
            }
        }

        return copy;
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

    /// <summary>The made-up document with two branches under <c>/new</c>, the first one the published body holds.</summary>
    private static JsonObject Branched()
    {
        var document = Published();
        document["body"]!["properties"]!["new"]!["anyOf"] = JsonNode.Parse("""
            [
              { "properties": { "reason": { "enum": ["a holiday"] } }, "required": ["added"] },
              { "properties": { "added": { "type": "string" } } }
            ]
            """);
        return document;
    }

    /// <summary>The made-up document with closed lists: a list, a list one branch holds, and a list of one word.</summary>
    private static JsonObject Listed()
    {
        var document = Published();
        var body = (JsonObject)document["body"]!["properties"]!["new"]!;
        var properties = (JsonObject)body["properties"]!;
        properties["reason"] = JsonNode.Parse("""{ "enum": ["a holiday", "a strike"] }""");
        properties["pinned"] = JsonNode.Parse("""{ "const": "yes" }""");
        body["anyOf"] = JsonNode.Parse("""[{ "properties": { "kind": { "enum": ["a", "b"] } } }]""");
        return document;
    }

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
