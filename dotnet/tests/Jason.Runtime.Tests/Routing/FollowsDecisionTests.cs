using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Operations;
using Jason.Contracts.Plugins;
using Jason.Runtime.Execution;
using Jason.Runtime.Persistence;
using Jason.Runtime.Plugins.Manifest;
using Jason.Runtime.Plugins.Registry;
using Jason.Runtime.Routing;
using Jason.Runtime.Tests.Plugins;
using Jason.Runtime.WorkItems;

namespace Jason.Runtime.Tests.Routing;

/// <summary>
/// Which earlier letter a message follows, as the claim decides it: a pure function over the facts the claim read, the route
/// and the routed plugin, with no database. Every branch is a row of one table. Absent, the runtime's default follows the
/// line's root, or makes a new letter and says why, and never refuses; <c>new_thread</c> is a letter of its own; a work
/// item's id is followed only past five checks, asked in one order; and a repeat of an attempt that may have acted keeps
/// the decision its first claim recorded, whatever the facts say now.
/// </summary>
public sealed class FollowsDecisionTests
{
    private const string Plugin = "stand-in-provider";
    private const string Binding = "sha256:west";
    private const string Send = "message.send";
    private const string Letter = "wi_01K9R000000000000000000001";
    private const string Reference = "email-1001";
    private const int Person = 7;

    public static TheoryData<string> EveryBranch() => [.. Cases.Keys];

    /// <summary>
    /// The whole table is the decision, so every row of it is the tier's: a branch that moves is a letter sent into the
    /// wrong thread, or out of the one it belongs in.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryBranch))]
    [Critical(Family.W3, "which earlier letter a message follows")]
    public void The_decision_is_the_table_s(string branch)
    {
        var scene = Cases[branch];

        Assert.Equal(scene.Expected, Said(scene.Decide()));
    }

    /// <summary>
    /// The default never refuses: every condition it meets makes a new letter, whatever the root looks like, while a letter
    /// the caller named is refused for the same facts.
    /// </summary>
    [Fact]
    public void The_default_never_refuses()
    {
        var letters = new EarlierLetter?[]
        {
            null,
            Earlier(),
            Earlier() with { PluginId = "another-provider" },
            Earlier() with { BindingIdentity = "sha256:east" },
            Earlier() with { Sender = "snd-2" },
            Earlier() with { Sender = null },
        };

        foreach (var offered in new[] { true, false })
        {
            foreach (var letter in letters)
            {
                var decision = new Scene(Written: null, Offered: offered, Letter: letter, Expected: string.Empty).Decide();
                Assert.IsNotType<FollowsDecision.Refused>(decision);
                Assert.NotNull(decision);
            }
        }
    }

    /// <summary>
    /// Only a letter followed asks follows of the routed plugin, whichever way it was decided — named, or the runtime's
    /// default — so the twenty-first check asks the manifest for it; a letter of its own asks nothing, since every plugin
    /// sends one, <c>new_thread</c> written included. Every other argument the decision leaves to what the work writes.
    /// </summary>
    [Fact]
    public void Only_a_letter_followed_asks_follows_of_the_plugin()
    {
        Assert.False(new Scene(FollowsDecision.NewThread, Offered: false, Letter: null, Expected: string.Empty).Decide()!.Asks(FollowsDecision.Key));
        Assert.True(new Scene(Letter, Offered: false, Letter: Earlier(), Expected: string.Empty).Decide()!.Asks(FollowsDecision.Key));
        Assert.True(new Scene(Written: null, Offered: true, Letter: Earlier(), Expected: string.Empty).Decide()!.Asks(FollowsDecision.Key));
        Assert.False(new Scene(Written: null, Offered: false, Letter: null, Expected: string.Empty).Decide()!.Asks(FollowsDecision.Key));
        Assert.Null(new Scene(Written: null, Offered: true, Letter: Earlier(), Expected: string.Empty).Decide()!.Asks("send_after"));
    }

