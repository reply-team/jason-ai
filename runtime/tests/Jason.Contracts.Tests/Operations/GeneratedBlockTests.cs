using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Jason.Contracts.Json;
using Jason.Contracts.Operations;

namespace Jason.Contracts.Tests.OperationContracts;

/// <summary>
/// Prose explains; the generated block governs. Each operation's markdown carries a table rendered from its own
/// document, and this is what stops the two disagreeing — the failure message is the block to paste.
/// </summary>
/// <remarks>
/// The package's own README states two lists the validator alone can honour — the keywords of the dialect and the
/// reason codes a refusal carries — and neither is generated, because both are prose a reader needs around them.
/// So they are checked in both directions instead: nothing the README publishes is missing from the code, and
/// nothing the code does is missing from the README.
/// </remarks>
public class GeneratedBlockTests
{
    private const string Begin = "<!-- BEGIN GENERATED properties -->";
    private const string End = "<!-- END GENERATED properties -->";
    private const string Unknown = "schema_keyword_unknown";

    /// <summary>
    /// The keywords of JSON Schema 2020-12, which is the pool this dialect is drawn from. Each one is either
    /// published — and then the validator must handle it — or withheld, and then the validator must name it rather
    /// than skip it. Standing the whole standard against the published list is what states the withholding as a
    /// rule: a keyword taught to the validator and to no list is caught here, where naming two examples caught
    /// only those two.
    /// </summary>
    private static readonly string[] Standard =
    [
        "$schema", "$id", "$ref", "$anchor", "$dynamicRef", "$dynamicAnchor", "$vocabulary", "$comment", "$defs",
        "prefixItems", "items", "contains", "additionalProperties", "properties", "patternProperties",
        "dependentSchemas", "propertyNames", "if", "then", "else", "allOf", "anyOf", "oneOf", "not",
        "unevaluatedItems", "unevaluatedProperties",
        "type", "enum", "const", "multipleOf", "maximum", "exclusiveMaximum", "minimum", "exclusiveMinimum",
        "maxLength", "minLength", "pattern", "maxItems", "minItems", "uniqueItems", "maxContains", "minContains",
        "maxProperties", "minProperties", "required", "dependentRequired",
        "format", "contentEncoding", "contentMediaType", "contentSchema",
        "title", "description", "default", "deprecated", "readOnly", "writeOnly", "examples",
    ];

    /// <summary>
    /// One case per published reason code, each provoking the refusal that carries it. A code is published to a
    /// plugin author as something a refusal can say, so the check that it is real is a refusal that says it.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, Func<IReadOnlyList<SchemaProblem>>> Provocations =
        new Dictionary<string, Func<IReadOnlyList<SchemaProblem>>>(StringComparer.Ordinal)
        {
            ["type"] = () => Refuse("""{"type":"integer"}""", "\"seven\""),
            ["required"] = () => Refuse("""{"type":"object","required":["a"]}""", "{}"),
            ["enum"] = () => Refuse("""{"enum":["a","b"]}""", "\"c\""),
            ["const"] = () => Refuse("""{"const":"a"}""", "\"b\""),
            ["min_length"] = () => Refuse("""{"type":"string","minLength":2}""", "\"a\""),
            ["max_length"] = () => Refuse("""{"type":"string","maxLength":1}""", "\"ab\""),
            ["pattern"] = () => Refuse("""{"type":"string","pattern":"^a$"}""", "\"b\""),
            ["minimum"] = () => Refuse("""{"minimum":1}""", "0"),
            ["maximum"] = () => Refuse("""{"maximum":1}""", "2"),
            ["exclusive_minimum"] = () => Refuse("""{"exclusiveMinimum":0}""", "0"),
            ["exclusive_maximum"] = () => Refuse("""{"exclusiveMaximum":1}""", "1"),
            ["multiple_of"] = () => Refuse("""{"multipleOf":5}""", "7"),
            ["min_items"] = () => Refuse("""{"type":"array","minItems":1}""", "[]"),
            ["max_items"] = () => Refuse("""{"type":"array","maxItems":1}""", "[1,2]"),
            ["unique_items"] = () => Refuse("""{"type":"array","uniqueItems":true}""", "[1,1]"),
            ["additional_properties"] = () =>
                Refuse("""{"type":"object","additionalProperties":false,"properties":{"a":{"type":"integer"}}}""", """{"b":1}"""),
            ["any_of"] = () => Refuse("""{"anyOf":[{"type":"string"},{"type":"integer"}]}""", "true"),
            ["all_of"] = () => Refuse("""{"allOf":[{"type":"integer"},{"minimum":5}]}""", "1"),
            ["not"] = () => Refuse("""{"not":{"type":"string"}}""", "\"a\""),
            ["format"] = () => Refuse("""{"type":"string","format":"date-time"}""", "\"the fifth\""),
            [Unknown] = () => Check("""{"type":"object","patternProperties":{}}"""),
            ["schema_ref_unresolved"] = () => Check("""{"$ref":"#/$defs/absent"}"""),
            ["schema_cyclic"] = () => Check("""{"$ref":"#/$defs/a","$defs":{"a":{"$ref":"#/$defs/a"}}}"""),
            ["schema_pattern_invalid"] = () => Check("""{"type":"string","pattern":"^(?=a)a$"}"""),
            ["schema_too_deep"] = () => SchemaValidator.CheckDialect(Deep()),
            ["schema_too_large"] = () => SchemaValidator.CheckDialect(Large()),
        };

