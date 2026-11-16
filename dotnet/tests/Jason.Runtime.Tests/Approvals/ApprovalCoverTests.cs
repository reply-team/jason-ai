using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Jason.Contracts.Api;
using Jason.Contracts.Ids;
using Jason.Contracts.Operations;
using Jason.Runtime.Approvals;
using Jason.Runtime.Json;
using Jason.Runtime.Persistence;
using Jason.Runtime.Routing;
using Jason.Runtime.Tests.Plugins;
using Jason.Runtime.WorkItems;

namespace Jason.Runtime.Tests.Approvals;

/// <summary>
/// What an approval covers, as the one test the claim, the automatic mode's retry and the review hearing all ask. It covers
/// the subject it was given about, and the same subject with a provider identifier added where it held none, for the person
/// or for the campaign: such a pin is either not shown to the person at all or bound to a value they read. A pin it held
/// with another value, a pin it held and the subject no longer carries, and every other member of the subject are something
/// else, so it covers none of them.
/// </summary>
public class ApprovalCoverTests
{
    private const string Plugin = "stand-in-provider";

    private const string Enroll = "campaign.enroll";

    private static readonly IReadOnlyDictionary<string, string> NoPins = PreflightFacts.NoPins;

    [Theory]
    [InlineData("the same subject", true)]
    [InlineData("the person pinned since", true)]
    [InlineData("the campaign pinned since", true)]
    [InlineData("both pinned since", true)]
    public void An_approval_covers_its_subject_and_the_subject_with_a_pin_added_where_it_held_none(string now, bool covered)
    {
        var scene = new Scene();
        var approved = scene.Approved(NoPins, NoPins);

        var subject = now switch
        {
            "the same subject" => scene.Subject(NoPins, NoPins),
            "the person pinned since" => scene.Subject(Pins("contact", "p-48211"), NoPins),
            "the campaign pinned since" => scene.Subject(NoPins, Pins("campaign", "sq-1129")),
            _ => scene.Subject(Pins("contact", "p-48211"), Pins("campaign", "sq-1129")),
        };

        Assert.Equal(covered, ApprovalGate.Authorizes(approved, subject));
    }

    /// <summary>
    /// "Added" is a kind the approved subject does not hold at that place. A kind it holds is held to its value: another
    /// value, or no value any more, is not what the person approved, whatever is added beside it.
    /// </summary>
    [Theory]
    [Critical(Family.S2, "a changed or vanished pin needs a new approval")]
    [InlineData("the person's pin holds another value")]
    [InlineData("the person's pin is gone")]
    [InlineData("the campaign's pin holds another value")]
    [InlineData("the campaign's pin is gone")]
    [InlineData("both pins are gone")]
    public void A_pin_the_approval_held_that_changed_or_is_gone_ends_its_cover(string now)
    {
        var scene = new Scene();
        var person = Pins("contact", "p-48211");
        var campaign = Pins("campaign", "sq-1129");
        var approved = scene.Approved(person, campaign);

        var subject = now switch
        {
            "the person's pin holds another value" => scene.Subject(Pins("contact", "p-59322"), campaign),
            "the person's pin is gone" => scene.Subject(NoPins, campaign),
            "the campaign's pin holds another value" => scene.Subject(person, Pins("campaign", "sq-2240")),
            "the campaign's pin is gone" => scene.Subject(person, NoPins),
            _ => scene.Subject(NoPins, NoPins),
        };

        Assert.False(ApprovalGate.Authorizes(approved, subject));
    }

    [Theory]
    [InlineData("the person's pin added beside the campaign's changed")]
    [InlineData("the person's pin added beside the campaign's gone")]
    [InlineData("the campaign's pin added beside the person's changed")]
    [InlineData("the campaign's pin added beside the person's gone")]
    public void A_pin_added_beside_a_pin_of_the_other_kind_that_changed_does_not_keep_the_cover(string now)
    {
        var scene = new Scene();
        var person = Pins("contact", "p-48211");
        var campaign = Pins("campaign", "sq-1129");
        var heldCampaign = scene.Approved(NoPins, campaign);
        var heldPerson = scene.Approved(person, NoPins);

        var (approved, subject) = now switch
        {
            "the person's pin added beside the campaign's changed" => (heldCampaign, scene.Subject(person, Pins("campaign", "sq-2240"))),
            "the person's pin added beside the campaign's gone" => (heldCampaign, scene.Subject(person, NoPins)),
            "the campaign's pin added beside the person's changed" => (heldPerson, scene.Subject(Pins("contact", "p-59322"), campaign)),
            _ => (heldPerson, scene.Subject(NoPins, campaign)),
        };

        Assert.False(ApprovalGate.Authorizes(approved, subject));
    }

