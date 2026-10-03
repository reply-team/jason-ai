using System.Text.RegularExpressions;
using Jason.Contracts.Operations;

namespace Jason.Runtime.Tests.Documentation;

/// <summary>
/// The pages that say what an operation is, how a person is handed to one, how a page is read and what a person
/// deciding reads — held to what the documents and the runtime now do, so a sentence that stops being true fails
/// here rather than being found by a reader.
/// </summary>
public sealed partial class CanonicalReadsDocTests
{
    [Theory]
    [InlineData("`contacts` appears when `preflight.contact` is `required`, or when it is `optional` and the work item names a contact")]
    [InlineData("## The preview a person reads")]
    [InlineData("The preview is never part of what is approved:")]
    [InlineData("A **preview fixture** is `{operation, version, case, input, expect}`")]
    [InlineData("## Pages")]
    [InlineData("The next page starts at `offset` plus the number of items this page returned, never at `offset + limit`.")]
    [InlineData("768 KiB")]
    [InlineData("**Two namespaces, the same words.**")]
    public void The_contracts_readme_says_it(string sentence) =>
        Assert.Contains(sentence, Flat(Source("docs/contracts/README.md")), StringComparison.Ordinal);

    [Theory]
    [InlineData("**`contacts`** appears when the operation's pre-flight says a contact is `required`, or when it says `optional` and the work item names one")]
    [InlineData("### Pages")]
    [InlineData("`provider_answer_too_large`")]
    [InlineData("a code every published contract declares")]
    public void The_plugin_authors_page_says_it(string sentence) =>
        Assert.Contains(sentence, Flat(Source("docs/plugins.md")), StringComparison.Ordinal);

    [Fact]
    public void The_routing_page_says_an_optional_contact_runs_with_one_or_without() =>
        Assert.Contains(
            "| 8 | `contact_required` | permanent | the operation acts on somebody and the work item names nobody. Create the item with `--contact`. An operation whose contact is `optional` runs with one or without, so it never meets this check |",
            Source("docs/routing.md"),
            StringComparison.Ordinal);

    [Fact]
    public void The_approvals_section_says_a_preview_may_lead_with_a_summary() =>
        Assert.Contains(
            "and, where the operation declares one, a summary rendered from the composed input with the values it highlights",
            Flat(Source("docs/work-execution.md")),
            StringComparison.Ordinal);

    /// <summary>
    /// Every place Jason's subset departs from the catalogue it is seeded from has a row, and the reads added rows of
    /// their own: the paging, what each read leaves out, and the words the catalogue lacks.
    /// </summary>
    [Theory]
    [InlineData("| Paging |")]
    [InlineData("| `enrollment.list` from the person's end |")]
    [InlineData("| `enrollment.list` status filter |")]
    [InlineData("| `engagement.summarize` grouping |")]
    [InlineData("| Basis and denominator |")]
    [InlineData("| Rates |")]
    [InlineData("| `conversation.list` order |")]
    [InlineData("| A thread's classification |")]
    [InlineData("| `conversation.list` unread-only filter |")]
    [InlineData("| `enrollment.list` step |")]
    [InlineData("| A thread's person |")]
    [InlineData("| `feature_unavailable` and `contact_not_linked` |")]
    [InlineData("| `provider_answer_too_large` |")]
    [InlineData("| `per_item_results` written plainly |")]
    [InlineData("| `accepts_collection` on the reads |")]
    public void The_divergence_file_has_a_row_for_it(string row) =>
        Assert.Contains(row, Source("docs/contracts/divergence-from-l1.md"), StringComparison.Ordinal);

    /// <summary>
    /// A page that counts the published operations counts them right, and one that lists them lists them all — the
    /// count is read from the catalog, so the next operation added fails here until the pages say so.
    /// </summary>
    [Theory]
    [InlineData("README.md")]
    [InlineData("docs/plugins.md")]
    [InlineData("docs/reports.md")]
    [InlineData("docs/routing.md")]
    [InlineData("docs/contracts/divergence-from-l1.md")]
    [InlineData("plugins/reply/README.md")]
    public void No_page_counts_the_published_operations_as_three(string page)
    {
        var text = Flat(Source(page));

        Assert.Equal(7, OperationCatalog.All.Count);
        Assert.DoesNotMatch(ThreePublished(), text);
    }

    [Fact]
    public void The_plugins_index_lists_every_operation_the_reply_plugin_performs()
    {
        var index = Source("plugins/README.md");
        var row = index.Split('\n').Single(line => line.StartsWith("| [`reply/`]", StringComparison.Ordinal));

        Assert.All(OperationCatalog.All, contract => Assert.Contains($"`{contract.Id}`", row, StringComparison.Ordinal));
    }