    private static readonly Regex Aside = new(@"\([^)]*\)", RegexOptions.None, TimeSpan.FromSeconds(5));

    private static readonly Regex Term = new("`(?<term>[^`]+)`", RegexOptions.None, TimeSpan.FromSeconds(5));

    /// <summary>A problem built where it is reported, and the reason code it is built with.</summary>
    private static readonly Regex Constructed =
        new(@"new SchemaProblem\(\s*[^""]*?,\s*""(?<reason>[a-z_]+)""", RegexOptions.None, TimeSpan.FromSeconds(5));

    /// <summary>A problem built by a helper that returns one, which is the other shape the code uses.</summary>
    private static readonly Regex Returned =
        new(
            @"static SchemaProblem \w+\([^)]*\)\s*=>\s*new\(\s*[^""]*?,\s*""(?<reason>[a-z_]+)""",
            RegexOptions.None,
            TimeSpan.FromSeconds(5));

    public static TheoryData<string> Published() => ContractDocumentTests.Published();

    public static TheoryData<string> PublishedKeywords() => Data(SchemaValidator.Keywords);

    public static TheoryData<string> WithheldKeywords() =>
        Data(Standard.Except(SchemaValidator.Keywords, StringComparer.Ordinal));

    public static TheoryData<string> PublishedReasonCodes() => Data(ListedUnder("**Reason codes.**"));

    [Theory]
    [MemberData(nameof(Published))]
    public void The_generated_block_is_what_the_document_says(string id)
    {
        var contract = OperationCatalog.Find(id)!;
        var path = Path.Combine(ContractFiles.Operations, id + ".md");

        Assert.True(File.Exists(path), $"{ContractFiles.Relative(path)} explains {id} for a plugin author, and is missing.");

        var page = File.ReadAllText(path).ReplaceLineEndings("\n");
        var start = page.IndexOf(Begin, StringComparison.Ordinal);
        var finish = page.IndexOf(End, StringComparison.Ordinal);

        Assert.True(start >= 0 && finish > start, $"{ContractFiles.Relative(path)} carries no generated block.");

        var found = page[start..(finish + End.Length)];
        var expected = Render(contract);

        Assert.True(found == expected, $"{ContractFiles.Relative(path)} is out of step with its document. The block should read:\n\n{expected}\n");
    }

    [Fact]
    public void The_readme_publishes_the_keywords_the_validator_holds_and_no_others()
    {
        Assert.Equal(
            ListedUnder("**The vocabulary.**").Order(StringComparer.Ordinal).ToList(),
            SchemaValidator.Keywords.Order(StringComparer.Ordinal).ToList());
    }

    [Fact]
    public void The_dialect_is_drawn_from_the_standard_and_invents_nothing_of_its_own()
    {
        Assert.All(SchemaValidator.Keywords, keyword => Assert.Contains(keyword, Standard, StringComparer.Ordinal));
    }

    [Theory]
    [MemberData(nameof(PublishedKeywords))]
    public void A_published_keyword_is_one_both_entry_points_actually_handle(string keyword)
    {
        var schema = new JsonObject { [keyword] = new JsonObject() };

        Assert.DoesNotContain(SchemaValidator.CheckDialect(schema), problem => problem.Reason == Unknown);
        Assert.DoesNotContain(SchemaValidator.Validate(new JsonObject(), schema), problem => problem.Reason == Unknown);
    }