    /// <summary>
    /// What a decision composes at the root of the input, and what the claim records under the reserved key: the same object
    /// less the provider's reference.
    /// </summary>
    [Fact]
    public void A_decision_composes_four_members_and_records_three()
    {
        var followed = new FollowsDecision.Follow(Letter, Send, Reference, FollowsDecision.LineRoot);
        var composed = followed.Composed()!;

        Assert.Equal(
            """{"work_item":"wi_01K9R000000000000000000001","operation":"message.send","reference":"email-1001","because":"line_root"}""",
            composed.ToJsonString());
        Assert.Equal(
            """{"work_item":"wi_01K9R000000000000000000001","operation":"message.send","because":"line_root"}""",
            FollowsDecision.Recorded(composed).ToJsonString());
        Assert.Equal(
            """{"work_item":null,"operation":null,"reference":null,"because":"other_mailbox"}""",
            new FollowsDecision.NewLetter(FollowsDecision.OtherMailbox).Composed()!.ToJsonString());
        Assert.Null(new FollowsDecision.Refused(AttemptErrors.FollowsUnknown, "No such letter.").Composed());
    }

    private static readonly IReadOnlyDictionary<string, Scene> Cases = new Dictionary<string, Scene>(StringComparer.Ordinal)
    {
        // Absent: the runtime's default, in its published order. A plugin that does not take the argument on the channel is
        // asked first, so no root is followed there however good it is.
        ["absent, the plugin takes no follows"] = new(null, Offered: false, Letter: Earlier(), Expected: "new not_offered"),
        ["absent, the plugin takes follows on another channel only"] = new(null, Offered: true, Letter: Earlier(), Channel: "linkedin", Expected: "new not_offered"),
        ["absent, the plugin takes no follows and the line holds no letter"] = new(null, Offered: false, Letter: null, Expected: "new not_offered"),
        ["absent, the line holds no letter"] = new(null, Offered: true, Letter: null, Expected: "new no_earlier_letter"),
        ["absent, the root went through another plugin"] = new(null, Offered: true, Letter: Earlier() with { PluginId = "another-provider" }, Expected: "new other_account"),
        ["absent, the root went under another binding"] = new(null, Offered: true, Letter: Earlier() with { BindingIdentity = "sha256:east" }, Expected: "new other_account"),
        ["absent, the root went under no binding where this route has one"] = new(null, Offered: true, Letter: Earlier() with { BindingIdentity = null }, Expected: "new other_account"),
        ["absent, the root went through another account and from another mailbox"] = new(null, Offered: true, Letter: Earlier() with { PluginId = "another-provider", Sender = "snd-2" }, Expected: "new other_account"),
        ["absent, the root went from another mailbox"] = new(null, Offered: true, Letter: Earlier() with { Sender = "snd-2" }, Expected: "new other_mailbox"),
        ["absent, the root says no mailbox"] = new(null, Offered: true, Letter: Earlier() with { Sender = null }, Expected: "new other_mailbox"),
        ["absent, the line's root is followed"] = new(null, Offered: true, Letter: Earlier(), Expected: $"follow {Letter} {Send} {Reference} line_root"),

        // new_thread: a letter of its own, on any plugin, whatever the line holds.
        ["new_thread on a plugin that takes follows"] = new(FollowsDecision.NewThread, Offered: true, Letter: null, Expected: "new asked_new_thread"),
        ["new_thread on a plugin that takes no follows"] = new(FollowsDecision.NewThread, Offered: false, Letter: null, Expected: "new asked_new_thread"),

        // A letter named: five checks in one order, the first that fails names the refusal.
        ["named, the campaign holds no such work item"] = new(Letter, Offered: true, Letter: null, Expected: "refused follows_unknown"),
        ["named, another operation"] = new(Letter, Offered: true, Letter: Earlier() with { Operation = "social_invitation.send" }, Expected: "refused follows_unknown"),
        ["named, another person"] = new(Letter, Offered: true, Letter: Earlier() with { ContactId = Person + 1 }, Expected: "refused follows_unknown"),
        ["named, nobody"] = new(Letter, Offered: true, Letter: Earlier() with { ContactId = null }, Expected: "refused follows_unknown"),
        ["named, another channel"] = new(Letter, Offered: true, Letter: Earlier() with { Channel = "linkedin" }, Expected: "refused follows_unknown"),
        ["named, unknown before anything else"] = new(Letter, Offered: true, Letter: Earlier() with { Channel = "linkedin", Status = WorkItemStatus.Failed, Reference = null, TakenBack = true, PluginId = "another-provider", Sender = "snd-2" }, Expected: "refused follows_unknown"),
        ["named, not succeeded"] = new(Letter, Offered: true, Letter: Earlier() with { Status = WorkItemStatus.Created, Reference = null }, Expected: "refused follows_reference_absent"),
        ["named, succeeded with no reference"] = new(Letter, Offered: true, Letter: Earlier() with { Reference = null }, Expected: "refused follows_reference_absent"),
        ["named, failed with a reference an earlier attempt returned"] = new(Letter, Offered: true, Letter: Earlier() with { Status = WorkItemStatus.Failed }, Expected: "refused follows_reference_absent"),
        ["named, no reference before taken back"] = new(Letter, Offered: true, Letter: Earlier() with { Reference = null, TakenBack = true }, Expected: "refused follows_reference_absent"),
        ["named, taken back"] = new(Letter, Offered: true, Letter: Earlier() with { TakenBack = true }, Expected: "refused follows_taken_back"),
        ["named, taken back before another account"] = new(Letter, Offered: true, Letter: Earlier() with { TakenBack = true, PluginId = "another-provider", Sender = "snd-2" }, Expected: "refused follows_taken_back"),
        ["named, another plugin"] = new(Letter, Offered: true, Letter: Earlier() with { PluginId = "another-provider" }, Expected: "refused follows_route_mismatch"),
        ["named, another binding"] = new(Letter, Offered: true, Letter: Earlier() with { BindingIdentity = "sha256:east" }, Expected: "refused follows_route_mismatch"),
        ["named, another account before another mailbox"] = new(Letter, Offered: true, Letter: Earlier() with { BindingIdentity = "sha256:east", Sender = "snd-2" }, Expected: "refused follows_route_mismatch"),
        ["named, another mailbox"] = new(Letter, Offered: true, Letter: Earlier() with { Sender = "snd-2" }, Expected: "refused follows_sender_mismatch"),
        ["named, no mailbox said"] = new(Letter, Offered: true, Letter: Earlier() with { Sender = null }, Expected: "refused follows_sender_mismatch"),
        ["named, followed"] = new(Letter, Offered: true, Letter: Earlier(), Expected: $"follow {Letter} {Send} {Reference} named"),

        // A letter named on a plugin that takes no follows passes the five: the twenty-first check refuses it later, where
        // every argument the plugin does not take is refused.
        ["named on a plugin that takes no follows"] = new(Letter, Offered: false, Letter: Earlier(), Expected: $"follow {Letter} {Send} {Reference} named"),

        // A repeat of an attempt that may have acted keeps what its first claim recorded, and asks none of the checks.
        ["a repeat keeps the root it followed though it was taken back since"] = new(null, Offered: true, Letter: Earlier() with { TakenBack = true }, Unsettled: true, Recorded: Record(Letter, FollowsDecision.LineRoot), Expected: $"follow {Letter} {Send} {Reference} line_root"),
        ["a repeat keeps the letter named though its route moved since"] = new(Letter, Offered: false, Letter: Earlier() with { PluginId = "another-provider" }, Unsettled: true, Recorded: Record(Letter, FollowsDecision.Named), Expected: $"follow {Letter} {Send} {Reference} named"),
        ["a repeat keeps its new letter though a root appeared since"] = new(null, Offered: true, Letter: Earlier(), Unsettled: true, Recorded: Record(null, FollowsDecision.NoEarlierLetter), Expected: "new no_earlier_letter"),
        ["a repeat keeps a letter of its own"] = new(FollowsDecision.NewThread, Offered: false, Letter: null, Unsettled: true, Recorded: Record(null, FollowsDecision.AskedNewThread), Expected: "new asked_new_thread"),
        ["a repeat whose recorded letter is not found again follows it with no reference, which the schema refuses"] =new(null, Offered: true, Letter: null, Unsettled: true, Recorded: Record(Letter, FollowsDecision.LineRoot), Expected: $"follow {Letter} {Send} - line_root"),
        ["a repeat of a claim that recorded nothing composes nothing, as that claim did"] = new(null, Offered: true, Letter: Earlier(), Unsettled: true, Recorded: null, Expected: "none"),

        // An operation whose input names no follows decides nothing.
        ["another operation"] = new(null, Offered: true, Letter: Earlier(), Operation: "message.status", Expected: "none"),
    };

