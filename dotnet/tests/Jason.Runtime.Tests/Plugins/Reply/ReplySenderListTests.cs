using System.Text.Json.Nodes;
using Jason.Contracts.Operations;
using Jason.Contracts.Plugins;

namespace Jason.Runtime.Tests.Plugins.Reply;

/// <summary>
/// <c>sender.list</c> as the official package performs it: <c>reply auth whoami</c> says who the credential signs in as,
/// and <c>reply account list</c>, one channel at a time, gives the mailboxes and the LinkedIn accounts with the Reply user
/// whose each one is. Under <c>own</c> a teammate's account, and one Reply names no owner for, is never offered; under
/// <c>team</c> every account is, each marked.
/// </summary>
[Collection(ProcessEnvironmentCollection.Name)]
public class ReplySenderListTests
{
    private const string Operation = "sender.list";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Two accounts of the caller's, one of a teammate's and one whose owner Reply does not name.</summary>
    private static ReplyAccount Planted() => new ReplyAccount()
        .WithSender(12, "email", "Ann Owner", "ann@example.com")
        .WithSender(13, "email", " ", "lena@example.com", owner: 502)
        .WithSender(34, "linkedin", "Ann Owner", "https://www.linkedin.com/in/ann-owner", status: "cookieInvalid")
        .WithSender(35, "linkedin", "Shared", "", owner: null, status: "dailyLimitReached");

    [Fact]
    public async Task Own_lists_the_caller_s_accounts_alone_each_channel_asked_for_on_its_own()
    {
        using var account = Planted();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = ReplyOperations.Succeeded(Operation, await ReplyOperations.InvokeAsync(api, Operation, Input("own"), Ct));

        Assert.Equal(
            """{"items":[{"external_id":"12","channel":"email","name":"Ann Owner","address":"ann@example.com","status":"active","owner":"own","vendor":{"status":"connected"}},"""
            + """{"external_id":"34","channel":"linkedin","name":"Ann Owner","address":"https://www.linkedin.com/in/ann-owner","status":"disconnected","owner":"own","vendor":{"status":"cookieInvalid"}}]"""
            + ""","has_more":false,"channels":{"email":"listed","linkedin":"listed"},"vendor":{"ordering":"email_then_linkedin"}}""",
            result.ToJsonString());
        Assert.Equal(["auth whoami", "account list", "account list"], account.Calls.Select(call => call.Line));
        Assert.Equal(["email", "linkedin"], account.Calls.Skip(1).Select(call => call.Option("--channel")));
    }

    [Fact]
    public async Task Team_lists_every_account_and_says_whose_each_one_is()
    {
        using var account = Planted();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = ReplyOperations.Succeeded(Operation, await ReplyOperations.InvokeAsync(api, Operation, Input("team"), Ct));

        var items = result["items"]!.AsArray();
        Assert.Equal(
            [("12", "own"), ("13", "teammate"), ("34", "own"), ("35", "unknown")],
            items.Select(item => ((string)item!["external_id"]!, (string)item["owner"]!)));

        // A name or an address Reply holds as blank is none at all.
        var teammate = items[1]!.AsObject();
        Assert.Null(teammate["name"]);
        Assert.Null(items[3]!["address"]);
        Assert.Equal(("active", "dailyLimitReached"), ((string?)items[3]!["status"], (string?)items[3]!["vendor"]!["status"]));
    }

    /// <summary>
    /// The short page. Accounts whose names and addresses are as long as the document allows are cut at the plugin's own
    /// budget, which counts coarser than the runtime does and so stops short of the cap: the plugin says there is more, and
    /// the page that follows, started at the offset plus what the first returned, carries on with no account lost and none
    /// repeated. The row passes the document's length bound because SchemaValidator.ApplyLength counts runes, as the
    /// plugin's cut counts code points.
    /// </summary>
    [Fact]
    public async Task A_page_of_accounts_too_large_for_one_result_comes_back_short_and_the_next_one_carries_on()
    {
        const int Accounts = 100;
        var name = string.Concat(Enumerable.Repeat("\U0001F600", 200));
        var address = string.Concat(Enumerable.Repeat("\U0001F600", 500));
        using var account = new ReplyAccount();
        for (var id = 1; id <= Accounts; id++)
        {
            account.WithSender(id, "email", name, address);
        }

        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var seen = new List<string>();
        var pages = new List<int>();
        var offset = 0;
        var more = true;
        while (more)
        {
            var result = ReplyOperations.Succeeded(Operation, await ReplyOperations.InvokeAsync(api, Operation, Input("own", offset: offset, limit: 100), Ct));
            var items = result["items"]!.AsArray();
            seen.AddRange(items.Select(item => (string)item!["external_id"]!));
            pages.Add(items.Count);
            more = (bool)result["has_more"]!;
            offset += items.Count;
            Assert.True(pages.Count <= 3, $"the pages never ended: {string.Join(",", pages)} so far");
        }

        Assert.True(pages[0] < Accounts, $"the first page held all {Accounts} accounts, so nothing was cut");
        Assert.True(pages.Count > 1, $"the pages were {string.Join(",", pages)}");
        Assert.Equal(Enumerable.Range(1, Accounts).Select(id => id.ToString(System.Globalization.CultureInfo.InvariantCulture)), seen);
    }