    [Theory]
    [MemberData(nameof(WithheldKeywords))]
    public void A_keyword_of_the_standard_this_dialect_withholds_is_refused_by_both(string keyword)
    {
        var schema = new JsonObject { ["type"] = "object", [keyword] = new JsonObject() };

        Assert.Contains(
            SchemaValidator.CheckDialect(schema),
            problem => problem.Reason == Unknown && problem.Pointer == "/" + keyword);
        Assert.Contains(SchemaValidator.Validate(new JsonObject(), schema), problem => problem.Reason == Unknown);
    }

    [Fact]
    public void Every_reason_code_here_is_one_the_readme_publishes_and_the_other_way_round()
    {
        Assert.Equal(
            ListedUnder("**Reason codes.**").Order(StringComparer.Ordinal).ToList(),
            Provocations.Keys.Order(StringComparer.Ordinal).ToList());
    }

    [Theory]
    [MemberData(nameof(PublishedReasonCodes))]
    public void A_published_reason_code_is_one_the_validator_actually_reports(string reason)
    {
        Assert.True(
            Provocations.TryGetValue(reason, out var provoke),
            $"Nothing here provokes `{reason}`, so the README's claim that a refusal can carry it is only a claim.");
        Assert.Contains(provoke(), problem => problem.Reason == reason);
    }

    [Fact]
    public void The_code_names_no_reason_the_readme_does_not_publish()
    {
        var found = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(Path.Combine(ContractFiles.Root, "runtime", "src"), "*.cs", SearchOption.AllDirectories))
        {
            var source = File.ReadAllText(file);
            var constructed = Constructed.Matches(source);
            var returned = Returned.Matches(source);

            // Both counts guard the reading itself: a construction whose reason is not a literal is one this scan
            // cannot read, and a scan that quietly skipped it would report the drift it exists to catch as absence.
            Assert.True(
                constructed.Count == Regex.Count(source, @"new SchemaProblem\("),
                $"{ContractFiles.Relative(file)} builds a problem whose reason code this check cannot read.");
            Assert.True(
                returned.Count == Regex.Count(source, @"static SchemaProblem \w+\([^)]*\)\s*=>\s*new\("),
                $"{ContractFiles.Relative(file)} returns a problem whose reason code this check cannot read.");

            found.UnionWith(constructed.Concat(returned).Select(match => match.Groups["reason"].Value));
        }

