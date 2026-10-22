using System.Text.Json.Nodes;
using Jason.Contracts.Events;
using Jason.Contracts.Notifications;
using Jason.Contracts.Operations;
using Jason.Contracts.Preferences;

namespace Jason.Contracts.Tests.Preferences;

/// <summary>
/// The named preferences: a family of its own beside the canonical operations, the delivery contract and the events
/// contract, never among them. A preference is a fact about the client a person states, not work anybody performs, so
/// no work item names one, no route carries one and no plugin is invoked for one.
/// </summary>
public sealed class PreferenceContractTests
{
    [Fact]
    public void The_family_publishes_the_sender_s_identity_and_the_tone_of_voice_at_version_1()
    {
        Assert.Equal(["sender.identity", "tone_of_voice"], PreferenceCatalog.All.Select(contract => contract.Key));
        Assert.All(PreferenceCatalog.All, contract => Assert.True(contract.Version == 1, $"{contract.Key} is at version {contract.Version}."));
        Assert.Same(PreferenceCatalog.All[0], PreferenceCatalog.Find("sender.identity"));
        Assert.True(PreferenceCatalog.Knows("tone_of_voice"));
        Assert.False(PreferenceCatalog.Knows("tone"));
        Assert.Equal(["sender.identity", "tone_of_voice"], PreferenceCatalog.Keys);
    }

    [Fact]
    public void A_preference_is_neither_an_operation_a_delivery_nor_an_events_read()
    {
        foreach (var key in PreferenceCatalog.Keys)
        {
            Assert.False(OperationCatalog.Knows(key), $"{key} is published as a canonical operation too.");
            Assert.False(DeliveryCatalog.Knows(key), $"{key} is published as a delivery contract too.");
            Assert.False(EventsCatalog.Knows(key), $"{key} is published as an events contract too.");
        }

        Assert.False(PreferenceCatalog.Knows("campaign.get"));
        Assert.Equal(Directory.EnumerateFiles(Path.Combine(OperationContracts.ContractFiles.Package, "preferences"), "*.json").Count(), PreferenceCatalog.All.Count);
    }

    /// <summary>
    /// Held here rather than as the document loads, as the other families' are: checking the dialect compiles every
    /// pattern a schema writes, and the command-line client, which validates no value, loads this catalog too.
    /// </summary>
    [Fact]
    public void Every_value_schema_is_in_the_published_dialect_and_describes_an_object()
    {
        Assert.All(PreferenceCatalog.All, contract =>
        {
            var problems = SchemaValidator.CheckDialect(contract.ValueSchema);
            Assert.True(problems.Count == 0, $"{contract.Key}: {string.Join("; ", problems.Select(problem => problem.Pointer + " " + problem.Reason))}");
            Assert.Equal("object", (string?)contract.ValueSchema["type"]);
        });
    }

    /// <summary>
    /// The fields a writer reads from each value, and the closed lists a writer relies on: a word added to an enum would be
    /// a value no reader knows, so the lists are pinned where a change to them has to be made on purpose.
    /// </summary>
    [Fact]
    public void The_sender_s_identity_names_each_sender_and_the_forms_their_messages_use()
    {
        var schema = PreferenceCatalog.Find("sender.identity")!.ValueSchema;
        Assert.Equal(["senders"], Names(schema["required"]));
        var senders = (JsonObject)schema["properties"]!["senders"]!;
        Assert.Equal((1, 20), ((int)senders["minItems"]!, (int)senders["maxItems"]!));

        var sender = (JsonObject)senders["items"]!;
        Assert.Equal(["name", "gender_forms"], Names(sender["required"]));
        Assert.Equal(
            ["name", "addressed_as", "gender_forms", "title", "company", "aliases", "details"],
            ((JsonObject)sender["properties"]!).Select(property => property.Key));
        Assert.Equal(["feminine", "masculine", "neutral"], Names(sender["properties"]!["gender_forms"]!["enum"]));
    }

    [Fact]
    public void The_tone_of_voice_is_a_summary_in_words_and_what_may_stand_beside_it()
    {
        var schema = PreferenceCatalog.Find("tone_of_voice")!.ValueSchema;
        Assert.Equal(["summary"], Names(schema["required"]));
        Assert.Equal(
            ["summary", "formality", "greeting", "sign_off", "examples"],
            ((JsonObject)schema["properties"]!).Select(property => property.Key));
        Assert.Equal(["informal", "neutral", "formal"], Names(schema["properties"]!["formality"]!["enum"]));
        Assert.Equal(3, (int)schema["properties"]!["examples"]!["maxItems"]!);
    }

