using System.Text.Json.Nodes;
using Jason.Contracts.Context;
using Jason.Contracts.Events;
using Jason.Contracts.Notifications;
using Jason.Contracts.Operations;
using Jason.Contracts.Preferences;

namespace Jason.Contracts.Tests.Context;

/// <summary>
/// The reserved keys of a campaign's context: a family of its own beside the canonical operations, the delivery contract,
/// the events contract and the named preferences, never among them. Every other key of a context is the caller's own and
/// the runtime never reads it; a reserved key's value is held to its published document.
/// </summary>
public sealed class ContextKeyContractTests
{
    [Fact]
    public void The_family_publishes_the_campaign_s_goals_at_version_1()
    {
        Assert.Equal(["goals"], ContextKeyCatalog.Keys);
        Assert.Equal(1, ContextKeyCatalog.Find("goals")!.Version);
        Assert.Equal("id", ContextKeyCatalog.Find("goals")!.DistinctBy);
        Assert.Null(ContextKeyCatalog.Find("goal"));
    }

    [Fact]
    public void A_reserved_key_is_neither_an_operation_a_delivery_an_events_read_nor_a_preference()
    {
        foreach (var key in ContextKeyCatalog.Keys)
        {
            Assert.False(OperationCatalog.Knows(key), $"{key} is published as a canonical operation too.");
            Assert.False(DeliveryCatalog.Knows(key), $"{key} is published as a delivery contract too.");
            Assert.False(EventsCatalog.Knows(key), $"{key} is published as an events contract too.");
            Assert.False(PreferenceCatalog.Knows(key), $"{key} is published as a named preference too.");
        }

        Assert.Equal(Directory.EnumerateFiles(Path.Combine(OperationContracts.ContractFiles.Package, "context"), "*.json").Count(), ContextKeyCatalog.All.Count);
    }

    /// <summary>Held here rather than as the document loads, as the other families' are: checking the dialect compiles every pattern.</summary>
    [Fact]
    public void Every_value_schema_is_in_the_published_dialect()
    {
        Assert.All(ContextKeyCatalog.All, contract =>
        {
            var problems = SchemaValidator.CheckDialect(contract.ValueSchema);
            Assert.True(problems.Count == 0, $"{contract.Key}: {string.Join("; ", problems.Select(problem => problem.Pointer + " " + problem.Reason))}");
        });
    }

    /// <summary>
    /// The fields a role reads from each goal, and the closed list of kinds a role relies on: a kind added here is a kind
    /// no skill knows yet, so the list is pinned where a change to it has to be made on purpose.
    /// </summary>
    [Fact]
    public void A_goal_is_an_id_a_kind_a_title_when_it_is_reached_and_an_order()
    {
        var schema = ContextKeyCatalog.Find("goals")!.ValueSchema;
        var goal = (JsonObject)schema["items"]!;

        Assert.Equal("array", (string?)schema["type"]);
        Assert.Equal(["id", "kind", "title", "reached_when", "order"], Names(goal["required"]));
        Assert.Equal(["id", "kind", "title", "reached_when", "order"], ((JsonObject)goal["properties"]!).Select(pair => pair.Key));
        Assert.Equal(["meeting", "demo", "trial", "interested_reply", "purchase", "custom"], Names(goal["properties"]!["kind"]!["enum"]));
        Assert.False((bool)goal["additionalProperties"]!);
    }

    [Theory]
    [InlineData("goals")]
    [InlineData("a")]
    [InlineData("standing_approvals2")]
    public void A_key_in_the_grammar_is_well_formed(string key) => Assert.True(ContextKeyContract.IsWellFormedKey(key));

    [Theory]
    [InlineData("")]
    [InlineData("Goals")]
    [InlineData("_goals")]
    [InlineData("2goals")]
    [InlineData("campaign.goals")]
    [InlineData("goal-list")]
    public void A_key_outside_the_grammar_is_not(string key) => Assert.False(ContextKeyContract.IsWellFormedKey(key));

    [Fact]
    public void A_key_is_at_most_64_characters()
    {
        Assert.True(ContextKeyContract.IsWellFormedKey(new string('a', 64)));
        Assert.False(ContextKeyContract.IsWellFormedKey(new string('a', 65)));
    }