    /// <summary>
    /// Every other member of the subject still asks again with a pin added beside it, so what an added pin keeps never widens
    /// to anything else: the arguments, a field of the person, the address the send goes to, the campaign's name and status,
    /// the plugin, the account, the operation's version, the operation and the work item.
    /// </summary>
    [Theory]
    [Critical(Family.S2, "anything else beside an added pin needs a new approval")]
    [InlineData("an argument")]
    [InlineData("a field of the person")]
    [InlineData("the address")]
    [InlineData("the campaign's name")]
    [InlineData("the campaign's status")]
    [InlineData("the plugin")]
    [InlineData("the account")]
    [InlineData("the operation's version")]
    [InlineData("the operation")]
    [InlineData("the work item")]
    public void Every_other_member_that_changed_beside_a_pin_added_ends_the_cover(string member) =>
        AnotherMember(member, pinAdded: true);

    /// <summary>The same members, changed with no pin added, end the cover as they always do.</summary>
    [Theory]
    [InlineData("an argument")]
    [InlineData("a field of the person")]
    [InlineData("the address")]
    [InlineData("the campaign's name")]
    [InlineData("the campaign's status")]
    [InlineData("the plugin")]
    [InlineData("the account")]
    [InlineData("the operation's version")]
    [InlineData("the operation")]
    [InlineData("the work item")]
    public void Every_other_member_that_changed_ends_the_cover(string member) => AnotherMember(member, pinAdded: false);

    private static void AnotherMember(string member, bool pinAdded)
    {
        var scene = new Scene();
        var approved = scene.Approved(NoPins, NoPins);
        var person = pinAdded ? Pins("contact", "p-48211") : NoPins;
        var campaign = pinAdded ? Pins("campaign", "sq-1129") : NoPins;

        switch (member)
        {
            case "an argument":
                scene.Item.Context = new JsonObject { [WorkItemService.InputKey] = Scene.Arguments("refuse") };
                break;
            case "a field of the person":
                scene.Contact.FirstName = "Adah";
                break;
            case "the address":
                scene.Contact.Channels.Single().Value = "ada.lovelace@example.test";
                break;
            case "the campaign's name":
                scene.Campaign.Name = "Q4 founders";
                break;
            case "the campaign's status":
                scene.Campaign.Status = CampaignStatus.Paused;
                break;
        }

        var subject = member switch
        {
            "the plugin" => scene.Subject(person, campaign, plugin: "another-provider"),
            "the account" => scene.Subject(person, campaign, bindingIdentity: "sha256:another-account"),
            _ => scene.Subject(person, campaign),
        };

        subject = member switch
        {
            "the operation's version" => Rewritten(subject, document => document["operation_version"] = 2),
            "the operation" => Rewritten(subject, document => document["operation"] = "campaign.activate"),
            "the work item" => Rewritten(subject, document => document["work_item"] = PublicId.New("wi")),
            _ => subject,
        };

        Assert.False(ApprovalGate.Authorizes(approved, subject));
    }