    /// <summary>A whoami that does not say who the caller is answers nothing an owner can be compared with, so nothing is listed.</summary>
    [Fact]
    public async Task A_caller_reply_does_not_name_lists_nothing_and_the_read_is_asked_again()
    {
        using var account = Planted();
        account.Returns("auth whoami", new JsonObject { ["username"] = "owner@example.com", ["teamId"] = 900 });
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await ReplyOperations.InvokeAsync(api, Operation, Input("team"), Ct));

        Assert.Equal(("provider_unavailable", FailureClass.Transient), (error.Code, error.Class));
        Assert.Equal(["auth whoami"], account.Calls.Select(call => call.Line));
    }

    [Fact]
    public async Task One_channel_named_is_the_only_one_asked_for()
    {
        using var account = Planted();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = ReplyOperations.Succeeded(Operation, await ReplyOperations.InvokeAsync(api, Operation, Input("own", channel: "linkedin"), Ct));

        Assert.Equal(["34"], result["items"]!.AsArray().Select(item => (string)item!["external_id"]!));
        Assert.Equal("""{"email":"not_asked","linkedin":"listed"}""", result["channels"]!.ToJsonString());
        Assert.Equal(["--json", "-q", "account", "list", "--channel", "linkedin"], account.Calls[^1].Args);
    }

    /// <summary>
    /// A plan without LinkedIn still has mailboxes to send from: asked for one by one, the channel the plan lacks is said
    /// and the other listed. Named on its own, the same channel is a refusal.
    /// </summary>
    [Fact]
    public async Task A_channel_the_plan_does_not_include_is_said_beside_the_others_or_refused_when_named()
    {
        using var account = Planted().Without("linkedInAccounts");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var every = ReplyOperations.Succeeded(Operation, await ReplyOperations.InvokeAsync(api, Operation, Input("own"), Ct));
        Assert.Equal(["12"], every["items"]!.AsArray().Select(item => (string)item!["external_id"]!));
        Assert.Equal("""{"email":"listed","linkedin":"not_included"}""", every["channels"]!.ToJsonString());

        var named = ReplyOperations.Failed(Operation, await ReplyOperations.InvokeAsync(api, Operation, Input("own", channel: "linkedin"), Ct));
        Assert.Equal(("feature_unavailable", FailureClass.Permanent), (named.Code, named.Class));
    }

    [Theory]
    [InlineData(0, 2, new[] { "12", "13" }, true)]
    [InlineData(2, 2, new[] { "34", "35" }, false)]
    [InlineData(3, 5, new[] { "35" }, false)]
    [InlineData(4, 5, new string[0], false)]
    public async Task A_page_starts_at_its_offset_in_the_provider_s_order(int offset, int limit, string[] expected, bool more)
    {
        using var account = Planted();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = ReplyOperations.Succeeded(Operation, await ReplyOperations.InvokeAsync(api, Operation, Input("team", offset: offset, limit: limit), Ct));

        Assert.Equal(expected, result["items"]!.AsArray().Select(item => (string)item!["external_id"]!));
        Assert.Equal(more, (bool)result["has_more"]!);
    }

    [Fact]
    public async Task A_mailbox_numbered_past_thirty_two_bits_is_listed_by_its_whole_number()
    {
        using var account = new ReplyAccount().WithSender(9_007_199_254_740_991, "email", "Ann", "ann@example.com");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = ReplyOperations.Succeeded(Operation, await ReplyOperations.InvokeAsync(api, Operation, Input("own", channel: "email"), Ct));

        Assert.Equal("9007199254740991", (string?)result["items"]![0]!["external_id"]);
    }

    /// <summary>An answer that is not a list of accounts is no answer; a read changes nothing, so it is asked again.</summary>
    [Fact]
    public async Task An_answer_that_is_not_a_list_is_asked_again()
    {
        using var account = Planted();
        account.Returns("account list", new JsonObject { ["accounts"] = new JsonArray() });
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await ReplyOperations.InvokeAsync(api, Operation, Input("own"), Ct));

        Assert.Equal(("provider_unavailable", FailureClass.Transient), (error.Code, error.Class));
        Assert.True(OutcomeContract.Retriable(error.Class, OperationCatalog.Find(Operation)!));
    }

    [Fact]
    public async Task A_list_reply_could_not_read_to_its_end_lists_nothing()
    {
        using var account = Planted();
        account.Refuses("account list", "account.list_incomplete", status: 200);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed(Operation, await ReplyOperations.InvokeAsync(api, Operation, Input("own"), Ct));

        Assert.Equal(("provider_call_failed", FailureClass.Permanent), (error.Code, error.Class));
    }

    private static JsonObject Input(string owners, string? channel = null, int offset = 0, int limit = 20)
    {
        var args = new JsonObject { ["owners"] = owners, ["limit"] = limit, ["offset"] = offset };
        if (channel is not null)
        {
            args["sender_channel"] = channel;
        }

        return new JsonObject
        {
            ["args"] = args,
            ["campaign"] = new JsonObject
            {
                ["id"] = "cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRE",
                ["name"] = "Q3 LatAm founders",
                ["status"] = "active",
                ["external_ids"] = new JsonObject(),
            },
        };
    }
}
