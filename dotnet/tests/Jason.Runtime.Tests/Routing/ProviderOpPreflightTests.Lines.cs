using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Operations;
using Jason.Contracts.Plugins;
using Jason.Runtime.Execution;
using Jason.Runtime.Routing;

namespace Jason.Runtime.Tests.Routing;

/// <summary>
/// The line with one person in one campaign has an end: the claim refuses a touch to a person whose line was ended, and a
/// touch past the ceiling of touches, by the person and the operation, before anybody is asked to approve it. Every other
/// operation — a reply in a thread the person opened, a take-back, a read — passes whatever the line's state.
/// </summary>
public partial class ProviderOpPreflightTests
{
    private const string Reply = "conversation.reply";
    private const string Invite = "social_invitation.send";

    private const string Ended = "The line with this person in this campaign was ended; only the person reopens it.";

    /// <summary>The two checks are asked right after the opt-out lock, the ended line first, and before the pin of the person.</summary>
    [Fact]
    public void The_line_s_two_checks_are_asked_right_after_the_opt_out_lock()
    {
        var codes = ProviderOpPreflight.Codes.ToList();

        Assert.Equal(
            [AttemptErrors.OptedOut, AttemptErrors.LineFinished, AttemptErrors.TouchCeilingReached, AttemptErrors.ContactNotLinked],
            codes.Skip(codes.IndexOf(AttemptErrors.OptedOut)).Take(4));
        Assert.Equal(30, codes.Count);
    }

    /// <summary>
    /// They take the next two numbers free, 29 and 30, and no number published before them moves: a check's number is its
    /// name, wherever it is asked.
    /// </summary>
    [Fact]
    public void The_line_s_checks_are_numbered_29_and_30_and_no_other_number_moves()
    {
        string[] before =
        [
            "operation_unknown", "no_route", "plugin_not_loaded", "plugin_unavailable", "plugin_operation_unsupported",
            "contract_incompatible", "binding_invalid", "contact_required", "no_channel_value", "suppressed",
            "contact_not_linked", "send_unknown", "send_reference_absent", "send_route_mismatch", "search_unknown",
            "search_reference_absent", "search_route_mismatch", "audience_unknown", "input_invalid", "definition_invalid",
            "argument_unsupported", "template_not_offered", "provider_answer_lost", "search_unfinished",
            "approval_required", "provider_blocked", "thread_route_mismatch", "opted_out",
        ];

        Assert.Equal(
            [.. before.Select((code, index) => $"{index + 1} {code}"), "29 line_finished", "30 touch_ceiling_reached"],
            ProviderOpPreflight.Numbers.OrderBy(pair => pair.Value).Select(pair => $"{pair.Value} {pair.Key}"));
    }

