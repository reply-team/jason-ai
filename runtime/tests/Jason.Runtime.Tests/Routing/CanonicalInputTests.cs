using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Ids;
using Jason.Contracts.Operations;
using Jason.Runtime.Persistence;
using Jason.Runtime.Routing;
using Jason.Runtime.WorkItems;

namespace Jason.Runtime.Tests.Routing;

/// <summary>
/// What a plugin is actually handed. The document is composed from the operation's own contract, so what reaches
/// a provider is what the operation declares it consumes and nothing else a person happens to be reachable by.
/// </summary>
public class CanonicalInputTests
{
    private const string ListMembershipAdd = "list_membership.add";
    private const string CampaignGet = "campaign.get";

    /// <summary>
    /// A3. The person's other addresses are not this operation's business, and because the projection is declared
    /// in the contract rather than decided here, widening it is a visible change to a published document.
    /// </summary>
    [Fact]
    public void A_plugin_receives_the_channel_the_operation_consumes_and_no_other()
    {
        var contact = ContactWith(("email", "ada@example.test", true), ("phone", "+15555550100", false), ("linkedin", "https://example.test/in/ada", false));
        var facts = Facts(ListMembershipAdd, Arguments("email"), contact);

        var input = CanonicalInput.Compose(Contract(ListMembershipAdd), facts);

        var person = input["contacts"]!.AsArray()[0]!.AsObject();
        var channel = Assert.Single(person["channels"]!.AsArray());
        Assert.Equal("email", (string?)channel!["channel"]);
        Assert.Equal("ada@example.test", (string?)channel["value"]);
        Assert.True((bool?)channel["primary"]);

        // Exactly the contract's projection, its channels and its pins: no custom fields, no second address.
        Assert.Equal(
            ["id", "first_name", "last_name", "company", "title", "time_zone", "channels", "external_ids"],
            person.Select(member => member.Key));
    }

    /// <summary>
    /// A person can be reachable twice on one channel, and the operation acts once. The primary address is the
    /// one the campaign would use anywhere else, so it is the one that travels.
    /// </summary>
    [Fact]
    public void The_primary_address_of_the_consumed_channel_is_the_one_that_travels()
    {
        var contact = ContactWith(("email", "work@example.test", false), ("email", "home@example.test", true));

        var input = CanonicalInput.Compose(Contract(ListMembershipAdd), Facts(ListMembershipAdd, Arguments("email"), contact));

        var channel = Assert.Single(input["contacts"]![0]!["channels"]!.AsArray());
        Assert.Equal("home@example.test", (string?)channel!["value"]);
    }

    [Fact]
    public void An_operation_that_uses_no_contact_is_composed_without_one()
    {
        var facts = Facts(CampaignGet, new JsonObject { ["campaign"] = new JsonObject { ["external_id"] = "c-7" } }, contact: null);

        var input = CanonicalInput.Compose(Contract(CampaignGet), facts);

        Assert.Equal(["args", "campaign"], input.Select(member => member.Key));
        Assert.Equal("c-7", (string?)input["args"]!["campaign"]!["external_id"]);
    }

    /// <summary>
    /// The key is the work item's, not the attempt's: it is stable across every attempt, which is exactly what
    /// "the prior run under this key" needs. The attempt id is the correlation, and it travels elsewhere.
    /// </summary>
    [Fact]
    public void The_idempotency_key_is_the_work_item_s_own_identifier()
    {
        var facts = Facts(ListMembershipAdd, Arguments("email"), ContactWith(("email", "ada@example.test", true)));

        var input = CanonicalInput.Compose(Contract(ListMembershipAdd), facts);

        Assert.Equal(facts.Item.PublicId, (string?)input["idempotency_key"]);
    }

    [Fact]
    public void The_campaign_travels_with_the_pins_this_plugin_recorded_for_it()
    {
        var facts = Facts(
            CampaignGet,
            arguments: null,
            contact: null,
            campaignPins: new Dictionary<string, string>(StringComparer.Ordinal) { ["campaign"] = "c-7714" });

        var campaign = CanonicalInput.Compose(Contract(CampaignGet), facts)["campaign"]!.AsObject();

        Assert.Equal(facts.Campaign.PublicId, (string?)campaign["id"]);
        Assert.Equal("Flow", (string?)campaign["name"]);
        Assert.Equal("active", (string?)campaign["status"]);
        Assert.Equal("c-7714", (string?)campaign["external_ids"]!["campaign"]);
    }

