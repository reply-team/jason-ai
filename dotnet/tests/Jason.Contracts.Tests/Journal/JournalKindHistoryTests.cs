using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using Jason.Contracts.Journal;
using Jason.Contracts.Tests.OperationContracts;

namespace Jason.Contracts.Tests.Journal;

/// <summary>
/// A kind's document only grows. A reader written against a release's documents keeps reading every line a later release
/// writes, so a later document keeps every kind, every <c>since</c> and every body field the published one had, and
/// requires nothing the published one did not. The published documents are the last release's, read from its tag.
/// </summary>
public sealed class JournalKindHistoryTests
{
    private const string Journal = "docs/contracts/journal";

    /// <summary>The documents this build embeds against the last published release's, where that release had any.</summary>
    [Fact]
    public void No_document_of_the_last_published_release_is_removed_moved_or_narrowed()
    {
        var tag = Git("describe", "--tags", "--abbrev=0", "--match", "v[0-9]*.[0-9]*.[0-9]*", "HEAD");
        if (tag is null)
        {
            var failure = NoReleaseTagFailure(UnderCi);
            Assert.True(failure is null, failure);
            Assert.Skip("No release tag is reachable from this checkout, so there is no published release to compare with.");
        }

        tag = tag!.Trim();

        var files = Git("ls-tree", "--name-only", tag, Journal + "/")?
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(file => file.EndsWith(".json", StringComparison.Ordinal))
            .ToList() ?? [];
        Assert.SkipWhen(files.Count == 0, $"{tag} publishes no journal documents, so there is nothing yet to keep.");

        var problems = new List<string>();
        foreach (var file in files)
        {
            var published = Assert.IsType<JsonObject>(JsonNode.Parse(Git("show", $"{tag}:{file}")!));
            var kind = (string)published["kind"]!;
            var current = JournalKindCatalog.Find(kind);
            problems.AddRange(Narrowed(
                published,
                current is null ? null : Assert.IsType<JsonObject>(JsonNode.Parse(File.ReadAllText(Path.Combine(ContractFiles.Root, Journal, kind + ".json"))))));
        }

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
    }

    [Fact]
    public void Outside_ci_no_reachable_release_tag_skips()
    {
        Assert.Null(NoReleaseTagFailure(underCi: false));
    }

    [Fact]
    public void A_document_that_only_grew_is_kept()
    {
        var grown = Published();
        var added = (JsonObject)grown["body"]!["properties"]!["new"]!["properties"]!;
        added["note"] = new JsonObject { ["type"] = "string" };
        grown["field_since"] = new JsonObject { ["/new/note"] = "0.31.0" };

        Assert.Empty(Narrowed(Published(), grown));
    }

    [Theory]
    [InlineData("removed", "is no longer documented")]
    [InlineData("since", "`since` was 0.28.0 and is now 0.29.0")]
    [InlineData("field removed", "`/new/added` is no longer in the body")]
    [InlineData("item field removed", "`/new/contacts/*/id` is no longer in the body")]
    [InlineData("newly required", "`/new/reason` is required now and was not")]
    public void A_document_that_lost_or_narrowed_anything_is_refused(string change, string problem)
    {
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
        }

        Assert.Contains(Narrowed(Published(), current), said => said.Contains(problem, StringComparison.Ordinal));
    }

    private static bool UnderCi => Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true";

    /// <summary>Why a missing release tag fails the test, or null where it only skips it.</summary>
    private static string? NoReleaseTagFailure(bool underCi) => underCi
        ? "No release tag is reachable from this checkout under CI, so nothing would compare the documents with the last "
          + "published release's: the build-test-publish job's actions/checkout in .github/workflows/ci.yml must keep "
          + "fetch-depth: 0, which brings the tags."
        : null;

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
    }

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
          "entities": { "campaign": { "at": "/campaign_id", "presence": "always" } },
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

    /// <summary>What git printed, or null where it refused: no tag reachable, or no such tree.</summary>
    private static string? Git(params string[] args)
    {
        var start = new ProcessStartInfo("git")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            WorkingDirectory = ContractFiles.Root,
        };
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
}
