using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Plugins;
using Jason.Runtime.Execution;
using Jason.Runtime.Persistence;
using Jason.Runtime.Plugins.Manifest;
using Jason.Runtime.Plugins.Registry;
using Jason.Runtime.Routing;

namespace Jason.Runtime.Tests.Routing;

/// <summary>
/// Which earlier letter a message follows, as the claim decides it among its checks: the argument as the twenty-first check
/// reads it, what reaches the plugin, and a repeat that keeps what its first claim decided.
/// </summary>
public partial class ProviderOpPreflightTests
{
    /// <summary>The earlier letter these scenarios name or follow.</summary>
    private const string LetterItem = "wi_01K9R000000000000000000001";

    /// <summary>
    /// The letter that went: a send of this campaign to Ada on email — <see cref="Scenario.Person"/> makes her unsaved, so
    /// her id is 0 — with the provider's reference, under the route <see cref="Scenario.Sending"/> resolves, the installed
    /// package with no binding, from the provider's account snd-1.
    /// </summary>
    private static EarlierLetter Went() => new(
        LetterItem,
        Send,
        WorkItemStatus.Succeeded,
        "email-1001",
        Scenario.PluginId,
        BindingIdentity: null,
        ContactId: 0,
        Channel: "email",
        Sender: "snd-1",
        TakenBack: false);