    [Fact]
    public void Work_execution_names_where_the_operations_are_rather_than_listing_them() =>
        Assert.Contains(
            "one of the canonical operations this build publishes a contract for — the documents under `docs/contracts/operations/` —",
            Flat(Source("docs/work-execution.md")),
            StringComparison.Ordinal);

    /// <summary>
    /// The draft roles say what this build gives them, and two of those sentences said a read did not exist. They now
    /// say where a reply and a count come from — the result of a provider operation — and the three that listed the
    /// operations say which two act.
    /// </summary>
    [Theory]
    [InlineData("skills/runtime/roles/responder/SKILL.md", "**This build reads a reply only through the provider.** A `provider_op` item naming `conversation.list` or `conversation.get` does it, and its result carries the threads; no verb of Jason's reads a mailbox.")]
    [InlineData("skills/runtime/roles/analyst/SKILL.md", "Jason records no open, click, reply or delivery of its own; the provider's counts reach it only as the result of a `provider_op` item naming `engagement.summarize`, with the envelope that says what each number counts.")]
    [InlineData("skills/runtime/roles/copywriter/SKILL.md", "The two that act are `campaign.enroll` and `list_membership.add`, and the rest only read:")]
    [InlineData("skills/runtime/roles/responder/SKILL.md", "The two that act are `campaign.enroll` and `list_membership.add`, and the rest only read:")]
    [InlineData("skills/runtime/roles/personalizer/SKILL.md", "The two that act are `campaign.enroll` and `list_membership.add`, and the rest only read:")]
    public void A_draft_role_says_where_a_read_comes_from(string skill, string sentence) =>
        Assert.Contains(sentence, Flat(Source(skill)), StringComparison.Ordinal);

    [Fact]
    public void The_register_records_how_canonical_operations_are_published() =>
        Assert.Contains(
            "| DEC-OPS-003 | Canonical operations are published in subsets chosen by the role sentences they retire and the chain they complete, each copied from the vendor-neutral catalogue with its properties unsoftened; an operation on the catalogue's protective floor runs `auto`, and the claim's own guards do not block it. |",
            Source("docs/architecture.md"),
            StringComparison.Ordinal);

    /// <summary>
    /// The preview is the contract's and the runtime's: declared by the document, rendered at the claim, and kept out
    /// of both the subject a person approves and the input a plugin receives — which the register row says, and the
    /// subject and the composed input show.
    /// </summary>
    [Fact]
    public void The_register_records_what_an_approval_preview_is_and_neither_the_subject_nor_the_plugin_carries_one()
    {
        Assert.Contains(
            "| DEC-APPR-001 | An approval's preview is declared by the operation's contract as a template over the composed input and rendered by the runtime at the claim; the plugin never sees it, and a population the preview names is held by the plugin at execution. |",
            Source("docs/architecture.md"),
            StringComparison.Ordinal);

        var enroll = OperationCatalog.Find("campaign.enroll")!;
        Assert.NotNull(enroll.Preview);
        var campaign = WorkItemFactory.NewCampaign();
        var input = new System.Text.Json.Nodes.JsonObject
        {
            ["args"] = new System.Text.Json.Nodes.JsonObject { ["collision"] = "skip" },
            ["campaign"] = new System.Text.Json.Nodes.JsonObject { ["name"] = "Flow" },
        };
        var subject = Jason.Runtime.Approvals.ApprovalSubject.Of(
            new Jason.Runtime.Routing.ProviderOpPlan(
                Plugins.TestPlugins.Loaded("provider", ["campaign.enroll"]),
                "snp_one",
                "rts_one",
                Jason.Contracts.Api.RouteScope.CampaignDefault,
                null,
                null,
                enroll,
                input),
            WorkItemFactory.NewProviderOp(campaign, "campaign.enroll"));
        var preview = Jason.Runtime.Approvals.ApprovalPreview.Of(enroll, subject, campaign, contact: null, channel: null);

        Assert.NotNull(preview["summary"]);
        foreach (var carrier in new[] { subject.Document, subject.Document["input"]!.AsObject(), preview["subject"]!.AsObject() })
        {
            Assert.DoesNotContain(carrier, member => member.Key is "preview" or "summary" or "highlights");
        }
    }

    private static string Flat(string text) => Whitespace().Replace(text, " ");

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    /// <summary>
    /// A count of the published operations as three, however it is put — "three published", "the published three",
    /// "all three operations". "The first three published" names a subset, and passes.
    /// </summary>
    [GeneratedRegex(
        @"(?<!first )\bthree (published|operations this (build|version) publishes)|\bpublished three\b|\ball three (published )?operations\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex ThreePublished();

    private static string Source(string relative)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine(directory.FullName, relative.Replace('/', Path.DirectorySeparatorChar)));
    }
}