    /// <summary>
    /// The compare is total over what an approval's row may hold, its stored subject written by any earlier build included:
    /// a subject whose person or campaign carries no pins object at all, or whose input is not shaped as one composed now,
    /// is compared without an error. It still covers exactly the subject its hash names, and a pin added beside it asks.
    /// </summary>
    [Theory]
    [InlineData("a campaign with no pins object")]
    [InlineData("a person with no pins object")]
    [InlineData("no person")]
    [InlineData("people that are not a list")]
    [InlineData("an empty list of people")]
    [InlineData("a campaign that is not an object")]
    [InlineData("no input")]
    [InlineData("an input that is not an object")]
    public void An_approval_whose_stored_subject_has_another_shape_is_compared_without_error(string shape)
    {
        var scene = new Scene();
        var older = scene.Subject(NoPins, NoPins).Document.DeepClone().AsObject();
        var input = older["input"]!.AsObject();
        switch (shape)
        {
            case "a campaign with no pins object":
                input["campaign"]!.AsObject().Remove("external_ids");
                break;
            case "a person with no pins object":
                input["contacts"]![0]!.AsObject().Remove("external_ids");
                break;
            case "no person":
                input.Remove("contacts");
                break;
            case "people that are not a list":
                input["contacts"] = "ada";
                break;
            case "an empty list of people":
                input["contacts"] = new JsonArray();
                break;
            case "a campaign that is not an object":
                input["campaign"] = 7;
                break;
            case "no input":
                older.Remove("input");
                break;
            default:
                older["input"] = "composed";
                break;
        }

        var approved = Row(older);
        var exact = new ApprovalSubject(older.DeepClone().AsObject(), CanonicalJson.Hash(older));

        Assert.True(ApprovalGate.Authorizes(approved, exact));
        Assert.False(ApprovalGate.Authorizes(approved, scene.Subject(Pins("contact", "p-48211"), Pins("campaign", "sq-1129"))));
        Assert.False(ApprovalGate.Authorizes(approved, scene.Subject(NoPins, NoPins)));
    }

    /// <summary>
    /// The same compare the other way round: a subject composed now whose shape is not the one composed by this build, held
    /// to a row with every pins object, is compared without an error and is not covered.
    /// </summary>
    [Fact]
    public void A_subject_of_another_shape_held_to_a_row_is_compared_without_error()
    {
        var scene = new Scene();
        var approved = scene.Approved(NoPins, NoPins);
        var odd = new JsonObject { ["input"] = new JsonObject { ["contacts"] = new JsonArray(7), ["campaign"] = "x" } };

        Assert.False(ApprovalGate.Authorizes(approved, new ApprovalSubject(odd, CanonicalJson.Hash(odd))));
    }

    /// <summary>Only an approved row covers anything: a pending, superseded, rejected or cancelled one covers nothing.</summary>
    [Theory]
    [InlineData(ApprovalStatus.Pending)]
    [InlineData(ApprovalStatus.Superseded)]
    [InlineData(ApprovalStatus.Rejected)]
    [InlineData(ApprovalStatus.Cancelled)]
    public void Only_an_approved_row_covers_a_pin_added(ApprovalStatus status)
    {
        var scene = new Scene();
        var row = scene.Approved(NoPins, NoPins);
        row.Status = status;

        Assert.False(ApprovalGate.Authorizes(row, scene.Subject(NoPins, NoPins)));
        Assert.False(ApprovalGate.Authorizes(row, scene.Subject(Pins("contact", "p-48211"), NoPins)));
        Assert.False(ApprovalGate.Authorizes(null, scene.Subject(NoPins, NoPins)));
    }

    /// <summary>
    /// Composing with a pin added moves nothing else: for every operation of the catalog, and each of the catalog's two pin
    /// kinds — the person's <c>contact</c> and the campaign's <c>campaign</c> — whether the operation declares it or not, the
    /// input composed with the pin is the input composed without it, plus that one pin at its one place, or the same input
    /// where the operation composes no such entity. Its reach is the composing function; the claim's own gathering of facts
    /// is held by the claim's rows, which compare what a claim composed with the approved subject.
    /// </summary>
    [Fact]
    public void Composing_with_a_pin_added_moves_nothing_else_in_any_operation_s_input()
    {
        var composedWithAPlace = 0;
        foreach (var contract in OperationCatalog.All)
        {
            foreach (var (person, kind, value) in new[] { (true, "contact", "p-48211"), (false, "campaign", "sq-1129") })
            {
                var scene = new Scene(contract.Id);
                var without = CanonicalInput.Compose(contract, scene.Facts(NoPins, NoPins));
                var with = CanonicalInput.Compose(contract, scene.Facts(person ? Pins(kind, value) : NoPins, person ? NoPins : Pins(kind, value)));

                var place = person ? with["contacts"]?[0]?["external_ids"] : with["campaign"]?["external_ids"];
                if (place is null)
                {
                    Assert.True(JsonNode.DeepEquals(without, with), $"{contract.Id}: a pin that travels nowhere moved the input.");
                    continue;
                }

                Assert.Equal(value, (string?)place[kind]);
                place.AsObject().Remove(kind);
                Assert.True(JsonNode.DeepEquals(without, with), $"{contract.Id}: adding the {kind} pin moved more than the pin.");
                composedWithAPlace++;
            }
        }

        Assert.True(composedWithAPlace > OperationCatalog.All.Count, $"Only {composedWithAPlace} pins had a place to go.");
    }

