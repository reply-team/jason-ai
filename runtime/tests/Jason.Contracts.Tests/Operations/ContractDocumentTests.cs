using System.Text.Json.Nodes;
using Jason.Contracts.Operations;
using Jason.Runtime.Tests;

namespace Jason.Contracts.Tests.OperationContracts;

/// <summary>
/// The rules a published contract has to satisfy to be worth enforcing. Each one is separate so that a failure
/// names the operation and the rule rather than "a document is wrong somewhere".
/// </summary>
public class ContractDocumentTests
{
    /// <summary>Every member an operation document states, and nothing else: absence is not a default.</summary>
    private static readonly string[] Members =
    [
        "id", "version", "l1", "intent",
        "reach", "reversibility", "approval", "approval_artefact", "approval_departs", "protective_floor",
        "approval_reason", "before_repeating", "idempotency_key", "per_item_results", "accepts_collection", "cost",
        "cost_basis", "meter", "invariants", "preflight", "dispatch", "contact_projection", "precondition", "preview",
        "input_schema", "output_schema", "external_ids",
        "recovery_read", "repeat_after_ambiguous", "failure_codes", "timeout_ms", "conformance",
    ];

    public static TheoryData<string> Published()
    {
        var data = new TheoryData<string>();
        foreach (var contract in OperationCatalog.All)
        {
            data.Add(contract.Id);
        }

        return data;
    }

    // Critical: message.send says never; campaign.enroll does not.
    public static TheoryData<string> CriticalPublishedForTheSendRule() =>
        CriticalRows.Mark(Published(), Family.W5, data => data is ["message.send"] or ["campaign.enroll"]);

    // Critical: the embedded resource is the published file.
    public static TheoryData<string> CriticalPublishedForTheFileOnDisk() =>
        CriticalRows.Mark(Published(), Family.R3, data => data is ["message.send"]);

    // Critical: a document is closed.
    public static TheoryData<string> CriticalPublishedForEveryField() =>
        CriticalRows.Mark(Published(), Family.R3, data => data is ["message.send"]);

    // Critical: approval implies a preview.
    public static TheoryData<string> CriticalPublishedForThePreview() =>
        CriticalRows.Mark(Published(), Family.S2, data => data is ["message.send"]);

    // Critical: the schemas use the schema dialect's vocabulary.
    public static TheoryData<string> CriticalPublishedForTheDialect() =>
        CriticalRows.Mark(Published(), Family.R3, data => data is ["message.send"]);

    private static OperationContract Contract(string id) => OperationCatalog.Find(id)!;

