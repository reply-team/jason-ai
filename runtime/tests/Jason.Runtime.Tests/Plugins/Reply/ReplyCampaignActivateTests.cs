using System.Text.Json.Nodes;
using Jason.Contracts.Plugins;
using Jason.Runtime.Plugins.Invocation;

namespace Jason.Runtime.Tests.Plugins.Reply;

/// <summary>
/// <c>campaign.activate</c> as the official package performs it: the real plugin JavaScript in a real plugin-host child,
/// driving the stand-in CLI against an account a test planted. One test per line of the document's conformance list,
/// and then the refusals Reply publishes. The population a person approved is held here, at execution.
/// </summary>
[Collection(ProcessEnvironmentCollection.Name)]
public class ReplyCampaignActivateTests
{
    private const string Operation = "campaign.activate";
    private const int Sequence = ReplyOperations.Sequence;
    private const string SequencePath = "/v3/sequences/7";
    private const string CountPath = "/v3/sequences/7/contacts/count";
    private const string StartPath = "/v3/sequences/7/start";
    private const string WorkItemId = "wi_01JB6K8TQ2W9V4MZ0C3Y7H5NRK";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("new", "draft")]
    [InlineData("paused", "paused")]
    public async Task A_campaign_holding_the_approved_population_is_started_and_says_what_it_left(string status, string was)
    {
        using var account = Planted(status, people: 2);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await InvokeAsync(api, Input(expected: 2, supplied: "7"));

        var answer = ReplyOperations.Succeeded(Operation, result);
        Assert.Equal(
            $$"""{"items":[{"campaign_external_id":"7","status":"activated","was":"{{was}}","enrolled":2}]}""",
            answer.ToJsonString());
        var outcome = Assert.IsType<InvocationOutcome.Succeeded>(result.Outcome);
        Assert.Equal("""{"campaign":"7"}""", outcome.ExternalIds!.ToJsonString());
        Assert.Equal("active", (string?)account.Sequences.Single()!["status"]);
        Assert.Equal(
            [$"GET {SequencePath}", $"GET {CountPath}", $"POST {StartPath}"],
            account.Calls.Select(call => call.Method + " " + call.Path));
        Assert.All(account.Calls, call => Assert.Null(call.Body));
    }

    [Fact]
    public async Task A_campaign_already_live_answers_already_live_with_its_count_and_starts_nothing()
    {
        using var account = Planted("active", people: 2);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var answer = ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Input(expected: 2, pinned: "7")));

        Assert.Equal(
            """{"items":[{"campaign_external_id":"7","status":"already_live","was":null,"enrolled":2}]}""",
            answer.ToJsonString());
        Assert.DoesNotContain(account.Calls, call => call.Path == StartPath);
    }

    /// <summary>
    /// A person approved starting the campaign for one population. A different count at execution is a different
    /// decision, so nothing starts and both numbers travel out for the planner who reads the count again.
    /// </summary>
    [Fact]
    public async Task A_population_that_moved_since_the_approval_starts_nothing_and_says_both_numbers()
    {
        using var account = Planted("new", people: 3);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(expected: 2, pinned: "7")));

        Assert.Equal("population_changed", error.Code);
        Assert.Equal(2, (int)error.Details!["expected"]!);
        Assert.Equal(3, (int)error.Details["actual"]!);

        // The numbers reach a person through the attempt's trace, never the last error, and the message says so.
        Assert.Contains("The attempt's trace carries both numbers", error.Message, StringComparison.Ordinal);
        Assert.Equal("7", (string?)error.ExternalIds!["campaign"]);
        Assert.Equal("new", (string?)account.Sequences.Single()!["status"]);
        Assert.DoesNotContain(account.Calls, call => call.Path == StartPath);
    }

    [Fact]
    public async Task An_archived_campaign_cannot_be_started_and_is_not_asked()
    {
        using var account = Planted("paused", people: 1, archived: true);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(expected: 1, pinned: "7")));

        Assert.Equal("campaign_not_activatable", error.Code);
        Assert.Contains("Either its state says it is archived, or Reply refused the start", error.Message, StringComparison.Ordinal);
        Assert.Contains("The attempt's trace says which", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(account.Calls, call => call.Path == StartPath);
    }

    /// <summary>
    /// Reply refuses a start with a word of its own — no contacts, nothing to send from — and that word travels in the
    /// details, whatever it is: the refusal is not activatable rather than a guess at which it was.
    /// </summary>
    [Fact]
    public async Task A_start_reply_refuses_is_not_activatable_and_carries_reply_s_own_code()
    {
        using var account = Planted("new", people: 1);
        account.WithoutSendingAccount(Sequence);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(expected: 1, pinned: "7")));

        Assert.Equal("campaign_not_activatable", error.Code);
        Assert.Equal("sequenceAction.noEmailAccounts", (string?)error.Details!["provider_code"]);
    }

    /// <summary>A campaign that holds nobody is started for nobody only if Reply takes it; it does not.</summary>
    [Fact]
    public async Task A_campaign_holding_nobody_is_refused_by_reply_and_nothing_starts()
    {
        using var account = Planted("new", people: 0);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(expected: 0, pinned: "7")));

        Assert.Equal("campaign_not_activatable", error.Code);
        Assert.Equal("sequenceAction.noContacts", (string?)error.Details!["provider_code"]);
        Assert.Equal("new", (string?)account.Sequences.Single()!["status"]);
    }

    /// <summary>
    /// The sequence was not archived when it was read and is by the time the start arrives: Reply's 409 is the same
    /// refusal the state read would have made, with Reply's own code carried.
    /// </summary>
    [Fact]
    public async Task A_start_refused_as_archived_after_the_read_is_not_activatable()
    {
        using var account = Planted("new", people: 1);
        account.Answers("POST", StartPath, 409, """{"type":"about:blank","title":"Conflict","status":409,"code":"sequenceAction.archived"}""");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(expected: 1, pinned: "7")));

        Assert.Equal("campaign_not_activatable", error.Code);
        Assert.Equal("sequenceAction.archived", (string?)error.Details!["provider_code"]);
    }

    [Fact]
    public async Task A_campaign_the_account_does_not_hold_is_not_found_and_pins_nothing()
    {
        using var account = Planted("new", people: 1);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(expected: 1, supplied: "8")));

        Assert.Equal("campaign_not_found", error.Code);
        Assert.Null(error.ExternalIds);
    }

    /// <summary>
    /// A start whose answer was lost may already be sending. The next attempt reads the state and the count first,
    /// and a start that already happened answers `already_live` rather than starting twice.
    /// </summary>
    [Fact]
    public async Task A_start_whose_answer_was_lost_is_read_first_and_answers_already_live()
    {
        using var account = Planted("new", people: 1);
        account.LosesTheAnswerAfterWriting("POST", StartPath);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var lost = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(expected: 1, pinned: "7")));
        Assert.Equal("provider_answer_lost", lost.Code);
        Assert.Equal(FailureClass.Ambiguous, lost.Class);
        Assert.Equal("7", (string?)lost.ExternalIds!["campaign"]);

        var mark = account.Mark();
        var repeated = ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Input(expected: 1, pinned: "7"), attempt: 2));
        Assert.Equal("already_live", (string?)repeated["items"]![0]!["status"]);
        Assert.DoesNotContain(account.CallsSince(mark), call => call.Path == StartPath);
    }

    private static ReplyAccount Planted(string status, int people, bool archived = false)
    {
        var account = ReplyOperations.Plant(new ReplyAccount());
        account.WithSequence(Sequence, "Q3 LatAm founders", status, archived, 11, 12);
        for (var at = 0; at < people; at++)
        {
            account.WithContact(2001 + at, $"person{at}@example.com", "Person" + at);
            account.WithEnrollment(Sequence, 2001 + at);
        }

        return account;
    }

    private static JsonObject Input(int expected, string? supplied = null, string? pinned = null)
    {
        var args = new JsonObject { ["expected"] = new JsonObject { ["enrolled"] = expected } };
        if (supplied is not null)
        {
            args["campaign"] = new JsonObject { ["external_id"] = supplied };
        }

        return new JsonObject
        {
            ["args"] = args,
            ["campaign"] = new JsonObject
            {
                ["id"] = "cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD",
                ["name"] = "Q3 LatAm founders",
                ["status"] = "active",
                ["external_ids"] = pinned is null ? new JsonObject() : new JsonObject { ["campaign"] = pinned },
            },
            ["idempotency_key"] = WorkItemId,
        };
    }

    private static Task<PluginInvocationResult> InvokeAsync(RuntimeApiFixture api, JsonObject input, int attempt = 1) =>
        ReplyOperations.InvokeAsync(api, Operation, input, Ct, attempt: attempt);
}
