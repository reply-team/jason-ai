using System.Text.Json.Nodes;
using Jason.Contracts.Context;
using Jason.Contracts.Events;
using Jason.Contracts.Journal;
using Jason.Contracts.Notifications;
using Jason.Contracts.Operations;
using Jason.Contracts.Preferences;
using Jason.Contracts.Tests.OperationContracts;

namespace Jason.Contracts.Tests.Journal;

/// <summary>
/// The kinds the runtime reserves for its own journal lines: a family of its own beside the operations, the delivery
/// contract, the events contract, the named preferences and the reserved context keys. A document is parsed strictly,
/// so a field misspelt or left out fails the build that embeds it rather than a reader who trusted it.
/// </summary>
public sealed class JournalKindContractTests
{
    private const string Source = "campaign_paused.json";

    /// <summary>A document every row below starts from and changes in one place.</summary>
    private static JsonObject Document() => (JsonObject)JsonNode.Parse("""
        {
          "kind": "campaign_paused",
          "version": 1,
          "since": "0.1.0",
          "summary": "A campaign stopped being active.",
          "subscribable": true,
          "entities": {
            "campaign": { "at": "/campaign_id", "presence": "always" },
            "contact": { "at": "/new/contacts/*/id", "presence": "sometimes", "each": true },
            "role": { "at": "/key", "presence": "sometimes" }
          },
          "field_since": { "/new/note": "0.12.0" },
          "body": {
            "type": "object",
            "additionalProperties": false,
            "required": ["key", "old", "new"],
            "properties": {
              "key": { "type": ["string", "null"] },
              "old": { "type": "null" },
              "new": {
                "type": "object",
                "additionalProperties": false,
                "required": ["contacts"],
                "properties": {
                  "contacts": { "type": "array", "items": { "type": "object", "properties": { "id": { "type": "string" } } } },
                  "note": { "type": "string" }
                }
              }
            }
          }
        }
        """)!;

    [Fact]
    public void A_document_reads_whole()
    {
        var contract = JournalKindContract.Parse(Source, Document());

        Assert.Equal("campaign_paused", contract.Kind);
        Assert.Equal(1, contract.Version);
        Assert.Equal("0.1.0", contract.Since);
        Assert.True(contract.Subscribable);
        Assert.Equal(new JournalKindEntity(["/campaign_id"], JournalEntityPresence.Always, false), contract.Entities["campaign"]);
        Assert.Equal(new JournalKindEntity(["/new/contacts/*/id"], JournalEntityPresence.Sometimes, true), contract.Entities["contact"]);
        Assert.Equal("0.12.0", contract.FieldSince["/new/note"]);
    }

    [Fact]
    public void A_field_the_family_does_not_know_is_refused()
    {
        var document = Document();
        document["meaning"] = "something";

        Refused(document, "`meaning` is not part of a journal kind's document");
    }

    [Theory]
    [InlineData("kind")]
    [InlineData("version")]
    [InlineData("since")]
    [InlineData("summary")]
    [InlineData("subscribable")]
    [InlineData("entities")]
    [InlineData("field_since")]
    [InlineData("body")]
    public void Every_field_is_required_and_absence_is_not_a_default(string field)
    {
        var document = Document();
        document.Remove(field);

        Refused(document, $"`{field}` is missing");
    }

