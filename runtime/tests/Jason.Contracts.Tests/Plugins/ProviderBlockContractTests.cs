using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Json;
using Jason.Contracts.Operations;
using Jason.Contracts.Plugins;
using Jason.Contracts.Tests.OperationContracts;

namespace Jason.Contracts.Tests.Plugins;

/// <summary>
/// The provider block: what a plugin says beside a failure of class <c>blocked</c>. One document, written once under
/// <c>docs/contracts/block/</c> and embedded, so the file a plugin author reads is the schema the runtime checks.
/// </summary>
public sealed class ProviderBlockContractTests
{
    [Fact]
    public void The_block_is_published_once_at_version_1()
    {
        var published = ProviderBlockContract.Published;

        Assert.Equal("provider_block", published.Id);
        Assert.Equal(1, published.Version);
        Assert.Equal(["provider_block.json"], Directory.EnumerateFiles(Path.Combine(ContractFiles.Package, "block"), "*.json").Select(Path.GetFileName));
    }

    /// <summary>Held here rather than as the document loads, as the other families' are: checking the dialect compiles every pattern.</summary>
    [Fact]
    public void The_schema_is_in_the_published_dialect_and_describes_an_object()
    {
        var problems = SchemaValidator.CheckDialect(ProviderBlockContract.Published.Schema);

        Assert.True(problems.Count == 0, string.Join("; ", problems.Select(problem => problem.Pointer + " " + problem.Reason)));
        Assert.Equal("object", (string?)ProviderBlockContract.Published.Schema["type"]);
    }

    /// <summary>
    /// The closed lists a reader relies on: a word added to one would be a value no reader knows, so they are pinned where a
    /// change has to be made on purpose. The binding and a detail are absent by design: the binding is the one the attempt
    /// ran under, and the words a person reads are the failure's own message.
    /// </summary>
    [Fact]
    public void The_block_names_what_is_blocked_why_where_to_go_and_when_it_clears()
    {
        var schema = ProviderBlockContract.Published.Schema;
        var properties = (JsonObject)schema["properties"]!;

        Assert.Equal(["scope", "sender_external_id", "reason", "link", "clears_at"], properties.Select(property => property.Key));
        Assert.Equal(["scope", "reason"], Names(schema["required"]));
        Assert.Equal(["account", "sender"], Names(properties["scope"]!["enum"]));
        Assert.Equal(["billing", "limit", "credential", "channel", "other"], Names(properties["reason"]!["enum"]));
        Assert.False((bool)schema["additionalProperties"]!);
    }

    /// <summary>
    /// The sentence the class rests on, held where the page and the document say it: <c>blocked</c> asserts the provider did
    /// nothing for this call, so a repeat once the block clears is a first act, and a plugin that cannot be sure says
    /// <c>ambiguous</c>. A document or a page that lost it would leave a reader free to report a half-done call as blocked.
    /// </summary>
    [Fact]
    public void The_block_says_the_provider_did_nothing_and_that_doubt_is_ambiguous()
    {
        Assert.Contains("did nothing for it", ProviderBlockContract.Published.Summary, StringComparison.Ordinal);
        Assert.Contains("a repeat once the block clears is a first act", ProviderBlockContract.Published.Summary, StringComparison.Ordinal);

        var page = File.ReadAllText(Path.Combine(ContractFiles.Package, "block", "provider_block.md"));
        Assert.Contains("MUST", page, StringComparison.Ordinal);
        Assert.Contains("the provider did nothing for this call", page, StringComparison.Ordinal);
        Assert.Contains("says `ambiguous`, never `blocked`", page, StringComparison.Ordinal);
    }

    [Fact]
    public void A_block_is_checked_against_the_published_schema()
    {
        Assert.Empty(ProviderBlockContract.Published.Check(new JsonObject { ["scope"] = "account", ["reason"] = "billing" }));
        Assert.Equal(
            ["/reason"],
            ProviderBlockContract.Published.Check(new JsonObject { ["scope"] = "account", ["reason"] = "money" }).Select(problem => problem.Pointer));
    }

    /// <summary>The bound is on the whole payload, as the error's details are bounded; the outcome reader holds it.</summary>
    [Fact]
    public void A_block_is_at_most_8_KiB()
    {
        Assert.Equal(8 * 1024, ProviderBlockContract.MaxBytes);
    }

    [Theory]
    [InlineData("id")]
    [InlineData("version")]
    [InlineData("summary")]
    [InlineData("schema")]
    public void A_document_missing_a_field_is_refused(string field)
    {
        var document = Document();
        document.Remove(field);

        var refused = Assert.Throws<InvalidOperationException>(() => ProviderBlockContract.Parse("provider_block.json", document));
        Assert.Contains($"`{field}` is missing", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_document_with_a_field_of_its_own_is_refused()
    {
        var document = Document();
        document["codes"] = new JsonArray("provider_blocked");

        var refused = Assert.Throws<InvalidOperationException>(() => ProviderBlockContract.Parse("provider_block.json", document));
        Assert.Contains("`codes` is not part of the provider block", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_schema_that_does_not_describe_an_object_is_refused()
    {
        var document = Document();
        document["schema"] = new JsonObject { ["type"] = "string" };

        var refused = Assert.Throws<InvalidOperationException>(() => ProviderBlockContract.Parse("provider_block.json", document));
        Assert.Contains("`schema` describes a JSON object", refused.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The block travels on the error it explains, after the identifiers, and is left out where there is none, so an outcome
    /// of the four classes that were there before reads byte for byte as it did.
    /// </summary>
    [Fact]
    public void The_block_travels_on_the_error_and_is_absent_from_every_other_error()
    {
        var block = new JsonObject { ["scope"] = "account", ["reason"] = "billing" };
        var blocked = new OutcomeError(FailureClass.Blocked, "provider_blocked", "The account has no credits left.", Block: block);
        var plain = new OutcomeError(FailureClass.Permanent, "not_found", "No such campaign.");

        Assert.Equal(
            """{"class":"blocked","code":"provider_blocked","message":"The account has no credits left.","block":{"scope":"account","reason":"billing"}}""",
            JsonSerializer.Serialize(blocked, JasonJson.Options));
        Assert.Equal("""{"class":"permanent","code":"not_found","message":"No such campaign."}""", JsonSerializer.Serialize(plain, JasonJson.Options));
        Assert.Equal(FailureClass.Blocked, JsonSerializer.Deserialize<FailureClass>("\"blocked\"", JasonJson.Options));
    }

    private static string[] Names(JsonNode? array) => [.. ((JsonArray)array!).Select(name => (string)name!)];

    private static JsonObject Document() =>
        (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(ContractFiles.Package, "block", "provider_block.json")))!;
}
