using System.Text;
using System.Text.Json;
using Jason.Contracts.Json;
using Jason.Contracts.Operations;

namespace Jason.Contracts.Tests.OperationContracts;

/// <summary>
/// Prose explains; the generated block governs. Each operation's markdown carries a table rendered from its own
/// document, and this is what stops the two disagreeing — the failure message is the block to paste.
/// </summary>
public class GeneratedBlockTests
{
    private const string Begin = "<!-- BEGIN GENERATED properties -->";
    private const string End = "<!-- END GENERATED properties -->";

    public static TheoryData<string> Published() => ContractDocumentTests.Published();

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
    public void The_readme_lists_the_dialect_the_validator_actually_enforces()
    {
        var readme = File.ReadAllText(Path.Combine(ContractFiles.Package, "README.md"));

        Assert.All(SchemaValidator.Keywords, keyword =>
            Assert.Contains("`" + keyword + "`", readme, StringComparison.Ordinal));
        Assert.DoesNotContain("`patternProperties`", readme, StringComparison.Ordinal);
        Assert.DoesNotContain("`dependentSchemas`", readme, StringComparison.Ordinal);
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
}