    [Fact]
    public void A_kind_s_document_is_named_for_it()
    {
        var error = Assert.Throws<InvalidOperationException>(() => JournalKindContract.Parse("campaign_started.json", Document()));
        Assert.Contains("named for it: campaign_paused.json", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("0.1")]
    [InlineData("0.1.0.0")]
    [InlineData("01.1.0")]
    [InlineData("v0.1.0")]
    [InlineData("0.1.x")]
    public void Since_is_a_release_s_version(string since)
    {
        var document = Document();
        document["since"] = since;

        Refused(document, "`since` is the release that first wrote the kind");
    }

    /// <summary>A patch release may one day write a new kind, so the third number is any whole number, not a zero alone.</summary>
    [Fact]
    public void Since_may_name_a_patch_release()
    {
        var document = Document();
        document["since"] = "0.8.1";
        document["field_since"] = new JsonObject { ["/new/note"] = "0.8.2" };

        Assert.Equal("0.8.1", JournalKindContract.Parse(Source, document).Since);
    }

    [Fact]
    public void Versions_are_ordered_by_their_numbers()
    {
        Assert.True(JournalKindContract.CompareVersions("0.9.0", "0.10.0") < 0);
        Assert.True(JournalKindContract.CompareVersions("0.10.1", "0.10.0") > 0);
        Assert.Equal(0, JournalKindContract.CompareVersions("1.2.3", "1.2.3"));
    }

    [Theory]
    [InlineData("Two sentences. Of meaning.\nOn two lines.")]
    [InlineData("No full stop")]
    public void The_summary_is_one_sentence_on_one_line(string summary)
    {
        var document = Document();
        document["summary"] = summary;

        Refused(document, "`summary` is one sentence");
    }

    [Theory]
    [InlineData("""{ "type": "object", "required": ["key", "old", "new"], "properties": { "key": {}, "old": {}, "new": {} } }""")]
    [InlineData("""{ "type": "object", "additionalProperties": false, "required": ["key", "new"], "properties": { "key": {}, "old": {}, "new": {} } }""")]
    [InlineData("""{ "type": "object", "additionalProperties": false, "required": ["key", "old", "new"], "properties": { "key": {}, "new": {} } }""")]
    [InlineData("""{ "type": "object", "additionalProperties": false, "required": ["key", "old", "new"], "properties": { "key": {}, "old": {}, "new": {}, "reason": {} } }""")]
    [InlineData("""{ "type": ["object", "null"], "additionalProperties": false, "required": ["key", "old", "new"], "properties": { "key": {}, "old": {}, "new": {} } }""")]
    public void The_body_is_closed_and_names_and_requires_key_old_and_new(string body)
    {
        var document = Document();
        document["entities"] = new JsonObject();
        document["field_since"] = new JsonObject();
        document["body"] = JsonNode.Parse(body);

        Refused(document, "`body`");
    }

    [Fact]
    public void An_entity_outside_the_vocabulary_is_refused()
    {
        var document = Document();
        document["entities"]!["mailbox"] = new JsonObject { ["at"] = "/key", ["presence"] = "always" };

        Refused(document, "`mailbox` is not an entity a line names");
    }

    [Fact]
    public void The_vocabulary_is_the_fifteen_entities()
    {
        Assert.Equal(
            ["campaign", "project", "contact", "work_item", "attempt", "approval", "role", "plugin", "account", "decision", "report", "profile", "provider_block", "event_source", "backfill"],
            JournalKindContract.EntityNames);
    }

    [Theory]
    [InlineData("""{ "at": "/work_item_id", "presence": "always" }""", "is the column that names one work_item")]
    [InlineData("""{ "at": "/campaign_id", "presence": "always", "each": true }""", "is the column that names one campaign")]
    [InlineData("""{ "at": "/reason", "presence": "always" }""", "neither an entity column")]
    [InlineData("""{ "at": "/new/nothing", "presence": "always" }""", "the body describes nothing there")]
    [InlineData("""{ "at": "/new/contacts/*/id", "presence": "always" }""", "so it says `each`")]
    [InlineData("""{ "at": "/campaign_id", "presence": "often" }""", "`always` or `sometimes`")]
    [InlineData("""{ "at": "/campaign_id", "presence": "always", "each": false }""", "is true where present")]
    [InlineData("""{ "at": "/campaign_id", "presence": "always", "column": "campaign_id" }""", "is not part of where a line names an entity")]
    [InlineData("""{ "at": "campaign_id", "presence": "always" }""", "is a JSON pointer into the line")]
    [InlineData("""{ "at": ["/campaign_id", "campaign_id"], "presence": "always" }""", "is a JSON pointer into the line")]
    [InlineData("""{ "at": ["/campaign_id", 3], "presence": "always" }""", "is a JSON pointer into the line")]
    [InlineData("""{ "at": ["/campaign_id", "/new/nothing"], "presence": "always" }""", "the body describes nothing there")]
    [InlineData("""{ "at": ["/campaign_id", "/work_item_id"], "presence": "always" }""", "is the column that names one work_item")]
    public void An_entity_is_named_where_the_line_names_it(string place, string refusal)
    {
        var document = Document();
        document["entities"]!["campaign"] = JsonNode.Parse(place);

        Refused(document, refusal);
    }

    /// <summary>
    /// An entity a line may name in more than one place — the plugin a route had and the plugin it has — lists them, and
    /// each is held to the rules one place is.
    /// </summary>
    [Fact]
    public void An_entity_named_in_several_places_lists_every_one()
    {
        var document = Document();
        document["entities"]!["campaign"] = JsonNode.Parse("""{ "at": ["/campaign_id", "/key"], "presence": "always" }""");

        var contract = JournalKindContract.Parse(Source, document);

        Assert.Equal(new JournalKindEntity(["/campaign_id", "/key"], JournalEntityPresence.Always, false), contract.Entities["campaign"]);
    }

    [Fact]
    public void An_entity_named_in_no_place_is_refused()
    {
        var document = Document();
        document["entities"]!["campaign"] = JsonNode.Parse("""{ "at": [], "presence": "always" }""");

        Refused(document, "`entities.campaign.at` lists no place");
    }

    /// <summary>A route taken away names the plugin it went to in what it was, so the kind names its plugin in both.</summary>
    [Fact]
    public void A_route_names_its_plugin_where_it_was_and_where_it_is()
    {
        var contract = JournalKindCatalog.Find("routes_updated");

        Assert.Equal(["/new/plugin", "/old/plugin"], Assert.IsType<JournalKindContract>(contract).Entities["plugin"].At);
    }

    [Theory]
    [InlineData("/new/nothing", "0.12.0", "the body describes nothing there")]
    [InlineData("/new", "0.12.0", "not a member of an object")]
    [InlineData("/reason/note", "0.12.0", "not a member of an object")]
    [InlineData("/new/note", "0.1.0", "which is not after 0.1.0")]
    [InlineData("/new/note", "0.12", "something other than a release's version")]
    [InlineData("/new/contacts", "0.12.0", "the body cannot require it")]
    [InlineData("/new/note/*/id", "0.12.0", "stands for any index of an array, and the body describes no array at `/new/note`")]
    [InlineData("/new/contacts/*", "0.12.0", "not a member of an object")]
    public void A_field_added_later_is_a_member_the_body_describes_and_does_not_require(string pointer, string version, string refusal)
    {
        var document = Document();
        document["field_since"] = new JsonObject { [pointer] = version };

        Refused(document, refusal);
    }

    /// <summary>
    /// A field added to every item of an array is named with <c>*</c> for the item's index, and found through the
    /// array's <c>items</c>.
    /// </summary>
    [Fact]
    public void A_field_added_to_every_item_of_an_array_is_named_with_a_star_for_its_index()
    {
        var document = Document();
        document["field_since"] = new JsonObject { ["/new/contacts/*/id"] = "0.12.0" };

        var contract = JournalKindContract.Parse(Source, document);

        Assert.Equal("0.12.0", contract.FieldSince["/new/contacts/*/id"]);
    }

    /// <summary>
    /// A body is one object of the line's three members, every one present: the check a line is held to sees a member
    /// the line left empty as JSON null, which is what <c>journal.list</c> shows a reader.
    /// </summary>
    [Fact]
    public void A_body_is_checked_as_its_three_members_with_null_where_the_line_has_nothing()
    {
        var contract = JournalKindContract.Parse(Source, Document());
        var contacts = new JsonObject { ["contacts"] = new JsonArray(new JsonObject { ["id"] = "cnt_1" }) };

        Assert.Empty(contract.CheckBody(null, null, contacts));
        Assert.Empty(contract.CheckBody("owner", null, contacts));
        Assert.Contains(contract.CheckBody(null, JsonValue.Create(3), contacts), problem => problem.Pointer == "/old" && problem.Reason == "type");
        Assert.Contains(contract.CheckBody(null, null, null), problem => problem.Pointer == "/new" && problem.Reason == "type");

        // The line's own node is untouched: the check reads a copy.
        Assert.Null(contacts.Parent);
    }

    /// <summary>The documents the repository publishes are this family's and no other's.</summary>
    [Fact]
    public void A_journal_kind_is_no_operation_delivery_events_read_preference_or_context_key()
    {
        foreach (var kind in JournalKindCatalog.Kinds)
        {
            Assert.False(OperationCatalog.Knows(kind), $"{kind} is published as a canonical operation too.");
            Assert.False(DeliveryCatalog.Knows(kind), $"{kind} is published as a delivery contract too.");
            Assert.False(EventsCatalog.Knows(kind), $"{kind} is published as an events contract too.");
            Assert.False(PreferenceCatalog.Knows(kind), $"{kind} is published as a named preference too.");
            Assert.False(ContextKeyCatalog.Knows(kind), $"{kind} is published as a reserved context key too.");
        }

        var folder = Path.Combine(ContractFiles.Package, "journal");
        var files = Directory.Exists(folder) ? Directory.EnumerateFiles(folder, "*.json").Count() : 0;
        Assert.Equal(files, JournalKindCatalog.All.Count);
    }

    /// <summary>
    /// Held here rather than as the documents load, as the other families' are: checking the dialect compiles every
    /// pattern a schema writes, and the command-line client loads this assembly too.
    /// </summary>
    [Fact]
    public void Every_published_body_is_written_in_the_dialect()
    {
        var problems = JournalKindCatalog.All
            .SelectMany(contract => SchemaValidator.CheckDialect(contract.Body).Select(problem => $"{contract.Kind}: {problem.Pointer} {problem.Reason} {problem.Message}"))
            .ToList();

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    /// <summary>
    /// A document stays at version 1: a body grows by optional fields only, each named in <c>field_since</c>, so nothing
    /// a reader relies on ever changes under the same name, and a kind is never given a second meaning.
    /// </summary>
    [Fact]
    public void Every_published_document_is_at_version_1_and_names_fields_no_later_than_this_build()
    {
        var build = BuildVersion();
        foreach (var contract in JournalKindCatalog.All)
        {
            Assert.True(contract.Version == 1, $"{contract.Kind} is at version {contract.Version}.");
            Assert.True(JournalKindContract.CompareVersions(contract.Since, build) <= 0, $"{contract.Kind} says it was first written in {contract.Since}, after this build's {build}.");
            foreach (var (pointer, version) in contract.FieldSince)
            {
                Assert.True(JournalKindContract.CompareVersions(version, build) <= 0, $"{contract.Kind} says {pointer} was added in {version}, after this build's {build}.");
            }
        }
    }

    /// <summary>The release this build is, without what follows its three numbers: <c>0.28.0-dev+1a2b3c4</c> is 0.28.0.</summary>
    private static string BuildVersion()
    {
        var current = JasonVersion.Current;
        var end = current.IndexOfAny(['-', '+']);
        var version = end < 0 ? current : current[..end];
        Assert.True(JournalKindContract.IsWellFormedVersion(version), $"This build's version `{JasonVersion.Current}` does not start with a release's three numbers.");
        return version;
    }

    private static void Refused(JsonObject document, string words)
    {
        var error = Assert.Throws<InvalidOperationException>(() => JournalKindContract.Parse(Source, document));
        Assert.Contains(words, error.Message, StringComparison.Ordinal);
    }
}