    [Theory]
    [InlineData("tone_of_voice")]
    [InlineData("sender.identity")]
    [InlineData("a")]
    [InlineData("a1.b_2.c")]
    public void A_key_in_the_grammar_is_well_formed(string key) =>
        Assert.True(PreferenceContract.IsWellFormedKey(key), $"'{key}' is refused by the key grammar.");

    [Theory]
    [InlineData("")]
    [InlineData("Tone")]
    [InlineData("1tone")]
    [InlineData("_tone")]
    [InlineData("tone-of-voice")]
    [InlineData("sender.")]
    [InlineData(".sender")]
    [InlineData("sender..identity")]
    [InlineData("sender.1identity")]
    [InlineData("tone of voice")]
    [InlineData("toné")]
    public void A_key_outside_the_grammar_is_not(string key) =>
        Assert.False(PreferenceContract.IsWellFormedKey(key), $"'{key}' passes the key grammar.");

    [Fact]
    public void A_key_is_at_most_64_characters()
    {
        Assert.True(PreferenceContract.IsWellFormedKey(new string('a', PreferenceContract.MaxKeyLength)));
        Assert.False(PreferenceContract.IsWellFormedKey(new string('a', PreferenceContract.MaxKeyLength + 1)));
        Assert.Equal(64, PreferenceContract.MaxKeyLength);
        Assert.Equal(64 * 1024, PreferenceContract.MaxValueBytes);
    }

    [Theory]
    [InlineData("key")]
    [InlineData("version")]
    [InlineData("summary")]
    [InlineData("value_schema")]
    public void A_document_missing_a_field_is_refused(string field)
    {
        var document = Document("tone_of_voice");
        document.Remove(field);

        var refused = Assert.Throws<InvalidOperationException>(() => PreferenceContract.Parse("tone_of_voice.json", document));
        Assert.Contains($"`{field}` is missing", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_document_with_a_field_of_its_own_is_refused()
    {
        var document = Document("tone_of_voice");
        document["scope"] = "project";

        var refused = Assert.Throws<InvalidOperationException>(() => PreferenceContract.Parse("tone_of_voice.json", document));
        Assert.Contains("`scope` is not part of a named preference", refused.Message, StringComparison.Ordinal);
    }

    /// <summary>One document per key, named for it, so the file a reader opens is the key they asked about.</summary>
    [Fact]
    public void A_document_whose_key_is_not_its_file_s_name_is_refused()
    {
        var refused = Assert.Throws<InvalidOperationException>(() => PreferenceContract.Parse("tone.json", Document("tone_of_voice")));
        Assert.Contains("`tone_of_voice` is published in tone.json", refused.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Tone_Of_Voice")]
    [InlineData("tone-of-voice")]
    public void A_document_whose_key_is_outside_the_grammar_is_refused(string key)
    {
        var document = Document("tone_of_voice");
        document["key"] = key;

        var refused = Assert.Throws<InvalidOperationException>(() => PreferenceContract.Parse(key + ".json", document));
        Assert.Contains("is not a preference key", refused.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_version_below_1_is_refused(int version)
    {
        var document = Document("tone_of_voice");
        document["version"] = version;

        var refused = Assert.Throws<InvalidOperationException>(() => PreferenceContract.Parse("tone_of_voice.json", document));
        Assert.Contains("`version` is a whole number from 1", refused.Message, StringComparison.Ordinal);
    }

    /// <summary>A value is a JSON object, always: a schema that would let a string or a list stand as a value is refused.</summary>
    [Fact]
    public void A_value_schema_that_does_not_describe_an_object_is_refused()
    {
        var document = Document("tone_of_voice");
        document["value_schema"] = new JsonObject { ["type"] = "string" };

        var refused = Assert.Throws<InvalidOperationException>(() => PreferenceContract.Parse("tone_of_voice.json", document));
        Assert.Contains("`value_schema` describes a JSON object", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_value_is_checked_against_its_key_s_schema()
    {
        var tone = PreferenceCatalog.Find("tone_of_voice")!;

        Assert.Empty(tone.CheckValue(new JsonObject { ["summary"] = "Plain and warm." }));
        Assert.Equal(["/formality"], tone.CheckValue(new JsonObject { ["summary"] = "Plain.", ["formality"] = "casual" }).Select(problem => problem.Pointer));
    }

    private static string[] Names(JsonNode? array) => [.. ((JsonArray)array!).Select(name => (string)name!)];

    /// <summary>The published document, as a node a test may change.</summary>
    private static JsonObject Document(string key) => (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(
        OperationContracts.ContractFiles.Package, "preferences", key + ".json")))!;
}
