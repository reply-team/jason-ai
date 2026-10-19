using System.Text.Json.Nodes;
using Jason.Contracts.Operations;
using Jason.Contracts.Plugins;
using Jason.Runtime.Plugins.Invocation;

namespace Jason.Runtime.Tests.Plugins.Reply;

/// <summary>
/// <c>conversation.classify</c> as the official package performs it: a thread filed under one of the account's own
/// categories — named by its identifier or by its name, which is matched in the package against Reply's list and never
/// put on a command line — or taken out of the one it is in. The set is outright, so a second one leaves the thread as
/// the first did.
/// </summary>
[Collection(ProcessEnvironmentCollection.Name)]
public class ReplyConversationClassifyTests
{
    private const string Operation = "conversation.classify";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_category_named_by_its_identifier_is_one_reply_lists_and_the_thread_is_filed_under_it()
    {
        using var account = Planted();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = await InvokeAsync(api, Input(new JsonObject { ["external_id"] = "3" }));

        Assert.Equal(
            """{"items":[{"thread_external_id":"501","status":"classified","category":{"external_id":"3","name":"Interested"}}]}""",
            ReplyOperations.Succeeded(Operation, result).ToJsonString());
        Assert.Null(Assert.IsType<InvocationOutcome.Succeeded>(result.Outcome).ExternalIds);
        Assert.Equal(["inbox categories", "inbox categorize 501"], Paths(account));
        Assert.Equal(["--json", "-q", "inbox", "categorize", "501", "--category-id", "3"], account.Calls[1].Args);
        Assert.Equal(3, (int)account.ThreadOf(501)["category"]!["id"]!);
    }

