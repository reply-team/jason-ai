using Jason.Contracts.Operations;

namespace Jason.Contracts.Tests.OperationContracts;

/// <summary>
/// The template standard the contracts README publishes is the one the validator reads, row by row: a construct or a
/// name the page lists and the code does not, or the other way round, or the same one said differently, is a failure
/// that names the row.
/// </summary>
public class TemplateGrammarPageTests
{
    private const string Constructs = "### What a template may write";

    private const string Names = "### The names a template may use";

    [Fact]
    public void The_published_constructs_are_the_ones_the_validator_reads_row_by_row()
    {
        Compare(TemplateGrammar.Table(), PublishedTable(Constructs), Constructs);
    }

    [Fact]
    public void The_published_names_are_the_ones_the_validator_reads_row_by_row()
    {
        Compare(TemplateGrammar.NamesTable(), PublishedTable(Names), Names);
    }

    /// <summary>
    /// The table of names says what the composed input holds of each name on every channel a document offers a template
    /// on, and on no other: a channel it did not speak of would leave the example without an answer there.
    /// </summary>
    [Fact]
    public void The_names_speak_of_exactly_the_channels_a_template_is_offered_on()
    {
        var offered = OperationCatalog.All.SelectMany(contract => contract.ChannelTemplates ?? []).Distinct().Order(StringComparer.Ordinal);

        Assert.Equal(offered, TemplateGrammar.Channels.Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// What counts as no value, and how two values compare, is the provider's: the standard says so and leaves Reply's to
    /// the Reply package's README, the rule the runtime's own example follows is stated as the example's, not the
    /// standard's, and the standard says what that means for the message sent.
    /// </summary>
    [Fact]
    public void The_standard_leaves_no_value_and_comparing_to_the_provider()
    {
        var page = File.ReadAllText(Path.Combine(ContractFiles.Package, "README.md")).ReplaceLineEndings("\n");
        var start = page.IndexOf("\n## The template standard\n", StringComparison.Ordinal);
        Assert.True(start >= 0, "docs/contracts/README.md has no '## The template standard' heading.");
        var section = string.Join(' ', page[start..page.IndexOf(Constructs, start, StringComparison.Ordinal)].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        Assert.Contains(
            "What counts as no value, and how two values compare, is the provider's; [the Reply package's README](../../plugins/reply/README.md) says Reply's.",
            section,
            StringComparison.Ordinal);
        Assert.Contains("The approval's example counts a value that is null, empty or white space as no value", section, StringComparison.Ordinal);
        Assert.DoesNotContain("A value that is null, empty or white space is **no value**", section, StringComparison.Ordinal);
        Assert.DoesNotContain("A comparison compares the value with it exactly", section, StringComparison.Ordinal);
        Assert.Contains(
            "The message sent may take another branch than the example shows, and the provider may fill a name the example lists as unfilled.",
            section,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A comparison is a condition on the value being, or not being, the quoted text: how the two compare is the
    /// provider's, so the standard's row does not say exactly.
    /// </summary>
    [Fact]
    public void The_comparison_s_row_leaves_how_the_two_compare_to_the_provider()
    {
        var row = Assert.Single(TemplateGrammar.Table(), line => line.Contains("`{{#if name == 'text'}}`", StringComparison.Ordinal));

        Assert.EndsWith("| accepted | a condition on the value being, or not being, the quoted text |", row, StringComparison.Ordinal);
    }

    /// <summary>
    /// The pages say what ships: the Reply package refuses any <c>{{</c> in a final message, and from 0.10.0 writes a
    /// template in Reply's syntax rather than refusing every one.
    /// </summary>
    [Fact]
    public void The_pages_say_the_reply_package_writes_a_template_from_0_10_0()
    {
        var operation = Flat(File.ReadAllText(Path.Combine(ContractFiles.Operations, "message.send.md")));
        var contracts = Flat(File.ReadAllText(Path.Combine(ContractFiles.Package, "README.md")));

        Assert.Contains(
            "The Reply package refuses any `{{` in a final message, as it did before 0.10.0; from 0.10.0 it writes a template in Reply's syntax, and refuses before anything is called what it cannot write there.",
            operation,
            StringComparison.Ordinal);
        Assert.Contains("as the Reply package before 0.10.0 refused any `{{`; otherwise it is breaking.", contracts, StringComparison.Ordinal);
        Assert.DoesNotContain("every template", operation, StringComparison.Ordinal);
        Assert.DoesNotContain("refuses every template", contracts, StringComparison.Ordinal);
    }

    /// <summary>
    /// A plugin is handed a template only on a channel its manifest lists under <c>templates</c>, because the claim
    /// refuses one on any other channel before a child exists; so no plugin written before <c>compose</c> ever reads a
    /// template as a final message. The page says so, and says in one sentence what was offered before.
    /// </summary>
    [Fact]
    public void The_page_says_a_plugin_receives_a_template_only_on_a_channel_its_manifest_lists()
    {
        var operation = Flat(File.ReadAllText(Path.Combine(ContractFiles.Operations, "message.send.md")));

        Assert.Contains(
            "A plugin receives a template only on a channel its manifest lists under `templates`; on any other channel the claim refuses it, `template_not_offered`, before a child exists. Until Jason 0.22.0 only an email could be a template.",
            operation,
            StringComparison.Ordinal);
        Assert.DoesNotContain("reads a template as a final message", operation, StringComparison.Ordinal);
    }

    /// <summary>
    /// A send that goes now is not always made within the call: a provider may queue it behind the account's other sends
    /// and answer it accepted. What is filled in before anything goes is a send made within the call, so the page and the
    /// document say that of it, and that a template's length is its length once filled in, within the call or later.
    /// </summary>
    [Fact]
    public void A_template_the_provider_does_not_send_within_the_call_is_filled_in_when_it_goes()
    {
        var operation = Flat(File.ReadAllText(Path.Combine(ContractFiles.Operations, "message.send.md")));
        var compose = OperationCatalog.Find("message.send")!
            .InputSchema["properties"]!["args"]!["properties"]!["message"]!["properties"]!["compose"]!["description"]!.GetValue<string>();

        Assert.Contains(
            "A send the provider does not make within the call — held for a later moment, or queued by the provider behind the account's other sends — is filled in when it goes, and a template it cannot fill fails it then, which `message.status` shows as `failed` with the provider's reason — not `template_unrendered` at the send.",
            operation,
            StringComparison.Ordinal);
        Assert.DoesNotContain("A send held for later is filled in by the provider when it goes", operation, StringComparison.Ordinal);
        Assert.EndsWith(
            "The bounds below hold a template as written. Its length is the length once the provider fills it in, within the call or later: a text found too long within the call fails `message_too_long`, and one found too long later fails the send then.",
            compose,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The text a template went out as is the provider's to fill in, after the approval: the page says where that text is
    /// read afterwards, from the provider's read of the send, where the provider gives it.
    /// </summary>
    [Fact]
    public void The_page_says_where_the_text_the_provider_filled_in_is_read()
    {
        var operation = Flat(File.ReadAllText(Path.Combine(ContractFiles.Operations, "message.send.md")));

        Assert.Contains(
            "The text as the provider filled it in is read afterwards from `message.status`, as its `rendered_subject` and `rendered_body`, where the provider gives it.",
            operation,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The loader refuses an empty <c>channel_templates</c>, since a document whose message is never a template writes
    /// null, so the contracts README's row says the field is a non-empty list or null.
    /// </summary>
    [Fact]
    public void The_contracts_readme_says_channel_templates_is_a_non_empty_list_or_null()
    {
        var row = Assert.Single(File.ReadAllLines(Path.Combine(ContractFiles.Package, "README.md")), line => line.StartsWith("| `channel_templates` |", StringComparison.Ordinal));

        Assert.Contains(
            "the claim refusing a template elsewhere as `template_not_offered`: a non-empty list, or null where the message is never one.",
            row,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The example leaves to the provider every name the composed input does not hold, and on LinkedIn the input holds no
    /// email address, so the page names that among them.
    /// </summary>
    [Fact]
    public void The_page_names_the_person_s_email_on_linkedin_among_the_names_left_to_the_provider()
    {
        var operation = Flat(File.ReadAllText(Path.Combine(ContractFiles.Operations, "message.send.md")));

        Assert.Contains(
            "with the names the input does not hold — a custom field, the sending account's names, and on LinkedIn the person's email address — left for the provider",
            operation,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The names table has no category for a name the channel carries no value for, so the example rule names none:
    /// a name the input does not hold, and one it holds with no value, are the two the example lists.
    /// </summary>
    [Fact]
    public void The_example_rule_names_no_category_the_names_table_does_not_have()
    {
        var readme = Flat(File.ReadAllText(Path.Combine(ContractFiles.Package, "README.md")));

        Assert.DoesNotContain("the channel carries no value for", readme, StringComparison.Ordinal);
        Assert.Contains(
            "A name the input does not hold there, and a name it holds with no value, renders its fallback, or else itself in brackets",
            readme,
            StringComparison.Ordinal);
    }

    private static string Flat(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static void Compare(IReadOnlyList<string> expected, List<string> published, string heading)
    {
        for (var row = 0; row < Math.Max(expected.Count, published.Count); row++)
        {
            var wanted = row < expected.Count ? expected[row] : "(no row)";
            var found = row < published.Count ? published[row] : "(no row)";
            Assert.True(
                string.Equals(wanted, found, StringComparison.Ordinal),
                $"Line {row + 1} of the table under '{heading}' in docs/contracts/README.md:\n  the standard: {wanted}\n  the page:     {found}");
        }
    }

    /// <summary>The table's lines, from the first line that starts a table after the heading to the last.</summary>
    private static List<string> PublishedTable(string heading)
    {
        var lines = File.ReadAllLines(Path.Combine(ContractFiles.Package, "README.md"));
        var at = Array.IndexOf(lines, heading);
        Assert.True(at >= 0, $"docs/contracts/README.md has no '{heading}' heading.");

        return [.. lines.Skip(at + 1).SkipWhile(line => !line.StartsWith('|')).TakeWhile(line => line.StartsWith('|'))];
    }
}
