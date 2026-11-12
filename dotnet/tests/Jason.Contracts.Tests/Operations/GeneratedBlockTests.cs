using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Jason.Contracts.Journal;
using Jason.Contracts.Json;
using Jason.Contracts.Operations;
using Jason.Runtime.Tests;

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
    private const string TableBegin = "<!-- BEGIN GENERATED operations -->";
    private const string TableEnd = "<!-- END GENERATED operations -->";
    private const string KindsBegin = "<!-- BEGIN GENERATED journal-kinds -->";
    private const string KindsEnd = "<!-- END GENERATED journal-kinds -->";
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
            ["schema_keyword_malformed"] = () => Check("""{"type":"object","required":"a"}"""),
            ["schema_ref_unresolved"] = () => Check("""{"$ref":"#/$defs/absent"}"""),
            ["schema_cyclic"] = () => Check("""{"$ref":"#/$defs/a","$defs":{"a":{"$ref":"#/$defs/a"}}}"""),
            ["schema_pattern_invalid"] = () => Check("""{"type":"string","pattern":"^(?=a)a$"}"""),
            ["schema_too_deep"] = () => SchemaValidator.CheckDialect(Deep()),
            ["schema_too_large"] = () => SchemaValidator.CheckDialect(Large()),
            ["schema_number_not_finite"] = () =>
                SchemaValidator.CheckDialect(new JsonObject { ["maximum"] = JsonValue.Create(double.PositiveInfinity) }),
            ["duplicate_property"] = () => Check("""{"type":"object","properties":{"a":{},"a":{}}}"""),
            ["number_not_comparable"] = () => Refuse("""{"type":"number","maximum":100}""", "1e40"),
            ["pattern_timed_out"] = () =>
                SchemaValidator.Validate(JsonNode.Parse("\"a\""), (JsonObject)JsonNode.Parse("""{"type":"string","pattern":"^a$"}""")!, TimingOut.Always()),
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

    // Critical: a withheld keyword is refused, not ignored.
    public static TheoryData<string> CriticalWithheldKeywords() =>
        CriticalRows.Mark(WithheldKeywords(), Family.R3, data => data is ["$anchor"]);

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

    /// <summary>
    /// The contracts README, docs/contracts/README.md, lists every published operation in one table, rendered from the
    /// documents and compared whole: the level-one name each is seeded from, what it reaches, the approval it needs and
    /// what may be done after an ambiguous end — the dangerous readings, as the runtime reads them — how the provider's
    /// own contact for its person is found, and the one line its page's title gives it.
    /// Nothing in the table is written twice, so a document that changes and a table that does not fail here.
    /// </summary>
    [Fact]
    public void The_readme_s_table_of_operations_is_rendered_from_the_documents()
    {
        var readme = File.ReadAllText(Path.Combine(ContractFiles.Package, "README.md")).ReplaceLineEndings("\n");
        var start = readme.IndexOf(TableBegin, StringComparison.Ordinal);
        var finish = readme.IndexOf(TableEnd, StringComparison.Ordinal);

        Assert.True(start >= 0 && finish > start, "docs/contracts/README.md carries no generated table of the operations.");

        var found = readme[start..(finish + TableEnd.Length)];
        var expected = OperationsTable();

        Assert.True(found == expected, $"docs/contracts/README.md's table of operations is out of step with the documents. It should read:\n\n{expected}\n");
    }

    /// <summary>
    /// The same README lists every kind the runtime reserves for its own journal lines in one table, rendered from the
    /// kinds' documents and compared whole: the release that first wrote the kind, whether a program may subscribe to it,
    /// the things its lines name, and its summary.
    /// </summary>
    [Fact]
    public void The_readme_s_table_of_journal_kinds_is_rendered_from_the_documents()
    {
        var readme = File.ReadAllText(Path.Combine(ContractFiles.Package, "README.md")).ReplaceLineEndings("\n");
        var start = readme.IndexOf(KindsBegin, StringComparison.Ordinal);
        var finish = readme.IndexOf(KindsEnd, StringComparison.Ordinal);

        Assert.True(start >= 0 && finish > start, "docs/contracts/README.md carries no generated table of the journal's kinds.");

        var found = readme[start..(finish + KindsEnd.Length)];
        var expected = JournalKindsTable();

        Assert.True(found == expected, $"docs/contracts/README.md's table of the journal's kinds is out of step with the documents. It should read:\n\n{expected}\n");
    }

    /// <summary>The delivery contract's page carries a block rendered from its own document, as an operation's does.</summary>
    [Fact]
    public void The_delivery_page_s_generated_block_is_what_its_document_says()
    {
        var contract = Jason.Contracts.Notifications.DeliveryCatalog.Find("notification.send")!;
        var path = Path.Combine(ContractFiles.Package, "notifications", "notification.send.md");

        Assert.True(File.Exists(path), $"{ContractFiles.Relative(path)} explains the delivery contract for a plugin author, and is missing.");

        var page = File.ReadAllText(path).ReplaceLineEndings("\n");
        var start = page.IndexOf(Begin, StringComparison.Ordinal);
        var finish = page.IndexOf(End, StringComparison.Ordinal);

        Assert.True(start >= 0 && finish > start, $"{ContractFiles.Relative(path)} carries no generated block.");

        var found = page[start..(finish + End.Length)];
        var expected = Render(contract);

        Assert.True(found == expected, $"{ContractFiles.Relative(path)} is out of step with its document. The block should read:\n\n{expected}\n");
    }

    /// <summary>The events contract's page carries a block rendered from its own document, as the delivery contract's does.</summary>
    [Fact]
    public void The_events_page_s_generated_block_is_what_its_document_says()
    {
        var contract = Jason.Contracts.Events.EventsCatalog.Find("events.read")!;
        var path = Path.Combine(ContractFiles.Package, "events", "events.read.md");

        Assert.True(File.Exists(path), $"{ContractFiles.Relative(path)} explains the events contract for a plugin author, and is missing.");

        var page = File.ReadAllText(path).ReplaceLineEndings("\n");
        var start = page.IndexOf(Begin, StringComparison.Ordinal);
        var finish = page.IndexOf(End, StringComparison.Ordinal);

        Assert.True(start >= 0 && finish > start, $"{ContractFiles.Relative(path)} carries no generated block.");

        var found = page[start..(finish + End.Length)];
        var expected = Render(contract);

        Assert.True(found == expected, $"{ContractFiles.Relative(path)} is out of step with its document. The block should read:\n\n{expected}\n");
    }

    /// <summary>The subscription contract's page carries a block rendered from its own document, as the others' do.</summary>
    [Fact]
    public void The_subscription_page_s_generated_block_is_what_its_document_says()
    {
        var contract = Jason.Contracts.Subscriptions.SubscriptionCatalog.Find("subscription.handle")!;
        var path = Path.Combine(ContractFiles.Package, "subscriptions", "subscription.handle.md");

        Assert.True(File.Exists(path), $"{ContractFiles.Relative(path)} explains the subscription contract for a plugin author, and is missing.");

        var page = File.ReadAllText(path).ReplaceLineEndings("\n");
        var start = page.IndexOf(Begin, StringComparison.Ordinal);
        var finish = page.IndexOf(End, StringComparison.Ordinal);

        Assert.True(start >= 0 && finish > start, $"{ContractFiles.Relative(path)} carries no generated block.");

        var found = page[start..(finish + End.Length)];
        var expected = Render(contract);

        Assert.True(found == expected, $"{ContractFiles.Relative(path)} is out of step with its document. The block should read:\n\n{expected}\n");
    }

    [Fact]
    [Critical(Family.R3, "the dialect's vocabulary is the published one")]
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
    [MemberData(nameof(CriticalWithheldKeywords))]
    public void A_keyword_of_the_standard_this_dialect_withholds_is_refused_by_both(string keyword)
    {
        var schema = new JsonObject { ["type"] = "object", [keyword] = new JsonObject() };

        Assert.Contains(
            SchemaValidator.CheckDialect(schema),
            problem => problem.Reason == Unknown && problem.Pointer == "/" + keyword);
        Assert.Contains(SchemaValidator.Validate(new JsonObject(), schema), problem => problem.Reason == Unknown);
    }

    [Fact]
    [Critical(Family.R3, "the validator's reason codes are the published ones")]
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
        foreach (var file in Directory.EnumerateFiles(Path.Combine(ContractFiles.Root, "dotnet", "src"), "*.cs", SearchOption.AllDirectories))
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
        block.Append("| `protective_floor` | `").Append(contract.ProtectiveFloor ? "true" : "false").Append("` | ")
            .Append(contract.ApprovalReason ?? "—").Append(" |\n");
        Property(block, "before_repeating", contract.BeforeRepeating);
        Property(block, "idempotency_key", contract.IdempotencyKey);
        Property(block, "per_item_results", contract.PerItemResults);
        Property(block, "cost", contract.Cost);
        block.Append('\n');

        block.Append("| What Jason adds | Value |\n|---|---|\n");
        Row(
            block,
            "pre-flight",
            $"contact `{Name(contract.Preflight.Contact)}`, channel `{Name(contract.Preflight.Channel)}`, suppression `{Name(contract.Preflight.Suppression)}`, provider contact `{Name(contract.Preflight.ProviderContact)}`");
        if (contract.ChannelArguments is { } perChannel)
        {
            Row(
                block,
                "arguments a plugin takes per channel",
                string.Join(", ", perChannel.Select(name => $"`{name}`")) + ", only on a channel the routed plugin's manifest lists it for");
        }

        if (contract.ChannelTemplates is { } templated)
        {
            Row(
                block,
                "channels its message may be a template on",
                string.Join(", ", templated.Select(channel => $"`{channel}`")) + ", each only where the routed plugin's manifest lists it under `templates`");
        }

        Row(block, "in a paused campaign", $"`{Name(contract.Dispatch.InPausedCampaign)}`");
        Row(block, "in an archived campaign", $"`{Name(contract.Dispatch.InArchivedCampaign)}`");
        Row(block, "under a provider block", $"`{Name(contract.Dispatch.UnderProviderBlock)}`");
        Row(block, "contact projection", Projection(contract));
        Row(block, "precondition", contract.Precondition);
        Row(block, "preview a person reads", Preview(contract));
        Row(
            block,
            "pinned identifiers",
            contract.ExternalIds.Count == 0
                ? "none — this operation pins nothing"
                : string.Join("; ", contract.ExternalIds.Select(kind => $"`{kind.Key}` names a {Name(kind.Value.Entity)}")));
        Row(block, "recovery read", contract.RecoveryRead is null ? "none" : contract.RecoveryRead.Reads);
        Row(block, "after an ambiguous end", Ambiguous(contract));
        Row(block, "timeout", contract.TimeoutMs.ToString(System.Globalization.CultureInfo.InvariantCulture) + " ms");
        Row(block, "invariants", contract.Invariants.Count == 0 ? "none names this operation" : string.Join(", ", contract.Invariants));
        block.Append('\n').Append(End);

        return block.ToString();
    }

    /// <summary>The table of the published operations, in the catalog's own order.</summary>
    private static string OperationsTable()
    {
        var table = new StringBuilder();
        table.Append(TableBegin).Append('\n');
        table.Append("<!-- Rendered from each document and its page's title. Edit those; this table follows them. -->\n\n");
        table.Append("| Operation | Level one | Reach | Approval | After an ambiguous end | Provider contact | What it is |\n|---|---|---|---|---|---|---|\n");
        foreach (var contract in OperationCatalog.All)
        {
            table.Append("| [`").Append(contract.Id).Append("`](operations/").Append(contract.Id).Append(".md) | `")
                .Append(contract.L1.Name).Append("` | `")
                .Append(contract.Reach.Value).Append("` | `")
                .Append(contract.Approval.Value).Append("` | `")
                .Append(Name(contract.RepeatAfterAmbiguous)).Append("` | `")
                .Append(Name(contract.Preflight.ProviderContact)).Append("` | ")
                .Append(Title(contract.Id)).Append(" |\n");
        }

        table.Append('\n').Append(TableEnd);
        return table.ToString();
    }

    /// <summary>The table of the journal's kinds, in the catalog's own order, each kind's names in the vocabulary's.</summary>
    private static string JournalKindsTable()
    {
        var table = new StringBuilder();
        table.Append(KindsBegin).Append('\n');
        table.Append("<!-- Rendered from each kind's document. Edit those; this table follows them. -->\n\n");
        table.Append("| Kind | Since | Subscribable | Names | What a line records |\n|---|---|---|---|---|\n");
        foreach (var contract in JournalKindCatalog.All)
        {
            var names = JournalKindContract.EntityNames.Where(contract.Entities.ContainsKey).ToList();
            table.Append("| [`").Append(contract.Kind).Append("`](journal/").Append(contract.Kind).Append(".json) | `")
                .Append(contract.Since).Append("` | ")
                .Append(contract.Subscribable ? "yes" : "no").Append(" | ")
                .Append(names.Count == 0 ? "none" : string.Join(", ", names)).Append(" | ")
                .Append(contract.Summary).Append(" |\n");
        }

        table.Append('\n').Append(KindsEnd);
        return table.ToString();
    }

    /// <summary>The one line a page's title gives its operation: what follows the name and the dash.</summary>
    private static string Title(string id)
    {
        var path = Path.Combine(ContractFiles.Operations, id + ".md");
        var first = File.ReadAllText(path).ReplaceLineEndings("\n").Split('\n')[0];
        var heading = $"# `{id}` — ";

        Assert.True(
            first.StartsWith(heading, StringComparison.Ordinal) && first.Length > heading.Length,
            $"{ContractFiles.Relative(path)} opens with '{first}', where its title names the operation and says in one line what it is.");
        return first[heading.Length..];
    }

    private static string Render(Jason.Contracts.Notifications.DeliveryContract contract)
    {
        var block = new StringBuilder();
        block.Append(Begin).Append('\n');
        block.Append("<!-- Rendered from ").Append(contract.Id).Append(".json. Edit the document; this block follows it. -->\n\n");
        block.Append("| What it is | Value |\n|---|---|\n");
        Row(block, "version", contract.Version.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Row(block, "input", string.Join(", ", Names(contract.InputSchema)));
        Row(block, "output", string.Join(", ", Names(contract.OutputSchema)) + ", or nothing");
        Row(block, "timeout", contract.TimeoutMs.ToString(System.Globalization.CultureInfo.InvariantCulture) + " ms");
        block.Append('\n');

        block.Append("| Failure code | Class | When |\n|---|---|---|\n");
        foreach (var code in contract.FailureCodes)
        {
            block.Append("| `").Append(code.Code).Append("` | `").Append(Name(code.Class)).Append("` | ").Append(code.When).Append(" |\n");
        }

        block.Append('\n').Append(End);
        return block.ToString();
    }

    private static string Render(Jason.Contracts.Events.EventsContract contract)
    {
        var block = new StringBuilder();
        block.Append(Begin).Append('\n');
        block.Append("<!-- Rendered from ").Append(contract.Id).Append(".json. Edit the document; this block follows it. -->\n\n");
        block.Append("| What it is | Value |\n|---|---|\n");
        Row(block, "version", contract.Version.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Row(block, "input", string.Join(", ", Names(contract.InputSchema)));
        Row(block, "output", string.Join(", ", Names(contract.OutputSchema)));
        Row(block, "kinds", string.Join(", ", contract.Kinds.Select(kind => "`" + kind + "`")));
        Row(block, "page", "at most " + ((int)contract.InputSchema["properties"]!["limit"]!["maximum"]!).ToString(System.Globalization.CultureInfo.InvariantCulture) + " events");
        Row(block, "timeout", contract.TimeoutMs.ToString(System.Globalization.CultureInfo.InvariantCulture) + " ms");
        block.Append('\n');

        block.Append("| Failure code | Class | When |\n|---|---|---|\n");
        foreach (var code in contract.FailureCodes)
        {
            block.Append("| `").Append(code.Code).Append("` | `").Append(Name(code.Class)).Append("` | ").Append(code.When).Append(" |\n");
        }

        block.Append('\n').Append(End);
        return block.ToString();
    }

    private static string Render(Jason.Contracts.Subscriptions.SubscriptionContract contract)
    {
        var block = new StringBuilder();
        block.Append(Begin).Append('\n');
        block.Append("<!-- Rendered from ").Append(contract.Id).Append(".json. Edit the document; this block follows it. -->\n\n");
        block.Append("| What it is | Value |\n|---|---|\n");
        Row(block, "version", contract.Version.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Row(block, "input", string.Join(", ", Names(contract.InputSchema)));
        Row(block, "output", string.Join(", ", Names(contract.OutputSchema)));
        Row(block, "timeout", contract.TimeoutMs.ToString(System.Globalization.CultureInfo.InvariantCulture) + " ms, lowered to the plugin's own limit");
        block.Append('\n');

        block.Append("| Failure code | Class | When |\n|---|---|---|\n");
        foreach (var code in contract.FailureCodes)
        {
            block.Append("| `").Append(code.Code).Append("` | `").Append(Name(code.Class)).Append("` | ").Append(code.When).Append(" |\n");
        }

        block.Append('\n').Append(End);
        return block.ToString();
    }

    private static IEnumerable<string> Names(JsonObject schema) =>
        ((JsonObject?)schema["properties"] ?? []).Select(property => "`" + property.Key + "`");

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

    private static string Preview(OperationContract contract) =>
        contract.Preview is null
            ? "none"
            : $"`{contract.Preview.Summary.Text}`; highlighted: {string.Join(", ", contract.Preview.Highlights.Select(pointer => "`" + pointer + "`"))}";

    private static string Ambiguous(OperationContract contract) => contract.RepeatAfterAmbiguous switch
    {
        RepeatAfterAmbiguous.Safe => "`safe` — repeating changes nothing and costs nothing, so the item is handed out again",
        RepeatAfterAmbiguous.AfterRecoveryRead when OutcomeContract.SettlesByKey(contract) =>
            "`after_recovery_read`, by the work item's key — a later attempt performs the recovery read above and answers from it when the effect already happened, and only under a plugin whose manifest lists the operation under `settles_by_key`",
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
