using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Jason.Contracts.Api;
using Jason.Contracts.Execution;
using Jason.Contracts.Json;

namespace Jason.Runtime.Tests.Documentation;

/// <summary>
/// The launch envelope as the work-execution contract shows it, held to the envelope the runtime writes: the example is at
/// the current version and names exactly the members the contract has, at the top and in each of its fixed objects, so a
/// host written against the page is written against the runtime; and the rule for which memory files are named is said.
/// </summary>
public sealed partial class LaunchEnvelopeDocTests
{
    [Fact]
    public void The_example_is_at_the_current_version_and_names_every_member_the_envelope_has()
    {
        var shown = Example();
        var written = JsonNode.Parse(JsonSerializer.Serialize(Whole(), JasonJson.Options))!.AsObject();

        Assert.Equal(LaunchEnvelope.CurrentVersion, (int?)shown["envelope_version"]);
        Assert.Equal(Members(written), Members(shown));
        foreach (var member in (string[])["runtime", "role_memory", "memory_files", "resumption"])
        {
            Assert.True(shown[member] is JsonObject, $"The example's '{member}' is {shown[member]?.ToJsonString() ?? "null"}, not an object.");
            Assert.Equal(Members(written[member]!.AsObject()), Members(shown[member]!.AsObject()));
        }

        var item = shown["resumption"]!["items"] is JsonArray { Count: > 0 } items ? items[0] : null;
        Assert.True(item is JsonObject, $"The example's resumption names no item: {shown["resumption"]!.ToJsonString()}.");
        Assert.Equal(Members(written["resumption"]!["items"]![0]!.AsObject()), Members(item!.AsObject()));
    }

    [Fact]
    public void The_contract_says_when_a_memory_file_is_named()
    {
        var page = Whitespace().Replace(Section(), " ");

        Assert.Contains(
            "a path is given when Jason knows the folder is its own — the recorded `folder_path` for a project and a "
                + "campaign, the root marker for the root, which no record holds; no existence check on a recorded path, a "
                + "stale one is the host's to find and `project link` re-records it; the object itself is never null at 4; "
                + "additive over 3; the reading order is the skill's, never the runtime's.",
            page,
            StringComparison.Ordinal);
    }

    /// <summary>An envelope with every member given, so each is written as the contract names it.</summary>
    private static LaunchEnvelope Whole() => new(
        LaunchEnvelope.CurrentVersion,
        "att_A",
        1,
        "wi_A",
        "cmp_A",
        "cnt_A",
        WorkItemKind.AiRole,
        "researcher",
        "local-host",
        [],
        new JsonObject(),
        3600,
        120,
        DateTimeOffset.UnixEpoch,
        "/work",
        new RuntimeLocation("/run/runtime.json", "v1", "jason"),
        new RoleMemoryLocation("cmp_A", "researcher", "prj_A"),
        new MemoryFileLocations("/a/AGENTS.md", "/b/AGENTS.md", "/c/AGENTS.md"),
        new Resumption(
            "att_P",
            1,
            DateTimeOffset.UnixEpoch,
            WaitReleaseReason.ChildrenEnded,
            [new ResumedItem("wi_R", WorkItemKind.AiRole, "researcher", null, WorkItemStatus.Succeeded, null, null, new JsonObject(), false)]));

    private static List<string> Members(JsonObject node) => [.. node.Select(pair => pair.Key).Order(StringComparer.Ordinal)];

    /// <summary>The envelope's section of the page, from its heading to the next.</summary>
    private static string Section()
    {
        var page = File.ReadAllText(Path.Combine(RepositoryTree.Root(), "docs", "work-execution.md"));
        var section = EnvelopeSection().Match(page);
        Assert.True(section.Success, "docs/work-execution.md has no '### The launch envelope' section.");
        return section.Value;
    }

    /// <summary>The section's one JSON example.</summary>
    private static JsonObject Example()
    {
        var example = JsonBlock().Match(Section());
        Assert.True(example.Success, "The launch envelope's section of docs/work-execution.md shows no JSON example.");
        return JsonNode.Parse(example.Groups["json"].Value)!.AsObject();
    }

    [GeneratedRegex(@"^### The launch envelope\r?$.*?(?=^#{1,3} )", RegexOptions.Multiline | RegexOptions.Singleline)]
    private static partial Regex EnvelopeSection();

    [GeneratedRegex(@"^```json\r?\n(?<json>.*?)^```", RegexOptions.Multiline | RegexOptions.Singleline)]
    private static partial Regex JsonBlock();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
