using System.Text.Json.Nodes;
using Jason.Contracts.Plugins;
using Jason.Runtime.Plugins.Invocation;

namespace Jason.Runtime.Tests.Plugins.Reply;

/// <summary>
/// <c>campaign.pause</c> as the official package performs it: the real plugin JavaScript in a real plugin-host child,
/// driving the stand-in CLI against an account a test planted. One test per line of the document's conformance list,
/// and then the refusals Reply publishes.
/// </summary>
[Collection(ProcessEnvironmentCollection.Name)]
public class ReplyCampaignPauseTests
{
    private const string Operation = "campaign.pause";
    private const int Sequence = ReplyOperations.Sequence;
    private const string SequencePath = "/v3/sequences/7";
    private const string PausePath = "/v3/sequences/7/pause";
    private const string WorkItemId = "wi_01JB6K8TQ2W9V4MZ0C3Y7H5NRJ";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_live_campaign_is_paused_and_a_first_link_pins_it()
    {
        using var account = Planted("active");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await InvokeAsync(api, Input(supplied: "7"));

        var answer = ReplyOperations.Succeeded(Operation, result);
        Assert.Equal("""{"items":[{"campaign_external_id":"7","status":"paused"}]}""", answer.ToJsonString());
        var outcome = Assert.IsType<InvocationOutcome.Succeeded>(result.Outcome);
        Assert.Equal("""{"campaign":"7"}""", outcome.ExternalIds!.ToJsonString());
        Assert.Equal("paused", (string?)account.Sequences.Single()!["status"]);

        // The state first, then the one write; nothing on stdin, because the pause takes no body.
        Assert.Equal([$"GET {SequencePath}", $"POST {PausePath}"], account.Calls.Select(call => call.Method + " " + call.Path));
        Assert.All(account.Calls, call => Assert.Null(call.Body));
    }

    [Fact]
    public async Task A_campaign_already_paused_answers_already_paused_and_writes_nothing()
    {
        using var account = Planted("paused");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var answer = ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Input(pinned: "7")));

        Assert.Equal("already_paused", (string?)answer["items"]![0]!["status"]);
        Assert.Equal([$"GET {SequencePath}"], account.Calls.Select(call => call.Method + " " + call.Path));
    }

    /// <summary>
    /// An archived sequence, or one that never started, is not pausable, and the state read already says so: the
    /// package asks for no pause it knows will be refused.
    /// </summary>
    [Theory]
    [InlineData("active", true)]
    [InlineData("new", false)]
    public async Task A_campaign_that_cannot_be_paused_is_refused_from_its_state_without_asking(string status, bool archived)
    {
        using var account = Planted(status, archived);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(pinned: "7")));

        Assert.Equal("campaign_not_pausable", error.Code);
        Assert.Equal("7", (string?)error.ExternalIds!["campaign"]);
        Assert.Equal([$"GET {SequencePath}"], account.Calls.Select(call => call.Method + " " + call.Path));
    }

    /// <summary>
    /// A sequence archived between the read and the pause: Reply's own refusal is carried, with its code, rather than
    /// guessed at from a state that is no longer true.
    /// </summary>
    [Theory]
    [InlineData(409, "sequenceAction.archived")]
    [InlineData(400, "sequenceAction.teamMemberNotFound")]
    public async Task A_pause_reply_refuses_is_not_pausable_and_carries_reply_s_own_code(int status, string providerCode)
    {
        using var account = Planted("active");
        account.Answers("POST", PausePath, status, Problem(status, providerCode));
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(pinned: "7")));

        Assert.Equal("campaign_not_pausable", error.Code);
        Assert.Equal(providerCode, (string?)error.Details!["provider_code"]);

        // The message is all that reaches the work item's last error, so it names no cause the package cannot know:
        // Reply's own code says why, and the message says where to read it.
        Assert.DoesNotContain("never started", error.Message, StringComparison.Ordinal);
        Assert.Contains("details.provider_code", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_campaign_the_account_does_not_hold_is_not_found_and_pins_nothing()
    {
        using var account = Planted("active");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(supplied: "8")));

        Assert.Equal("campaign_not_found", error.Code);
        Assert.Null(error.ExternalIds);
    }

    /// <summary>A reason is Jason's record: nothing of it reaches the command line or a body.</summary>
    [Fact]
    public async Task The_reason_never_reaches_the_provider()
    {
        const string Note = "Reason-marker-5c1e";
        using var account = Planted("active");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Input(pinned: "7", reason: new JsonObject { ["code"] = "deliverability", ["note"] = Note })));

        Assert.All(account.Calls, call =>
        {
            Assert.DoesNotContain(Note, string.Join(' ', call.Args), StringComparison.Ordinal);
            Assert.DoesNotContain("deliverability", string.Join(' ', call.Args), StringComparison.Ordinal);
            Assert.Null(call.Body);
        });
    }

    /// <summary>
    /// The pause is documented idempotent, which is what makes a repeat after a lost answer safe: the next attempt
    /// reads the state and answers `already_paused`.
    /// </summary>
    [Fact]
    public async Task A_pause_whose_answer_was_lost_is_repeated_and_answers_already_paused()
    {
        using var account = Planted("active");
        account.LosesTheAnswerAfterWriting("POST", PausePath);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var lost = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(pinned: "7")));
        Assert.Equal("provider_answer_lost", lost.Code);
        Assert.Equal(FailureClass.Ambiguous, lost.Class);
        Assert.Equal("7", (string?)lost.ExternalIds!["campaign"]);

        var repeated = ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Input(pinned: "7"), attempt: 2));
        Assert.Equal("already_paused", (string?)repeated["items"]![0]!["status"]);
    }

    private static ReplyAccount Planted(string status, bool archived = false)
    {
        var account = ReplyOperations.Plant(new ReplyAccount());
        return account.WithSequence(Sequence, "Q3 LatAm founders", status, archived, 11, 12);
    }

    private static JsonObject Input(string? supplied = null, string? pinned = null, JsonObject? reason = null)
    {
        var args = new JsonObject();
        if (supplied is not null)
        {
            args["campaign"] = new JsonObject { ["external_id"] = supplied };
        }

        if (reason is not null)
        {
            args["reason"] = reason;
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

    private static string Problem(int status, string code) => new JsonObject
    {
        ["type"] = "about:blank",
        ["title"] = status == 409 ? "Conflict" : "Bad Request",
        ["status"] = status,
        ["detail"] = "The stand-in was told to answer this.",
        ["code"] = code,
    }.ToJsonString();
}
