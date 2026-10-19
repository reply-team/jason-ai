using System.Text.Json.Nodes;
using Jason.Contracts.Events;
using Jason.Contracts.Notifications;
using Jason.Contracts.Operations;
using Jason.Contracts.Plugins;

namespace Jason.Contracts.Tests.Events;

/// <summary>
/// The events contract: a family of its own beside the canonical operations and the delivery contract, never among
/// either. A read of what happened at a provider belongs to no campaign, so it is not work a work item could name, a
/// route could carry or a lease would be sized for; and it is not a pointer put in front of a person.
/// </summary>
public sealed class EventsContractTests
{
    [Fact]
    public void The_family_publishes_one_contract_events_read_at_version_1()
    {
        var contract = Assert.Single(EventsCatalog.All);

        Assert.Equal("events.read", contract.Id);
        Assert.Equal(1, contract.Version);
        Assert.Equal(EventsContract.CurrentVersion, contract.Version);
        Assert.Equal(60_000, contract.TimeoutMs);
        Assert.Equal(
            [
                ("events_unreachable", FailureClass.Transient),
                ("events_unauthorized", FailureClass.Permanent),
                ("events_unsupported", FailureClass.Permanent),
                ("events_cursor_unknown", FailureClass.Permanent),
            ],
            contract.FailureCodes.Select(code => (code.Code, code.Class)));
        Assert.Same(contract, EventsCatalog.Find("events.read"));
        Assert.True(EventsCatalog.Knows("events.read"));
    }

    [Fact]
    public void A_read_is_neither_a_canonical_operation_nor_a_delivery()
    {
        Assert.False(OperationCatalog.Knows("events.read"));
        Assert.False(DeliveryCatalog.Knows("events.read"));
        Assert.False(EventsCatalog.Knows("conversation.list"));
        Assert.False(EventsCatalog.Knows("notification.send"));
        Assert.Equal(Directory.EnumerateFiles(Path.Combine(OperationContracts.ContractFiles.Package, "events"), "*.json").Count(), EventsCatalog.All.Count);
    }

    /// <summary>
    /// Held here rather than as the document loads: checking the dialect compiles every pattern, and the command-line
    /// client, which matches none, loads this catalog too.
    /// </summary>
    [Fact]
    public void Both_of_its_schemas_are_in_the_published_dialect()
    {
        var contract = EventsCatalog.Find("events.read")!;
        Assert.Empty(SchemaValidator.CheckDialect(contract.InputSchema));
        Assert.Empty(SchemaValidator.CheckDialect(contract.OutputSchema));
    }

    /// <summary>The four kinds, read from the document's own branches, so nothing else in the build lists them twice.</summary>
    [Fact]
    public void The_kinds_are_the_four_the_output_schema_publishes()
    {
        Assert.Equal(
            ["thread_moved", "reply_detected", "invitation_accepted", "bounce"],
            EventsCatalog.Find("events.read")!.Kinds);
    }

    [Fact]
    public void A_document_with_a_field_of_its_own_is_refused()
    {
        var document = Document();
        document["reach"] = "read";

        var refused = Assert.Throws<InvalidOperationException>(() => EventsContract.Parse("test.json", document));
        Assert.Contains("`reach` is not part of an events contract", refused.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("id")]
    [InlineData("version")]
    [InlineData("intent")]
    [InlineData("input_schema")]
    [InlineData("output_schema")]
    [InlineData("failure_codes")]
    [InlineData("timeout_ms")]
    [InlineData("conformance")]
    public void A_document_missing_a_field_is_refused(string field)
    {
        var document = Document();
        document.Remove(field);

        var refused = Assert.Throws<InvalidOperationException>(() => EventsContract.Parse("test.json", document));
        Assert.Contains($"`{field}` is missing", refused.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A move both an events read and a conversation read find is said once only because both name it alike: the thread
    /// by the identifier the conversation read returns for it, and the moment as the moment the thread last moved. That is
    /// the plugin's to keep, so the document says so, and the plugin author's page with it.
    /// </summary>
    [Fact]
    public void The_conformance_says_a_moved_thread_is_named_as_the_conversation_read_names_it()
    {
        const string Promise = "with occurred_at the moment the thread last moved — the moment conversation.list reports as its last_activity_at, to the millisecond —";
        var contract = EventsCatalog.Find("events.read")!;

        var line = Assert.Single(contract.Conformance, line => line.Contains(Promise, StringComparison.Ordinal));
        Assert.Contains("the identifier conversation.list returns as the thread's external_id", line, StringComparison.Ordinal);

        var page = string.Join(' ', File.ReadAllText(Path.Combine(OperationContracts.ContractFiles.Package, "events", "events.read.md"))
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        Assert.Contains("the moment `conversation.list` reports as its `last_activity_at`, to the millisecond", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// The inbound read steps aside for a campaign on the strength of one promise: a moved thread names the provider
    /// campaign it belongs to, by the identifier the campaign's own operation pinned. The document makes it, and the
    /// plugin author's page says it and what breaking it costs.
    /// </summary>
    [Fact]
    public void The_conformance_says_a_moved_thread_names_its_provider_campaign()
    {
        const string Promise = "Names, under campaign, the provider campaign a moved thread belongs to wherever the provider keeps one, by the identifier campaign.get pins, so that the runtime can leave that campaign's inbound read to the events; null is for a thread outside every provider campaign, which reaches no campaign until a contact pin can say whose it is.";
        var contract = EventsCatalog.Find("events.read")!;

        Assert.Contains(Promise, contract.Conformance);
        Assert.Equal(1, contract.Version);

        var page = string.Join(' ', File.ReadAllText(Path.Combine(OperationContracts.ContractFiles.Package, "events", "events.read.md"))
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        Assert.Contains("names, under `campaign`, the provider campaign a moved thread belongs to wherever the provider keeps one, by the identifier `campaign.get` pins", page, StringComparison.Ordinal);
        Assert.Contains("a plugin that reads events and names no campaign on a move silences the campaigns it serves", page, StringComparison.Ordinal);
    }

    /// <summary>A read finds pins and never writes one: any identifier beside its result is refused, whatever its kind.</summary>
    [Fact]
    public void An_answer_carrying_identifiers_is_refused_whatever_their_kind()
    {
        var problems = EventsContract.CheckExternalIds(new JsonObject { ["campaign"] = "p_1", ["contact"] = "c_1" });

        Assert.Equal(["/external_ids/campaign", "/external_ids/contact"], problems.Select(problem => problem.Pointer));
        Assert.All(problems, problem => Assert.Equal("additional_properties", problem.Reason));
        Assert.Empty(EventsContract.CheckExternalIds(null));
        Assert.Empty(EventsContract.CheckExternalIds([]));
    }

    /// <summary>The published document, as a node a test may change.</summary>
    private static JsonObject Document() => (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(
        OperationContracts.ContractFiles.Package, "events", "events.read.json")))!;
}