    /// <summary>
    /// An added pin keeps the cover only because the person never read one that matters: every operation a person approves
    /// whose preview names a pin, in its sentence or among the values it highlights, either is never approved without that
    /// pin — a person's pin an operation works by alone, <c>provider_contact: pinned_only</c>, whose absence is refused before
    /// anybody is asked — or binds it to an argument the person read: the input requires the argument or the pin, and a pin
    /// that contradicts the argument is refused before the gate. A document that breaks this turns the row red.
    /// </summary>
    [Fact]
    [Critical(Family.S2, "a pin a person reads is never one added after their approval")]
    public void Every_pin_a_preview_shows_is_held_when_approved_or_bound_to_an_argument()
    {
        var shown = 0;
        foreach (var contract in OperationCatalog.All.Where(contract => contract.Approval.Value != "auto"))
        {
            var problems = PreviewPinProblems(contract);
            Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
            shown += ShownPins(contract).Count;
        }

        Assert.True(shown >= 3, $"Only {shown} pins are shown by a preview; the row checks nothing.");
    }

    /// <summary>The row above is not vacuous: a document that shows a pin it neither requires nor binds is caught.</summary>
    [Theory]
    [InlineData("contact.delete", "it no longer works by the person's pin alone")]
    [InlineData("campaign.activate", "its input no longer requires the argument or the pin")]
    [InlineData("enrollment.stop", "its argument is no longer the pin's")]
    public void A_preview_showing_a_pin_neither_held_nor_bound_is_caught(string operation, string broken)
    {
        var contract = OperationCatalog.Find(operation)!;
        var doctored = broken switch
        {
            "it no longer works by the person's pin alone" => contract with { Preflight = contract.Preflight with { ProviderContact = ProviderContact.FindOrCreate } },
            "its input no longer requires the argument or the pin" => contract with { InputSchema = Without(contract.InputSchema, "anyOf") },
            _ => contract with { ExternalIds = contract.ExternalIds.Where(entry => entry.Key != "campaign").ToDictionary() },
        };

        Assert.Empty(PreviewPinProblems(contract));
        Assert.NotEmpty(PreviewPinProblems(doctored));
    }

    private static readonly Regex PinPointer = new(@"/(contacts/0|campaign)/external_ids/([a-z_]+)", RegexOptions.CultureInvariant);

    /// <summary>The pins a preview shows: (true for the person, the kind), from its sentence and its highlights.</summary>
    private static List<(bool Person, string Kind)> ShownPins(OperationContract contract) =>
        contract.Preview is not { } preview
            ? []
            : [.. preview.Highlights.Append(preview.Summary.Text)
                .SelectMany(text => PinPointer.Matches(text))
                .Select(match => (match.Groups[1].Value == "contacts/0", match.Groups[2].Value))
                .Distinct()];