    [Theory]
    [InlineData("key")]
    [InlineData("version")]
    [InlineData("summary")]
    [InlineData("value_schema")]
    public void A_document_missing_a_field_is_refused(string field)
    {
        var document = Document("goals");
        document.Remove(field);

        var refused = Assert.Throws<InvalidOperationException>(() => ContextKeyContract.Parse("goals.json", document));
        Assert.Contains($"`{field}` is missing", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_document_with_a_field_of_its_own_is_refused()
    {
        var document = Document("goals");
        document["scope"] = "campaign";

        var refused = Assert.Throws<InvalidOperationException>(() => ContextKeyContract.Parse("goals.json", document));
        Assert.Contains("`scope` is not part of a reserved context key", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_document_whose_key_is_not_its_file_s_name_is_refused()
    {
        var refused = Assert.Throws<InvalidOperationException>(() => ContextKeyContract.Parse("goal.json", Document("goals")));
        Assert.Contains("`goals` is published in goal.json", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_document_whose_key_is_outside_the_grammar_is_refused()
    {
        var document = Document("goals");
        document["key"] = "campaign.goals";

        var refused = Assert.Throws<InvalidOperationException>(() => ContextKeyContract.Parse("campaign.goals.json", document));
        Assert.Contains("is not a context key", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_version_below_1_is_refused()
    {
        var document = Document("goals");
        document["version"] = 0;

        var refused = Assert.Throws<InvalidOperationException>(() => ContextKeyContract.Parse("goals.json", document));
        Assert.Contains("`version` is a whole number from 1", refused.Message, StringComparison.Ordinal);
    }

    /// <summary>A member to tell items apart by is one every item of a list must carry; anywhere else it could never be read.</summary>
    [Theory]
    [InlineData("title_of_its_own")]
    [InlineData(null)]
    public void Distinct_by_names_a_member_every_item_of_a_list_requires(string? member)
    {
        var document = Document("goals");
        if (member is null)
        {
            document["value_schema"] = new JsonObject { ["type"] = "object" };
        }
        else
        {
            document["distinct_by"] = member;
        }

        var refused = Assert.Throws<InvalidOperationException>(() => ContextKeyContract.Parse("goals.json", document));
        Assert.Contains("`distinct_by` names a member every item of a list must carry", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_document_without_distinct_by_tells_no_items_apart()
    {
        var document = Document("goals");
        document.Remove("distinct_by");

        var contract = ContextKeyContract.Parse("goals.json", document);

        Assert.Null(contract.DistinctBy);
        Assert.Empty(contract.CheckValue(new JsonArray(Goal("first"), Goal("first"))));
    }

    /// <summary>
    /// Items that share the member are refused after the schema has accepted the list, the later one at its member's
    /// pointer, so the error names the item to change; a list the schema refuses is answered with the schema's words alone.
    /// </summary>
    [Fact]
    public void Items_that_share_their_distinct_member_are_refused_at_the_later_one()
    {
        var goals = ContextKeyCatalog.Find("goals")!;

        Assert.Empty(goals.CheckValue(new JsonArray(Goal("first"), Goal("second"))));

        var repeated = goals.CheckValue(new JsonArray(Goal("first"), Goal("second"), Goal("first")));
        var problem = Assert.Single(repeated);
        Assert.Equal("/2/id", problem.Pointer);
        Assert.Equal(ContextKeyContract.UniqueItems, problem.Reason);

        var bothWrong = goals.CheckValue(new JsonArray(Goal("first"), new JsonObject { ["id"] = "first" }));
        Assert.DoesNotContain(bothWrong, found => found.Reason == ContextKeyContract.UniqueItems);
    }

    private static JsonObject Goal(string id) => new()
    {
        ["id"] = id,
        ["kind"] = "demo",
        ["title"] = "A demo with the team",
        ["reached_when"] = "a demo is on the calendar",
        ["order"] = 1,
    };

    private static string[] Names(JsonNode? array) => [.. ((JsonArray)array!).Select(name => (string)name!)];

    private static JsonObject Document(string key) => (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(
        OperationContracts.ContractFiles.Package, "context", key + ".json")))!;
}