    /// <summary>An entity nobody has pinned carries an empty object, never a missing one: absence is stated.</summary>
    [Fact]
    public void An_entity_no_plugin_has_pinned_yet_still_says_so()
    {
        var input = CanonicalInput.Compose(Contract(CampaignGet), Facts(CampaignGet, null, contact: null));

        Assert.Empty(input["campaign"]!["external_ids"]!.AsObject());
    }

    /// <summary>
    /// The document is composed, not referenced: an edit to the item's context after the claim belongs to the
    /// next attempt, and must not reach the plugin this one is about to run.
    /// </summary>
    [Fact]
    public void The_arguments_are_copied_rather_than_shared_with_the_work_item()
    {
        var facts = Facts(ListMembershipAdd, Arguments("email"), ContactWith(("email", "ada@example.test", true)));
        var input = CanonicalInput.Compose(Contract(ListMembershipAdd), facts);

        ((JsonObject)facts.Item.Context[WorkItemService.InputKey]!)["channel"] = "phone";

        Assert.Equal("email", (string?)input["args"]!["channel"]);
    }

    /// <summary>An item whose planner wrote no arguments is composed with an empty object, never with nothing.</summary>
    [Fact]
    public void An_item_that_carries_no_arguments_is_composed_with_none_rather_than_nothing()
    {
        var input = CanonicalInput.Compose(Contract(CampaignGet), Facts(CampaignGet, arguments: null, contact: null));

        Assert.Empty(input["args"]!.AsObject());
    }

    /// <summary>
    /// The composed document is what the operation's own schema is applied to at claim, so the composer and the
    /// published contract have to agree about every property, not only the ones a test remembers to name.
    /// </summary>
    [Fact]
    public void A_composed_document_satisfies_the_operation_s_published_schema()
    {
        var contact = ContactWith(("email", "ada@example.test", true));
        var facts = Facts(
            ListMembershipAdd,
            Arguments("email"),
            contact,
            contactPins: new Dictionary<string, string>(StringComparer.Ordinal) { ["contact"] = "p-88421" });

        var contract = Contract(ListMembershipAdd);

        Assert.Empty(SchemaValidator.Validate(CanonicalInput.Compose(contract, facts), contract.InputSchema));
    }

    /// <summary>
    /// A projection field the composer does not know would compose a document missing a property the schema
    /// requires — a failure at claim rather than in this repository, which is the wrong place to find it.
    /// </summary>
    [Fact]
    public void Every_field_a_published_projection_names_is_one_the_composer_knows()
    {
        var unknown = OperationCatalog.All
            .Where(contract => contract.ContactProjection is not null)
            .SelectMany(contract => contract.ContactProjection!.Fields)
            .Where(field => !CanonicalInput.ProjectedFields.Contains(field))
            .Distinct(StringComparer.Ordinal);

        Assert.Empty(unknown);
    }

    private static OperationContract Contract(string id) => OperationCatalog.Find(id)!;

    private static JsonObject Arguments(string channel) => new()
    {
        ["list"] = new JsonObject { ["external_id"] = "L-1129" },
        ["channel"] = channel,
    };

    private static PreflightFacts Facts(
        string operation,
        JsonObject? arguments,
        Contact? contact,
        IReadOnlyDictionary<string, string>? contactPins = null,
        IReadOnlyDictionary<string, string>? campaignPins = null)
    {
        var campaign = WorkItemFactory.NewCampaign();
        var item = WorkItemFactory.NewProviderOp(campaign, operation);
        item.Contact = contact;
        if (arguments is not null)
        {
            item.Context = new JsonObject { [WorkItemService.InputKey] = arguments };
        }

        return new PreflightFacts(
            item,
            campaign,
            contact,
            contactPins ?? PreflightFacts.NoPins,
            campaignPins ?? PreflightFacts.NoPins,
            Suppressed: false);
    }

    private static Contact ContactWith(params (string Channel, string Value, bool Primary)[] channels)
    {
        var contact = new Contact
        {
            PublicId = PublicId.New("cnt"),
            FirstName = "Ada",
            LastName = null,
            Company = null,
            Title = null,
            TimeZone = null,
            Custom = new JsonObject { ["seniority"] = "founder" },
        };

        foreach (var (channel, value, primary) in channels)
        {
            contact.Channels.Add(new ContactChannel { Channel = channel, Value = value, IsPrimary = primary });
        }

        return contact;
    }
}