    /// <summary>
    /// The installed package performing <paramref name="operations"/>, or the send alone, its manifest listing follows for
    /// the send on <paramref name="channels"/>, or on email.
    /// </summary>
    private static LoadedPlugin Offering(IReadOnlyList<string>? operations = null, JsonObject? binding = null, IReadOnlyList<string>? channels = null)
    {
        var plugin = Scenario.Package(operations: operations ?? [Send], binding: binding);
        var arguments = new ManifestArguments(new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>>(StringComparer.Ordinal)
        {
            [Send] = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal) { [FollowsDecision.Key] = channels ?? ["email"] },
        });
        return plugin with { Manifest = plugin.Manifest with { Arguments = arguments } };
    }

    /// <summary>The same package, settling the send by the work item's key, as a repeat after a lost answer needs.</summary>
    private static LoadedPlugin Keyed(LoadedPlugin plugin) => plugin with { Manifest = plugin.Manifest with { KeyedOperations = [Send] } };

    /// <summary>
    /// The five checks of a letter named are asked right after the fourteenth, in their order — what is named, whether it
    /// went with the reference, whether it was taken back, where it went through, and from which mailbox — and take the
    /// numbers after every check numbered before them, 31 to 35.
    /// </summary>
    [Fact]
    public void The_letter_s_five_checks_are_asked_right_after_the_fourteenth_and_numbered_31_to_35()
    {
        var codes = ProviderOpPreflight.Codes.ToList();
        string[] five =
        [
            AttemptErrors.FollowsUnknown,
            AttemptErrors.FollowsReferenceAbsent,
            AttemptErrors.FollowsTakenBack,
            AttemptErrors.FollowsRouteMismatch,
            AttemptErrors.FollowsSenderMismatch,
        ];

        Assert.Equal(
            [AttemptErrors.SendRouteMismatch, .. five, AttemptErrors.SearchUnknown],
            codes.Skip(codes.IndexOf(AttemptErrors.SendRouteMismatch)).Take(7));
        Assert.Equal([31, 32, 33, 34, 35], five.Select(code => ProviderOpPreflight.Numbers[code]));
    }

    /// <summary>
    /// A person who must not be written to is refused as that, never as a letter named wrongly: the register's entry, a
    /// standing opt-out, a line that was ended and a line at the ceiling of touches each answer before the letter a message
    /// names is asked about, even where that letter is one the campaign does not hold. What the refusal tells the manager is
    /// what the person asked, or what was decided about the line.
    /// </summary>
    [Theory]
    [InlineData(AttemptErrors.Suppressed)]
    [InlineData(AttemptErrors.OptedOut)]
    [InlineData(AttemptErrors.LineFinished)]
    [InlineData(AttemptErrors.TouchCeilingReached)]
    [Critical(Family.W3, "a person who must not be written to is refused as that, never as a letter named wrongly")]
    public void A_person_who_must_not_be_written_to_is_refused_as_that_before_the_letter_named_is_asked_about(string code)
    {
        var scenario = Scenario.Sending("snd-1");
        scenario.Plugin = Offering();
        scenario.Arguments![FollowsDecision.Key] = "wi_01K9R000000000000000000009";
        Assert.Equal(AttemptErrors.FollowsUnknown, scenario.Check().Code);

        switch (code)
        {
            case AttemptErrors.Suppressed:
                scenario.Suppressed = true;
                break;
            case AttemptErrors.OptedOut:
                scenario.OptedOut = OptOutVerdict;
                break;
            case AttemptErrors.LineFinished:
                scenario.Line = new LineFacts(MembershipState.Finished, Touches: 0, Ceiling: 5);
                break;
            default:
                scenario.Line = new LineFacts(MembershipState.Enrolled, Touches: 5, Ceiling: 5);
                break;
        }

        var verdict = scenario.Check();

        Assert.Equal((code, FailureClass.Permanent), (verdict.Code, verdict.Class));
        Assert.True(verdict.Refused);
        Assert.Null(verdict.Plan);
    }

    /// <summary>
    /// What reaches the plugin carries the provider's reference of an earlier letter only where the routed plugin's manifest
    /// lists follows for the channel the message goes on. Every way the argument is written, every letter the claim can find,
    /// on a plugin that lists follows there, one that lists it on another channel only and one that lists nothing; at a first
    /// claim, at a repeat under the same package, and at a repeat after the package changed: a package is known by the
    /// digest of its content, its manifest included, so a manifest that no longer lists follows is another digest, and a
    /// repeat is handed only to the digest that lost the answer. Nowhere does a plugin learn a thread it never said it can
    /// place a message in.
    /// </summary>
    [Fact]
    [Critical(Family.W3, "the provider's reference of a thread reaches only a plugin that takes follows")]
    public void A_composed_follows_carries_a_reference_only_where_the_routed_plugin_takes_follows_on_the_channel()
    {
        var plugins = new (LoadedPlugin Plugin, bool Takes)[]
        {
            (Keyed(Offering()), true),
            (Keyed(Offering(channels: ["linkedin"])), false),
            (Keyed(Scenario.Package(operations: [Send])), false),
        };
        string?[] written = [null, FollowsDecision.NewThread, LetterItem];
        var letters = new EarlierLetter?[]
        {
            null,
            Went(),
            Went() with { BindingIdentity = "sha256:east" },
            Went() with { PluginId = "another-provider" },
            Went() with { Sender = "snd-2" },
            Went() with { TakenBack = true },
            Went() with { Reference = null },
            Went() with { Status = WorkItemStatus.Failed },
        };

        var planned = 0;
        var referenced = 0;
        foreach (var (plugin, takes) in plugins)
        {
            foreach (var wish in written)
            {
                foreach (var letter in letters)
                {
                    // A first claim.
                    var scenario = Scenario.Sending("snd-1");
                    scenario.Plugin = plugin;
                    scenario.Followed = letter;
                    if (wish is not null)
                    {
                        scenario.Arguments![FollowsDecision.Key] = wish;
                    }

                    var first = scenario.Check();
                    Hold(first, takes);
                    if (first.Plan?.Input[FollowsDecision.Key] is not JsonObject composed)
                    {
                        continue;
                    }

                    // A repeat under the same package, keeping what that claim recorded.
                    scenario.RecordedFollows = FollowsDecision.Recorded(composed);
                    scenario.Unsettled = true;
                    scenario.UnsettledClaim = facts => facts with { Followed = letter };
                    Hold(scenario.Check(), takes);

                    // A repeat after the package changed to each of the others: the attempt that lost the answer ran under the
                    // package as it was.
                    scenario.UnsettledClaim = null;
                    scenario.UnsettledUnder = (Scenario.PluginId, Scenario.RoutedDigest, null);
                    foreach (var (now, nowTakes) in plugins.Where(other => !ReferenceEquals(other.Plugin, plugin)))
                    {
                        scenario.Plugin = now with { Digest = "sha256:changed" };
                        Hold(scenario.Check(), nowTakes);
                    }
                }
            }
        }

        // Neither half is empty: plans went out, and some of them followed a letter by its reference.
        Assert.True(planned > 0);
        Assert.True(referenced > 0);

        void Hold(PreflightVerdict verdict, bool takes)
        {
            if (verdict.Plan is not { } plan)
            {
                return;
            }

            planned++;
            if (plan.Input[FollowsDecision.Key]?["reference"] is JsonValue)
            {
                referenced++;
                Assert.True(takes, $"A plan carries the reference of an earlier letter to a plugin that does not take follows on email: {plan.Input[FollowsDecision.Key]!.ToJsonString()}");
            }
        }
    }

    /// <summary>
    /// The argument asks the plugin something only where it names a work item. A letter named on a plugin that does not
    /// list follows for the channel is refused by the twenty-first check, which says what to do instead; a letter of its
    /// own asks nothing of the plugin, which sends one whatever its manifest lists, so new_thread goes on to the person's
    /// approval as a new letter that says it was asked for.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("linkedin")]
    public void On_a_plugin_that_does_not_take_follows_a_letter_named_is_refused_and_a_letter_of_its_own_is_not(string? listed)
    {
        var scenario = Scenario.Sending("snd-1");
        scenario.Plugin = listed is null ? Scenario.Package(operations: [Send]) : Offering(channels: [listed]);
        scenario.Followed = Went();
        scenario.Arguments![FollowsDecision.Key] = LetterItem;

        var named = scenario.Check();

        Assert.Equal(AttemptErrors.ArgumentUnsupported, named.Code);
        Assert.True(named.Refused);
        Assert.Equal(FailureClass.Permanent, named.Class);
        Assert.Null(named.Plan);
        Assert.Equal(
            $"'{Scenario.PluginId}' takes no 'follows' for 'message.send' on the 'email' channel: its manifest does not list it there. Leave the argument out, or route the operation to a plugin that takes it.",
            named.Message);

        scenario.Followed = null;
        scenario.Arguments[FollowsDecision.Key] = FollowsDecision.NewThread;

        var own = scenario.Check();

        Assert.Equal(AttemptErrors.ApprovalRequired, own.Code);
        Assert.Equal(
            """{"work_item":null,"operation":null,"reference":null,"because":"asked_new_thread"}""",
            own.Plan!.Input[FollowsDecision.Key]!.ToJsonString());
        Assert.Equal(FollowsDecision.NewThread, own.Plan.Input["args"]![FollowsDecision.Key]!.GetValue<string>());
    }

    /// <summary>
    /// On a plugin that takes follows on the channel, each way the argument is written reaches it as the decision composed
    /// it: a letter named, followed by its reference; new_thread, a letter of its own; absent, the line's root.
    /// </summary>
    [Theory]
    [InlineData(LetterItem, """{"work_item":"wi_01K9R000000000000000000001","operation":"message.send","reference":"email-1001","because":"named"}""")]
    [InlineData(FollowsDecision.NewThread, """{"work_item":null,"operation":null,"reference":null,"because":"asked_new_thread"}""")]
    [InlineData(null, """{"work_item":"wi_01K9R000000000000000000001","operation":"message.send","reference":"email-1001","because":"line_root"}""")]
    public void On_a_plugin_that_takes_follows_the_decision_reaches_it_as_composed(string? wish, string composed)
    {
        var scenario = Scenario.Sending("snd-1");
        scenario.Plugin = Offering();
        scenario.Followed = Went();
        if (wish is not null)
        {
            scenario.Arguments![FollowsDecision.Key] = wish;
        }

        var verdict = scenario.Check();

        Assert.Equal(AttemptErrors.ApprovalRequired, verdict.Code);
        Assert.Equal(composed, verdict.Plan!.Input[FollowsDecision.Key]!.ToJsonString());
    }

    /// <summary>
    /// A send whose earlier attempt may have reached the person keeps, at its repeat, what its first claim decided: the
    /// letter it followed, its reference read again, or the new letter it was, whatever the claim finds now and whatever
    /// became of that letter since. None of the five checks of a letter named is asked again, and the input composed is the
    /// one the first claim composed, so the digest the provider's key settles holds: a letter that went into a thread is
    /// never repeated as a new one, nor a new letter into a thread. A first claim made by a runtime that composed no follows
    /// recorded none, and its repeat composes none either.
    /// </summary>
    [Theory]
    [InlineData("the line's root followed")]
    [InlineData("a letter named, taken back since")]
    [InlineData("a letter named, which the checks would refuse now")]
    [InlineData("no earlier letter, and a letter since")]
    [InlineData("a letter of its own asked for, and a letter since")]
    [InlineData("a claim that composed no follows, and a letter since")]
    [Critical(Family.W5, "a repeat keeps the letter its first claim decided")]
    public void A_send_repeated_after_a_lost_answer_follows_what_its_first_claim_decided(string since)
    {
        var scenario = Scenario.Sending("snd-1");
        scenario.Plugin = Keyed(Offering());
        var letterSince = Went() with { WorkItem = "wi_01K9R000000000000000000002", Reference = "email-1002" };

        // What the argument says, the letter the first claim found, and the letter the claim finds at the repeat.
        (string? Wish, EarlierLetter? Then, EarlierLetter? Now) facts = since switch
        {
            "the line's root followed" => (null, Went(), Went()),
            "a letter named, taken back since" => (LetterItem, Went(), Went() with { TakenBack = true }),
            "a letter named, which the checks would refuse now" => (LetterItem, Went(), Went() with { BindingIdentity = "sha256:east", Sender = "snd-2" }),
            "no earlier letter, and a letter since" => (null, null, letterSince),
            "a letter of its own asked for, and a letter since" => (FollowsDecision.NewThread, null, letterSince),
            "a claim that composed no follows, and a letter since" => (null, null, letterSince),
            _ => throw new ArgumentOutOfRangeException(nameof(since), since, null),
        };
        var before = since.StartsWith("a claim that composed no follows", StringComparison.Ordinal);
        if (facts.Wish is not null)
        {
            scenario.Arguments![FollowsDecision.Key] = facts.Wish;
        }

        // The first claim, as it decided and composed.
        scenario.Followed = facts.Then;
        var first = scenario.Check();
        Assert.Equal(AttemptErrors.ApprovalRequired, first.Code);
        var composed = first.Plan!.Input[FollowsDecision.Key]!.AsObject();

        // What it recorded, or nothing where its runtime composed no follows; and the repeat, on what the claim finds now.
        scenario.Unsettled = true;
        var then = facts.Then;
        scenario.UnsettledClaim = earlier => earlier with { Followed = then };
        scenario.UnsettledBeforeFollows = before;
        scenario.RecordedFollows = before ? null : FollowsDecision.Recorded(composed);
        scenario.Followed = facts.Now;

        var repeat = scenario.Check();

        Assert.Equal(AttemptErrors.ApprovalRequired, repeat.Code);
        if (before)
        {
            Assert.False(repeat.Plan!.Input.ContainsKey(FollowsDecision.Key));
        }
        else
        {
            Assert.Equal(composed.ToJsonString(), repeat.Plan!.Input[FollowsDecision.Key]!.ToJsonString());
        }
    }

    /// <summary>
    /// A repeat whose recorded letter the claim cannot read again with its reference composes the letter it followed with
    /// none, which the operation's schema refuses: the repeat is never made as a new letter, and the refusal says the earlier
    /// attempt may have reached the person.
    /// </summary>
    [Fact]
    public void A_repeat_whose_letter_is_not_found_again_is_refused_rather_than_sent_as_a_new_letter()
    {
        var scenario = Scenario.Sending("snd-1");
        scenario.Plugin = Keyed(Offering());
        scenario.Followed = Went();
        var composed = scenario.Check().Plan!.Input[FollowsDecision.Key]!.AsObject();

        scenario.Unsettled = true;
        scenario.UnsettledClaim = earlier => earlier with { Followed = Went() };
        scenario.RecordedFollows = FollowsDecision.Recorded(composed);
        scenario.Followed = null;

        var repeat = scenario.Check();

        Assert.Equal(AttemptErrors.InputInvalid, repeat.Code);
        Assert.True(repeat.Refused);
        Assert.Equal(FailureClass.Ambiguous, repeat.Class);
        Assert.Null(repeat.Plan);
        Assert.Contains("may have reached the person", repeat.Message, StringComparison.Ordinal);
    }
}
