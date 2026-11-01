using System.Text.Json.Nodes;
using Jason.Contracts.Operations;
using Jason.Contracts.Plugins;
using Jason.Runtime.Plugins.Invocation;

namespace Jason.Runtime.Tests.Plugins.Reply;

/// <summary>
/// <c>conversation.close</c> and <c>conversation.reopen</c> as the official package performs them: Reply holds no closed
/// state, and read is how its inbox shows a thread as worked, so closing marks the thread read and reopening marks it
/// unread. Each sets the state outright, so a second one changes nothing.
/// </summary>
[Collection(ProcessEnvironmentCollection.Name)]
public class ReplyConversationCloseTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("conversation.close", "mark-read", "closed", true)]
    [InlineData("conversation.reopen", "mark-unread", "reopened", false)]
    public async Task A_thread_is_set_outright_and_a_second_time_changes_nothing(string operation, string verb, string word, bool read)
    {
        using var account = new ReplyAccount().WithThread(ReplyConversationListTests.Thread(501, "2026-10-03T07:41:09Z", read: !read));
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        foreach (var _ in new[] { 1, 2 })
        {
            var result = await ReplyOperations.InvokeAsync(api, operation, Input(), Ct);

            Assert.Equal(
                $$"""{"items":[{"thread_external_id":"501","status":"{{word}}"}]}""",
                ReplyOperations.Succeeded(operation, result).ToJsonString());
            Assert.Null(Assert.IsType<InvocationOutcome.Succeeded>(result.Outcome).ExternalIds);
            Assert.Equal(read, (bool)account.ThreadOf(501)["isRead"]!);
        }

        Assert.Equal([$"inbox {verb} 501", $"inbox {verb} 501"], account.Calls.Select(call => call.Line));
        Assert.All(account.Calls, call => Assert.Equal(["--json", "-q", "inbox", verb, "501"], call.Args));
    }

    [Theory]
    [InlineData("conversation.close")]
    [InlineData("conversation.reopen")]
    public async Task A_thread_reply_does_not_hold_is_not_found(string operation)
    {
        using var account = new ReplyAccount();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(operation, await ReplyOperations.InvokeAsync(api, operation, Input(thread: "999"), Ct));

        Assert.Equal(("conversation_not_found", FailureClass.Permanent), (error.Code, error.Class));
    }

    [Theory]
    [InlineData("conversation.close", "inbox mark-read")]
    [InlineData("conversation.reopen", "inbox mark-unread")]
    public async Task A_lost_answer_is_handed_out_again_because_a_second_time_changes_nothing(string operation, string command)
    {
        using var account = new ReplyAccount().WithThread(ReplyConversationListTests.Thread(501, "2026-10-03T07:41:09Z"));
        account.LosesTheOutcome(command);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(operation, await ReplyOperations.InvokeAsync(api, operation, Input(), Ct));

        Assert.Equal(("provider_answer_lost", FailureClass.Ambiguous), (error.Code, error.Class));
        Assert.True(OutcomeContract.Retriable(error.Class, OperationCatalog.Find(operation)!));
    }

    [Fact]
    public async Task An_identifier_that_is_not_a_reply_thread_number_names_no_thread_and_nothing_is_called()
    {
        using var account = new ReplyAccount();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(
            "conversation.close", await ReplyOperations.InvokeAsync(api, "conversation.close", Input(thread: "t_99812"), Ct));

        Assert.Equal("conversation_not_found", error.Code);
        Assert.Empty(account.Calls);
    }

    private static JsonObject Input(string thread = "501") => new()
    {
        ["args"] = new JsonObject { ["thread"] = new JsonObject { ["external_id"] = thread } },
        ["campaign"] = new JsonObject
        {
            ["id"] = "cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD",
            ["name"] = "Q3 LatAm founders",
            ["status"] = "active",
            ["external_ids"] = new JsonObject { ["campaign"] = "7" },
        },
        ["idempotency_key"] = "wi_01JB6K8TQ2W9V4MZ0C3Y7H5NRR",
    };
}