        Assert.NotEmpty(found);
        Assert.Equal(ListedUnder("**Reason codes.**").Order(StringComparer.Ordinal).ToList(), [.. found]);
    }

    /// <summary>The block as the document states it: the level-1 properties first, then what Jason adds to them.</summary>
    private static string Render(OperationContract contract)
    {
        var block = new StringBuilder();
        block.Append(Begin).Append('\n');
        block.Append("<!-- Rendered from ").Append(contract.Id).Append(".json. Edit the document; this block follows it. -->\n\n");
        block.Append("| Property | Value | Detail |\n|---|---|---|\n");
        Property(block, "reach", contract.Reach);
        Property(block, "reversibility", contract.Reversibility);
        Property(block, "approval", contract.Approval);
        Property(block, "before_repeating", contract.BeforeRepeating);
        Property(block, "idempotency_key", contract.IdempotencyKey);
        Property(block, "per_item_results", contract.PerItemResults);
        Property(block, "cost", contract.Cost);
        block.Append('\n');

        block.Append("| What Jason adds | Value |\n|---|---|\n");
        Row(block, "pre-flight", $"contact `{Name(contract.Preflight.Contact)}`, channel `{Name(contract.Preflight.Channel)}`");
        Row(block, "contact projection", Projection(contract));
        Row(block, "precondition", contract.Precondition);
        Row(block, "pinned identifiers", string.Join("; ", contract.ExternalIds.Select(kind => $"`{kind.Key}` names a {Name(kind.Value.Entity)}")));
        Row(block, "recovery read", contract.RecoveryRead is null ? "none" : contract.RecoveryRead.Reads);
        Row(block, "after an ambiguous end", Ambiguous(contract));
        Row(block, "timeout", contract.TimeoutMs.ToString(System.Globalization.CultureInfo.InvariantCulture) + " ms");
        Row(block, "invariants", contract.Invariants.Count == 0 ? "none names this operation" : string.Join(", ", contract.Invariants));
        block.Append('\n').Append(End);

        return block.ToString();
    }

    private static void Property(StringBuilder block, string name, ContractProperty property) =>
        block.Append("| `").Append(name).Append("` | ").Append(Value(property))
            .Append(" | ").Append(property.Conditional ? "conditional — " + property.Detail : "—").Append(" |\n");

    private static string Value(ContractProperty property) =>
        property.Value.Contains(' ', StringComparison.Ordinal) ? property.Value : "`" + property.Value + "`";

    private static void Row(StringBuilder block, string name, string value) =>
        block.Append("| ").Append(name).Append(" | ").Append(value).Append(" |\n");

    private static string Projection(OperationContract contract) =>
        contract.ContactProjection is null
            ? "none — this operation never receives the work item's contact"
            : string.Join(", ", contract.ContactProjection.Fields.Select(field => "`" + field + "`"))
              + $"; channels: `{contract.ContactProjection.Channels}`";

    private static string Ambiguous(OperationContract contract) => contract.RepeatAfterAmbiguous switch
    {
        RepeatAfterAmbiguous.Safe => "`safe` — repeating changes nothing and costs nothing, so the item is handed out again",
        RepeatAfterAmbiguous.AfterRecoveryRead =>
            "`after_recovery_read` — a later attempt performs the recovery read above and answers from it when the effect already happened",
        _ => "`never` — the item ends for a person to look at",
    };

    private static string Name<TValue>(TValue value)
        where TValue : struct, Enum =>
        JsonSerializer.Serialize(value, JasonJson.Options).Trim('"');

    // ---------------------------------------------------------------------------------------------------------
    // Reading the README as a statement rather than as prose
    // ---------------------------------------------------------------------------------------------------------

    /// <summary>
    /// The terms the README lists in the paragraph under one bolded lead: its backticked words, with parenthetical
    /// asides removed first, since an aside explains a term — `date-time`, `#/$defs/&lt;name&gt;` — rather than
    /// adding one to the list.
    /// </summary>
    private static IReadOnlyList<string> ListedUnder(string lead)
    {
        var readme = File.ReadAllText(Path.Combine(ContractFiles.Package, "README.md")).ReplaceLineEndings("\n");
        var start = readme.IndexOf(lead, StringComparison.Ordinal);
        if (start < 0)
        {
            throw new InvalidOperationException($"docs/contracts/README.md no longer carries a paragraph beginning `{lead}`.");
        }

        var finish = readme.IndexOf("\n\n", start, StringComparison.Ordinal);
        var paragraph = Aside.Replace(readme[start..(finish < 0 ? readme.Length : finish)], " ");

        return [.. Term.Matches(paragraph).Select(match => match.Groups["term"].Value)];
    }

    private static TheoryData<string> Data(IEnumerable<string> values)
    {
        var data = new TheoryData<string>();
        foreach (var value in values)
        {
            data.Add(value);
        }

        return data;
    }

    // ---------------------------------------------------------------------------------------------------------
    // Provoking a refusal
    // ---------------------------------------------------------------------------------------------------------

    private static IReadOnlyList<SchemaProblem> Refuse(string schema, string value) =>
        SchemaValidator.Validate(JsonNode.Parse(value), (JsonObject)JsonNode.Parse(schema)!);

    private static IReadOnlyList<SchemaProblem> Check(string schema) =>
        SchemaValidator.CheckDialect((JsonObject)JsonNode.Parse(schema)!);

    /// <summary>A schema nested past what the dialect descends into, which is deeper than JsonNode parses by default.</summary>
    private static JsonObject Deep()
    {
        var schema = new StringBuilder();
        for (var level = 0; level <= SchemaValidator.MaxDepth; level++)
        {
            schema.Append("""{"type":"object","properties":{"x":""");
        }

        schema.Append("""{"type":"integer"}""");
        for (var level = 0; level <= SchemaValidator.MaxDepth; level++)
        {
            schema.Append("}}");
        }

        return (JsonObject)JsonNode.Parse(schema.ToString(), documentOptions: new JsonDocumentOptions { MaxDepth = 256 })!;
    }

    /// <summary>A schema past the size the dialect check reads at all.</summary>
    private static JsonObject Large()
    {
        var properties = new JsonObject();
        for (var index = 0; properties.ToJsonString().Length <= SchemaValidator.MaxSchemaBytes; index++)
        {
            properties["a_property_named_at_length_so_the_document_grows_" + index] = new JsonObject { ["type"] = "string" };
        }

        return new JsonObject { ["type"] = "object", ["properties"] = properties };
    }
}