    private static List<string> PreviewPinProblems(OperationContract contract)
    {
        var problems = new List<string>();
        foreach (var (person, kind) in ShownPins(contract))
        {
            if (person)
            {
                if (contract.Preflight.ProviderContact != ProviderContact.PinnedOnly || kind != "contact")
                {
                    problems.Add($"{contract.Id}: its preview shows the person's '{kind}' pin, and it is approved without that pin.");
                }

                continue;
            }

            var bound = contract.ExternalIds.TryGetValue(kind, out var declared) && declared.Entity == PinnedEntity.Campaign
                && contract.ArgumentSchema["properties"]?[kind]?["required"] is JsonArray required
                && required.Any(member => (string?)member == "external_id");
            var eitherRequired = contract.InputSchema["anyOf"] is JsonArray branches
                && branches.Count == 2
                && branches.Any(branch => branch?["properties"]?["args"]?["required"] is JsonArray args && args.Any(member => (string?)member == kind))
                && branches.Any(branch => branch?["properties"]?["campaign"]?["properties"]?["external_ids"]?["required"] is JsonArray pins
                    && pins.Any(member => (string?)member == kind));
            if (!bound || !eitherRequired)
            {
                problems.Add($"{contract.Id}: its preview shows the campaign's '{kind}' pin, which is neither required with the argument the person read nor bound to it.");
            }
        }

        return problems;
    }

    private static JsonObject Without(JsonObject schema, string member)
    {
        var copy = schema.DeepClone().AsObject();
        copy.Remove(member);
        return copy;
    }

    private static ApprovalSubject Rewritten(ApprovalSubject subject, Action<JsonObject> change)
    {
        var document = subject.Document.DeepClone().AsObject();
        change(document);
        return new ApprovalSubject(document, CanonicalJson.Hash(document));
    }

    private static IReadOnlyDictionary<string, string> Pins(string kind, string value) =>
        new Dictionary<string, string>(StringComparer.Ordinal) { [kind] = value };

    private static Approval Row(JsonObject subject) => new()
    {
        PublicId = PublicId.New(ApprovalGate.IdPrefix),
        Operation = Enroll,
        OperationVersion = 1,
        Subject = subject.DeepClone().AsObject(),
        SubjectHash = CanonicalJson.Hash(subject),
        Preview = [],
        PluginId = Plugin,
        PluginSnapshotId = "snp_one",
        RoutingSnapshotId = "rts_one",
        Status = ApprovalStatus.Approved,
        Reason = ApprovalGate.ApprovalRequired,
    };

    /// <summary>One work item about one person in one campaign, composed as the claim composes it.</summary>
    private sealed class Scene
    {
        public Scene(string operation = Enroll)
        {
            Contract = OperationCatalog.Find(operation)!;
            Campaign = WorkItemFactory.NewCampaign("Q3 founders");
            Contact = new Contact { PublicId = PublicId.New("cnt"), FirstName = "Ada", LastName = "Lovelace" };
            Contact.Channels.Add(new ContactChannel { Channel = "email", Value = "ada@example.test", IsPrimary = true });
            Item = WorkItemFactory.NewProviderOp(Campaign, operation);
            Item.Contact = Contact;
            Item.Context = new JsonObject { [WorkItemService.InputKey] = Arguments("skip") };
        }

        public OperationContract Contract { get; }

        public Campaign Campaign { get; }

        public Contact Contact { get; }

        public WorkItem Item { get; }

        public static JsonObject Arguments(string collision) => new()
        {
            ["channel"] = "email",
            ["collision"] = collision,
            ["start"] = new JsonObject { ["position"] = "first_step" },
            ["first_touch"] = "authored_delay",
        };

        public PreflightFacts Facts(IReadOnlyDictionary<string, string> person, IReadOnlyDictionary<string, string> campaign) =>
            new(Item, Campaign, Contact, person, campaign, Suppressed: false);

        public ApprovalSubject Subject(
            IReadOnlyDictionary<string, string> person,
            IReadOnlyDictionary<string, string> campaign,
            string plugin = Plugin,
            string bindingIdentity = "sha256:account") =>
            ApprovalSubject.Of(
                new ProviderOpPlan(
                    TestPlugins.Loaded(plugin, [Contract.Id]),
                    "snp_one",
                    "rts_one",
                    RouteScope.CampaignDefault,
                    new JsonObject { ["account"] = "one" },
                    bindingIdentity,
                    Contract,
                    CanonicalInput.Compose(Contract, Facts(person, campaign))),
                Item);

        public Approval Approved(IReadOnlyDictionary<string, string> person, IReadOnlyDictionary<string, string> campaign) =>
            Row(Subject(person, campaign).Document);
    }
}
