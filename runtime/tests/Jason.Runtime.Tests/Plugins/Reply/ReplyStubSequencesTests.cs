using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Jason.Runtime.Tests.Plugins.Reply;

/// <summary>
/// The stand-in's two raw calls on <c>/v3/sequences</c>, which <c>campaign.create</c> makes because reply-cli has no
/// command that creates a sequence: the list, and the create. Each answers in the shape a live account answered it —
/// the list every sequence, newest first, paged with <c>top</c> and <c>skip</c>; the create a sequence in Reply's
/// <c>new</c> state, with the account's default schedule and mailbox attached though none was asked for, and a
/// <c>created</c> moment written with two fractional digits and an offset.
/// </summary>
public class ReplyStubSequencesTests
{
    private const string Sequences = "/v3/sequences";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string[] Prefix => ["--json", "-q"];

    private static string[] List(string query) => [.. Prefix, "api", Sequences + query];

    private static string[] Create => [.. Prefix, "api", Sequences, "--method", "POST", "--body", "-"];

    private const string Placeholder =
        """{"type":"task","actionType":"toDo","delayInMinutes":0,"description":"A step that reaches nobody."}""";

    [Fact]
    public async Task The_list_answers_every_sequence_newest_first_and_pages_with_top_and_skip()
    {
        using var account = new ReplyAccount()
            .WithSequence(7, "Q3 LatAm founders", "active", false, 11)
            .WithSequence(8, "Q4 Nordics", "new", false, 21)
            .WithSequence(9, "Old one", "paused", true, 31)
            .CreatedAt(7, new DateTimeOffset(2026, 10, 2, 9, 0, 0, TimeSpan.Zero))
            .CreatedAt(8, new DateTimeOffset(2026, 10, 3, 9, 0, 0, TimeSpan.Zero))
            .CreatedAt(9, new DateTimeOffset(2026, 9, 1, 9, 0, 0, TimeSpan.Zero));

        var (exit, stdout, _) = await account.RunAsync(List("?top=2&skip=0"), Ct);

        Assert.Equal(0, exit);
        var (code, data) = Answer(stdout);
        Assert.Equal(200, code);
        Assert.Equal([8, 7], data!["items"]!.AsArray().Select(item => item!["id"]!.GetValue<int>()));
        Assert.True(data["hasMore"]!.GetValue<bool>());

        // An item is what Reply's list answers: no steps, and nothing the stand-in keeps for itself.
        var item = data["items"]![0]!.AsObject();
        Assert.Equal(
            ["created", "health", "id", "isArchived", "name", "ownerUserId", "status"],
            item.Select(member => member.Key).Order(StringComparer.Ordinal));
        Assert.Equal("2026-10-03T09:00:00.00+00:00", item["created"]!.GetValue<string>());

        var (_, rest, _) = await account.RunAsync(List("?top=2&skip=2"), Ct);
        var (_, last) = Answer(rest);
        Assert.Equal([9], last!["items"]!.AsArray().Select(entry => entry!["id"]!.GetValue<int>()));
        Assert.True(last["items"]![0]!["isArchived"]!.GetValue<bool>());
        Assert.False(last["hasMore"]!.GetValue<bool>());
    }

    /// <summary>Reply's own name filter is partial and ignores case, which is why a caller compares names itself.</summary>
    [Fact]
    public async Task A_name_filter_matches_part_of_a_name_whatever_the_case()
    {
        using var account = new ReplyAccount()
            .WithSequence(7, "Q3 LatAm founders", "active", false, 11)
            .WithSequence(8, "Q3 LatAm founders (copy)", "new", false, 21)
            .WithSequence(9, "Q4 Nordics", "new", false, 31);

        var (_, stdout, _) = await account.RunAsync(List("?name=latam&top=1000&skip=0"), Ct);

        var (_, data) = Answer(stdout);
        Assert.Equal([7, 8], data!["items"]!.AsArray().Select(item => item!["id"]!.GetValue<int>()).Order());
    }