    /// <summary>
    /// Both are asked before <c>template_not_offered</c>, so they are among the refusals an unsettled earlier attempt turns
    /// ambiguous: a send repeated after a lost answer, to a person whose line has ended since, may already have reached them,
    /// and the refusal says so rather than having a planner write the send again.
    /// </summary>
    [Fact]
    public void A_line_s_refusal_after_a_lost_answer_keeps_its_code_and_says_the_earlier_attempt_may_have_reached_the_person()
    {
        Assert.Contains(AttemptErrors.LineFinished, ProviderOpPreflight.SilentOnTheEarlierAttempt);
        Assert.Contains(AttemptErrors.TouchCeilingReached, ProviderOpPreflight.SilentOnTheEarlierAttempt);

        var finished = Scenario.Sending("snd-1");
        finished.Unsettled = true;
        finished.Line = new LineFacts(MembershipState.Finished, Touches: 0, Ceiling: 5);
        var atTheCeiling = Scenario.Sending("snd-1");
        atTheCeiling.Unsettled = true;
        atTheCeiling.Line = new LineFacts(MembershipState.Enrolled, Touches: 5, Ceiling: 5);

        foreach (var (verdict, code) in new[] { (finished.Check(), AttemptErrors.LineFinished), (atTheCeiling.Check(), AttemptErrors.TouchCeilingReached) })
        {
            Assert.Equal((code, FailureClass.Ambiguous), (verdict.Code, verdict.Class));
            Assert.True(verdict.Refused);
            Assert.Contains("may have reached the person", verdict.Message, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Every touch to a person whose line in this campaign was ended is refused, final and permanent, before a person is asked
    /// to approve it, with the route it would have gone by kept and the one sentence that says who reopens the line.
    /// </summary>
    [Theory]
    [InlineData(Send)]
    [InlineData(Enroll)]
    [InlineData(Invite)]
    public void A_finished_person_is_refused_every_touch(string operation)
    {
        Assert.Contains(operation, OperationCatalog.Touches);
        var scenario = Touching(operation);
        Assert.Equal(AttemptErrors.ApprovalRequired, scenario.Check().Code);

        scenario.Line = new LineFacts(MembershipState.Finished, Touches: 0, Ceiling: 5);
        var verdict = scenario.Check();

        Assert.Equal((AttemptErrors.LineFinished, FailureClass.Permanent), (verdict.Code, verdict.Class));
        Assert.True(verdict.Refused);
        Assert.Null(verdict.Plan);
        Assert.NotNull(verdict.Resolution);
        Assert.Equal(Ended, verdict.Message);
    }

    /// <summary>
    /// A paused person is not refused here, nor a person the campaign does not list, nor an enrolled one below the ceiling:
    /// the line has ended only where the membership says <c>finished</c>.
    /// </summary>
    [Theory]
    [InlineData(MembershipState.Enrolled)]
    [InlineData(MembershipState.Paused)]
    [InlineData(null)]
    public void Only_a_finished_line_refuses_a_touch(MembershipState? state)
    {
        var scenario = Touching(Send);
        scenario.Line = new LineFacts(state, Touches: 4, Ceiling: 5);

        Assert.Equal(AttemptErrors.ApprovalRequired, scenario.Check().Code);
    }

    /// <summary>
    /// A reply in a thread the person opened carries on their conversation rather than the campaign's line, and a take-back
    /// reaches nobody: neither is a touch, so both pass a finished person, and a person past the ceiling, exactly as they
    /// would pass anybody.
    /// </summary>
    [Theory]
    [InlineData(Reply, MembershipState.Finished, 0)]
    [InlineData(Reply, MembershipState.Enrolled, 9)]
    [InlineData(Cancel, MembershipState.Finished, 0)]
    [InlineData(Cancel, MembershipState.Enrolled, 9)]
    public void A_reply_and_a_take_back_pass_whatever_the_line_s_state(string operation, MembershipState state, int touches)
    {
        Assert.DoesNotContain(operation, OperationCatalog.Touches);
        var scenario = operation == Reply ? Touching(Reply) : Naming(Cancel, Made());
        scenario.Contact ??= Scenario.Person(("email", "ada@example.test"));
        var untouched = scenario.Check();

        scenario.Line = new LineFacts(state, touches, Ceiling: 5);
        var verdict = scenario.Check();

        Assert.Equal(untouched.Code, verdict.Code);
        Assert.Equal(operation == Reply ? AttemptErrors.ApprovalRequired : null, verdict.Code);
    }

    /// <summary>Work that touches nobody passes a finished person and a person past the ceiling: an add to a list, an update.</summary>
    [Theory]
    [InlineData(MembershipState.Finished, 0)]
    [InlineData(MembershipState.Enrolled, 5)]
    public void A_non_touch_operation_passes_whatever_the_line_s_state(MembershipState state, int touches)
    {
        Assert.DoesNotContain(Add, OperationCatalog.Touches);
        var scenario = new Scenario
        {
            Operation = Add,
            Route = Scenario.ToTheInstalledPackage,
            Contact = Scenario.Person(("email", "ada@example.test")),
            Arguments = Scenario.CompleteArguments,
            Line = new LineFacts(state, touches, Ceiling: 5),
        };

        Assert.True(scenario.Check().Passed);
    }

    /// <summary>
    /// The ceiling refuses at its value and past it, and passes one below it, whatever the value set: the person has had the
    /// most touches the setting allows in this campaign, the refusal says how many that is, and that the manager ends the line.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(20)]
    public void The_ceiling_refuses_at_its_value_and_passes_one_below_it(int ceiling)
    {
        var scenario = Touching(Send);

        scenario.Line = new LineFacts(MembershipState.Enrolled, ceiling - 1, ceiling);
        Assert.Equal(AttemptErrors.ApprovalRequired, scenario.Check().Code);

        foreach (var touches in new[] { ceiling, ceiling + 1 })
        {
            scenario.Line = new LineFacts(MembershipState.Enrolled, touches, ceiling);
            var verdict = scenario.Check();

            Assert.Equal((AttemptErrors.TouchCeilingReached, FailureClass.Permanent), (verdict.Code, verdict.Class));
            Assert.True(verdict.Refused);
            Assert.NotNull(verdict.Resolution);
            Assert.Equal(
                $"This person has had the most touches Lines:MaxTouches allows in this campaign, {ceiling}; the manager ends the line.",
                verdict.Message);
        }
    }

    /// <summary>An ended line answers before the ceiling: it says what a person did, and the ceiling only what the count reached.</summary>
    [Fact]
    public void An_ended_line_answers_before_the_ceiling()
    {
        var scenario = Touching(Send);
        scenario.Line = new LineFacts(MembershipState.Finished, Touches: 7, Ceiling: 5);

        Assert.Equal(AttemptErrors.LineFinished, scenario.Check().Code);
    }

    /// <summary>A touch of <paramref name="operation"/> to Ada with nothing else wrong, so that only a person's approval stops it.</summary>
    private static Scenario Touching(string operation) => operation switch
    {
        Send => Scenario.Sending("snd-1"),
        Enroll => new Scenario
        {
            Operation = Enroll,
            Route = Scenario.ToTheInstalledPackage,
            Contact = Scenario.Person(("email", "ada@example.test")),
            Arguments = Scenario.CompleteEnrollment,
        },
        Invite => new Scenario
        {
            Operation = Invite,
            Route = Scenario.ToTheInstalledPackage,
            Plugin = Scenario.Package(operations: [Invite]),
            Contact = Scenario.Person(("linkedin", "https://www.linkedin.com/in/ada-example")),
            Arguments = new JsonObject
            {
                ["channel"] = "linkedin",
                ["sender"] = new JsonObject { ["external_id"] = "snd-1", ["address"] = "https://www.linkedin.com/in/marta-owner" },
                ["note"] = null,
            },
        },
        Reply => new Scenario
        {
            Operation = Reply,
            Route = Scenario.ToTheInstalledPackage,
            Plugin = Scenario.Package(operations: [Reply]),
            Contact = Scenario.Person(("email", "ada@example.test")),
            Arguments = new JsonObject
            {
                ["channel"] = "email",
                ["thread"] = new JsonObject { ["external_id"] = "th-1" },
                ["body"] = "Thank you, Tuesday works.",
            },
        },
        _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, "No touch scenario for this operation."),
    };
}
