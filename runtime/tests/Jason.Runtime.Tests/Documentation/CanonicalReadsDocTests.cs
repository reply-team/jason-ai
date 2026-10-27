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
    [InlineData("and how the plugin finds the provider's own contact for that person (`provider_contact`: `find_or_create`, `find_only`, `pinned_only` or `not_used`")]
    [InlineData("what must be true at the provider before the operation can act, beyond how its contact is found, in one sentence")]
    [InlineData("a required input field made optional")]
    [InlineData("**Breaking** — a newly required field, a removed field, a changed enum")]
    [InlineData("`find_or_create` takes its channel `from_args`")]
    [InlineData("are checked against that operation's argument schema, all but the one reserved key that [`args`](#the-input-a-plugin-receives) names")]
    [InlineData("One key of `args` is reserved for every operation and declared by none: **`provider`**, the provider's and its skills' own object, which a caller may place beside the arguments the document declares.")]
    [InlineData("The runtime never reads it: it holds the key to being a JSON object whose own member names are distinct, as every object a request carries must be, and to nothing else, refusing any other kind with `type` at the key's own pointer, sets it aside before the operation's argument schema applies, and hands it to the plugin exactly as written.")]
    public void The_contracts_readme_says_it(string sentence) =>
        Assert.Contains(sentence, Flat(Source("docs/contracts/README.md")), StringComparison.Ordinal);

    [Theory]
    [InlineData("**`contacts`** appears when the operation's pre-flight says a contact is `required`, or when it says `optional` and the work item names one")]
    [InlineData("### Pages")]
    [InlineData("`provider_answer_too_large`")]
    [InlineData("a code every published contract declares")]
    [InlineData("### The contact proxy rule")]
    [InlineData("`find_or_create` takes its channel `from_args`")]
    [InlineData("1. **The plugin owns the binding.**")]
    [InlineData("2. **A pin is worked by, and never overwritten.**")]
    [InlineData("3. **Deleting a Jason contact never deletes the provider's.**")]
    [InlineData("4. **Fields and lists travel with the operation that needs them.**")]
    [InlineData("If the provider answers with another identifier, return the one it answered: the runtime records it beside the pin as a divergence, keeps the pin, and the attempt still succeeds.")]
    [InlineData("The second keys a link by the profile's name and deletes nothing: a value the rule refuses is left as it stands")]
    [InlineData("Where the value the claim reads for a contact is left beside the one holding the key, the primary moves to the holder, so the claim asks the register about the key.")]
    [InlineData("No stop is lost by either rewrite, the journal keeps every line it had, and neither adds one.")]
    [InlineData("### `host.channelKey`")]
    [InlineData("### A read on request")]
    [InlineData("the runtime takes only the route's half of the claim: the plugin is loaded and available, it performs the operation, and the route's binding passes its checks.")]
    [InlineData("It skips the claim's other half, and with it the attempt, provenance, the journal, pins, approval and retry, so nothing is recorded about the read, no identifier you return is pinned, and the invocation's `context` carries no attempt, work item or campaign.")]
    [InlineData("Such a read has no bound of its own: it runs outside the handler pool `Dispatcher:MaxParallel` sizes, one child each.")]
    [InlineData("Its one reserved member, `provider`, is the caller's object for your package ([the rule](contracts/README.md#the-input-a-plugin-receives)): a package that reads it and cannot make sense of what it was given fails the item before any call, under a code the operation declares, with a message that names the shape it expects and never quotes the text, and a package that does not read it ignores it.")]
    public void The_plugin_authors_page_says_it(string sentence) =>
        Assert.Contains(sentence, Flat(Source("docs/plugins.md")), StringComparison.Ordinal);

    /// <summary>
    /// The plugin authors' page names, beside each way of finding the provider's contact, the operations published with
    /// it — read from the catalog, so a document added under a word fails here until the table names it, and the last
    /// row says the rest name nobody.
    /// </summary>
    [Fact]
    public void The_contact_proxy_rule_names_each_way_s_operations_as_the_documents_state_them()
    {
        var text = Source("docs/plugins.md").ReplaceLineEndings("\n");
        var start = text.IndexOf("### The contact proxy rule", StringComparison.Ordinal);
        var rows = text[start..text.IndexOf("\n### ", start + 1, StringComparison.Ordinal)]
            .Split('\n')
            .Where(line => line.StartsWith("| `", StringComparison.Ordinal) && !line.StartsWith("| `preflight.", StringComparison.Ordinal))
            .ToDictionary(line => line.Split('|')[1].Trim().Trim('`'), line => line.Split('|')[3], StringComparer.Ordinal);

        Assert.Equal(["find_only", "find_or_create", "not_used", "pinned_only"], rows.Keys.Order(StringComparer.Ordinal));
        foreach (var (word, cell) in rows)
        {
            var named = Regex.Matches(cell, "`(?<id>[a-z_]+\\.[a-z_]+)`").Select(match => match.Groups["id"].Value).Order(StringComparer.Ordinal).ToList();
            var stated = OperationCatalog.All
                .Where(contract => System.Text.Json.JsonSerializer.Serialize(contract.Preflight.ProviderContact, Jason.Contracts.Json.JasonJson.Options).Trim('"') == word)
                .Select(contract => contract.Id)
                .Order(StringComparer.Ordinal)
                .ToList();

            if (word == "not_used")
            {
                Assert.True(named.Count == 0 && cell.Contains("every other operation", StringComparison.Ordinal), $"The `not_used` row reads: {cell}");
                Assert.NotEmpty(stated);
                continue;
            }

            Assert.True(named.SequenceEqual(stated), $"The `{word}` row names {string.Join(", ", named)}, and the documents state {string.Join(", ", stated)}.");
        }
    }

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

    /// <summary>The refusal of an argument points to the one key every operation reserves, and to the rule that key keeps.</summary>
    [Fact]
    public void The_invalid_argument_row_points_to_the_provider_rule() =>
        Assert.Contains(
            "all of them reported at once. The one key every operation reserves, `provider`, is held only to [its own rule](contracts/README.md#the-input-a-plugin-receives) |",
            Source("docs/work-execution.md"),
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
    [InlineData("| The contact proxy rule as a field |")]
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
    [InlineData("| An update of one record |")]
    [InlineData("| Which fields an update changes |")]
    [InlineData("| `field_not_found` and `invalid_field_value` |")]
    [InlineData("| Deleting by the pin alone |")]
    [InlineData("| `contact_opted_out` |")]
    [InlineData("| `accepts_collection` on the invitation |")]
    [InlineData("| What `sender.list` lists |")]
    [InlineData("| Whose accounts a list offers |")]
    [InlineData("| `sender.list` status vocabulary |")]
    [InlineData("| A send names its sender by its address too |")]
    [InlineData("| A profile travels with a LinkedIn send |")]
    [InlineData("| The provider's own stop before a send |")]
    [InlineData("| `contact_channel_conflict`, `sender_not_found`, `sender_mismatch`, `sender_unavailable`, `sender_limit_reached` and `message_too_long` |")]
    [InlineData("| A send that names its recipient |")]
    [InlineData("| An email's text is written as markup |")]
    [InlineData("| `not_connected` and `insufficient_credits` |")]
    [InlineData("| Named schedules |")]
    [InlineData("| The provider's own object in the arguments |")]
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
    [InlineData("docs/contracts/divergence-from-l1.md", "auto for a single record; confirm_once for a collection.")]
    [InlineData("docs/contracts/divergence-from-l1.md", "Change these fields on these people")]
    [InlineData("docs/contracts/divergence-from-l1.md", "Take this person out of the working database")]
    [InlineData("docs/contracts/divergence-from-l1.md", "mailboxes, social accounts, numbers and business messaging identities in one list")]
    [InlineData("docs/contracts/divergence-from-l1.md", "Show me the actual calendar")]
    [InlineData("docs/contracts/divergence-from-l1.md", "the invitation state for this pair: none / pending / accepted / withdrawn / declined.")]
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
    /// The pin passage and the precondition row speak for every operation about a person, the stop included: every
    /// other write that takes a person creates them where the provider holds nobody, the stop creates nobody, and a pin
    /// the provider no longer holds is, for the stop, the completed stop it cannot be told apart from.
    /// </summary>
    [Theory]
    [InlineData("the stop never creates anybody")]
    [InlineData("the enrollment, the list add, the opt-out, the update, the invitation and the message look the person up and create them only when nobody matches")]
    [InlineData("Every one of them but the stop creates the contact when the provider holds nobody under the value, and only then")]
    [InlineData("a person Jason holds is not refused for being new to the provider")]
    [InlineData("the read of a person's threads and the delete work by the pin and fail `contact_not_linked` without one")]
    [InlineData("Eight of the writes about a person declare `contact_not_found` for it")]
    [InlineData("for `contact.delete` the same missing contact is `contact_not_found` on a first attempt and `already_deleted` on any later attempt, since an earlier attempt may have deleted it")]
    [InlineData("The ninth, the reply in a thread, declares none")]
    [InlineData("for `enrollment.stop` a pin the provider no longer holds reads as the completed stop it cannot be told apart from")]
    [InlineData("except the stop, which reads it as a person who takes no part")]
    [InlineData("it also covers a lookup by address the plugin could not finish")]
    [InlineData("reply-cli gives an unfinished lookup the same word as an unreachable Reply, so the Reply plugin answers `provider_unavailable`, which is transient")]
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
        "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen", "twenty", "twenty-one", "twenty-two",
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
    /// The approvals section says a pin the item's own earlier attempt learned changes what the plugin receives, so a
    /// person decides once more: the claim composes the input with every pin, and the page may not promise otherwise.
    /// </summary>
    [Fact]
    public void Work_execution_says_a_pin_an_earlier_attempt_learned_asks_for_one_more_decision()
    {
        var text = Flat(Source("docs/work-execution.md"));

        Assert.Contains("A pin that the item's own earlier attempt learned is such a change too", text, StringComparison.Ordinal);
        Assert.Contains("a person decides once more on what the plugin now receives", text, StringComparison.Ordinal);
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
    /// The table of the claim's fourteen answers says, at row 10, which operations the suppression register does not
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
    /// Row 11 of the same table says which operations work by the routed plugin's pin alone — every one the catalog
    /// marks `pinned_only`, and the field that marks it.
    /// </summary>
    [Fact]
    public void Routing_row_11_names_every_operation_that_works_by_the_pin_alone()
    {
        var row = Source("docs/routing.md").Split('\n').Single(line => line.StartsWith("| 11 | `contact_not_linked` |", StringComparison.Ordinal));
        var pinnedOnly = OperationCatalog.All.Where(contract => contract.Preflight.ProviderContact == ProviderContact.PinnedOnly).ToList();

        Assert.Contains("`preflight.provider_contact: pinned_only`", row, StringComparison.Ordinal);
        Assert.NotEmpty(pinnedOnly);
        Assert.All(pinnedOnly, contract => Assert.Contains($"`{contract.Id}`", row, StringComparison.Ordinal));
    }

    /// <summary>
    /// An operation that may create the provider's contact says, where its answer may be lost, that the contact may
    /// already have been made — so a repeat reads before it creates, whatever the operation does besides.
    /// </summary>
    [Fact]
    public void Every_operation_that_may_create_the_provider_s_contact_says_a_lost_answer_may_have_made_it()
    {
        var creating = OperationCatalog.All.Where(contract => contract.Preflight.ProviderContact == ProviderContact.FindOrCreate).ToList();

        Assert.NotEmpty(creating);
        Assert.All(creating, contract =>
            Assert.Contains(
                "the contact may already have been made",
                contract.FailureCodes.Single(code => code.Code == "provider_answer_lost").When,
                StringComparison.Ordinal));
    }

    /// <summary>
    /// The routing page names the reads on request once: which operations they are, what they skip of a claim, and how
    /// the account is chosen — the operations read from the code that serves them.
    /// </summary>
    [Theory]
    [InlineData("### A read on request")]
    [InlineData("`contact.threads`")]
    [InlineData("`contact.thread`")]
    [InlineData("It skips the claim's other half, and with it the attempt, provenance, the journal, pins, approval and retry")]
    [InlineData("whose binding has the identity recorded in the provenance of the attempt that learned the pin")]
    public void The_routing_page_names_the_read_on_request(string sentence) =>
        Assert.Contains(sentence, Flat(Source("docs/routing.md")), StringComparison.Ordinal);

    /// <summary>
    /// The two published pages that say what a read on request keeps of a claim and what it skips say it in the words
    /// the routing page does, so a change to the routing page's wording fails here until they follow.
    /// </summary>
    [Theory]
    [InlineData("docs/plugins.md")]
    [InlineData("docs/ui.md")]
    public void The_published_pages_say_a_read_on_request_in_the_routing_page_s_words(string page)
    {
        var published = Flat(Source(page));
        var routing = Flat(Source("docs/routing.md"));

        foreach (var words in new[] { "the plugin is loaded and available, it performs the operation, and the route's binding passes its checks", "the attempt, provenance, the journal, pins, approval and retry" })
        {
            Assert.Contains(words, published, StringComparison.Ordinal);
            Assert.Contains(words, routing, StringComparison.Ordinal);
        }
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

    /// <summary>Every count campaign.summary and campaign.activity answer, as their wire names say it, with its definition.</summary>
    public static TheoryData<string> Counts =>
    [
        .. new[] { "people" }
            .Concat(Enum.GetValues<Jason.Contracts.Api.CampaignSummaryFilter>().Select(count => count.ToString()))
            .Concat(Enum.GetValues<Jason.Contracts.Api.CampaignActivityFilter>().Select(count => count.ToString()))
            .Select(System.Text.Json.JsonNamingPolicy.SnakeCaseLower.ConvertName)
            .Distinct(),
    ];

    [Theory]
    [MemberData(nameof(Counts))]
    public void Work_execution_defines_every_count_of_a_campaign_s_people(string count)
    {
        var page = Source("docs/work-execution.md");
        var start = page.IndexOf("## A campaign's people, counted", StringComparison.Ordinal);
        Assert.True(start >= 0, "docs/work-execution.md has no section \"A campaign's people, counted\".");
        var end = page.IndexOf("\n## ", start + 1, StringComparison.Ordinal);
        Assert.Contains($"- `{count}` — ", page[start..(end < 0 ? page.Length : end)], StringComparison.Ordinal);
    }

    /// <summary>The series' two refusals, in the numbers the runtime enforces.</summary>
    [Fact]
    public void Work_execution_states_the_limits_a_series_is_refused_by()
    {
        var text = Flat(Source("docs/work-execution.md"));

        Assert.Contains($"A window cut into more than {Jason.Runtime.Campaigns.ActivityBuckets.MaxBuckets} buckets is refused", text, StringComparison.Ordinal);
        Assert.Equal(8, Jason.Runtime.Campaigns.ActivityBuckets.Edge.TotalDays);
        Assert.Contains("within eight days of either end of the calendar", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Work_execution_names_where_the_operations_are_rather_than_listing_them() =>
        Assert.Contains(
            "one of the canonical operations this build publishes a contract for — the documents under `docs/contracts/operations/` —",
            Flat(Source("docs/work-execution.md")),
            StringComparison.Ordinal);

    /// <summary>
    /// The draft roles say what this build gives them, and two of those sentences said a read did not exist. They now
    /// say where a reply and a count come from — the result of a provider operation — and the two that listed the
    /// operations that act now say what writing does without counting the writes, which a new one would make untrue. The
    /// responder, which now writes a reply, has its sentence held by its own role's tests.
    /// </summary>
    [Theory]
    [InlineData("skills/runtime/roles/responder/SKILL.md", "**This build reads a reply only through the provider.** A `provider_op` item naming `conversation.list` or `conversation.get` does it, and its result carries the threads; no verb of Jason's reads a mailbox.")]
    [InlineData("skills/runtime/roles/analyst/SKILL.md", "Jason records no open, click, reply or delivery of its own; the provider's counts reach it only as the result of a `provider_op` item naming `engagement.summarize`, with the envelope that says what each number counts.")]
    [InlineData("skills/runtime/roles/copywriter/SKILL.md", "The other operations write no message of their own: enrolling somebody puts them into the provider's own campaign, starting a campaign releases its first step to everybody in it,")]
    [InlineData("skills/runtime/roles/personalizer/SKILL.md", "The other operations write no message of their own: enrolling somebody puts them into the provider's own campaign, starting a campaign releases its first step to everybody in it,")]
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
        @"(?<!first )\b(?<n>zero|one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve|thirteen|fourteen|fifteen|sixteen|seventeen|eighteen|nineteen|twenty-two|twenty-one|twenty) (published operations|operations this (build|version) publishes|operations, not a catalog|operations — the whole set)|\bpublished (?<n>zero|one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve|thirteen|fourteen|fifteen|sixteen|seventeen|eighteen|nineteen|twenty-two|twenty-one|twenty)\b",
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