    /// <summary>The root of the line, or the letter named, as the claim read it: everything about it fit to follow.</summary>
    private static EarlierLetter Earlier() => new(
        Letter, Send, WorkItemStatus.Succeeded, Reference, Plugin, Binding, ContactId: Person, Channel: "email", Sender: "snd-1", TakenBack: false);

    /// <summary>What a first claim recorded under the reserved key, less the reference.</summary>
    private static JsonObject Record(string? letter, string because) => new()
    {
        ["work_item"] = letter,
        ["operation"] = letter is null ? null : Send,
        ["because"] = because,
    };

    /// <summary>A decision in a few words a row can name: follow, new, refused or none.</summary>
    private static string Said(FollowsDecision? decision) => decision switch
    {
        null => "none",
        FollowsDecision.Follow follow => $"follow {follow.WorkItem} {follow.Operation} {follow.Reference ?? "-"} {follow.Because}",
        FollowsDecision.NewLetter letter => $"new {letter.Because}",
        FollowsDecision.Refused refused => $"refused {refused.Code}",
        _ => throw new ArgumentOutOfRangeException(nameof(decision)),
    };

    /// <summary>
    /// One row: what the caller wrote under <c>args.follows</c> (null for nothing), whether the routed plugin's manifest lists
    /// <c>follows</c> on email, the letter the claim read, and, for a repeat, what the first claim recorded.
    /// </summary>
    private sealed record Scene(
        string? Written,
        bool Offered,
        EarlierLetter? Letter,
        string Expected,
        string Channel = "email",
        bool Unsettled = false,
        JsonObject? Recorded = null,
        string Operation = Send)
    {
        public FollowsDecision? Decide()
        {
            var campaign = WorkItemFactory.NewCampaign();
            var contact = new Contact { Id = Person, PublicId = "cnt_01K9R000000000000000000007", FirstName = "Ada" };
            contact.Channels.Add(new ContactChannel { Channel = Channel, Value = Channel == "email" ? "ada@example.test" : "https://www.linkedin.com/in/ada-example", IsPrimary = true });
            var item = WorkItemFactory.NewProviderOp(campaign, Operation);
            item.Contact = contact;
            var arguments = new JsonObject
            {
                ["channel"] = Channel,
                ["sender"] = new JsonObject { ["external_id"] = "snd-1", ["address"] = "marta@example.com" },
                ["message"] = new JsonObject { ["format"] = "email", ["subject"] = "Hello", ["body"] = "Hello, Ada." },
            };
            if (Written is not null)
            {
                arguments["follows"] = Written;
            }

            item.Context = new JsonObject { [WorkItemService.InputKey] = arguments };
            if (Recorded is not null)
            {
                item.Context[FollowsDecision.Key] = Recorded.DeepClone();
            }

            var earlier = Unsettled
                ? new Attempt
                {
                    PublicId = "att_01K9R000000000000000000001",
                    Number = 1,
                    Status = AttemptStatus.Failed,
                    ContextSnapshot = item.Context.DeepClone().AsObject(),
                    Error = new AttemptErrorDto(AttemptErrors.ProviderAnswerLost, "The plugin's answer was lost.", true, Class: FailureClass.Ambiguous),
                }
                : null;
            var facts = new PreflightFacts(item, campaign, contact, PreflightFacts.NoPins, PreflightFacts.NoPins, Suppressed: false, Unsettled: earlier, Followed: Letter);

            var loaded = TestPlugins.Loaded(Plugin, [Send, "message.status"]);
            var plugin = Offered
                ? loaded with
                {
                    Manifest = loaded.Manifest with
                    {
                        Arguments = new ManifestArguments(new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>>(StringComparer.Ordinal)
                        {
                            [Send] = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal) { ["follows"] = ["email"] },
                        }),
                    },
                }
                : loaded;

            return FollowsDecision.Decide(OperationCatalog.Find(Operation)!, facts, new Route(Plugin, new JsonObject { ["workspace"] = "west" }, Binding), plugin);
        }
    }
}
