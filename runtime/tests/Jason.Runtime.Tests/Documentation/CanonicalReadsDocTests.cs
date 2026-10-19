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
    [InlineData("| `campaign.resume` |")]
    [InlineData("| A first-touch ledger |")]
    [InlineData("| `expected.enrolled` |")]
    [InlineData("| A population that moved |")]
    [InlineData("| `campaign_not_pausable` and `campaign_not_activatable` |")]
    [InlineData("| A reason for a pause |")]
    [InlineData("| Work in a paused Jason campaign |")]
    [InlineData("| Two records of a stop |")]
    [InlineData("| The suppression register and a stop |")]
    [InlineData("| Finding a person before creating one |")]
    [InlineData("| `contact_not_unique` |")]
    [InlineData("| A reason for a stop |")]
    [InlineData("| Taken out, never finished |")]
    [InlineData("| A pin the provider no longer holds |")]
    [InlineData("| What a metered write consumed |")]
    [InlineData("| A send bound to a thread |")]
    [InlineData("| Nobody else on a reply |")]
    [InlineData("| A lost send is never repeated |")]
    [InlineData("| Closing a thread is reading it |")]
    [InlineData("| `channel_mismatch`, `thread_contact_mismatch` and `delivery_refused` |")]
    [InlineData("| `message_invalid` |")]
    [InlineData("| `category_not_found` and `category_not_unique` |")]
    [InlineData("| `category_opts_out` |")]
    public void The_divergence_file_has_a_row_for_it(string row) =>
        Assert.Contains(row, Source("docs/contracts/divergence-from-l1.md"), StringComparison.Ordinal);

    /// <summary>
    /// Where a page quotes the catalogue, the words are the catalogue's: each quotation is on the page and, with the
    /// comment marks and line breaks of the YAML taken out, in the catalogue's own files. A paraphrase in quotation
    /// marks is how a page comes to argue with something the catalogue never said.
    /// </summary>
    [Theory]
    [InlineData("docs/contracts/divergence-from-l1.md", "a mandatory preview naming the population, not counting it")]
    [InlineData("docs/contracts/divergence-from-l1.md", "whose non-performance is itself the harm")]
    [InlineData("docs/contracts/divergence-from-l1.md", "Take them out, and record why")]
    [InlineData("docs/contracts/divergence-from-l1.md", "the reason is mandatory and must be able to name a different person as the cause")]
    [InlineData("docs/contracts/divergence-from-l1.md", "Every metered operation returns what it actually consumed, including zero")]
    [InlineData("docs/contracts/README.md", "true when the value RAISES above the derivation table")]
    [InlineData("docs/contracts/README.md", "true for the one legitimate LOWERING: an operation whose non-performance is itself the harm")]
    public void A_quotation_of_the_catalogue_is_the_catalogue_s_words(string page, string quotation)
    {
        Assert.Contains($"\"{quotation}\"", Flat(Source(page)), StringComparison.Ordinal);

        var words = string.Join(' ', Directory.GetFiles(Rooted("skills/business/sdr-operations/operations"), "*.yaml").Order(StringComparer.Ordinal).Select(file => Flat(CommentMarks().Replace(File.ReadAllText(file), " "))));
        Assert.Contains(quotation, words, StringComparison.Ordinal);
    }

    /// <summary>
    /// The catalogue's approval for an enrollment and an activation asks for a preview that names the population, and
    /// Jason's activation preview counts it: the row says so and puts it back to the catalogue as a question, rather
    /// than calling it no disagreement.
    /// </summary>
    [Fact]
    public void The_approval_row_says_the_activation_preview_counts_and_asks_back()
    {
        var row = Source("docs/contracts/divergence-from-l1.md").Split('\n').Single(line => line.StartsWith("| Approval |", StringComparison.Ordinal));

        Assert.Contains("counts the population", row, StringComparison.Ordinal);
        Assert.Contains("it names no member of the population", row, StringComparison.Ordinal);
        Assert.EndsWith("| **Yes** — whether the campaign with its count is the naming `+p` asks for |", row.TrimEnd('\r'), StringComparison.Ordinal);
    }

    /// <summary>
    /// The pin passage and the precondition row speak for every operation about a person, the stop included: the stop
    /// creates nobody, and a pin the provider no longer holds is, for the stop, the completed stop it cannot be told
    /// apart from.
    /// </summary>
    [Theory]
    [InlineData("the stop never creates anybody")]
    [InlineData("the opt-out looks the address up first and creates it only when nobody matches")]
    [InlineData("Four of the writes about a person declare `contact_not_found` for it")]
    [InlineData("The fifth, the reply in a thread, declares none")]
    [InlineData("for `enrollment.stop` a pin the provider no longer holds reads as the completed stop it cannot be told apart from")]
    [InlineData("except the stop, which reads it as a person who takes no part")]
    [InlineData("it also covers a lookup by address the plugin could not finish")]
    public void The_divergence_file_speaks_for_every_operation_about_a_person(string sentence) =>
        Assert.Contains(sentence, Flat(Source("docs/contracts/divergence-from-l1.md")), StringComparison.Ordinal);

    /// <summary>
    /// A page that counts the published operations counts them right: the number word is the catalog's own count, so
    /// the next operation added fails here until every page says so, and any other number in a count phrase fails
    /// whatever it is. The five pages that count say it at least once; the divergence file may not count at all.
    /// </summary>
    [Theory]
    [InlineData("README.md", true)]
    [InlineData("docs/plugins.md", true)]
    [InlineData("docs/reports.md", true)]
    [InlineData("docs/routing.md", true)]
    [InlineData("docs/contracts/divergence-from-l1.md", false)]
    [InlineData("plugins/reply/README.md", true)]
    public void A_page_that_counts_the_published_operations_says_the_catalog_s_number(string page, bool counts)
    {
        var expected = NumberWords[OperationCatalog.All.Count];
        var said = CountPhrase().Matches(Flat(Source(page))).Select(match => match.Groups["n"].Value.ToLowerInvariant()).ToList();

        Assert.True(!counts || said.Count > 0, $"{page} no longer counts the published operations.");
        Assert.All(said, word => Assert.Equal(expected, word));
    }

    private static readonly string[] NumberWords =
    [
        "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten",
        "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen", "twenty",
    ];

    [Fact]
    public void The_plugins_index_lists_every_operation_the_reply_plugin_performs()
    {
        var index = Source("plugins/README.md");
        var row = index.Split('\n').Single(line => line.StartsWith("| [`reply/`]", StringComparison.Ordinal));

        Assert.All(OperationCatalog.All, contract => Assert.Contains($"`{contract.Id}`", row, StringComparison.Ordinal));
    }

    /// <summary>
    /// The eligibility rule names the field that lets work run in a paused campaign, and every operation that declares
    /// it today — read from the catalog, so a third fails here until the sentence says so.
    /// </summary>
    [Fact]
    public void The_eligibility_rule_names_the_field_and_every_operation_that_runs_in_a_paused_campaign()
    {
        var text = Flat(Source("docs/work-execution.md"));
        var start = text.IndexOf("2. its campaign is `active`", StringComparison.Ordinal);
        var end = text.IndexOf("3. `not_before`", start, StringComparison.Ordinal);
        var item = text[start..end];

        Assert.Contains("— or `paused`", item, StringComparison.Ordinal);
        Assert.Contains("`dispatch.in_paused_campaign: runs`", item, StringComparison.Ordinal);
        Assert.NotEmpty(OperationCatalog.RunsInPausedCampaign);
        Assert.All(OperationCatalog.RunsInPausedCampaign, id => Assert.Contains($"`{id}`", item, StringComparison.Ordinal));
    }

    /// <summary>
    /// The approvals section names the operations a person confirms, and exactly those: every operation the catalog
    /// publishes with an approval other than `auto`, under the word it publishes, and none that runs `auto`.
    /// </summary>
    [Fact]
    public void Work_execution_names_every_operation_a_person_confirms_and_no_other()
    {
        var text = Flat(Source("docs/work-execution.md"));
        var start = text.IndexOf("the one approval a runtime may give itself.", StringComparison.Ordinal);
        var end = text.IndexOf("and work naming any of them is **parked**", start, StringComparison.Ordinal);
        var sentence = text[start..end];

        foreach (var contract in OperationCatalog.All)
        {
            Assert.True(
                (contract.Approval.Value != "auto") == sentence.Contains($"`{contract.Id}`", StringComparison.Ordinal),
                $"`{contract.Id}` is approved `{contract.Approval.Value}`, and the approvals sentence reads: {sentence}");
        }

        Assert.All(
            OperationCatalog.All.Where(contract => contract.Approval.Value != "auto").Select(contract => contract.Approval.Value).Distinct(),
            word => Assert.Contains($"`{word}`", sentence, StringComparison.Ordinal));
    }

    /// <summary>
    /// The table of the claim's twelve answers says, at row 10, which operations the suppression register does not
    /// refuse — every one the catalog marks `not_applicable`, and the field that marks it.
    /// </summary>
    [Fact]
    public void Routing_row_10_names_every_operation_the_register_does_not_refuse()
    {
        var row = Source("docs/routing.md").Split('\n').Single(line => line.StartsWith("| 10 | `suppressed` |", StringComparison.Ordinal));
        var exempt = OperationCatalog.All.Where(contract => contract.Preflight.Suppression == SuppressionCheck.NotApplicable).ToList();

        Assert.Contains("`preflight.suppression: not_applicable`", row, StringComparison.Ordinal);
        Assert.NotEmpty(exempt);
        Assert.All(exempt, contract => Assert.Contains($"`{contract.Id}`", row, StringComparison.Ordinal));
    }

    /// <summary>
    /// What "ambiguous" costs is told per repeat rule, and the operations that say `never` are named there — read from
    /// the catalog, so a send published later fails here until the page says so too.
    /// </summary>
    [Fact]
    public void The_routing_page_names_every_operation_that_is_never_repeated()
    {
        var text = Flat(Source("docs/routing.md"));
        var start = text.IndexOf("## 8. What \"ambiguous\" costs, in operator terms", StringComparison.Ordinal);
        var end = text.IndexOf("## 9.", start, StringComparison.Ordinal);
        var section = text[start..end];
        var never = OperationCatalog.All.Where(contract => contract.RepeatAfterAmbiguous == RepeatAfterAmbiguous.Never).ToList();

        Assert.NotEmpty(never);
        Assert.All(never, contract => Assert.Contains($"`{contract.Id}` says `never`", section, StringComparison.Ordinal));
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
    /// operations that act now say what writing does without counting the writes, which a new one would make untrue.
    /// </summary>
    [Theory]
    [InlineData("skills/runtime/roles/responder/SKILL.md", "**This build reads a reply only through the provider.** A `provider_op` item naming `conversation.list` or `conversation.get` does it, and its result carries the threads; no verb of Jason's reads a mailbox.")]
    [InlineData("skills/runtime/roles/analyst/SKILL.md", "Jason records no open, click, reply or delivery of its own; the provider's counts reach it only as the result of a `provider_op` item naming `engagement.summarize`, with the envelope that says what each number counts.")]
    [InlineData("skills/runtime/roles/copywriter/SKILL.md", "Some write and the rest only read: enrolling somebody puts them into the provider's own campaign, starting a campaign releases its first step to everybody in it,")]
    [InlineData("skills/runtime/roles/responder/SKILL.md", "Some write and the rest only read: enrolling somebody puts them into the provider's own campaign, starting a campaign releases its first step to everybody in it,")]
    [InlineData("skills/runtime/roles/personalizer/SKILL.md", "Some write and the rest only read: enrolling somebody puts them into the provider's own campaign, starting a campaign releases its first step to everybody in it,")]
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

    [GeneratedRegex(@"#\s*")]
    private static partial Regex CommentMarks();

    /// <summary>
    /// A count of the published operations, however a page puts it — "all N published operations", "Only N published
    /// operations", "N operations, not a catalog", "N operations — the whole set", "the published N". "The first three
    /// published" names a subset, and is not a count.
    /// </summary>
    [GeneratedRegex(
        @"(?<!first )\b(?<n>zero|one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve|thirteen|fourteen|fifteen|sixteen|seventeen|eighteen|nineteen|twenty) (published operations|operations this (build|version) publishes|operations, not a catalog|operations — the whole set)|\bpublished (?<n>zero|one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve|thirteen|fourteen|fifteen|sixteen|seventeen|eighteen|nineteen|twenty)\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex CountPhrase();

    private static string Source(string relative) => File.ReadAllText(Rooted(relative));

    private static string Rooted(string relative)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine(directory.FullName, relative.Replace('/', Path.DirectorySeparatorChar));
    }
}