    private static JsonObject Document(string id) =>
        (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(ContractFiles.Operations, id + ".json")))!;

    [Fact]
    public void The_catalog_publishes_exactly_the_operations_of_this_version()
    {
        Assert.Equal(
            [
                "campaign.activate", "campaign.create", "campaign.enroll", "campaign.get", "campaign.pause", "contact.delete",
                "contact.update", "conversation.classify", "conversation.close", "conversation.get", "conversation.list",
                "conversation.reopen", "conversation.reply",
                "engagement.summarize", "enrollment.list", "enrollment.stop", "list_membership.add", "message.send", "optout.record",
                "sender.list", "social_invitation.send",
            ],
            OperationCatalog.All.Select(c => c.Id));
        Assert.All(OperationCatalog.All, contract => Assert.Equal(1, contract.Version));
    }

    /// <summary>
    /// A failure code is one word in one vocabulary: the runtime reads the class, so a code filed as permanent in one
    /// document and transient in another would be repeated or given up on depending on which operation said it.
    /// </summary>
    [Fact]
    [Critical(Family.R1, "a failure code has one class")]
    public void A_failure_code_has_the_same_class_in_every_document_that_declares_it()
    {
        var classes = OperationCatalog.All
            .SelectMany(contract => contract.FailureCodes.Select(code => (contract.Id, code.Code, code.Class)))
            .GroupBy(entry => entry.Code, StringComparer.Ordinal);

        Assert.All(classes, code => Assert.True(
            code.Select(entry => entry.Class).Distinct().Count() == 1,
            $"`{code.Key}` is declared as " + string.Join(", ", code.Select(entry => $"{entry.Class} by {entry.Id}")) + "."));
    }

    /// <summary>
    /// A provider can refuse any call for a reason outside the work item — money, a limit it counts, a credential, a
    /// channel — so every operation declares the one code of the class <c>blocked</c>, the protective ones included, and
    /// no other code is filed under it: the code is the one that carries the block.
    /// </summary>
    [Theory]
    [MemberData(nameof(Published))]
    public void Every_operation_declares_the_provider_block_and_nothing_else_under_its_class(string id)
    {
        var blocked = Contract(id).FailureCodes.Where(code => code.Class == Jason.Contracts.Plugins.FailureClass.Blocked).ToList();

        Assert.Equal("provider_blocked", Assert.Single(blocked).Code);
        Assert.Contains("did nothing for it", blocked[0].When, StringComparison.Ordinal);
    }

    /// <summary>
    /// The host's own codes pass beside every operation's list under the class the host writes them with, so a document
    /// that declared one would file a word of the host's under a class of its own and leave two meanings for one code.
    /// </summary>
    [Theory]
    [MemberData(nameof(Published))]
    public void No_operation_declares_a_code_the_plugin_host_writes_itself(string id)
    {
        var taken = Contract(id).FailureCodes.Select(code => code.Code).Where(Jason.Contracts.Plugins.HostOutcomeCodes.All.Contains).ToList();

        Assert.True(taken.Count == 0, $"'{id}' declares the host's own code(s): {string.Join(", ", taken)}.");
    }

    [Theory]
    [MemberData(nameof(CriticalPublishedForEveryField))]
    public void Every_document_states_every_field_and_nothing_else(string id)
    {
        var document = Document(id);

        Assert.Equal(Members.Order(StringComparer.Ordinal), document.Select(member => member.Key).Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// The catalogue lets an operation lower its approval only as a protective floor and raise it only by a stated
    /// departure, and either one needs a reason. In this version three operations carry the catalogue's marks: recording
    /// that somebody said stop, whose not running is itself the harm, on the floor; deleting a provider's contact, raised
    /// because it destroys working history other operations key on; and an invitation to connect, raised because its note
    /// is content addressed to a named person. Every other document says it does neither, in the two fields, rather than
    /// leaving the reading of their absence to whoever reads it next.
    /// </summary>
    [Theory]
    [MemberData(nameof(Published))]
    public void Only_the_record_of_a_stop_lowers_its_approval_only_it_a_delete_and_an_invitation_depart_and_each_says_why(string id)
    {
        var contract = Contract(id);
        var floor = id == "optout.record";
        var departs = floor || id is "contact.delete" or "social_invitation.send";

        Assert.Equal(floor, contract.ProtectiveFloor);
        Assert.Equal(departs, contract.ApprovalDeparts);
        Assert.Equal(departs, contract.ApprovalReason is not null);
    }

    /// <summary>
    /// Changing a person's fields at the provider is one record per work item, which the catalogue itself approves
    /// automatically, so it runs without a person; deleting the provider's contact cannot be taken back, so a person
    /// decides it once, and its work waits with a paused campaign like any other decision a person has to make.
    /// </summary>
    [Fact]
    public void An_update_of_one_record_runs_on_its_own_and_a_delete_is_a_person_s_decision_that_waits()
    {
        var update = Contract("contact.update");
        var delete = Contract("contact.delete");

        Assert.Equal(("auto", false), (update.Approval.Value, update.Approval.Conditional));
        Assert.Equal(("confirm_once", "irreversible"), (delete.Approval.Value, delete.Reversibility.Value));
        Assert.Equal(InPausedCampaign.Waits, update.Dispatch.InPausedCampaign);
        Assert.Equal(InPausedCampaign.Waits, delete.Dispatch.InPausedCampaign);
        Assert.Equal(RepeatAfterAmbiguous.AfterRecoveryRead, update.RepeatAfterAmbiguous);
        Assert.Equal(RepeatAfterAmbiguous.AfterRecoveryRead, delete.RepeatAfterAmbiguous);
        Assert.Empty(delete.ExternalIds);

        // The update reads what a person the provider holds nobody for is created from, as every write that may create
        // them does; the delete reads the person's identifier and the one channel it consumes, and nothing else of them.
        Assert.Equal(["id", "first_name", "last_name", "company", "title", "time_zone"], update.ContactProjection!.Fields);
        Assert.Equal(["id"], delete.ContactProjection!.Fields);
    }

    /// <summary>
    /// The floor is read with everything it implies, on the document itself and not only on a test's minimal one:
    /// the record that somebody said stop is not refused for a suppressed person, and it runs while its campaign is
    /// paused.
    /// </summary>
    [Fact]
    public void The_record_that_somebody_said_stop_is_held_back_by_nothing_in_jason()
    {
        var contract = Contract("optout.record");

        Assert.Equal("auto", contract.Approval.Value);
        Assert.Equal(SuppressionCheck.NotApplicable, contract.Preflight.Suppression);
        Assert.Equal(InPausedCampaign.Runs, contract.Dispatch.InPausedCampaign);
    }

    /// <summary>
    /// Pausing a Jason campaign holds its work, and exactly two operations run past that: the provider's own pause,
    /// which is the incident response, and the record that somebody said stop. A third would be a change to what a
    /// pause means, and it is made here or not at all.
    /// </summary>
    [Fact]
    public void Exactly_the_incident_response_and_the_record_of_a_stop_run_in_a_paused_campaign()
    {
        Assert.Equal(["campaign.pause", "optout.record"], OperationCatalog.RunsInPausedCampaign.Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// The suppression register refuses what would reach a person, and exactly two operations only take a person out:
    /// the record that somebody said stop, and taking somebody out of a campaign. Every other document says the
    /// register applies, and a third that set it aside would be a change to what the register protects.
    /// </summary>
    [Fact]
    [Critical(Family.S1, "only taking a person out bypasses the register")]
    public void Exactly_the_two_operations_that_take_a_person_out_set_the_suppression_register_aside()
    {
        Assert.Equal(
            ["enrollment.stop", "optout.record"],
            OperationCatalog.All
                .Where(contract => contract.Preflight.Suppression == SuppressionCheck.NotApplicable)
                .Select(contract => contract.Id)
                .Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// How each document finds the provider's own contact for its person, stated as data rather than in a sentence: every
    /// write that takes a person — the enrollment, the list add, the opt-out, the update, the invitation and the message —
    /// creates them only where the provider holds nobody, the stop and the reply find them and create nobody, the read of
    /// a person's threads and the delete work by the pin alone, and the rest name nobody. A document that moved between
    /// these would change what a plugin does with a person it holds no pin for, so the map is held whole.
    /// </summary>
    [Fact]
    public void Each_document_says_how_the_provider_s_contact_is_found()
    {
        var expected = new Dictionary<string, ProviderContact>(StringComparer.Ordinal)
        {
            ["campaign.enroll"] = ProviderContact.FindOrCreate,
            ["list_membership.add"] = ProviderContact.FindOrCreate,
            ["optout.record"] = ProviderContact.FindOrCreate,
            ["contact.update"] = ProviderContact.FindOrCreate,
            ["social_invitation.send"] = ProviderContact.FindOrCreate,
            ["message.send"] = ProviderContact.FindOrCreate,
            ["enrollment.stop"] = ProviderContact.FindOnly,
            ["conversation.reply"] = ProviderContact.FindOnly,
            ["conversation.list"] = ProviderContact.PinnedOnly,
            ["contact.delete"] = ProviderContact.PinnedOnly,
        };

        Assert.All(OperationCatalog.All, contract => Assert.True(
            contract.Preflight.ProviderContact == (expected.TryGetValue(contract.Id, out var mode) ? mode : ProviderContact.NotUsed),
            $"{contract.Id} says the provider's contact is found `{contract.Preflight.ProviderContact}`."));
    }

    /// <summary>
    /// A write that may create the person is handed what they are created from: the person's own fields, the same on
    /// every such document, so a provider that requires a name can be given the one Jason holds rather than one made up
    /// from an address — and a LinkedIn profile, which is no name at all, does not leave a person Jason knows by name
    /// impossible to create.
    /// </summary>
    [Fact]
    public void A_write_that_may_create_the_person_is_handed_their_own_fields()
    {
        var creating = OperationCatalog.All.Where(contract => contract.Preflight.ProviderContact == ProviderContact.FindOrCreate).ToList();

        Assert.NotEmpty(creating);
        Assert.All(creating, contract => Assert.True(
            contract.ContactProjection!.Fields.SequenceEqual(["id", "first_name", "last_name", "company", "title", "time_zone"]),
            $"{contract.Id} may create the person and projects only {string.Join(", ", contract.ContactProjection.Fields)}."));
    }

    /// <summary>
    /// The way a document finds the provider's contact is its field, and `docs/plugins.md` states the rule once, so no
    /// precondition says it a second time: none names the identifier it returns or the provider's contact it finds.
    /// </summary>
    [Theory]
    [MemberData(nameof(Published))]
    public void A_precondition_says_only_what_the_way_the_contact_is_found_does_not(string id)
    {
        var precondition = Contract(id).Precondition;

        Assert.True(
            !precondition.Contains("external_ids.contact", StringComparison.Ordinal)
                && !precondition.Contains("the provider's own contact", StringComparison.OrdinalIgnoreCase),
            $"{id}'s precondition restates how the provider's contact is found: {precondition}");
    }

    /// <summary>
    /// Taking somebody out of a campaign cannot be undone at the provider, so a person decides it, and its work waits
    /// with a paused campaign like any other decision a person has to make.
    /// </summary>
    [Fact]
    public void Taking_a_person_out_of_a_campaign_is_a_person_s_decision_and_waits_in_a_paused_campaign()
    {
        var contract = Contract("enrollment.stop");

        Assert.Equal("confirm_once", contract.Approval.Value);
        Assert.Equal(SuppressionCheck.NotApplicable, contract.Preflight.Suppression);
        Assert.Equal(InPausedCampaign.Waits, contract.Dispatch.InPausedCampaign);
    }

    /// <summary>
    /// A preview is what a person reads before deciding, so an operation declares one exactly when a person decides
    /// on it: one that runs <c>auto</c> renders none, and one a person approves never leaves them reading the raw
    /// document alone.
    /// </summary>
    [Theory]
    [MemberData(nameof(CriticalPublishedForThePreview))]
    public void An_operation_declares_a_preview_exactly_when_a_person_approves_it(string id)
    {
        var contract = Contract(id);

        Assert.True(
            (contract.Approval.Value != "auto") == (contract.Preview is not null),
            contract.Preview is null
                ? $"{id} is approved `{contract.Approval.Value}` and declares no preview for the person approving it."
                : $"{id} runs `auto` and declares a preview nobody will read.");
    }

    [Theory]
    [MemberData(nameof(CriticalPublishedForTheFileOnDisk))]
    public void The_file_on_disk_is_the_document_the_catalog_enforces(string id)
    {
        using var embedded = typeof(OperationContract).Assembly.GetManifestResourceStream("Jason.Contracts.Operations." + id + ".json");
        Assert.NotNull(embedded);
        using var reader = new StreamReader(embedded);

        var published = File.ReadAllText(Path.Combine(ContractFiles.Operations, id + ".json"));

        Assert.Equal(published.ReplaceLineEndings("\n"), reader.ReadToEnd().ReplaceLineEndings("\n"));
    }

    /// <summary>
    /// A repeat is handed out blind only where a second performance changes nothing and costs nothing: a read, or a
    /// reversible control whose repeat is a no-op — pausing a paused campaign. Anything that may reach somebody, or
    /// that cannot be taken back, is repeated only after a recovery read, or never.
    /// </summary>
    [Theory]
    [MemberData(nameof(Published))]
    public void A_repeat_is_only_safe_where_a_second_performance_changes_nothing_and_costs_nothing(string id)
    {
        var contract = Contract(id);
        if (contract.RepeatAfterAmbiguous != RepeatAfterAmbiguous.Safe)
        {
            return;
        }

        Assert.Equal("none", contract.Cost.Value);
        Assert.True(
            contract.Reach.Value == "read" || (contract.Reach.Value == "control" && contract.Reversibility.Value == "reversible"),
            $"{id} repeats blind, but its reach is `{contract.Reach.Value}` and it is `{contract.Reversibility.Value}`.");
    }

    /// <summary>
    /// The rule above admits a reversible control, which an add to a list also is; so the writes that repeat blind are
    /// held to an exact set as well. Each one sets a state outright rather than adding to one: pausing a paused campaign
    /// is the provider's own documented no-op, and filing a thread under a category, or marking it closed or open again,
    /// leaves the thread exactly as the first performance left it. Any other write that repeated blind would be a choice
    /// made here and nowhere else.
    /// </summary>
    [Fact]
    public void The_writes_that_repeat_blind_are_the_ones_that_set_a_state_outright()
    {
        Assert.Equal(
            ["campaign.pause", "conversation.classify", "conversation.close", "conversation.reopen"],
            OperationCatalog.All
                .Where(contract => contract.Reach.Value != "read" && contract.RepeatAfterAmbiguous == RepeatAfterAmbiguous.Safe)
                .Select(contract => contract.Id));
    }

    /// <summary>
    /// The catalogue's one send verb reaches a person with a message, and no read can tell this work item's lost send
    /// from another one to the same person — so a document seeded from it never repeats an ambiguous end, and declares no
    /// recovery read that would pretend to settle it. And <c>never</c> is kept for what reaches somebody: an operation
    /// that only reads or controls has a repeat rule that settles it.
    /// </summary>
    [Theory]
    [MemberData(nameof(CriticalPublishedForTheSendRule))]
    public void A_send_is_never_repeated_after_an_ambiguous_end_and_only_a_send_says_never(string id)
    {
        var contract = Contract(id);

        if (contract.L1.Name == "message.send")
        {
            Assert.True(
                contract.RepeatAfterAmbiguous == RepeatAfterAmbiguous.Never,
                $"{id} is the catalogue's send, and its repeat rule is `{contract.RepeatAfterAmbiguous}`.");
            Assert.Null(contract.RecoveryRead);
        }

        if (contract.RepeatAfterAmbiguous == RepeatAfterAmbiguous.Never)
        {
            Assert.True(contract.Reach.Value == "act", $"{id} says `never`, but its reach is `{contract.Reach.Value}`.");
        }
    }

    /// <summary>
    /// A reply goes to the thread's own person and to nobody else: an added recipient would be somebody the suppression
    /// register was never asked about. So the arguments are exactly these four, closed, and no copy or blind copy can be
    /// named in this version.
    /// </summary>
    [Fact]
    public void A_reply_names_no_recipient_of_its_own()
    {
        var arguments = (JsonObject)Contract("conversation.reply").InputSchema["properties"]!["args"]!;

        Assert.Equal(
            ["body", "channel", "reply_to_message", "thread"],
            ((JsonObject)arguments["properties"]!).Select(property => property.Key).Order(StringComparer.Ordinal));
        Assert.False((bool)arguments["additionalProperties"]!);
    }

    /// <summary>
    /// A message outside any thread goes to the work item's own person and to nobody else, from the one account named: an
    /// added recipient would be somebody the suppression register was never asked about. So the arguments are exactly the
    /// channel, the sender and the message, the message is exactly its format, its subject and its text, both closed, and
    /// no copy, blind copy, attachment or recording can be named in this version.
    /// </summary>
    [Fact]
    public void A_message_names_no_recipient_of_its_own()
    {
        var arguments = (JsonObject)Contract("message.send").InputSchema["properties"]!["args"]!;
        var message = (JsonObject)arguments["properties"]!["message"]!;

        Assert.Equal(["channel", "message", "sender"], ((JsonObject)arguments["properties"]!).Select(property => property.Key).Order(StringComparer.Ordinal));
        Assert.False((bool)arguments["additionalProperties"]!);
        Assert.Equal(["body", "format", "subject"], ((JsonObject)message["properties"]!).Select(property => property.Key).Order(StringComparer.Ordinal));
        Assert.False((bool)message["additionalProperties"]!);
    }

    /// <summary>
    /// The three writes a provider can make idempotent, and which it may or may not say were already in effect. Each
    /// document says, in one conformance entry, that a repeat may answer either word its status enum publishes: the
    /// `already_*` one where the provider can tell, and the plain one where it cannot. Both words are read from the enum,
    /// never restated, so the prose cannot drift from the vocabulary it describes.
    /// </summary>
    [Theory]
    [InlineData("list_membership.add")]
    [InlineData("optout.record")]
    [InlineData("campaign.pause")]
    public void A_write_a_provider_makes_idempotent_may_answer_its_plain_word_on_a_repeat(string id)
    {
        var words = Contract(id).OutputSchema["properties"]!["items"]!["items"]!["properties"]!["status"]!["enum"]!.AsArray()
            .Select(word => word!.GetValue<string>())
            .ToList();
        var already = Assert.Single(words, word => word.StartsWith("already_", StringComparison.Ordinal));
        var plain = Assert.Single(words, word => !word.StartsWith("already_", StringComparison.Ordinal));

        Assert.Contains(
            Conformance(id),
            entry => entry.Contains($"`{already}`", StringComparison.Ordinal) && entry.Contains($"`{plain}`", StringComparison.Ordinal));
    }

    /// <summary>
    /// A repeated idempotent write works by the contact the failed attempt pinned. A lost answer can arrive before any
    /// pin was learned, and a repeat by the person's fields could then make a second contact, so the same entry that
    /// names the pinned repeat names the read that finds the first attempt's contact when there is no pin.
    /// </summary>
    [Theory]
    [InlineData("list_membership.add")]
    [InlineData("optout.record")]
    public void A_repeat_with_no_pin_learned_finds_the_first_attempt_s_contact_by_a_read(string id)
    {
        Assert.Contains(
            Conformance(id),
            entry => entry.Contains("pinned on the failure", StringComparison.Ordinal)
                && entry.Contains("with no pin", StringComparison.Ordinal)
                && entry.Contains("a read finds first", StringComparison.Ordinal));
        Assert.Contains(
            "never created again from the person's fields",
            Document(id)["recovery_read"]!["reads"]!.GetValue<string>(),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A provider may give its reserved categories a meaning of its own, and one of them may be a stop. The classify
    /// document says both in its conformance list: a filing the provider reads as a stop is refused by its own code and
    /// sent to the operation that records a stop, and any other reserved category carries the provider's meaning with it.
    /// </summary>
    [Fact]
    public void Classify_refuses_a_category_that_stops_the_person_and_says_reserved_names_carry_the_provider_s_meaning()
    {
        var conformance = Conformance("conversation.classify");

        Assert.Contains(
            conformance,
            entry => entry.Contains("`category_opts_out`", StringComparison.Ordinal)
                && entry.Contains("`optout.record`", StringComparison.Ordinal)
                && entry.Contains("before anything is filed", StringComparison.Ordinal));
        Assert.Contains(
            conformance,
            entry => entry.Contains("reserved categories a meaning of its own", StringComparison.Ordinal)
                && entry.Contains("categories of its own", StringComparison.Ordinal));
    }

    private static IReadOnlyList<string> Conformance(string id) =>
        [.. Document(id)["conformance"]!.AsArray().Select(entry => entry!.GetValue<string>())];

    /// <summary>
    /// And the page beside each of the two documents says the no-pin case in the document's own words, in the
    /// hand-written section a plugin author reads first. Only a page's generated rows are held to its document
    /// otherwise, so this sentence could drift from the recovery read it repeats and nothing would say so.
    /// </summary>
    [Theory]
    [InlineData("list_membership.add")]
    [InlineData("optout.record")]
    public void A_page_says_the_no_pin_repeat_in_its_document_s_own_words(string id)
    {
        Assert.Contains(
            "with no pin learned, the contact the first attempt may have made is found by a read first, never created again from the person's fields",
            Section(Path.Combine(ContractFiles.Operations, id + ".md"), "## Repeating after a lost answer"),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The class a prose-only change belongs to is named where the versions are, with what keeps it one: no field, enum
    /// value, property or schema changed. A change that says more exactly what a document already allows then needs
    /// no argument for keeping the version.
    /// </summary>
    [Fact]
    public void Versioning_names_a_clarification_that_keeps_the_version()
    {
        Assert.Contains(
            "- **Clarification** — prose that says more exactly what a document's machine fields already allow, with no field, enum value, property or schema changed. It keeps the version.",
            Section(Path.Combine(ContractFiles.Package, "README.md"), "## Versioning"),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A plugin built against a field being required may still read it unchecked, so making the field optional keeps the
    /// version only when that plugin cannot be handed its absence.
    /// </summary>
    [Fact]
    public void Versioning_says_when_a_required_field_made_optional_stays_additive()
    {
        Assert.Contains(
            "A required input field made optional is additive only where the runtime supplies the field's former value to the plugin's input, or the operation's document says the plugin must take its absence; otherwise it is a breaking change of the operation's version.",
            Section(Path.Combine(ContractFiles.Package, "README.md"), "## Versioning"),
            StringComparison.Ordinal);
    }

    /// <summary>A page's section under a heading, up to the next heading of its level, with its whitespace run together.</summary>
    private static string Section(string page, string heading)
    {
        var text = File.ReadAllText(page).ReplaceLineEndings("\n");
        var start = text.IndexOf(heading, StringComparison.Ordinal);
        Assert.True(start >= 0, $"{ContractFiles.Relative(page)} has no section '{heading}'.");
        var end = text.IndexOf("\n## ", start + heading.Length, StringComparison.Ordinal);
        return System.Text.RegularExpressions.Regex.Replace(text[start..(end < 0 ? text.Length : end)], @"\s+", " ");
    }

    [Theory]
    [MemberData(nameof(Published))]
    public void A_repeat_after_a_recovery_read_declares_what_that_read_is(string id)
    {
        var contract = Contract(id);
        if (contract.RepeatAfterAmbiguous != RepeatAfterAmbiguous.AfterRecoveryRead)
        {
            Assert.Null(contract.RecoveryRead);
            return;
        }

        Assert.NotNull(contract.RecoveryRead);
        Assert.NotEmpty(contract.RecoveryRead.Reads);
        Assert.NotEmpty(contract.RecoveryRead.Justification);
    }

    [Theory]
    [MemberData(nameof(Published))]
    public void An_operation_that_acts_on_a_channel_takes_it_as_an_argument(string id)
    {
        var contract = Contract(id);
        var arguments = contract.InputSchema["properties"]?["args"]?["properties"] as JsonObject;

        if (contract.Preflight.Channel == ChannelRequirement.FromArgs)
        {
            Assert.NotNull(arguments);
            Assert.True(arguments.ContainsKey("channel"), $"{id} consumes a channel but its args do not declare one.");
            Assert.Contains("channel", Required(contract.InputSchema["properties"]!["args"]!));
        }
        else
        {
            Assert.False(arguments?.ContainsKey("channel"), $"{id} declares no channel but its args carry one.");
        }
    }

    /// <summary>
    /// Every object a document defines — under <c>properties</c>, <c>items</c> or <c>$defs</c>, at any depth — is closed,
    /// so nothing the document did not name can ride along in an argument or an answer. The one exception is the
    /// provider's own untranslated answer beside a read's neutral part, which is open by declaration. The branches of a
    /// combinator only add rules to the object they sit on, so they are not objects of their own. And a write answers
    /// one item for its one subject, never two.
    /// </summary>
    [Theory]
    [MemberData(nameof(Published))]
    public void Every_object_a_document_defines_is_closed_and_a_write_answers_one_item(string id)
    {
        var contract = Contract(id);
        var open = new List<string>();
        foreach (var (name, schema) in new[] { ("input_schema", contract.InputSchema), ("output_schema", contract.OutputSchema) })
        {
            OpenObjects(schema, name, open);
        }

        var undeclared = open.Where(path => !OpenByDeclaration.Contains((id, path))).ToList();
        Assert.True(undeclared.Count == 0, $"{id} leaves open: {string.Join(", ", undeclared)}");
        if (contract.Reach.Value != "read")
        {
            Assert.Equal(1, (int)contract.OutputSchema["properties"]!["items"]!["maxItems"]!);
        }
    }

    private static readonly HashSet<(string, string)> OpenByDeclaration = [("campaign.get", "output_schema/properties/vendor")];

    private static void OpenObjects(JsonNode? node, string path, List<string> open)
    {
        if (node is not JsonObject schema)
        {
            return;
        }

        var type = schema["type"];
        var isObject = schema.ContainsKey("properties")
            || (type is JsonValue single && (string?)single == "object")
            || (type is JsonArray many && many.Any(entry => (string?)entry == "object"));
        var closed = schema["additionalProperties"] is JsonValue flag && flag.GetValueKind() == System.Text.Json.JsonValueKind.False;
        if (isObject && !closed)
        {
            open.Add(path);
        }

        if (schema["properties"] is JsonObject properties)
        {
            foreach (var (name, child) in properties)
            {
                OpenObjects(child, $"{path}/properties/{name}", open);
            }
        }

        if (schema["$defs"] is JsonObject definitions)
        {
            foreach (var (name, child) in definitions)
            {
                OpenObjects(child, $"{path}/$defs/{name}", open);
            }
        }

        OpenObjects(schema["items"], $"{path}/items", open);
    }

    [Theory]
    [MemberData(nameof(Published))]
    public void An_operation_that_takes_the_contact_takes_exactly_one(string id)
    {
        var contract = Contract(id);
        var contacts = contract.InputSchema["properties"]?["contacts"];

        if (contract.Preflight.Contact == ContactRequirement.NotUsed)
        {
            Assert.Null(contacts);
            return;
        }

        Assert.NotNull(contacts);
        Assert.Equal(1, (int)contacts["minItems"]!);
        Assert.Equal(1, (int)contacts["maxItems"]!);

        // An optional contact is there when the work item names one and absent when it does not, never empty.
        Assert.Equal(contract.Preflight.Contact == ContactRequirement.Required, Required(contract.InputSchema).Contains("contacts"));
    }

    /// <summary>
    /// The declared projection and the schema the plugin is actually handed are one statement, so they are held
    /// against each other rather than each against a list written here: a hardcoded list agrees with a copy of
    /// itself while the two documents drift apart. The declared fields plus the two the projection does not name
    /// — the channels it consumes, when it consumes any, and the pins it may see — are exactly the contact a plugin
    /// receives.
    /// </summary>
    [Theory]
    [MemberData(nameof(Published))]
    public void The_declared_contact_projection_follows_the_pre_flight(string id)
    {
        var contract = Contract(id);

        if (contract.Preflight.Contact == ContactRequirement.NotUsed)
        {
            Assert.Null(contract.ContactProjection);
            Assert.Null(contract.InputSchema["$defs"]?["contact"]);
            return;
        }

        Assert.NotNull(contract.ContactProjection);

        var contact = Assert.IsType<JsonObject>(contract.InputSchema["$defs"]?["contact"]);
        var properties = Assert.IsType<JsonObject>(contact["properties"]);
        var consumed = contract.ContactProjection.Channels == "consumed";

        Assert.Equal(
            contract.ContactProjection.Fields.Concat(consumed ? ["channels", "external_ids"] : ["external_ids"]).Order(StringComparer.Ordinal).ToList(),
            properties.Select(property => property.Key).Order(StringComparer.Ordinal).ToList());

        if (consumed)
        {
            // `consumed` is one channel, not a person's whole reachability, and the schema is what enforces it.
            Assert.Equal(1, (int)properties["channels"]!["maxItems"]!);
        }
    }

    [Theory]
    [MemberData(nameof(Published))]
    public void An_operation_that_requires_an_idempotency_key_takes_one(string id)
    {
        var contract = Contract(id);
        var declared = (contract.InputSchema["properties"] as JsonObject)?.ContainsKey("idempotency_key") == true;

        if (contract.IdempotencyKey.Value == "required")
        {
            Assert.True(declared, $"{id} requires an idempotency key but its input does not carry one.");
            Assert.Contains("idempotency_key", Required(contract.InputSchema));
        }
        else
        {
            Assert.False(declared, $"{id} needs no idempotency key but its input carries one.");
        }
    }

    /// <summary>
    /// An operation that pins something says what each pin identifies. One may pin nothing at all: a thread read by
    /// the identifier the caller already holds links nothing new.
    /// </summary>
    [Theory]
    [MemberData(nameof(Published))]
    public void Every_declared_identifier_says_what_it_identifies(string id)
    {
        var contract = Contract(id);

        Assert.All(contract.ExternalIds, kind =>
        {
            Assert.Matches("^[a-z][a-z0-9_]*$", kind.Key);
            Assert.NotEmpty(kind.Value.Description);
        });
    }

    [Theory]
    [MemberData(nameof(CriticalPublishedForTheDialect))]
    public void Both_schemas_are_written_in_the_published_dialect(string id)
    {
        var contract = Contract(id);

        Assert.Empty(SchemaValidator.CheckDialect(contract.InputSchema));
        Assert.Empty(SchemaValidator.CheckDialect(contract.OutputSchema));
        Assert.Equal("https://json-schema.org/draft/2020-12/schema", (string?)contract.InputSchema["$schema"]);
        Assert.Equal("https://json-schema.org/draft/2020-12/schema", (string?)contract.OutputSchema["$schema"]);
    }

    [Theory]
    [MemberData(nameof(Published))]
    public void The_timeout_is_a_budget_a_person_chose(string id)
    {
        var timeout = Contract(id).TimeoutMs;

        Assert.InRange(timeout, 1_000, 3_600_000);
    }

    [Theory]
    [MemberData(nameof(Published))]
    public void The_origin_names_a_family_of_the_contract_this_catalog_is_seeded_from(string id)
    {
        var origin = Contract(id).L1;

        Assert.Equal("2.0.0", origin.ContractVersion);
        Assert.InRange(origin.Family, 1, 21);
        Assert.NotEmpty(origin.Name);
    }

    [Theory]
    [MemberData(nameof(Published))]
    public void Every_failure_a_plugin_may_report_is_named_with_its_class_and_its_moment(string id)
    {
        var contract = Contract(id);

        Assert.NotEmpty(contract.FailureCodes);
        Assert.All(contract.FailureCodes, failure =>
        {
            Assert.Matches("^[a-z][a-z0-9_]*$", failure.Code);
            Assert.NotEmpty(failure.When);
        });
        Assert.Equal(contract.FailureCodes.Select(f => f.Code).Distinct(StringComparer.Ordinal).Count(), contract.FailureCodes.Count);
    }

    [Theory]
    [MemberData(nameof(Published))]
    public void A_metered_operation_says_what_it_is_metered_on(string id)
    {
        var contract = Contract(id);

        if (contract.Cost.Value == "metered")
        {
            Assert.NotNull(contract.CostBasis);
            Assert.NotNull(contract.Meter);
        }
        else
        {
            Assert.Null(contract.CostBasis);
            Assert.Null(contract.Meter);
        }
    }

    private static IEnumerable<string> Required(JsonNode schema) =>
        schema["required"] is JsonArray names ? names.Select(name => (string)name!) : [];
}