    /// <summary>
    /// A name is the one category whose name equals it whatever its case — "interested" is not "Not interested" — and
    /// only Reply's identifier for it reaches the command line.
    /// </summary>
    [Fact]
    public async Task A_category_named_by_its_name_is_the_one_whose_name_equals_it_whatever_its_case()
    {
        using var account = Planted();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var answer = ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Input(new JsonObject { ["name"] = "interested" })));

        Assert.Equal("""{"external_id":"3","name":"Interested"}""", answer["items"]![0]!["category"]!.ToJsonString());
        Assert.Equal("3", account.Calls[1].Option("--category-id"));
        Assert.All(account.Calls, call => Assert.DoesNotContain(call.Args, argument => argument.Contains("nterested", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task No_category_takes_the_thread_out_of_its_category_without_reading_the_list()
    {
        using var account = Planted().WithThread(ReplyConversationListTests.Thread(501, "2026-10-03T07:41:09Z", category: (3, "Interested")));
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var answer = ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Input(null)));

        Assert.Equal("""{"items":[{"thread_external_id":"501","status":"cleared","category":null}]}""", answer.ToJsonString());
        Assert.Equal(["inbox categorize 501"], Paths(account));
        Assert.Equal(["--json", "-q", "inbox", "categorize", "501", "--clear"], account.Calls[0].Args);
        Assert.Null(account.ThreadOf(501)["category"]);
    }

    /// <summary>
    /// Filing a thread under the category it is already under leaves it there and asks for the same thing again: the
    /// set is outright, never a toggle, which is what makes a repeat after a lost answer safe.
    /// </summary>
    [Fact]
    public async Task Filing_twice_leaves_the_thread_as_the_first_filing_did()
    {
        using var account = Planted();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Input(new JsonObject { ["name"] = "Interested" })));
        var again = ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Input(new JsonObject { ["name"] = "Interested" })));

        Assert.Equal("classified", (string?)again["items"]![0]!["status"]);
        Assert.Equal(3, (int)account.ThreadOf(501)["category"]!["id"]!);
        Assert.Equal(["3", "3"], account.Calls.Where(call => call.Command == "inbox categorize").Select(call => call.Option("--category-id")));
    }

    [Theory]
    [InlineData("""{"external_id":"9"}""")]
    [InlineData("""{"name":"Meeting booked"}""")]
    public async Task A_category_reply_does_not_list_files_nothing(string category)
    {
        using var account = Planted();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(JsonNode.Parse(category)!.AsObject())));

        Assert.Equal(("category_not_found", FailureClass.Permanent), (error.Code, error.Class));
        Assert.Equal(["inbox categories"], Paths(account));
        Assert.Null(account.ThreadOf(501)["category"]);
    }

    [Fact]
    public async Task A_name_two_categories_carry_files_nothing()
    {
        using var account = Planted().WithCategory(5, "INTERESTED");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(new JsonObject { ["name"] = "Interested" })));

        Assert.Equal(("category_not_unique", FailureClass.Permanent), (error.Code, error.Class));
        Assert.Equal(["inbox categories"], Paths(account));
    }

    /// <summary>
    /// An entry of Reply's list that no thread can be filed under — no name, or an identifier that is not a Reply number —
    /// is passed over, and the rest of the list still works: it is not taken for an answer that could not be read.
    /// </summary>
    [Fact]
    public async Task An_entry_no_thread_can_be_filed_under_is_passed_over_and_the_rest_still_work()
    {
        using var account = Planted()
            .WithCategoryEntry(new JsonObject { ["id"] = "triage", ["name"] = "Triage" })
            .WithCategoryEntry(new JsonObject { ["id"] = 6, ["name"] = string.Empty });
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var filed = ReplyOperations.Succeeded(Operation, await InvokeAsync(api, Input(new JsonObject { ["name"] = "Interested" })));
        var passedOver = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(new JsonObject { ["name"] = "Triage" })));

        Assert.Equal("""{"external_id":"3","name":"Interested"}""", filed["items"]![0]!["category"]!.ToJsonString());
        Assert.Equal(("category_not_found", FailureClass.Permanent), (passedOver.Code, passedOver.Class));
        Assert.Equal(3, (int)account.ThreadOf(501)["category"]!["id"]!);
    }

    /// <summary>
    /// A category Reply listed and no longer holds when the filing reaches it is answered by a live account as a missing
    /// thread, and so it is reported: `conversation_not_found`, with nothing filed. Reply's own word for a missing category
    /// never comes back from a filing.
    /// </summary>
    [Fact]
    public async Task A_category_gone_between_the_list_and_the_filing_is_answered_as_a_thread_reply_cannot_see()
    {
        using var account = Planted();
        account.Returns("inbox categories", new JsonObject
        {
            ["items"] = new JsonArray(new JsonObject
            {
                ["id"] = 9,
                ["name"] = "Meeting booked",
                ["color"] = "green",
                ["isReserved"] = false,
                ["unreadThreadsCount"] = 0,
            }),
        });
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(new JsonObject { ["external_id"] = "9" })));

        Assert.Equal(("conversation_not_found", FailureClass.Permanent), (error.Code, error.Class));
        Assert.Equal(["inbox categories", "inbox categorize 501"], Paths(account));
        Assert.Null(account.ThreadOf(501)["category"]);
    }

    [Fact]
    public async Task A_thread_reply_does_not_hold_is_not_found()
    {
        using var account = Planted();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(new JsonObject { ["external_id"] = "3" }, thread: "999")));

        Assert.Equal(("conversation_not_found", FailureClass.Permanent), (error.Code, error.Class));
    }

    [Fact]
    public async Task A_lost_answer_is_handed_out_again_because_a_second_filing_changes_nothing()
    {
        using var account = Planted();
        account.LosesTheOutcome("inbox categorize");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await InvokeAsync(api, Input(new JsonObject { ["external_id"] = "3" })));

        Assert.Equal(("provider_answer_lost", FailureClass.Ambiguous), (error.Code, error.Class));
        Assert.True(OutcomeContract.Retriable(error.Class, OperationCatalog.Find(Operation)!));
    }

    private static ReplyAccount Planted() => new ReplyAccount()
        .WithCategory(3, "Interested")
        .WithCategory(4, "Not interested", reserved: true)
        .WithThread(ReplyConversationListTests.Thread(501, "2026-10-03T07:41:09Z"));

    private static JsonObject Input(JsonObject? category, string thread = "501") => new()
    {
        ["args"] = new JsonObject
        {
            ["thread"] = new JsonObject { ["external_id"] = thread },
            ["category"] = category,
        },
        ["campaign"] = new JsonObject
        {
            ["id"] = "cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD",
            ["name"] = "Q3 LatAm founders",
            ["status"] = "active",
            ["external_ids"] = new JsonObject { ["campaign"] = "7" },
        },
        ["idempotency_key"] = "wi_01JB6K8TQ2W9V4MZ0C3Y7H5NRQ",
    };

    private static Task<PluginInvocationResult> InvokeAsync(RuntimeApiFixture api, JsonObject input) =>
        ReplyOperations.InvokeAsync(api, Operation, input, Ct);

    private static List<string> Paths(ReplyAccount account) => [.. account.Calls.Select(call => call.Line)];
}