    [Theory]
    [InlineData("?top=1001&skip=0")]
    [InlineData("?top=0&skip=0")]
    [InlineData("?top=10&skip=-1")]
    public async Task A_page_outside_what_reply_takes_is_refused_as_reply_refuses_it(string query)
    {
        using var account = new ReplyAccount();

        var (exit, stdout, _) = await account.RunAsync(List(query), Ct);

        Assert.Equal(1, exit);
        var (code, data) = Answer(stdout);
        Assert.Equal(400, code);
        Assert.Equal("sequence.invalidPagination", data!["code"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_create_makes_a_new_sequence_with_the_default_schedule_and_mailbox_and_answers_201()
    {
        using var account = new ReplyAccount().WithSequence(7, "Q3 LatAm founders", "active", false, 11);
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);

        var (exit, stdout, stderr) = await account.RunAsync(Create, $$"""{"name":"Q4 Nordics","steps":[{{Placeholder}}]}""", Ct);

        Assert.Equal(0, exit);
        Assert.Equal(string.Empty, stderr);
        var (code, data) = Answer(stdout);
        Assert.Equal(201, code);
        Assert.Equal("Q4 Nordics", data!["name"]!.GetValue<string>());
        Assert.Equal("new", data["status"]!.GetValue<string>());
        Assert.False(data["isArchived"]!.GetValue<bool>());

        // Seen live: Reply attaches the account's default schedule and its default mailbox on its own.
        Assert.NotNull(data["scheduleId"]);
        Assert.Single(data["emailAccounts"]!.AsArray());

        var step = Assert.Single(data["steps"]!.AsArray())!;
        Assert.Equal("task", step["type"]!.GetValue<string>());
        Assert.Equal("A step that reaches nobody.", step["description"]!.GetValue<string>());

        // Two fractional digits and an offset, as a live account wrote it.
        var created = data["created"]!.GetValue<string>();
        Assert.Matches(new Regex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{2}\+00:00$"), created);
        Assert.InRange(DateTimeOffset.Parse(created, CultureInfo.InvariantCulture), before, DateTimeOffset.UtcNow.AddSeconds(1));

        // And the account holds it: a second sequence, beside the planted one.
        Assert.Equal(2, account.Sequences.Count);
        Assert.Contains(account.Sequences, sequence => (string?)sequence!["name"] == "Q4 Nordics" && (int)sequence["id"]! == (int)data["id"]!);
    }

    [Fact]
    public async Task A_create_with_no_step_is_refused_at_the_steps_and_makes_nothing()
    {
        using var account = new ReplyAccount();

        var (exit, stdout, _) = await account.RunAsync(Create, """{"name":"Q4 Nordics","steps":[]}""", Ct);

        Assert.Equal(1, exit);
        var (code, data) = Answer(stdout);
        Assert.Equal(400, code);
        Assert.Equal("/steps", data!["errors"]![0]!["pointer"]!.GetValue<string>());
        Assert.Empty(account.Sequences);
    }

    /// <summary>Reply documents no name uniqueness, and accepted a second sequence of one name live.</summary>
    [Fact]
    public async Task A_create_of_a_name_the_account_holds_already_makes_a_second()
    {
        using var account = new ReplyAccount().WithSequence(7, "Q4 Nordics", "new", false, 11);

        var (exit, stdout, _) = await account.RunAsync(Create, $$"""{"name":"Q4 Nordics","steps":[{{Placeholder}}]}""", Ct);

        Assert.Equal(0, exit);
        Assert.Equal(201, Answer(stdout).Code);
        Assert.Equal(2, account.Sequences.Count(sequence => (string?)sequence!["name"] == "Q4 Nordics"));
    }

    /// <summary>
    /// A raw call whose answer is lost does everything it would have done, and then ends as reply-cli ends a write whose
    /// outcome it could not see: nothing on stdout, <c>outcome.unknown</c> on stderr, exit 3.
    /// </summary>
    [Fact]
    public async Task A_raw_call_told_to_lose_its_outcome_does_its_work_and_exits_three()
    {
        using var account = new ReplyAccount();
        account.LosesTheOutcomeOf("POST", Sequences);

        var (exit, stdout, stderr) = await account.RunAsync(Create, $$"""{"name":"Q4 Nordics","steps":[{{Placeholder}}]}""", Ct);

        Assert.Equal(3, exit);
        Assert.Equal(string.Empty, stdout);
        Assert.Equal("outcome.unknown", JsonNode.Parse(stderr)!["error"]!["code"]!.GetValue<string>());
        Assert.Single(account.Sequences);

        // Spent once: the call after it answers.
        var (again, _, _) = await account.RunAsync(List("?top=1000&skip=0"), Ct);
        Assert.Equal(0, again);
    }

    private static (int Code, JsonNode? Data) Answer(string stdout)
    {
        var envelope = JsonNode.Parse(stdout)!.AsObject();
        return (envelope["code"]!.GetValue<int>(), envelope["data"]);
    }
}
